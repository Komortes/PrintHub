namespace PrintHub.Api.Configuration;

public sealed class PrintHubApiOptions
{
    public const string SectionName = "PrintHub";
    public const long DefaultMaxUploadSizeBytes = 10 * 1024 * 1024;
    public const long DefaultMaxMultipartBodySizeBytes = 100 * 1024 * 1024;
    public const string DefaultApiKeyHeaderName = "X-PrintHub-Api-Key";
    public const string DefaultSettingsFilePath = "data/settings.json";
    public const string DefaultStorageDirectory = "data/documents";
    public const int DefaultPort = 5051;

    public string ServiceName { get; set; } = "PrintHub";

    public long MaxUploadSizeBytes { get; set; } = DefaultMaxUploadSizeBytes;

    public long MaxMultipartBodySizeBytes { get; set; } = DefaultMaxMultipartBodySizeBytes;

    public string? ApiKey { get; set; }

    public string ApiKeyHeaderName { get; set; } = DefaultApiKeyHeaderName;

    public string SettingsFilePath { get; set; } = DefaultSettingsFilePath;

    public string StorageDirectory { get; set; } = DefaultStorageDirectory;

    public int Port { get; set; } = DefaultPort;
}
