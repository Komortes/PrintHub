using PrintHub.Contracts.PrintJobs;
using PrintHub.Core.Models;

namespace PrintHub.Core.Documents;

public interface IPrintDocumentPipeline
{
    ValueTask<PrintDocument> PrepareAsync(
        string jobId,
        PrintDocumentRequest request,
        CancellationToken cancellationToken = default);
}
