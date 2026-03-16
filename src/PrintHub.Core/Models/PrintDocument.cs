using PrintHub.Contracts.PrintJobs;

namespace PrintHub.Core.Models;

public sealed class PrintDocument
{
    private PrintDocument(
        DocumentSourceType sourceType,
        PrintDocumentFormat format,
        string fileName,
        string storedPath,
        long sizeBytes,
        string? sourceUrl)
    {
        SourceType = sourceType;
        Format = format;
        FileName = fileName;
        StoredPath = storedPath;
        SizeBytes = sizeBytes;
        SourceUrl = sourceUrl;
    }

    public DocumentSourceType SourceType { get; }

    public PrintDocumentFormat Format { get; }

    public string FileName { get; }

    public string StoredPath { get; }

    public long SizeBytes { get; }

    public string? SourceUrl { get; }

    public static PrintDocument CreateStored(
        DocumentSourceType sourceType,
        PrintDocumentFormat format,
        string fileName,
        string storedPath,
        long sizeBytes,
        string? sourceUrl = null)
    {
        if (!Enum.IsDefined(sourceType))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceType), "Document source type is not supported.");
        }

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), "Document format is not supported.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Document file name is required.", nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(storedPath))
        {
            throw new ArgumentException("Document storage path is required.", nameof(storedPath));
        }

        if (sizeBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Document size must be greater than 0.");
        }

        return new PrintDocument(
            sourceType,
            format,
            fileName.Trim(),
            storedPath.Trim(),
            sizeBytes,
            Normalize(sourceUrl));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
