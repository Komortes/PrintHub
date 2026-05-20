using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PrintHub.Api.Tests.Infrastructure;
using PrintHub.Contracts.Settings;

namespace PrintHub.Api.Tests;

public sealed class SettingsAndAuthTests
{
    [Fact]
    public async Task ProtectedEndpoints_ReturnOk_ForLocalRequests()
    {
        // Local requests (null RemoteIpAddress in test context) always pass through —
        // no API key header needed for the local UI.
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/printers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetupStatus_ReportsOnboardingState_WhenApiKeyIsNotConfigured()
    {
        using var factory = new PrintHubApiFactory(new Dictionary<string, string?>
        {
            ["PrintHub:ApiKey"] = ""
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/settings/setup-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var setupStatus = await response.Content.ReadFromJsonAsync<SetupStatusDto>(TestJson.SerializerOptions);

        Assert.NotNull(setupStatus);
        Assert.True(setupStatus!.IsOnboardingRequired);
        Assert.False(setupStatus.HasApiKey);
        Assert.False(setupStatus.HasDefaultPrinter);
        Assert.Empty(setupStatus!.Printers);
    }

    [Fact]
    public async Task Settings_CanBeBootstrapped_WhenApiKeyIsNotConfigured()
    {
        using var factory = new PrintHubApiFactory(new Dictionary<string, string?>
        {
            ["PrintHub:ApiKey"] = ""
        });
        using var client = factory.CreateClient();

        var getResponse = await client.GetAsync("/settings");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var currentSettings = await getResponse.Content.ReadFromJsonAsync<PrintHubSettingsDto>();

        Assert.NotNull(currentSettings);
        Assert.Null(currentSettings!.ApiKey);

        var updateRequest = new UpdatePrintHubSettingsRequest(
            currentSettings.ServiceName,
            currentSettings.Port,
            "0.0.0.0",
            currentSettings.ApiKeyHeaderName,
            "bootstrapped-key",
            currentSettings.DefaultPrinterName,
            currentSettings.StorageDirectory,
            currentSettings.MaxUploadSizeBytes);

        var putResponse = await client.PutAsJsonAsync("/settings", updateRequest);

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        using var printersRequest = new HttpRequestMessage(HttpMethod.Get, "/printers");
        printersRequest.Headers.Add("X-PrintHub-Api-Key", "bootstrapped-key");

        var printersResponse = await client.SendAsync(printersRequest);

        Assert.Equal(HttpStatusCode.OK, printersResponse.StatusCode);
    }

    [Fact]
    public async Task Settings_PersistBindHostAndPort_WhenUpdated()
    {
        using var factory = new PrintHubApiFactory(new Dictionary<string, string?>
        {
            ["PrintHub:ApiKey"] = ""
        });
        using var client = factory.CreateClient();

        var currentSettings = await client.GetFromJsonAsync<PrintHubSettingsDto>("/settings");

        Assert.NotNull(currentSettings);

        var updateRequest = new UpdatePrintHubSettingsRequest(
            currentSettings!.ServiceName,
            8666,
            "0.0.0.0",
            currentSettings.ApiKeyHeaderName,
            "demo-key",
            currentSettings.DefaultPrinterName,
            currentSettings.StorageDirectory,
            currentSettings.MaxUploadSizeBytes);

        var response = await client.PutAsJsonAsync("/settings", updateRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updatedSettings = await response.Content.ReadFromJsonAsync<PrintHubSettingsDto>();

        Assert.NotNull(updatedSettings);
        Assert.Equal(8666, updatedSettings!.Port);
        Assert.Equal("0.0.0.0", updatedSettings.BindHost);

        var storedSettingsPath = Path.Combine(factory.TempRootPath, "settings.json");
        Assert.True(File.Exists(storedSettingsPath));

        await using var storedSettingsStream = File.OpenRead(storedSettingsPath);
        var storedSettings = await JsonDocument.ParseAsync(storedSettingsStream);
        Assert.Equal(8666, storedSettings.RootElement.GetProperty("port").GetInt32());
        Assert.Equal("0.0.0.0", storedSettings.RootElement.GetProperty("bindHost").GetString());
    }

    [Fact]
    public async Task Settings_ApplyDefaultBindHost_ForLegacySettingsFile()
    {
        var tempRootPath = Path.Combine(Path.GetTempPath(), $"printhub-settings-legacy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRootPath);

        try
        {
            var legacySettingsJson = """
                {
                  "serviceName": "PrintHub.Legacy",
                  "port": 8666,
                  "apiKeyHeaderName": "X-PrintHub-Api-Key",
                  "apiKey": "legacy-key",
                  "defaultPrinterName": null,
                  "storageDirectory": "documents",
                  "maxUploadSizeBytes": 10485760,
                  "printers": []
                }
                """;

            await File.WriteAllTextAsync(Path.Combine(tempRootPath, "settings.json"), legacySettingsJson);

            using var factory = new PrintHubApiFactory(tempRootPath: tempRootPath);
            using var client = factory.CreateClient();

            var settings = await client.GetFromJsonAsync<PrintHubSettingsDto>("/settings");

            Assert.NotNull(settings);
            Assert.Equal("127.0.0.1", settings!.BindHost);
            Assert.Equal(8666, settings.Port);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRootPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task Onboarding_CanBeCompleted_FromBootstrapState()
    {
        using var factory = new PrintHubApiFactory(new Dictionary<string, string?>
        {
            ["PrintHub:ApiKey"] = ""
        });
        using var client = factory.CreateClient();

        var onboardingResponse = await client.PostAsJsonAsync(
            "/settings/onboarding",
            new CompleteOnboardingRequest("bootstrapped-key"));

        Assert.Equal(HttpStatusCode.OK, onboardingResponse.StatusCode);

        var setupStatus = await onboardingResponse.Content.ReadFromJsonAsync<SetupStatusDto>(TestJson.SerializerOptions);

        Assert.NotNull(setupStatus);
        Assert.False(setupStatus!.IsOnboardingRequired);
        Assert.True(setupStatus.HasApiKey);
    }
}
