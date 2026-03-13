namespace PrintHub.Contracts.PrintJobs;

public enum PrintJobStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Canceled = 4
}
