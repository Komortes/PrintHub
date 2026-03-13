using PrintHub.Contracts.PrintJobs;

namespace PrintHub.Core.Models;

public sealed class PrintDocument
{
    private PrintDocument(
        DocumentSourceType sourceType,
        PrintDocumentFormat format,
        string? url,
        string? data,
        string? fileName)
    {
        SourceType = sourceType;
        Format = format;
        Url = url;
        Data = data;
        FileName = fileName;
    }

    public DocumentSourceType SourceType { get; }

    public PrintDocumentFormat Format { get; }

    public string? Url { get; }

    public string? Data { get; }

    public string? FileName { get; }

    public static PrintDocument FromRequest(PrintDocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.IsDefined(request.Type))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Document source type is not supported.");
        }

        if (!Enum.IsDefined(request.Format))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Document format is not supported.");
        }

        switch (request.Type)
        {
            case DocumentSourceType.Url when string.IsNullOrWhiteSpace(request.Url):
                throw new ArgumentException("Document URL is required for URL source type.", nameof(request));
            case DocumentSourceType.Base64 when string.IsNullOrWhiteSpace(request.Data):
                throw new ArgumentException("Base64 document data is required for Base64 source type.", nameof(request));
            case DocumentSourceType.Upload when string.IsNullOrWhiteSpace(request.Data) && string.IsNullOrWhiteSpace(request.FileName):
                throw new ArgumentException("Uploaded documents require file data or file name.", nameof(request));
        }

        return new PrintDocument(
            request.Type,
            request.Format,
            Normalize(request.Url),
            Normalize(request.Data),
            Normalize(request.FileName));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
