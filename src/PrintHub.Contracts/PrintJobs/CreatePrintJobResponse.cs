namespace PrintHub.Contracts.PrintJobs;

public sealed record CreatePrintJobResponse(
    string JobId,
    PrintJobStatus Status);
