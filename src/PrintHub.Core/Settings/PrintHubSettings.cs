using PrintHub.Contracts.Settings;

namespace PrintHub.Core.Settings;

public sealed record PrintHubSettings(
    string ServiceName,
    int Port,
    string ApiKeyHeaderName,
    string? ApiKey,
    string? DefaultPrinterName,
    string StorageDirectory,
    long MaxUploadSizeBytes)
{
    public static PrintHubSettings CreateDefaults(
        string serviceName,
        int port,
        string apiKeyHeaderName,
        string? apiKey,
        string? defaultPrinterName,
        string storageDirectory,
        long maxUploadSizeBytes) =>
        new(
            NormalizeRequired(serviceName, nameof(serviceName)),
            ValidatePort(port),
            NormalizeRequired(apiKeyHeaderName, nameof(apiKeyHeaderName)),
            NormalizeOptional(apiKey),
            NormalizeOptional(defaultPrinterName),
            NormalizeRequired(storageDirectory, nameof(storageDirectory)),
            ValidateMaxUploadSize(maxUploadSizeBytes));

    public static PrintHubSettings FromRequest(UpdatePrintHubSettingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PrintHubSettings(
            NormalizeRequired(request.ServiceName, nameof(request.ServiceName)),
            ValidatePort(request.Port),
            NormalizeRequired(request.ApiKeyHeaderName, nameof(request.ApiKeyHeaderName)),
            NormalizeRequired(request.ApiKey, nameof(request.ApiKey)),
            NormalizeOptional(request.DefaultPrinterName),
            NormalizeRequired(request.StorageDirectory, nameof(request.StorageDirectory)),
            ValidateMaxUploadSize(request.MaxUploadSizeBytes));
    }

    private static string NormalizeRequired(string? value, string paramName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", paramName)
            : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ValidatePort(int port) =>
        port is < 1 or > 65535
            ? throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.")
            : port;

    private static long ValidateMaxUploadSize(long maxUploadSizeBytes) =>
        maxUploadSizeBytes < 1
            ? throw new ArgumentOutOfRangeException(
                nameof(maxUploadSizeBytes),
                "Max upload size must be greater than 0.")
            : maxUploadSizeBytes;
}
