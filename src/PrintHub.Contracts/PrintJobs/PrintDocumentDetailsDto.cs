namespace PrintHub.Contracts.PrintJobs;

public sealed record PrintDocumentDetailsDto(
    DocumentSourceType SourceType,
    PrintDocumentFormat Format,
    string FileName,
    string StoredPath,
    long SizeBytes,
    string? SourceUrl,
    bool Exists);
