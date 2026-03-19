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

    public PrintJobService(
        TimeProvider timeProvider,
        IPrintDocumentPipeline documentPipeline,
        IPrintHubSettingsService settingsService,
        IPrintJobStore store,
        IPrintJobQueue queue)
    {
        _timeProvider = timeProvider;
        _documentPipeline = documentPipeline;
        _settingsService = settingsService;
        _store = store;
        _queue = queue;
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

    public async ValueTask<IReadOnlyCollection<PrintJobDto>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var jobs = await _store.ListAsync(cancellationToken);
        return jobs.Select(job => job.ToDto()).ToArray();
    }
}
