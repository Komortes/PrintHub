using System.Diagnostics;
using System.Globalization;
using PrintHub.Contracts.Printers;
using PrintHub.Core.Backends;
using PrintHub.Core.Models;

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

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<string?> TryGetDefaultPrinterNameAsync(
        CancellationToken cancellationToken)
    {
        var result = await RunCommandAsync(LpStatCommandName, ["-d"], cancellationToken);
        var output = result.GetCombinedOutput();

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

        if (Contains(output, "disabled") || Contains(output, "paused"))
        {
            return PrinterStatus.Offline;
        }

        if (Contains(output, "unable to connect") || Contains(output, "not accepting requests"))
        {
            return PrinterStatus.Error;
        }

        if (Contains(output, "printing") || Contains(output, "busy") || Contains(output, "processing"))
        {
            return PrinterStatus.Busy;
        }

        if (Contains(output, "idle") || Contains(output, "enabled") || Contains(output, "ready"))
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

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
