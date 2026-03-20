namespace PrintHub.Contracts.PrintJobs;

public sealed record PrintDocumentRequest(
    DocumentSourceType Type,
    PrintDocumentFormat Format,
    string? Url,
    string? Data,
    string? FileName);
