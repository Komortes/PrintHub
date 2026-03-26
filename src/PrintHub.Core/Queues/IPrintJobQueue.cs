namespace PrintHub.Core.Queues;

public interface IPrintJobQueue
{
    int Count { get; }

    bool IsPaused { get; }

    ValueTask EnqueueAsync(string jobId, CancellationToken cancellationToken = default);

    ValueTask<string> DequeueAsync(CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<string>> DrainAsync(CancellationToken cancellationToken = default);

    ValueTask PauseAsync(CancellationToken cancellationToken = default);

    ValueTask ResumeAsync(CancellationToken cancellationToken = default);
}
