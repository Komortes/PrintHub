using PrintHub.Contracts.Settings;

namespace PrintHub.Core.Settings;

public interface IPrintHubSettingsService
{
    ValueTask<PrintHubSettings> GetAsync(CancellationToken cancellationToken = default);

    ValueTask<PrintHubSettings> UpdateAsync(
        UpdatePrintHubSettingsRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PrintHubSettings> AddPrinterAsync(
        string id,
        string name,
        CancellationToken cancellationToken = default);

    ValueTask<PrintHubSettings> RemovePrinterAsync(
        string id,
        CancellationToken cancellationToken = default);
}
