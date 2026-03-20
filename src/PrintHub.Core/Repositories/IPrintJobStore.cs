using PrintHub.Core.Models;

namespace PrintHub.Core.Repositories;

public interface IPrintJobStore
{
    ValueTask AddAsync(PrintJob job, CancellationToken cancellationToken = default);

    ValueTask<PrintJob?> GetAsync(string jobId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<PrintJob>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask UpdateAsync(PrintJob job, CancellationToken cancellationToken = default);
}
