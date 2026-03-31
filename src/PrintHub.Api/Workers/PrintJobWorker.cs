using PrintHub.Core.Backends;
using PrintHub.Core.Documents;
using PrintHub.Contracts.PrintJobs;
using PrintHub.Core.Queues;
using PrintHub.Core.Repositories;

namespace PrintHub.Api.Workers;

public sealed class PrintJobWorker : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _timeProvider;
    private readonly IPrintJobQueue _queue;
    private readonly IPrintJobStore _store;
    private readonly IPrintBackend _backend;
    private readonly IPrintDocumentGarbageCollector _documentGarbageCollector;
    private readonly ILogger<PrintJobWorker> _logger;

    public PrintJobWorker(
        TimeProvider timeProvider,
        IPrintJobQueue queue,
        IPrintJobStore store,
        IPrintBackend backend,
        IPrintDocumentGarbageCollector documentGarbageCollector,
        ILogger<PrintJobWorker> logger)
    {
        _timeProvider = timeProvider;
        _queue = queue;
        _store = store;
        _backend = backend;
        _documentGarbageCollector = documentGarbageCollector;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Print job worker started.");
        await RecoverJobsAndCollectDocumentsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Unhandled error in print job worker.");
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }

        _logger.LogInformation("Print job worker stopped.");
    }

    private async Task RecoverJobsAndCollectDocumentsAsync(CancellationToken cancellationToken)
    {
        var jobs = await _store.ListAsync(cancellationToken);
        var recoveredJobs = 0;

        foreach (var job in jobs
                     .Where(job => job.Status is PrintJobStatus.Pending or PrintJobStatus.Processing)
                     .OrderBy(job => job.CreatedAt))
        {
            var wasProcessing = job.Status == PrintJobStatus.Processing;

            if (!job.TryRestorePendingAfterRecovery())
            {
                continue;
            }

            if (wasProcessing)
            {
                await _store.UpdateAsync(job, cancellationToken);
            }

            await _queue.EnqueueAsync(job.Id, cancellationToken);
            recoveredJobs++;
        }

        if (recoveredJobs > 0)
        {
            _logger.LogInformation("Recovered {RecoveredJobsCount} print job(s) into the queue after startup.", recoveredJobs);
        }

        var gcResult = await _documentGarbageCollector.CollectAsync(cancellationToken);
        if (gcResult.DeletedFilesCount > 0)
        {
            _logger.LogInformation(
                "Deleted {DeletedFilesCount} orphaned document file(s) and reclaimed {ReclaimedBytes} bytes.",
                gcResult.DeletedFilesCount,
                gcResult.ReclaimedBytes);
        }
    }

    private async Task ProcessNextJobAsync(CancellationToken cancellationToken)
    {
        var jobId = await _queue.DequeueAsync(cancellationToken);
        var job = await _store.GetAsync(jobId, cancellationToken);

        if (job is null)
        {
            _logger.LogWarning("Dequeued job {JobId}, but it was not found in storage.", jobId);
            return;
        }

        if (!job.TryMarkProcessing(_timeProvider.GetUtcNow()))
        {
            _logger.LogWarning(
                "Skipped job {JobId} because it is already in status {Status}.",
                job.Id,
                job.Status);
            return;
        }

        await _store.UpdateAsync(job, cancellationToken);
        _logger.LogInformation(
            "Started printing job {JobId} on printer {PrinterName}.",
            job.Id,
            job.PrinterName ?? "default");

        try
        {
            await _backend.PrintAsync(job, cancellationToken);

            if (!job.TryMarkCompleted(_timeProvider.GetUtcNow()))
            {
                _logger.LogWarning("Could not mark job {JobId} as completed from status {Status}.", job.Id, job.Status);
                return;
            }

            await _store.UpdateAsync(job, cancellationToken);
            _logger.LogInformation("Completed printing job {JobId}.", job.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            job.TryMarkFailed(_timeProvider.GetUtcNow(), exception.Message);
            await _store.UpdateAsync(job, cancellationToken);
            _logger.LogError(exception, "Print job {JobId} failed.", job.Id);
        }
    }
}
