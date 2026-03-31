namespace PrintHub.Core.Documents;

public interface IPrintDocumentGarbageCollector
{
    ValueTask<PrintDocumentGarbageCollectionResult> CollectAsync(
        CancellationToken cancellationToken = default);
}

public sealed record PrintDocumentGarbageCollectionResult(int DeletedFilesCount, long ReclaimedBytes);
