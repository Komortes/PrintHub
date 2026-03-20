using PrintHub.Core.Models;

namespace PrintHub.Core.Backends;

public interface IPrintBackend
{
    ValueTask<IReadOnlyCollection<PrinterInfo>> GetPrintersAsync(
        CancellationToken cancellationToken = default);

    ValueTask PrintAsync(
        PrintJob job,
        CancellationToken cancellationToken = default);
}
