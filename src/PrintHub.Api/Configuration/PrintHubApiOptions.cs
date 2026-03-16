namespace PrintHub.Api.Configuration;

public sealed class PrintHubApiOptions
{
    public const string SectionName = "PrintHub";
    public const long DefaultMaxUploadSizeBytes = 10 * 1024 * 1024;
    public const string DefaultApiKeyHeaderName = "X-PrintHub-Api-Key";

    public string ServiceName { get; set; } = "PrintHub";

    public long MaxUploadSizeBytes { get; set; } = DefaultMaxUploadSizeBytes;

    public string? ApiKey { get; set; }

    public string ApiKeyHeaderName { get; set; } = DefaultApiKeyHeaderName;
}
