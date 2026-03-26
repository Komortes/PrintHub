namespace PrintHub.Core.Queues;

public sealed class InMemoryPrintJobQueue : IPrintJobQueue
{
    private readonly object _sync = new();
    private readonly Queue<string> _jobs = new();
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private TaskCompletionSource<bool> _resumeSignal = CreateCompletedSignal();
    private int _count;
    private bool _isPaused;

    public int Count => Volatile.Read(ref _count);

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _isPaused;
            }
        }
    }

    public ValueTask EnqueueAsync(string jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID is required.", nameof(jobId));
        }

        lock (_sync)
        {
            _jobs.Enqueue(jobId.Trim());
            _count = _jobs.Count;
        }

        _signal.Release();
        return ValueTask.CompletedTask;
    }

    public async ValueTask<string> DequeueAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            await WaitUntilResumedAsync(cancellationToken);
            await _signal.WaitAsync(cancellationToken);

            Task waitForResumeTask;

            lock (_sync)
            {
                if (_isPaused)
                {
                    if (_jobs.Count > 0)
                    {
                        _signal.Release();
                    }

                    waitForResumeTask = _resumeSignal.Task;
                }
                else if (_jobs.Count > 0)
                {
                    var jobId = _jobs.Dequeue();
                    _count = _jobs.Count;
                    return jobId;
                }
                else
                {
                    waitForResumeTask = Task.CompletedTask;
                }
            }

            await waitForResumeTask.WaitAsync(cancellationToken);
        }
    }

    public ValueTask<IReadOnlyCollection<string>> DrainAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyCollection<string> drainedJobs;

        lock (_sync)
        {
            drainedJobs = _jobs.ToArray();
            _jobs.Clear();
            _count = 0;
        }

        while (_signal.Wait(0))
        {
        }

        return ValueTask.FromResult(drainedJobs);
    }

    public ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_isPaused)
            {
                return ValueTask.CompletedTask;
            }

            _isPaused = true;
            _resumeSignal = CreatePendingSignal();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<bool>? signalToRelease = null;

        lock (_sync)
        {
            if (!_isPaused)
            {
                return ValueTask.CompletedTask;
            }

            _isPaused = false;
            signalToRelease = _resumeSignal;
            _resumeSignal = CreateCompletedSignal();
        }

        signalToRelease.TrySetResult(true);
        return ValueTask.CompletedTask;
    }

    private Task WaitUntilResumedAsync(CancellationToken cancellationToken)
    {
        Task resumeTask;

        lock (_sync)
        {
            resumeTask = _isPaused
                ? _resumeSignal.Task
                : Task.CompletedTask;
        }

        return resumeTask.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource<bool> CreatePendingSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<bool> CreateCompletedSignal()
    {
        var signal = CreatePendingSignal();
        signal.TrySetResult(true);
        return signal;
    }
}
