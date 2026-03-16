using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using PrintHub.Api.Configuration;
using PrintHub.Contracts.PrintJobs;

namespace PrintHub.Api.Requests;

public static class PrintJobRequestParser
{
    public static void ConfigureFormOptions(IServiceCollection services, IConfiguration configuration)
    {
        var maxUploadSizeBytes = configuration.GetValue<long?>($"{PrintHubApiOptions.SectionName}:MaxUploadSizeBytes")
            ?? PrintHubApiOptions.DefaultMaxUploadSizeBytes;

        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = maxUploadSizeBytes;
        });
    }

    public static async ValueTask<CreatePrintJobRequest> ParseAsync(
        HttpRequest request,
        IOptions<PrintHubApiOptions> options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (request.HasFormContentType)
        {
            return await ParseMultipartAsync(request, options.Value, cancellationToken);
        }

        if (IsJsonRequest(request))
        {
            return await ParseJsonAsync(request, cancellationToken);
        }

        throw new InvalidOperationException("Unsupported content type. Use application/json or multipart/form-data.");
    }

    private static async ValueTask<CreatePrintJobRequest> ParseJsonAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<CreatePrintJobRequest>(cancellationToken: cancellationToken);

        return payload ?? throw new InvalidOperationException("Request body is required.");
    }

    private static async ValueTask<CreatePrintJobRequest> ParseMultipartAsync(
        HttpRequest request,
        PrintHubApiOptions options,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files["file"];

        if (file is null || file.Length <= 0)
        {
            throw new InvalidDataException("Multipart requests require a non-empty 'file' field.");
        }

        if (file.Length > options.MaxUploadSizeBytes)
        {
            throw new InvalidDataException(
                $"Uploaded file exceeds the configured limit of {options.MaxUploadSizeBytes} bytes.");
        }

        if (!IsPdf(file.FileName))
        {
            throw new InvalidDataException("Only PDF documents are supported in the current version.");
        }

        var copiesRaw = GetSingleValue(form, "copies");
        var copies = string.IsNullOrWhiteSpace(copiesRaw)
            ? 1
            : int.TryParse(copiesRaw, out var parsedCopies)
                ? parsedCopies
                : throw new InvalidDataException("Form field 'copies' must be a valid integer.");

        await using var stream = file.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);

        return new CreatePrintJobRequest(
            GetSingleValue(form, "printerName"),
            copies,
            new PrintDocumentRequest(
                DocumentSourceType.Upload,
                PrintDocumentFormat.Pdf,
                Url: null,
                Data: Convert.ToBase64String(memoryStream.ToArray()),
                FileName: file.FileName));
    }

    private static bool IsJsonRequest(HttpRequest request) =>
        !string.IsNullOrWhiteSpace(request.ContentType)
        && request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);

    private static string? GetSingleValue(IFormCollection form, string key) =>
        form.TryGetValue(key, out var value) ? value.ToString() : null;

    private static bool IsPdf(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
}
