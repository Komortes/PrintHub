namespace PrintHub.Api.Logging;

public sealed class PrintHubFileLoggerOptions
{
    public const string SectionName = "PrintHub:FileLogging";
    public const string DefaultDirectoryPath = "data/logs";
    public const string DefaultFileName = "printhub.log";
    public const long DefaultMaxFileSizeBytes = 1 * 1024 * 1024;
    public const int DefaultRetainedFileCountLimit = 7;

    public bool Enabled { get; set; } = true;

    public string DirectoryPath { get; set; } = DefaultDirectoryPath;

    public string FileName { get; set; } = DefaultFileName;

    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;

    public int RetainedFileCountLimit { get; set; } = DefaultRetainedFileCountLimit;
}
