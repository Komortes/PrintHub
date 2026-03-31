namespace PrintHub.Core.Backends;

public sealed record PrintBackendDiagnostics(
    string Backend,
    bool IsSupported,
    string Summary,
    IReadOnlyCollection<PrintBackendDiagnosticCheck> Checks,
    IReadOnlyCollection<PrinterDiagnosticInfo> Printers,
    IReadOnlyCollection<string> Recommendations);
