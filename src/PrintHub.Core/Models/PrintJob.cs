using PrintHub.Contracts.PrintJobs;

namespace PrintHub.Core.Models;

public sealed class PrintJob
{
    private PrintJob(
        string id,
        string? printerName,
        int copies,
        PrintDocument document,
        DateTimeOffset createdAt)
    {
        Id = id;
        PrinterName = printerName;
        Copies = copies;
        Document = document;
        CreatedAt = createdAt;
        Status = PrintJobStatus.Pending;
    }

    public string Id { get; }

    public string? PrinterName { get; }

    public int Copies { get; }

    public PrintDocument Document { get; }

    public PrintJobStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? ErrorMessage { get; private set; }

    public static PrintJob Create(
        string id,
        string? printerName,
        int copies,
        PrintDocument document,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Job ID is required.", nameof(id));
        }

        if (copies < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(copies), "Copies must be at least 1.");
        }

        ArgumentNullException.ThrowIfNull(document);

        return new PrintJob(id.Trim(), Normalize(printerName), copies, document, createdAt);
    }

    public bool TryMarkProcessing(DateTimeOffset startedAt)
    {
        if (Status != PrintJobStatus.Pending)
        {
            return false;
        }

        Status = PrintJobStatus.Processing;
        StartedAt = startedAt;
        ErrorMessage = null;

        return true;
    }

    public bool TryMarkCompleted(DateTimeOffset completedAt)
    {
        if (Status != PrintJobStatus.Processing)
        {
            return false;
        }

        Status = PrintJobStatus.Completed;
        CompletedAt = completedAt;
        ErrorMessage = null;

        return true;
    }

    public bool TryMarkFailed(DateTimeOffset completedAt, string errorMessage)
    {
        if (Status is PrintJobStatus.Completed or PrintJobStatus.Canceled)
        {
            return false;
        }

        Status = PrintJobStatus.Failed;
        CompletedAt = completedAt;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Unknown print failure." : errorMessage.Trim();

        return true;
    }

    public bool TryMarkCanceled(DateTimeOffset completedAt, string? errorMessage = null)
    {
        if (Status is PrintJobStatus.Completed or PrintJobStatus.Failed)
        {
            return false;
        }

        Status = PrintJobStatus.Canceled;
        CompletedAt = completedAt;
        ErrorMessage = Normalize(errorMessage);

        return true;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
