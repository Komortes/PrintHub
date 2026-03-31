using PrintHub.Contracts.Printers;
using PrintHub.Core.Backends;
using PrintHub.Core.Models;
using DiagnosticSeverity = PrintHub.Core.Backends.PrintBackendDiagnosticSeverity;

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

    public ValueTask<PrintBackendDiagnostics> GetDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        var printers = Printers
            .Select(printer => new PrinterDiagnosticInfo(
                printer.Id,
                printer.Name,
                printer.IsDefault,
                printer.Status,
                "Mock printer exposed by the development backend."))
            .ToArray();

        return ValueTask.FromResult(new PrintBackendDiagnostics(
            Backend: "mock",
            IsSupported: true,
            Summary: "Mock backend is active. PrintHub is not using the operating system printer stack.",
            Checks:
            [
                new PrintBackendDiagnosticCheck(
                    "backend-mode",
                    DiagnosticSeverity.Info,
                    "Backend mode",
                    "PrintHub is running against the built-in mock backend.",
                    "Mock"),
                new PrintBackendDiagnosticCheck(
                    "printer-count",
                    DiagnosticSeverity.Info,
                    "Mock printers",
                    $"The mock backend exposes {printers.Length} virtual printers.",
                    printers.Length.ToString())
            ],
            Printers: printers,
            Recommendations:
            [
                "Switch PrintHub:BackendMode to Auto or System to use real OS printers."
            ]));
    }

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
