namespace PrintHub.Contracts.Printers;

public sealed record PrinterDto(
    string Id,
    string Name,
    bool IsDefault,
    PrinterStatus Status);
