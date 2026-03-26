using System.Collections.Concurrent;
using PrintHub.Core.Models;

namespace PrintHub.Core.Repositories;

public sealed class InMemoryPrintJobStore : IPrintJobStore
{
    private readonly ConcurrentDictionary<string, PrintJob> _jobs = new(StringComparer.Ordinal);

    public ValueTask AddAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!_jobs.TryAdd(job.Id, job))
        {
            throw new InvalidOperationException($"A print job with ID '{job.Id}' already exists.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> DeleteAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID is required.", nameof(jobId));
        }

        var deleted = _jobs.TryRemove(jobId.Trim(), out _);
        return ValueTask.FromResult(deleted);
    }

    public ValueTask<PrintJob?> GetAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID is required.", nameof(jobId));
        }

        _jobs.TryGetValue(jobId.Trim(), out var job);
        return ValueTask.FromResult(job);
    }

    public ValueTask<IReadOnlyCollection<PrintJob>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<PrintJob> jobs = _jobs.Values
            .OrderByDescending(job => job.CreatedAt)
            .ToArray();

        return ValueTask.FromResult(jobs);
    }

    public ValueTask UpdateAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        _jobs[job.Id] = job;
        return ValueTask.CompletedTask;
    }
}
