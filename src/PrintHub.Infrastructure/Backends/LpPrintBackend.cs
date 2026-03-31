using System.Diagnostics;
using System.Globalization;
using PrintHub.Contracts.Printers;
using PrintHub.Core.Backends;
using PrintHub.Core.Models;
using DiagnosticSeverity = PrintHub.Core.Backends.PrintBackendDiagnosticSeverity;

namespace PrintHub.Infrastructure.Backends;

public sealed class LpPrintBackend : IPrintBackend
{
    private const string LpCommandName = "lp";
    private const string LpStatCommandName = "lpstat";

    public static bool IsSupported()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return false;
        }

        return IsCommandAvailable(LpCommandName) && IsCommandAvailable(LpStatCommandName);
    }

    public async ValueTask<IReadOnlyCollection<PrinterInfo>> GetPrintersAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureSupported();

        var printerNames = await GetPrinterNamesAsync(cancellationToken);

        if (printerNames.Count == 0)
        {
            return Array.Empty<PrinterInfo>();
        }

        var defaultPrinterName = await TryGetDefaultPrinterNameAsync(cancellationToken);
        var printerTasks = printerNames
            .Select(async printerName => new PrinterInfo(
                printerName,
                printerName,
                string.Equals(printerName, defaultPrinterName, StringComparison.OrdinalIgnoreCase),
                await TryGetPrinterStatusAsync(printerName, cancellationToken)))
            .ToArray();

        return await Task.WhenAll(printerTasks);
    }

    public async ValueTask<PrintBackendDiagnostics> GetDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return new PrintBackendDiagnostics(
                Backend: "lp-cups",
                IsSupported: false,
                Summary: "lp/cups backend is unavailable because PrintHub is not running on macOS or Linux.",
                Checks:
                [
                    new PrintBackendDiagnosticCheck(
                        "platform",
                        DiagnosticSeverity.Warning,
                        "Platform",
                        "This backend only works on macOS and Linux.",
                        Environment.OSVersion.Platform.ToString())
                ],
                Printers: Array.Empty<PrinterDiagnosticInfo>(),
                Recommendations:
                [
                    "Use the Windows spooler backend on Windows or run PrintHub on macOS/Linux."
                ]);
        }

        var missingCommands = new List<string>();

        if (!IsCommandAvailable(LpCommandName))
        {
            missingCommands.Add(LpCommandName);
        }

        if (!IsCommandAvailable(LpStatCommandName))
        {
            missingCommands.Add(LpStatCommandName);
        }

        if (missingCommands.Count > 0)
        {
            return new PrintBackendDiagnostics(
                Backend: "lp-cups",
                IsSupported: false,
                Summary: "lp/cups backend is unavailable because required commands are missing from PATH.",
                Checks:
                [
                    new PrintBackendDiagnosticCheck(
                        "commands",
                        DiagnosticSeverity.Error,
                        "Required commands",
                        $"Missing required command(s): {string.Join(", ", missingCommands)}.",
                        string.Join(", ", missingCommands))
                ],
                Printers: Array.Empty<PrinterDiagnosticInfo>(),
                Recommendations:
                [
                    "Install CUPS command line tools or make sure lp and lpstat are available on PATH."
                ]);
        }

        var schedulerResult = await RunCommandAsync(LpStatCommandName, ["-r"], cancellationToken);
        var defaultResult = await RunCommandAsync(LpStatCommandName, ["-d"], cancellationToken);
        var printerNamesResult = await RunCommandAsync(LpStatCommandName, ["-e"], cancellationToken);

        var schedulerOutput = NormalizeOutput(schedulerResult.GetCombinedOutput());
        var defaultOutput = NormalizeOutput(defaultResult.GetCombinedOutput());
        var printerNamesOutput = NormalizeOutput(printerNamesResult.GetCombinedOutput());
        var schedulerRunning = InferSchedulerRunning(schedulerOutput);
        var defaultPrinterName = ParseDefaultPrinterName(defaultOutput ?? string.Empty);
        var printerNames = ParsePrinterNames(printerNamesResult);
        var diagnosticPrinters = new List<PrinterDiagnosticInfo>();

        foreach (var printerName in printerNames)
        {
            var printerStatusResult = await RunCommandAsync(LpStatCommandName, ["-p", printerName], cancellationToken);
            var printerOutput = NormalizeOutput(printerStatusResult.GetCombinedOutput());

            diagnosticPrinters.Add(new PrinterDiagnosticInfo(
                printerName,
                printerName,
                string.Equals(printerName, defaultPrinterName, StringComparison.OrdinalIgnoreCase),
                InferPrinterStatus(printerOutput ?? string.Empty),
                printerOutput));
        }

        var checks = new List<PrintBackendDiagnosticCheck>
        {
            new(
                "commands",
                DiagnosticSeverity.Info,
                "Required commands",
                "lp and lpstat are available on PATH.",
                "lp, lpstat"),
            new(
                "scheduler",
                schedulerRunning == false ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
                "Scheduler",
                schedulerRunning == false
                    ? "The CUPS scheduler does not appear to be running."
                    : "The CUPS scheduler is available.",
                schedulerOutput ?? "(no output)"),
            new(
                "default-printer",
                string.IsNullOrWhiteSpace(defaultPrinterName) ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
                "Default printer",
                string.IsNullOrWhiteSpace(defaultPrinterName)
                    ? "No default printer was reported by lpstat."
                    : $"Default printer is '{defaultPrinterName}'.",
                defaultOutput ?? "(no output)"),
            new(
                "printer-discovery",
                diagnosticPrinters.Count == 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
                "Printer discovery",
                diagnosticPrinters.Count == 0
                    ? "lpstat did not return any printers."
                    : $"lpstat returned {diagnosticPrinters.Count} printer(s).",
                printerNamesOutput ?? "(no output)")
        };

        if (!printerNamesResult.IsSuccess && !ShouldTreatAsEmptyPrinterList(printerNamesResult))
        {
            checks.Add(new PrintBackendDiagnosticCheck(
                "printer-discovery-error",
                DiagnosticSeverity.Error,
                "Discovery error",
                "lpstat returned an error while listing printers.",
                printerNamesOutput ?? "(no output)"));
        }

        if (diagnosticPrinters.Count > 0 && diagnosticPrinters.All(printer => printer.Status == PrinterStatus.Unknown))
        {
            checks.Add(new PrintBackendDiagnosticCheck(
                "status-parsing",
                DiagnosticSeverity.Warning,
                "Printer status parsing",
                "PrintHub discovered printers, but their runtime state could not be normalized.",
                null));
        }

        var recommendations = new List<string>();

        if (schedulerRunning == false)
        {
            recommendations.Add("Start the system print scheduler or open Printers & Scanners to reactivate the print subsystem.");
        }

        if (diagnosticPrinters.Count == 0)
        {
            recommendations.Add("Add at least one printer in the operating system before using PrintHub.");
        }

        if (diagnosticPrinters.Count > 0 && string.IsNullOrWhiteSpace(defaultPrinterName))
        {
            recommendations.Add("Set a default printer if you want PrintHub to accept jobs without printerName.");
        }

        if (diagnosticPrinters.Any(printer => printer.Status == PrinterStatus.Unknown))
        {
            recommendations.Add("Use the diagnostic details from lpstat -p to verify the exact CUPS status text for each printer.");
        }

        var summary = schedulerRunning == false
            ? "lp/cups backend is available, but the CUPS scheduler does not appear to be running."
            : diagnosticPrinters.Count == 0
                ? "lp/cups backend is available, but no printers were discovered."
                : $"lp/cups backend discovered {diagnosticPrinters.Count} printer(s).";

        return new PrintBackendDiagnostics(
            Backend: "lp-cups",
            IsSupported: true,
            Summary: summary,
            Checks: checks,
            Printers: diagnosticPrinters,
            Recommendations: recommendations);
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

        var arguments = new List<string>();

        if (!string.IsNullOrWhiteSpace(job.PrinterName))
        {
            arguments.Add("-d");
            arguments.Add(job.PrinterName.Trim());
        }

        arguments.Add("-n");
        arguments.Add(job.Copies.ToString(CultureInfo.InvariantCulture));
        arguments.Add("-t");
        arguments.Add($"PrintHub {job.Id}");
        arguments.Add(job.Document.StoredPath);

        var result = await RunCommandAsync(LpCommandName, arguments, cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"The system print backend failed to print job '{job.Id}'. {result.GetErrorMessage()}");
        }
    }

    private static void EnsureSupported()
    {
        if (!IsSupported())
        {
            throw new PlatformNotSupportedException(
                "The system print backend requires macOS or Linux with 'lp' and 'lpstat' available on PATH.");
        }
    }

    private static async Task<IReadOnlyList<string>> GetPrinterNamesAsync(
        CancellationToken cancellationToken)
    {
        var result = await RunCommandAsync(LpStatCommandName, ["-e"], cancellationToken);

        if (!result.IsSuccess)
        {
            if (ShouldTreatAsEmptyPrinterList(result))
            {
                return Array.Empty<string>();
            }

            throw new InvalidOperationException(
                $"The system print backend could not enumerate printers. {result.GetErrorMessage()}");
        }

        return ParsePrinterNames(result);
    }

    private static async Task<string?> TryGetDefaultPrinterNameAsync(
        CancellationToken cancellationToken)
    {
        var result = await RunCommandAsync(LpStatCommandName, ["-d"], cancellationToken);
        return ParseDefaultPrinterName(result.GetCombinedOutput());
    }

    private static async Task<PrinterStatus> TryGetPrinterStatusAsync(
        string printerName,
        CancellationToken cancellationToken)
    {
        var result = await RunCommandAsync(LpStatCommandName, ["-p", printerName], cancellationToken);
        return InferPrinterStatus(result.GetCombinedOutput());
    }

    private static PrinterStatus InferPrinterStatus(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return PrinterStatus.Unknown;
        }

        if (ContainsAny(output, "disabled", "paused", "отключен", "отключён", "приостановлен", "приостановлено"))
        {
            return PrinterStatus.Offline;
        }

        if (ContainsAny(
            output,
            "unable to connect",
            "not accepting requests",
            "error",
            "не удается подключиться",
            "не удаётся подключиться",
            "не принимает запросы",
            "ошибка"))
        {
            return PrinterStatus.Error;
        }

        if (ContainsAny(
            output,
            "printing",
            "busy",
            "processing",
            "печатает",
            "занят",
            "обрабатывает"))
        {
            return PrinterStatus.Busy;
        }

        if (ContainsAny(
            output,
            "idle",
            "enabled",
            "ready",
            "свободен",
            "включен",
            "включён",
            "готов"))
        {
            return PrinterStatus.Ready;
        }

        return PrinterStatus.Unknown;
    }

    private static bool ShouldTreatAsEmptyPrinterList(CommandResult result)
    {
        var output = result.GetCombinedOutput();

        return string.IsNullOrWhiteSpace(output)
            || Contains(output, "no destinations added")
            || Contains(output, "scheduler is not running")
            || Contains(output, "планировщик не запущен")
            || Contains(output, "bad file descriptor");
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
            if (File.Exists(Path.Combine(directory, commandName)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(string value, string search) =>
        value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] searchValues) =>
        searchValues.Any(search => Contains(value, search));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOutput(string? value)
    {
        var normalized = Normalize(value);
        return normalized?.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static IReadOnlyList<string> ParsePrinterNames(CommandResult result) =>
        result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? ParseDefaultPrinterName(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var separatorIndex = output.IndexOf(':');

        if (separatorIndex < 0 || separatorIndex == output.Length - 1)
        {
            return null;
        }

        return Normalize(output[(separatorIndex + 1)..]);
    }

    private static bool? InferSchedulerRunning(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        if (ContainsAny(output, "is not running", "not running", "планировщик не запущен"))
        {
            return false;
        }

        if (ContainsAny(output, "is running", "running", "запущен"))
        {
            return true;
        }

        return null;
    }

    private static async Task<CommandResult> RunCommandAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(fileName, arguments)
        };

        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new CommandResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static ProcessStartInfo CreateStartInfo(
        string fileName,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.Environment["LANG"] = "C";
        startInfo.Environment["LC_ALL"] = "C";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private readonly record struct CommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public bool IsSuccess => ExitCode == 0;

        public string GetCombinedOutput()
        {
            if (string.IsNullOrWhiteSpace(StandardOutput))
            {
                return StandardError.Trim();
            }

            if (string.IsNullOrWhiteSpace(StandardError))
            {
                return StandardOutput.Trim();
            }

            return $"{StandardOutput.Trim()}{Environment.NewLine}{StandardError.Trim()}";
        }

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
