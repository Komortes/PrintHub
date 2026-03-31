namespace PrintHub.Contracts.PrintJobs;

public sealed record PrintJobDetailsDto(
    string JobId,
    string? PrinterName,
    PrintJobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int Copies,
    string? ErrorMessage,
    string? ParentJobId,
    IReadOnlyCollection<string> RetryJobIds,
    PrintDocumentDetailsDto Document);
