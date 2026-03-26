namespace PrintHub.Api.Configuration;

public sealed class PrintHubApiOptions
{
    public const string SectionName = "PrintHub";
    public const long DefaultMaxUploadSizeBytes = 10 * 1024 * 1024;
    public const long DefaultMaxMultipartBodySizeBytes = 100 * 1024 * 1024;
    public const string DefaultApiKeyHeaderName = "X-PrintHub-Api-Key";
    public const string DefaultSettingsFilePath = "data/settings.json";
    public const string DefaultJobsFilePath = "data/jobs.db";
    public const string DefaultStorageDirectory = "data/documents";
    public const int DefaultPort = 5051;
    public const PrintBackendMode DefaultBackendMode = PrintBackendMode.Auto;

    public string ServiceName { get; set; } = "PrintHub";

    public long MaxUploadSizeBytes { get; set; } = DefaultMaxUploadSizeBytes;

    public long MaxMultipartBodySizeBytes { get; set; } = DefaultMaxMultipartBodySizeBytes;

    public string? ApiKey { get; set; }

    public string ApiKeyHeaderName { get; set; } = DefaultApiKeyHeaderName;

    public string SettingsFilePath { get; set; } = DefaultSettingsFilePath;

    public string JobsFilePath { get; set; } = DefaultJobsFilePath;

    public string StorageDirectory { get; set; } = DefaultStorageDirectory;

    public int Port { get; set; } = DefaultPort;

    public PrintBackendMode BackendMode { get; set; } = DefaultBackendMode;

    public string? AutoStartUnixLauncherPath { get; set; }

    public string? AutoStartWindowsLauncherPath { get; set; }

    public string? AutoStartMacOsLaunchAgentsDirectoryPath { get; set; }

    public string? AutoStartLinuxDirectoryPath { get; set; }

    public string? AutoStartWindowsDirectoryPath { get; set; }
}
