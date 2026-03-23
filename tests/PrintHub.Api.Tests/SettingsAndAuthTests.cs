using System.Net;
using System.Net.Http.Json;
using PrintHub.Api.Tests.Infrastructure;
using PrintHub.Contracts.Settings;

namespace PrintHub.Api.Tests;

public sealed class SettingsAndAuthTests
{
    [Fact]
    public async Task ProtectedEndpoints_ReturnUnauthorizedWithoutApiKey()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/printers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
        Assert.True(setupStatus.HasDefaultPrinter);
        Assert.Equal(2, setupStatus.Printers.Count);
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
    public async Task Onboarding_CanBeCompleted_FromBootstrapState()
    {
        using var factory = new PrintHubApiFactory(new Dictionary<string, string?>
        {
            ["PrintHub:ApiKey"] = ""
        });
        using var client = factory.CreateClient();

        var onboardingResponse = await client.PostAsJsonAsync(
            "/settings/onboarding",
            new CompleteOnboardingRequest("bootstrapped-key", "printer_2"));

        Assert.Equal(HttpStatusCode.OK, onboardingResponse.StatusCode);

        var setupStatus = await onboardingResponse.Content.ReadFromJsonAsync<SetupStatusDto>(TestJson.SerializerOptions);

        Assert.NotNull(setupStatus);
        Assert.False(setupStatus!.IsOnboardingRequired);
        Assert.True(setupStatus.HasApiKey);
        Assert.True(setupStatus.HasDefaultPrinter);
        Assert.Contains(setupStatus.Printers, printer => printer.Id == "printer_2" && printer.IsDefault);

        using var settingsRequest = new HttpRequestMessage(HttpMethod.Get, "/settings");
        settingsRequest.Headers.Add("X-PrintHub-Api-Key", "bootstrapped-key");

        var settingsResponse = await client.SendAsync(settingsRequest);
        var settings = await settingsResponse.Content.ReadFromJsonAsync<PrintHubSettingsDto>();

        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
        Assert.NotNull(settings);
        Assert.Equal("Label Printer", settings!.DefaultPrinterName);
    }
}
