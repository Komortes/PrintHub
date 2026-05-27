using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using PrintHub.Api.Configuration;
using PrintHub.Contracts.PrintJobs;
using PrintHub.Core.Settings;

namespace PrintHub.Api.Requests;

public static class PrintJobRequestParser
{
    public static void ConfigureFormOptions(IServiceCollection services, IConfiguration configuration)
    {
        var maxMultipartBodySizeBytes = configuration.GetValue<long?>($"{PrintHubApiOptions.SectionName}:MaxMultipartBodySizeBytes")
            ?? PrintHubApiOptions.DefaultMaxMultipartBodySizeBytes;

        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = maxMultipartBodySizeBytes;
        });
    }

    public static async ValueTask<CreatePrintJobRequest> ParseAsync(
        HttpRequest request,
        IPrintHubSettingsService settingsService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settingsService);

        var settings = await settingsService.GetAsync(cancellationToken);

        if (request.HasFormContentType)
        {
            return await ParseMultipartAsync(request, settings, cancellationToken);
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
        var serializerOptions = request.HttpContext.RequestServices
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value
            .SerializerOptions;
        using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        var payload = document.Deserialize<CreatePrintJobRequest>(serializerOptions);

        if (payload is null)
        {
            throw new InvalidOperationException("Request body is required.");
        }

        var hasCopies = HasProperty(document.RootElement, "copies");

        return hasCopies
            ? payload
            : payload with { Copies = 1 };
    }

    private static async ValueTask<CreatePrintJobRequest> ParseMultipartAsync(
        HttpRequest request,
        PrintHubSettings settings,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files["file"];

        if (file is null || file.Length <= 0)
        {
            throw new InvalidDataException("Multipart requests require a non-empty 'file' field.");
        }

        if (file.Length > settings.MaxUploadSizeBytes)
        {
            throw new InvalidDataException(
                $"Uploaded file exceeds the configured limit of {settings.MaxUploadSizeBytes} bytes.");
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
        var orientationOverride = ParseOrientationOverride(
            GetSingleValue(form, "orientationOverride"));

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
                FileName: file.FileName)
            {
                OrientationOverride = orientationOverride
            });
    }

    private static bool IsJsonRequest(HttpRequest request) =>
        !string.IsNullOrWhiteSpace(request.ContentType)
        && request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);

    private static string? GetSingleValue(IFormCollection form, string key) =>
        form.TryGetValue(key, out var value) ? value.ToString() : null;

    private static bool IsPdf(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static PrintDocumentOrientationOverride ParseOrientationOverride(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PrintDocumentOrientationOverride.Auto;
        }

        return Enum.TryParse<PrintDocumentOrientationOverride>(value, ignoreCase: true, out var parsedValue)
            && Enum.IsDefined(parsedValue)
                ? parsedValue
                : throw new InvalidDataException(
                    "Form field 'orientationOverride' must be one of: auto, portrait, landscape.");
    }

    private static bool HasProperty(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
