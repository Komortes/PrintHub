namespace PrintHub.Core.Queues;

public interface IPrintJobQueue
{
    int Count { get; }

    ValueTask EnqueueAsync(string jobId, CancellationToken cancellationToken = default);

    ValueTask<string> DequeueAsync(CancellationToken cancellationToken = default);
}
