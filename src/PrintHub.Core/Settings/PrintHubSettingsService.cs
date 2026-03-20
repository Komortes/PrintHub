using PrintHub.Contracts.Settings;

namespace PrintHub.Core.Settings;

public sealed class PrintHubSettingsService : IPrintHubSettingsService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPrintHubSettingsStore _store;
    private readonly PrintHubSettings _defaults;

    private PrintHubSettings? _currentSettings;

    public PrintHubSettingsService(IPrintHubSettingsStore store, PrintHubSettings defaults)
    {
        _store = store;
        _defaults = defaults;
    }

    public async ValueTask<PrintHubSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_currentSettings is not null)
        {
            return _currentSettings;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_currentSettings is not null)
            {
                return _currentSettings;
            }

            _currentSettings = await _store.LoadAsync(cancellationToken) ?? _defaults;
            await _store.SaveAsync(_currentSettings, cancellationToken);
            return _currentSettings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<PrintHubSettings> UpdateAsync(
        UpdatePrintHubSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var updatedSettings = PrintHubSettings.FromRequest(request);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await _store.SaveAsync(updatedSettings, cancellationToken);
            _currentSettings = updatedSettings;
            return _currentSettings;
        }
        finally
        {
            _gate.Release();
        }
    }
}
