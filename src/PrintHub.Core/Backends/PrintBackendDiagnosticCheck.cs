namespace PrintHub.Core.Backends;

public sealed record PrintBackendDiagnosticCheck(
    string Code,
    PrintBackendDiagnosticSeverity Severity,
    string Title,
    string Message,
    string? Value);
