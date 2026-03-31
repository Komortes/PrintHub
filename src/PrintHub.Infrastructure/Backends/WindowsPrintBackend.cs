using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using PrintHub.Contracts.Printers;
using PrintHub.Core.Backends;
using PrintHub.Core.Models;
using DiagnosticSeverity = PrintHub.Core.Backends.PrintBackendDiagnosticSeverity;

namespace PrintHub.Infrastructure.Backends;

public sealed class WindowsPrintBackend : IPrintBackend
{
    private const string WindowsPowerShellCommandName = "powershell";
    private const string PowerShellCoreCommandName = "pwsh";

    public static bool IsSupported() =>
        OperatingSystem.IsWindows() && ResolvePowerShellCommandName() is not null;

    public async ValueTask<IReadOnlyCollection<PrinterInfo>> GetPrintersAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureSupported();

        var result = await RunPowerShellAsync(
            "$ErrorActionPreference = 'Stop'; " +
            "$printers = @(Get-CimInstance Win32_Printer | Select-Object Name, Default, PrinterStatus, WorkOffline); " +
            "if ($printers.Count -eq 0) { '[]' } else { $printers | ConvertTo-Json -Compress }",
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"The Windows print backend could not enumerate printers. {result.GetErrorMessage()}");
        }

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return Array.Empty<PrinterInfo>();
        }

        var printers = JsonSerializer.Deserialize<WindowsPrinterRecord[]>(
            result.StandardOutput,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (printers is null || printers.Length == 0)
        {
            return Array.Empty<PrinterInfo>();
        }

        return printers
            .Where(printer => !string.IsNullOrWhiteSpace(printer.Name))
            .Select(printer => new PrinterInfo(
                printer.Name.Trim(),
                printer.Name.Trim(),
                printer.Default,
                MapPrinterStatus(printer.PrinterStatus, printer.WorkOffline)))
            .ToArray();
    }

    public async ValueTask<PrintBackendDiagnostics> GetDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        var powerShellCommand = ResolvePowerShellCommandName();

        if (!OperatingSystem.IsWindows())
        {
            return new PrintBackendDiagnostics(
                Backend: "windows-spooler",
                IsSupported: false,
                Summary: "Windows print backend is unavailable because PrintHub is not running on Windows.",
                Checks:
                [
                    new PrintBackendDiagnosticCheck(
                        "platform",
                        DiagnosticSeverity.Warning,
                        "Platform",
                        "This backend can only run on Windows.",
                        Environment.OSVersion.Platform.ToString())
                ],
                Printers: Array.Empty<PrinterDiagnosticInfo>(),
                Recommendations:
                [
                    "Use the lp/cups backend on macOS/Linux or run PrintHub on Windows."
                ]);
        }

        if (powerShellCommand is null)
        {
            return new PrintBackendDiagnostics(
                Backend: "windows-spooler",
                IsSupported: false,
                Summary: "Windows print backend is unavailable because PowerShell was not found on PATH.",
                Checks:
                [
                    new PrintBackendDiagnosticCheck(
                        "powershell",
                        DiagnosticSeverity.Error,
                        "PowerShell",
                        "Windows print diagnostics require PowerShell or PowerShell Core on PATH.",
                        null)
                ],
                Printers: Array.Empty<PrinterDiagnosticInfo>(),
                Recommendations:
                [
                    "Install PowerShell or ensure powershell/pwsh is available on PATH."
                ]);
        }

        try
        {
            var printers = await GetPrintersAsync(cancellationToken);
            var diagnosticsPrinters = printers
                .Select(printer => new PrinterDiagnosticInfo(
                    printer.Id,
                    printer.Name,
                    printer.IsDefault,
                    printer.Status,
                    printer.IsDefault
                        ? "Default printer reported by Windows."
                        : "Printer discovered from Windows printer spooler."))
                .ToArray();
            var defaultPrinter = diagnosticsPrinters.FirstOrDefault(printer => printer.IsDefault);

            var summary = diagnosticsPrinters.Length == 0
                ? "Windows print backend is available, but no printers were discovered."
                : $"Windows print backend discovered {diagnosticsPrinters.Length} printer(s).";

            var checks = new List<PrintBackendDiagnosticCheck>
            {
                new(
                    "powershell",
                    DiagnosticSeverity.Info,
                    "PowerShell",
                    "PowerShell is available for Windows printer discovery.",
                    powerShellCommand),
                new(
                    "printer-count",
                    diagnosticsPrinters.Length == 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
                    "Discovered printers",
                    diagnosticsPrinters.Length == 0
                        ? "Windows did not return any configured printers."
                        : $"Windows returned {diagnosticsPrinters.Length} configured printer(s).",
                    diagnosticsPrinters.Length.ToString()),
                new(
                    "default-printer",
                    defaultPrinter is null ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
                    "Default printer",
                    defaultPrinter is null
                        ? "Windows did not report a default printer."
                        : $"Default printer is '{defaultPrinter.Name}'.",
                    defaultPrinter?.Name)
            };

            var recommendations = new List<string>();

            if (diagnosticsPrinters.Length == 0)
            {
                recommendations.Add("Add a printer in Windows Settings before using PrintHub.");
            }

            if (defaultPrinter is null && diagnosticsPrinters.Length > 0)
            {
                recommendations.Add("Set a default printer if you want to print without sending printerName.");
            }

            recommendations.Add("Make sure a PDF viewer with shell print support is installed for Windows PDF printing.");

            return new PrintBackendDiagnostics(
                Backend: "windows-spooler",
                IsSupported: true,
                Summary: summary,
                Checks: checks,
                Printers: diagnosticsPrinters,
                Recommendations: recommendations);
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            return new PrintBackendDiagnostics(
                Backend: "windows-spooler",
                IsSupported: true,
                Summary: "Windows print backend is available, but printer discovery failed.",
                Checks:
                [
                    new PrintBackendDiagnosticCheck(
                        "discovery-error",
                        DiagnosticSeverity.Error,
                        "Discovery error",
                        exception.Message,
                        null)
                ],
                Printers: Array.Empty<PrinterDiagnosticInfo>(),
                Recommendations:
                [
                    "Check Windows printer configuration and verify that PowerShell can enumerate Win32_Printer."
                ]);
        }
    }

    public async ValueTask PrintAsync(
        PrintJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        EnsureSupported();

        if (!File.Exists(job.Document.StoredPath))
        {
            throw new FileNotFoundException(
                $"Prepared document file was not found at '{job.Document.StoredPath}'.",
                job.Document.StoredPath);
        }

        var documentPath = Path.GetFullPath(job.Document.StoredPath);

        for (var copyIndex = 0; copyIndex < job.Copies; copyIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            StartPrintProcess(documentPath, job.PrinterName);

            if (copyIndex < job.Copies - 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }
    }

    private static void EnsureSupported()
    {
        if (!IsSupported())
        {
            throw new PlatformNotSupportedException(
                "The Windows print backend requires Windows with PowerShell support.");
        }
    }

    private static string? ResolvePowerShellCommandName() =>
        IsCommandAvailable(WindowsPowerShellCommandName)
            ? WindowsPowerShellCommandName
            : IsCommandAvailable(PowerShellCoreCommandName)
                ? PowerShellCoreCommandName
                : null;

    private static void StartPrintProcess(string documentPath, string? printerName)
    {
        try
        {
            using var process = Process.Start(CreateStartInfo(documentPath, printerName));

            if (process is null)
            {
                throw new InvalidOperationException("The Windows shell did not start a print process.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            var detail = string.IsNullOrWhiteSpace(printerName)
                ? "using the default printer"
                : $"for printer '{printerName}'";

            throw new InvalidOperationException(
                $"The Windows print backend could not start PDF printing {detail}. " +
                "Make sure a PDF viewer with shell print support is installed.",
                exception);
        }
    }

    private static ProcessStartInfo CreateStartInfo(string documentPath, string? printerName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = documentPath,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            Verb = string.IsNullOrWhiteSpace(printerName) ? "print" : "printto"
        };

        if (!string.IsNullOrWhiteSpace(printerName))
        {
            startInfo.Arguments = QuoteShellArgument(printerName.Trim());
        }

        return startInfo;
    }

    private static PrinterStatus MapPrinterStatus(int? printerStatus, bool workOffline)
    {
        if (workOffline)
        {
            return PrinterStatus.Offline;
        }

        return printerStatus switch
        {
            3 => PrinterStatus.Ready,
            4 or 5 => PrinterStatus.Busy,
            6 => PrinterStatus.Error,
            7 => PrinterStatus.Offline,
            _ => PrinterStatus.Unknown
        };
    }

    private static async Task<CommandResult> RunPowerShellAsync(
        string script,
        CancellationToken cancellationToken)
    {
        var powerShellCommand = ResolvePowerShellCommandName();

        if (powerShellCommand is null)
        {
            throw new PlatformNotSupportedException(
                "PowerShell was not found on PATH for the Windows print backend.");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = powerShellCommand,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(script);

        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new CommandResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static bool IsCommandAvailable(string commandName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (File.Exists(Path.Combine(directory, commandName))
                || File.Exists(Path.Combine(directory, $"{commandName}.exe")))
            {
                return true;
            }
        }

        return false;
    }

    private static string QuoteShellArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private sealed record WindowsPrinterRecord(
        string Name,
        bool Default,
        int? PrinterStatus,
        bool WorkOffline);

    private readonly record struct CommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public bool IsSuccess => ExitCode == 0;

        public string GetErrorMessage()
        {
            var error = string.IsNullOrWhiteSpace(StandardError)
                ? StandardOutput
                : StandardError;

            if (string.IsNullOrWhiteSpace(error))
            {
                return $"Command exited with code {ExitCode}.";
            }

            return error.Trim();
        }
    }
}
