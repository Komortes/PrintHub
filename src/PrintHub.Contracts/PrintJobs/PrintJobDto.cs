namespace PrintHub.Contracts.PrintJobs;

public sealed record PrintJobDto(
    string JobId,
    string? PrinterName,
    PrintJobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int Copies,
    PrintDocumentFormat Format,
    string? ErrorMessage);
