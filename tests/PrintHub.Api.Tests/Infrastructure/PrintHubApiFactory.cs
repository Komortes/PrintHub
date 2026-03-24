using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PrintHub.Api.Tests.Infrastructure;

public sealed class PrintHubApiFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _overrides;
    private readonly bool _ownsTempRoot;
    private readonly string _tempRootPath;

    public PrintHubApiFactory(
        IReadOnlyDictionary<string, string?>? overrides = null,
        string? tempRootPath = null)
    {
        _overrides = overrides ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        _ownsTempRoot = tempRootPath is null;
        _tempRootPath = tempRootPath ?? Path.Combine(
            Path.GetTempPath(),
            $"printhub-api-tests-{Guid.NewGuid():N}");
    }

    public string TempRootPath => _tempRootPath;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var settingsFilePath = Path.Combine(_tempRootPath, "settings.json");
            var jobsFilePath = Path.Combine(_tempRootPath, "jobs.json");
            var storageDirectory = Path.Combine(_tempRootPath, "documents");
            var logsDirectory = Path.Combine(_tempRootPath, "logs");
            var autoStartUnixLauncherPath = Path.Combine(_tempRootPath, "run-printhub.sh");
            var autoStartWindowsLauncherPath = Path.Combine(_tempRootPath, "run-printhub.ps1");
            var autoStartMacOsLaunchAgentsDirectoryPath = Path.Combine(_tempRootPath, "launch-agents");
            var autoStartLinuxDirectoryPath = Path.Combine(_tempRootPath, "autostart");
            var autoStartWindowsDirectoryPath = Path.Combine(_tempRootPath, "startup");

            Directory.CreateDirectory(_tempRootPath);
            File.WriteAllText(autoStartUnixLauncherPath, "#!/usr/bin/env bash\nexit 0\n");
            File.WriteAllText(autoStartWindowsLauncherPath, "exit 0\n");

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["PrintHub:ServiceName"] = "PrintHub.Test",
                ["PrintHub:ApiKey"] = "test-api-key",
                ["PrintHub:ApiKeyHeaderName"] = "X-PrintHub-Api-Key",
                ["PrintHub:BackendMode"] = "Mock",
                ["PrintHub:SettingsFilePath"] = settingsFilePath,
                ["PrintHub:JobsFilePath"] = jobsFilePath,
                ["PrintHub:StorageDirectory"] = storageDirectory,
                ["PrintHub:MaxUploadSizeBytes"] = "10485760",
                ["PrintHub:MaxMultipartBodySizeBytes"] = "104857600",
                ["PrintHub:AutoStartUnixLauncherPath"] = autoStartUnixLauncherPath,
                ["PrintHub:AutoStartWindowsLauncherPath"] = autoStartWindowsLauncherPath,
                ["PrintHub:AutoStartMacOsLaunchAgentsDirectoryPath"] = autoStartMacOsLaunchAgentsDirectoryPath,
                ["PrintHub:AutoStartLinuxDirectoryPath"] = autoStartLinuxDirectoryPath,
                ["PrintHub:AutoStartWindowsDirectoryPath"] = autoStartWindowsDirectoryPath,
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

        if (!disposing || !_ownsTempRoot || !Directory.Exists(_tempRootPath))
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
