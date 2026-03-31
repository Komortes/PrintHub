using PrintHub.Contracts.Printers;

namespace PrintHub.Core.Backends;

public sealed record PrinterDiagnosticInfo(
    string Id,
    string Name,
    bool IsDefault,
    PrinterStatus Status,
    string? Detail);
