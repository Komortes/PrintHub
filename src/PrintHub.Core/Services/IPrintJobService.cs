using PrintHub.Contracts.PrintJobs;

namespace PrintHub.Core.Services;

public interface IPrintJobService
{
    ValueTask<CreatePrintJobResponse> CreateAsync(
        CreatePrintJobRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PrintJobDto?> GetAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<PrintJobDto>> ListAsync(
        CancellationToken cancellationToken = default);
}
