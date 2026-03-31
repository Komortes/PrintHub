using PrintHub.Contracts.PrintJobs;
using PrintHub.Core.Documents;
using PrintHub.Core.Models;
using PrintHub.Core.Queues;
using PrintHub.Core.Repositories;
using PrintHub.Core.Settings;

namespace PrintHub.Core.Services;

public sealed class PrintJobService : IPrintJobService
{
    private readonly TimeProvider _timeProvider;
    private readonly IPrintDocumentPipeline _documentPipeline;
    private readonly IPrintHubSettingsService _settingsService;
    private readonly IPrintJobStore _store;
    private readonly IPrintJobQueue _queue;
    private readonly IPrintDocumentGarbageCollector _documentGarbageCollector;

    public PrintJobService(
        TimeProvider timeProvider,
        IPrintDocumentPipeline documentPipeline,
        IPrintHubSettingsService settingsService,
        IPrintJobStore store,
        IPrintJobQueue queue,
        IPrintDocumentGarbageCollector documentGarbageCollector)
    {
        _timeProvider = timeProvider;
        _documentPipeline = documentPipeline;
        _settingsService = settingsService;
        _store = store;
        _queue = queue;
        _documentGarbageCollector = documentGarbageCollector;
    }

    public async ValueTask<CreatePrintJobResponse> CreateAsync(
        CreatePrintJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Copies < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Copies must be at least 1.");
        }

        var jobId = Guid.NewGuid().ToString("n");
        var document = await _documentPipeline.PrepareAsync(jobId, request.Document, cancellationToken);
        var settings = await _settingsService.GetAsync(cancellationToken);
        var printerName = string.IsNullOrWhiteSpace(request.PrinterName)
            ? settings.DefaultPrinterName
            : request.PrinterName;
        var job = PrintJob.Create(
            jobId,
            printerName,
            request.Copies,
            document,
            _timeProvider.GetUtcNow());

        await _store.AddAsync(job, cancellationToken);
        await _queue.EnqueueAsync(job.Id, cancellationToken);

