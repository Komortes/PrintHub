using PrintHub.Contracts.Printers;
using PrintHub.Core.Backends;
using PrintHub.Core.Models;

namespace PrintHub.Infrastructure.Backends;

public sealed class MockPrintBackend : IPrintBackend
{
    private static readonly IReadOnlyCollection<PrinterInfo> Printers =
    [
        new PrinterInfo("printer_1", "Office Printer", true, PrinterStatus.Ready),
        new PrinterInfo("printer_2", "Label Printer", false, PrinterStatus.Ready)
    ];

    public ValueTask<IReadOnlyCollection<PrinterInfo>> GetPrintersAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Printers);

    public async ValueTask PrintAsync(
        PrintJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var printer = ResolvePrinter(job.PrinterName);

        if (printer.Status != PrinterStatus.Ready)
        {
            throw new InvalidOperationException($"Printer '{printer.Name}' is not ready.");
        }

        if (!File.Exists(job.Document.StoredPath))
        {
            throw new FileNotFoundException(
                $"Prepared document file was not found at '{job.Document.StoredPath}'.",
                job.Document.StoredPath);
        }

        var simulatedPrintDelay = TimeSpan.FromMilliseconds(500 + (job.Copies * 250));
        await Task.Delay(simulatedPrintDelay, cancellationToken);
    }

    private static PrinterInfo ResolvePrinter(string? printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return Printers.First(printer => printer.IsDefault);
        }

        var printer = Printers.FirstOrDefault(candidate =>
            candidate.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase) ||
            candidate.Id.Equals(printerName, StringComparison.OrdinalIgnoreCase));

        return printer ?? throw new InvalidOperationException($"Printer '{printerName}' was not found.");
    }
}
