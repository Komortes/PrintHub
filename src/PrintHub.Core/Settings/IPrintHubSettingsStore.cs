namespace PrintHub.Core.Settings;

public interface IPrintHubSettingsStore
{
    ValueTask<PrintHubSettings?> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(PrintHubSettings settings, CancellationToken cancellationToken = default);
}
