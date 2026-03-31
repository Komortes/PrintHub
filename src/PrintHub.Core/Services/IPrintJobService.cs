using PrintHub.Contracts.PrintJobs;

namespace PrintHub.Core.Services;

public interface IPrintJobService
{
    ValueTask<CreatePrintJobResponse> CreateAsync(
        CreatePrintJobRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PrintJobDto?> GetAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    ValueTask<PrintJobDetailsDto?> GetDetailsAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<PrintJobDto>> ListAsync(
        CancellationToken cancellationToken = default);

    ValueTask<PrintQueueStatusDto> GetQueueStatusAsync(
        CancellationToken cancellationToken = default);

    ValueTask<PrintQueueStatusDto> PauseQueueAsync(
        CancellationToken cancellationToken = default);

    ValueTask<PrintQueueStatusDto> ResumeQueueAsync(
        CancellationToken cancellationToken = default);

    ValueTask<ClearPrintQueueResponse> ClearQueueAsync(
        CancellationToken cancellationToken = default);

    ValueTask<PrintJobDto?> DeleteAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    ValueTask<CleanupPrintJobsResponse> CleanupAsync(
        CancellationToken cancellationToken = default);

    ValueTask<PrintJobDto?> CancelAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    ValueTask<CreatePrintJobResponse?> RetryAsync(
        string jobId,
        CancellationToken cancellationToken = default);
}
