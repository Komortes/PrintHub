using PrintHub.Core.Services;

namespace PrintHub.Api.Workers;

public sealed class CleanupWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CleanupWorker> _logger;

    public CleanupWorker(IServiceScopeFactory scopeFactory, ILogger<CleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IPrintJobService>();
                var result = await service.CleanupAsync(stoppingToken);
                if (result.DeletedCount > 0)
                {
                    _logger.LogInformation(
                        "Auto-cleanup deleted {DeletedCount} finished print job(s).",
                        result.DeletedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-cleanup failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
