namespace PrintHub.Contracts.PrintJobs;

public sealed record PrintQueueStatusDto(bool IsPaused, int QueuedCount);
