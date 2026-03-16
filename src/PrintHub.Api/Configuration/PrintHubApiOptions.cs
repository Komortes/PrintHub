namespace PrintHub.Api.Configuration;

public sealed class PrintHubApiOptions
{
    public const string SectionName = "PrintHub";
    public const long DefaultMaxUploadSizeBytes = 10 * 1024 * 1024;

    public string ServiceName { get; init; } = "PrintHub";

    public long MaxUploadSizeBytes { get; init; } = DefaultMaxUploadSizeBytes;
}
