namespace PrintHub.Contracts.Printers;

public sealed record PrintBackendDiagnosticCheckDto(
    string Code,
    PrintBackendDiagnosticSeverity Severity,
    string Title,
    string Message,
    string? Value);