        return new CreatePrintJobResponse(job.Id, job.Status);
    }

    public async ValueTask<PrintJobDto?> GetAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _store.GetAsync(jobId, cancellationToken);
        return job?.ToDto();
    }

    public async ValueTask<PrintJobDetailsDto?> GetDetailsAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _store.GetAsync(jobId, cancellationToken);

        if (job is null)
        {
            return null;
        }

        var jobs = await _store.ListAsync(cancellationToken);
        var retryJobIds = jobs
            .Where(candidate => string.Equals(candidate.ParentJobId, job.Id, StringComparison.Ordinal))
            .OrderBy(candidate => candidate.CreatedAt)
            .Select(candidate => candidate.Id)
            .ToArray();

        return job.ToDetailsDto(retryJobIds);
    }

    public async ValueTask<IReadOnlyCollection<PrintJobDto>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var jobs = await _store.ListAsync(cancellationToken);
        return jobs.Select(job => job.ToDto()).ToArray();
    }

    public ValueTask<PrintQueueStatusDto> GetQueueStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new PrintQueueStatusDto(_queue.IsPaused, _queue.Count));
    }

    public async ValueTask<PrintQueueStatusDto> PauseQueueAsync(
        CancellationToken cancellationToken = default)
    {
        await _queue.PauseAsync(cancellationToken);
        return new PrintQueueStatusDto(_queue.IsPaused, _queue.Count);
    }

    public async ValueTask<PrintQueueStatusDto> ResumeQueueAsync(
        CancellationToken cancellationToken = default)
    {
        await _queue.ResumeAsync(cancellationToken);
        return new PrintQueueStatusDto(_queue.IsPaused, _queue.Count);
    }

    public async ValueTask<ClearPrintQueueResponse> ClearQueueAsync(
        CancellationToken cancellationToken = default)
    {
        var wasPaused = _queue.IsPaused;
        await _queue.PauseAsync(cancellationToken);

        try
        {
            var queuedJobIds = await _queue.DrainAsync(cancellationToken);
            var canceledCount = 0;

            foreach (var jobId in queuedJobIds.Distinct(StringComparer.Ordinal))
            {
                var job = await _store.GetAsync(jobId, cancellationToken);

                if (job is null || job.Status != PrintJobStatus.Pending)
                {
                    continue;
                }

                if (!job.TryMarkCanceled(_timeProvider.GetUtcNow(), "Cleared from queue by user."))
                {
                    continue;
                }

                await _store.UpdateAsync(job, cancellationToken);
                canceledCount++;
            }

            return new ClearPrintQueueResponse(canceledCount);
        }
        finally
        {
            if (!wasPaused)
            {
                await _queue.ResumeAsync(cancellationToken);
            }
        }
    }

    public async ValueTask<PrintJobDto?> DeleteAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _store.GetAsync(jobId, cancellationToken);

        if (job is null)
        {
            return null;
        }

        if (!IsFinished(job.Status))
        {
            throw new InvalidOperationException(
                $"Print job '{job.Id}' cannot be deleted from status '{job.Status}'. Only completed, failed, or canceled jobs can be deleted.");
        }

        var deleted = await _store.DeleteAsync(job.Id, cancellationToken);
        if (deleted)
        {
            await _documentGarbageCollector.CollectAsync(cancellationToken);
        }

        return deleted ? job.ToDto() : null;
    }

    public async ValueTask<CleanupPrintJobsResponse> CleanupAsync(
        CancellationToken cancellationToken = default)
    {
        var jobs = await _store.ListAsync(cancellationToken);
        var deletedCount = 0;

        foreach (var job in jobs.Where(job => IsFinished(job.Status)))
        {
            if (await _store.DeleteAsync(job.Id, cancellationToken))
            {
                deletedCount++;
            }
        }

        if (deletedCount > 0)
        {
            await _documentGarbageCollector.CollectAsync(cancellationToken);
        }

        return new CleanupPrintJobsResponse(deletedCount);
    }

    public async ValueTask<PrintJobDto?> CancelAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _store.GetAsync(jobId, cancellationToken);

        if (job is null)
        {
            return null;
        }

        if (job.Status != PrintJobStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Print job '{job.Id}' cannot be canceled from status '{job.Status}'. Only pending jobs can be canceled.");
        }

        if (!job.TryMarkCanceled(_timeProvider.GetUtcNow(), "Canceled by user."))
        {
            throw new InvalidOperationException($"Print job '{job.Id}' could not be canceled.");
        }

        await _store.UpdateAsync(job, cancellationToken);
        return job.ToDto();
    }

    public async ValueTask<CreatePrintJobResponse?> RetryAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _store.GetAsync(jobId, cancellationToken);

        if (job is null)
        {
            return null;
        }

        if (job.Status is PrintJobStatus.Pending or PrintJobStatus.Processing)
        {
            throw new InvalidOperationException(
                $"Print job '{job.Id}' cannot be retried from status '{job.Status}'. Wait until it finishes or cancel it first.");
        }

        if (!File.Exists(job.Document.StoredPath))
        {
            throw new InvalidOperationException(
                $"Prepared document file '{job.Document.StoredPath}' is no longer available for retry.");
        }

        var retryJob = PrintJob.Create(
            Guid.NewGuid().ToString("n"),
            job.PrinterName,
            job.Copies,
            job.Document,
            _timeProvider.GetUtcNow(),
            job.Id);

        await _store.AddAsync(retryJob, cancellationToken);
        await _queue.EnqueueAsync(retryJob.Id, cancellationToken);

        return new CreatePrintJobResponse(retryJob.Id, retryJob.Status);
    }

    private static bool IsFinished(PrintJobStatus status) =>
        status is PrintJobStatus.Completed or PrintJobStatus.Failed or PrintJobStatus.Canceled;
}
