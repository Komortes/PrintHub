namespace PrintHub.Contracts.Printers;

public sealed record PrinterDiagnosticDto(
    string Id,
    string Name,
    bool IsDefault,
    PrinterStatus Status,
    string? Detail);
