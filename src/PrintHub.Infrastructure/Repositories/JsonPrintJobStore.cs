using System.Text.Json;
using System.Text.Json.Serialization;
using PrintHub.Contracts.PrintJobs;
using PrintHub.Core.Models;
using PrintHub.Core.Repositories;

namespace PrintHub.Infrastructure.Repositories;

public sealed class JsonPrintJobStore : IPrintJobStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _jobsFilePath;
    private Dictionary<string, PrintJob>? _jobs;

    public JsonPrintJobStore(string contentRootPath, string jobsFilePath)
    {
        _jobsFilePath = ResolvePath(contentRootPath, jobsFilePath);
    }

    public async ValueTask AddAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var jobs = await EnsureLoadedAsync(cancellationToken);

            if (!jobs.TryAdd(job.Id, job))
            {
                throw new InvalidOperationException($"A print job with ID '{job.Id}' already exists.");
            }

            await SaveAsync(jobs.Values, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> DeleteAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID is required.", nameof(jobId));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var jobs = await EnsureLoadedAsync(cancellationToken);

            if (!jobs.Remove(jobId.Trim()))
            {
                return false;
            }

            await SaveAsync(jobs.Values, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<PrintJob?> GetAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID is required.", nameof(jobId));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var jobs = await EnsureLoadedAsync(cancellationToken);
            jobs.TryGetValue(jobId.Trim(), out var job);
            return job;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyCollection<PrintJob>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var jobs = await EnsureLoadedAsync(cancellationToken);
            return jobs.Values
                .OrderByDescending(job => job.CreatedAt)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask UpdateAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var jobs = await EnsureLoadedAsync(cancellationToken);
            jobs[job.Id] = job;
            await SaveAsync(jobs.Values, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, PrintJob>> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_jobs is not null)
        {
            return _jobs;
        }

        _jobs = await LoadAsync(cancellationToken);
        return _jobs;
    }

    private async Task<Dictionary<string, PrintJob>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_jobsFilePath))
        {
            return new Dictionary<string, PrintJob>(StringComparer.Ordinal);
        }

        await using var stream = File.OpenRead(_jobsFilePath);

        try
        {
            var jobs = await JsonSerializer.DeserializeAsync<StoredPrintJob[]>(stream, SerializerOptions, cancellationToken)
                ?? [];

            return jobs
                .Select(job => job.ToModel())
                .ToDictionary(job => job.Id, StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Could not deserialize persisted print jobs from '{_jobsFilePath}'.",
                exception);
        }
    }

    private async Task SaveAsync(IEnumerable<PrintJob> jobs, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_jobsFilePath)
            ?? throw new InvalidOperationException("Jobs file path does not have a directory component.");

        Directory.CreateDirectory(directory);

        var payload = jobs
            .OrderByDescending(job => job.CreatedAt)
            .Select(StoredPrintJob.FromModel)
            .ToArray();
        var tempFilePath = $"{_jobsFilePath}.tmp";

        await using (var stream = File.Create(tempFilePath))
        {
            await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken);
        }

        File.Move(tempFilePath, _jobsFilePath, overwrite: true);
    }

    private static string ResolvePath(string contentRootPath, string path) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(contentRootPath, path));

    private sealed record StoredPrintJob(
        string Id,
        string? PrinterName,
        int Copies,
        StoredPrintDocument Document,
        PrintJobStatus Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        string? ErrorMessage,
        string? ParentJobId = null)
    {
        public static StoredPrintJob FromModel(PrintJob job) =>
            new(
                job.Id,
                job.PrinterName,
                job.Copies,
                StoredPrintDocument.FromModel(job.Document),
                job.Status,
                job.CreatedAt,
                job.StartedAt,
                job.CompletedAt,
                job.ErrorMessage,
                job.ParentJobId);

        public PrintJob ToModel() =>
            PrintJob.Restore(
                Id,
                PrinterName,
                Copies,
                Document.ToModel(),
                ParentJobId,
                CreatedAt,
                Status,
                StartedAt,
                CompletedAt,
                ErrorMessage);
    }

    private sealed record StoredPrintDocument(
        DocumentSourceType SourceType,
        PrintDocumentFormat Format,
        string FileName,
        string StoredPath,
        long SizeBytes,
        string? SourceUrl)
    {
        public static StoredPrintDocument FromModel(PrintDocument document) =>
            new(
                document.SourceType,
                document.Format,
                document.FileName,
                document.StoredPath,
                document.SizeBytes,
                document.SourceUrl);

        public PrintDocument ToModel() =>
            PrintDocument.CreateStored(
                SourceType,
                Format,
                FileName,
                StoredPath,
                SizeBytes,
                SourceUrl);
    }
}
