using PrintHub.Contracts.Settings;
using PrintHub.Core.Models;

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
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _currentSettings ??= await _store.LoadAsync(cancellationToken) ?? _defaults;
            // Preserve the printer registry when updating general settings
            var updated = PrintHubSettings.FromRequest(request) with
            {
                Printers = _currentSettings.Printers
            };
            await _store.SaveAsync(updated, cancellationToken);
            _currentSettings = updated;
            return _currentSettings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<PrintHubSettings> AddPrinterAsync(
        string id,
        string name,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _currentSettings ??= await _store.LoadAsync(cancellationToken) ?? _defaults;

            if (_currentSettings.Printers.Any(p =>
                    string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                return _currentSettings; // Already registered
            }

            var printer = new RegisteredPrinter(id, name, DateTimeOffset.UtcNow);
            var updated = _currentSettings with
            {
                Printers = [.. _currentSettings.Printers, printer]
            };
            await _store.SaveAsync(updated, cancellationToken);
            _currentSettings = updated;
            return _currentSettings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<PrintHubSettings> RemovePrinterAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _currentSettings ??= await _store.LoadAsync(cancellationToken) ?? _defaults;

            var remaining = _currentSettings.Printers
                .Where(p => !string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (remaining.Length == _currentSettings.Printers.Count)
            {
                return _currentSettings; // Printer not in registry — no-op
            }

            // If the removed printer was the default, clear DefaultPrinterName
            var wasDefault = _currentSettings.Printers
                .Where(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                .Any(p => string.Equals(p.Name, _currentSettings.DefaultPrinterName,
                              StringComparison.OrdinalIgnoreCase)
                          || string.Equals(p.Id, _currentSettings.DefaultPrinterName,
                              StringComparison.OrdinalIgnoreCase));

            var updated = _currentSettings with
            {
                Printers = remaining,
                DefaultPrinterName = wasDefault ? null : _currentSettings.DefaultPrinterName
            };
            await _store.SaveAsync(updated, cancellationToken);
            _currentSettings = updated;
            return _currentSettings;
        }
        finally
        {
            _gate.Release();
        }
    }
}
