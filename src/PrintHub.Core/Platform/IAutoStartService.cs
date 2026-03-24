namespace PrintHub.Core.Platform;

public interface IAutoStartService
{
    ValueTask<AutoStartRegistration> GetStatusAsync(CancellationToken cancellationToken = default);

    ValueTask<AutoStartRegistration> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
