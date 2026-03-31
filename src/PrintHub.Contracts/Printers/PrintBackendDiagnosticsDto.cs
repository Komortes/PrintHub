namespace PrintHub.Contracts.Printers;

public sealed record PrintBackendDiagnosticsDto(
    string Backend,
    bool IsSupported,
    string Summary,
    IReadOnlyCollection<PrintBackendDiagnosticCheckDto> Checks,
    IReadOnlyCollection<PrinterDiagnosticDto> Printers,
    IReadOnlyCollection<string> Recommendations);
