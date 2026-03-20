using System.Threading.Channels;

namespace PrintHub.Core.Queues;

public sealed class InMemoryPrintJobQueue : IPrintJobQueue
{
    private readonly Channel<string> _channel;
    private int _count;

    public InMemoryPrintJobQueue()
    {
        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    public int Count => Volatile.Read(ref _count);

    public async ValueTask EnqueueAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID is required.", nameof(jobId));
        }

        Interlocked.Increment(ref _count);

        try
        {
            await _channel.Writer.WriteAsync(jobId.Trim(), cancellationToken);
        }
        catch
        {
            Interlocked.Decrement(ref _count);
            throw;
        }
    }

    public async ValueTask<string> DequeueAsync(CancellationToken cancellationToken = default)
    {
        var jobId = await _channel.Reader.ReadAsync(cancellationToken);
        Interlocked.Decrement(ref _count);
        return jobId;
    }
}
