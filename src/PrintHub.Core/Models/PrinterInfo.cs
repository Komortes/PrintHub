using PrintHub.Contracts.Printers;

namespace PrintHub.Core.Models;

public sealed record PrinterInfo(
    string Id,
    string Name,
    bool IsDefault,
    PrinterStatus Status);
