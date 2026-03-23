using System.Net.Http;
using System.Text;
using PrintHub.Contracts.PrintJobs;
using PrintHub.Core.Documents;
using PrintHub.Core.Models;
using PrintHub.Core.Settings;
using PrintHub.Infrastructure.Paths;

namespace PrintHub.Infrastructure.Documents;

public sealed class FileSystemPrintDocumentPipeline : IPrintDocumentPipeline
{
    private static readonly byte[] PdfSignature = Encoding.ASCII.GetBytes("%PDF-");

    private readonly HttpClient _httpClient;
    private readonly IPrintHubSettingsService _settingsService;
    private readonly PrintHubAppDataPaths _appDataPaths;

    public FileSystemPrintDocumentPipeline(
        HttpClient httpClient,
        IPrintHubSettingsService settingsService,
        PrintHubAppDataPaths appDataPaths)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _appDataPaths = appDataPaths;
    }

    public async ValueTask<PrintDocument> PrepareAsync(
        string jobId,
        PrintDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID is required.", nameof(jobId));
        }

        ArgumentNullException.ThrowIfNull(request);

        if (request.Format != PrintDocumentFormat.Pdf)
        {
            throw new InvalidDataException("Only PDF documents are supported in the current version.");
        }

        var settings = await _settingsService.GetAsync(cancellationToken);
        var fileName = ResolveFileName(request);
        var storageDirectory = ResolveStorageDirectory(settings.StorageDirectory);
        var storedPath = Path.Combine(storageDirectory, $"{jobId}-{fileName}");

        try
        {
            var sizeBytes = request.Type switch
            {
                DocumentSourceType.Base64 or DocumentSourceType.Upload => await SaveBase64Async(
                    request.Data,
                    storedPath,
                    settings.MaxUploadSizeBytes,
                    cancellationToken),
                DocumentSourceType.Url => await DownloadUrlAsync(
                    request.Url,
                    storedPath,
                    settings.MaxUploadSizeBytes,
                    cancellationToken),
                _ => throw new InvalidDataException("Document source type is not supported.")
            };

            await EnsurePdfAsync(storedPath, cancellationToken);

            return PrintDocument.CreateStored(
                request.Type,
                request.Format,
                fileName,
                storedPath,
                sizeBytes,
                request.Url);
        }
        catch
        {
            if (File.Exists(storedPath))
            {
                File.Delete(storedPath);
            }

            throw;
        }
    }

    private async Task<long> SaveBase64Async(
        string? base64Data,
        string storedPath,
        long maxUploadSizeBytes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(base64Data))
        {
            throw new InvalidDataException("Document data is required.");
        }

        byte[] bytes;

        try
        {
            bytes = Convert.FromBase64String(base64Data);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Document data is not valid Base64.", exception);
        }

        if (bytes.LongLength > maxUploadSizeBytes)
        {
            throw new InvalidDataException(
                $"Document exceeds the configured limit of {maxUploadSizeBytes} bytes.");
        }

        await File.WriteAllBytesAsync(storedPath, bytes, cancellationToken);
        return bytes.LongLength;
    }

    private async Task<long> DownloadUrlAsync(
        string? url,
        string storedPath,
        long maxUploadSizeBytes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidDataException("Document URL is required.");
        }

        using var response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > 0 and var contentLength &&
            contentLength > maxUploadSizeBytes)
        {
            throw new InvalidDataException(
                $"Downloaded document exceeds the configured limit of {maxUploadSizeBytes} bytes.");
        }

        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var targetStream = File.Create(storedPath);

        var buffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;

            if (totalBytes > maxUploadSizeBytes)
            {
                throw new InvalidDataException(
                    $"Downloaded document exceeds the configured limit of {maxUploadSizeBytes} bytes.");
            }

            await targetStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        if (totalBytes == 0)
        {
            throw new InvalidDataException("Downloaded document is empty.");
        }

        return totalBytes;
    }

    private static async Task EnsurePdfAsync(string storedPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(storedPath);
        var header = new byte[PdfSignature.Length];
        var bytesRead = await stream.ReadAsync(header, cancellationToken);

        if (bytesRead != PdfSignature.Length || !header.SequenceEqual(PdfSignature))
        {
            throw new InvalidDataException("Only valid PDF documents are supported in the current version.");
        }
    }

    private string ResolveStorageDirectory(string storageDirectory)
    {
        var resolvedPath = _appDataPaths.ResolveDataPath(storageDirectory, "data/documents");
        Directory.CreateDirectory(resolvedPath);
        return resolvedPath;
    }

    private static string ResolveFileName(PrintDocumentRequest request)
    {
        var candidate = request.Type == DocumentSourceType.Url
            ? TryGetFileNameFromUrl(request.Url)
            : request.FileName;

        var normalized = string.IsNullOrWhiteSpace(candidate)
            ? "document.pdf"
            : Path.GetFileName(candidate.Trim());

        if (!normalized.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"{normalized}.pdf";
        }

        return normalized;
    }

    private static string? TryGetFileNameFromUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return Path.GetFileName(uri.LocalPath);
    }
}
