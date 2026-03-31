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

    public static PrintJobDetailsDto ToDetailsDto(
        this PrintJob job,
        IReadOnlyCollection<string> retryJobIds) =>
        new(
            job.Id,
            job.PrinterName,
            job.Status,
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.Copies,
            job.ErrorMessage,
            job.ParentJobId,
            retryJobIds,
            new PrintDocumentDetailsDto(
                job.Document.SourceType,
                job.Document.Format,
                job.Document.FileName,
                job.Document.StoredPath,
                job.Document.SizeBytes,
                job.Document.SourceUrl,
                File.Exists(job.Document.StoredPath)));
}
