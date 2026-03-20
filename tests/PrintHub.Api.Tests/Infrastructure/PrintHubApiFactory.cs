using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PrintHub.Api.Tests.Infrastructure;

public sealed class PrintHubApiFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _overrides;
    private readonly string _tempRootPath = Path.Combine(
        Path.GetTempPath(),
        $"printhub-api-tests-{Guid.NewGuid():N}");

    public PrintHubApiFactory(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        _overrides = overrides ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }

    public string TempRootPath => _tempRootPath;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var settingsFilePath = Path.Combine(_tempRootPath, "settings.json");
            var storageDirectory = Path.Combine(_tempRootPath, "documents");
            var logsDirectory = Path.Combine(_tempRootPath, "logs");

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["PrintHub:ServiceName"] = "PrintHub.Test",
                ["PrintHub:ApiKey"] = "test-api-key",
                ["PrintHub:ApiKeyHeaderName"] = "X-PrintHub-Api-Key",
                ["PrintHub:SettingsFilePath"] = settingsFilePath,
                ["PrintHub:StorageDirectory"] = storageDirectory,
                ["PrintHub:MaxUploadSizeBytes"] = "10485760",
                ["PrintHub:MaxMultipartBodySizeBytes"] = "104857600",
                ["PrintHub:FileLogging:Enabled"] = "false",
                ["PrintHub:FileLogging:DirectoryPath"] = logsDirectory
            };

            foreach (var pair in _overrides)
            {
                values[pair.Key] = pair.Value;
            }

            configBuilder.AddInMemoryCollection(values);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing || !Directory.Exists(_tempRootPath))
        {
            return;
        }

        try
        {
            Directory.Delete(_tempRootPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
