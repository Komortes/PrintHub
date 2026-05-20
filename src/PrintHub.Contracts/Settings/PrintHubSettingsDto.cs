namespace PrintHub.Contracts.Settings;

public sealed record PrintHubSettingsDto(
    string ServiceName,
    int Port,
    string BindHost,
    string ApiKeyHeaderName,
    string? ApiKey,
    string? DefaultPrinterName,
    string StorageDirectory,
    long MaxUploadSizeBytes);
