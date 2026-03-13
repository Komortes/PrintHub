namespace PrintHub.Contracts.PrintJobs;

public sealed record CreatePrintJobRequest(
    string? PrinterName,
    int Copies,
    PrintDocumentRequest Document);
