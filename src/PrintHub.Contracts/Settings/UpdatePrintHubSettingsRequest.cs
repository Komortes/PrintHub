namespace PrintHub.Contracts.Settings;

public sealed record UpdatePrintHubSettingsRequest(
    string ServiceName,
    int Port,
    string ApiKeyHeaderName,
    string? ApiKey,
    string StorageDirectory,
    long MaxUploadSizeBytes);
