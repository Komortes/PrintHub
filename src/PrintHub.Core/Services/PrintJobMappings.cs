using PrintHub.Contracts.PrintJobs;
using PrintHub.Core.Models;

namespace PrintHub.Core.Services;

internal static class PrintJobMappings
{
    public static PrintJobDto ToDto(this PrintJob job) =>
        new(
            job.Id,
            job.PrinterName,
            job.Status,
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.Copies,
            job.Document.Format,
            job.ErrorMessage);
}
