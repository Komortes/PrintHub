using PrintHub.Contracts.PrintJobs;
using PrintHub.Contracts.Printers;
using PrintHub.Core.Backends;
using PrintHub.Core.Platform;
using PrintHub.Core.Settings;
using PrintHub.Core.Services;
using PrintHub.Infrastructure.Paths;
using System.Runtime.InteropServices;
using System.Text;
using ContractDiagnosticSeverity = PrintHub.Contracts.Printers.PrintBackendDiagnosticSeverity;
using CoreDiagnosticSeverity = PrintHub.Core.Backends.PrintBackendDiagnosticSeverity;

namespace PrintHub.Api.Diagnostics;

internal static class PrintHubDiagnosticsFormatter
{
    public static PrintBackendDiagnosticsDto ToDto(PrintBackendDiagnostics diagnostics) =>
        new(
            diagnostics.Backend,
            diagnostics.IsSupported,
            diagnostics.Summary,
            diagnostics.Checks.Select(check => new PrintBackendDiagnosticCheckDto(
                check.Code,
                check.Severity switch
                {
                    CoreDiagnosticSeverity.Warning => ContractDiagnosticSeverity.Warning,
                    CoreDiagnosticSeverity.Error => ContractDiagnosticSeverity.Error,
                    _ => ContractDiagnosticSeverity.Info
                },
                check.Title,
                check.Message,
                check.Value))
                .ToArray(),
            diagnostics.Printers.Select(printer => new PrinterDiagnosticDto(
                printer.Id,
                printer.Name,
                printer.IsDefault,
                printer.Status,
                printer.Detail))
                .ToArray(),
            diagnostics.Recommendations.ToArray());

    public static string BuildReport(
        PrintHubSettings settings,
        PrintQueueStatusDto queueStatus,
        IReadOnlyCollection<PrintJobDto> jobs,
        PrintBackendDiagnostics diagnostics,
        AutoStartRegistration autoStartStatus,
        PrintHubAppDataPaths appDataPaths,
        DateTimeOffset generatedAtUtc)
    {
        var builder = new StringBuilder();
        var orderedJobs = jobs
            .OrderByDescending(job => job.CreatedAt)
            .ToArray();
        var failedJobs = orderedJobs
            .Where(job => job.Status == PrintJobStatus.Failed)
            .Take(5)
            .ToArray();

        builder.AppendLine("PrintHub Diagnostics Report");
        builder.AppendLine($"GeneratedAtUtc: {generatedAtUtc:O}");
        builder.AppendLine();

        builder.AppendLine("Runtime");
        builder.AppendLine($"- OS: {RuntimeInformation.OSDescription}");
        builder.AppendLine($"- Framework: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"- ProcessArchitecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"- AppDataRoot: {appDataPaths.AppDataRootPath}");
        builder.AppendLine();

        builder.AppendLine("Service");
        builder.AppendLine($"- ServiceName: {settings.ServiceName}");
        builder.AppendLine($"- Port: {settings.Port}");
        builder.AppendLine($"- StorageDirectory: {settings.StorageDirectory}");
        builder.AppendLine($"- ApiKeyConfigured: {FormatBoolean(!string.IsNullOrWhiteSpace(settings.ApiKey))}");
        builder.AppendLine($"- ApiKeyHeaderName: {settings.ApiKeyHeaderName}");
        builder.AppendLine($"- DefaultPrinter: {FormatNullable(settings.DefaultPrinterName)}");
        builder.AppendLine($"- RegisteredPrinters: {settings.Printers.Count}");
        foreach (var printer in settings.Printers)
        {
            builder.AppendLine($"  - {printer.Name} [{printer.Id}]");
        }
        builder.AppendLine();

        builder.AppendLine("AutoStart");
        builder.AppendLine($"- Supported: {FormatBoolean(autoStartStatus.IsSupported)}");
        builder.AppendLine($"- Enabled: {FormatBoolean(autoStartStatus.IsEnabled)}");
        builder.AppendLine($"- Provider: {FormatNullable(autoStartStatus.Provider)}");
        builder.AppendLine($"- EntryPath: {FormatNullable(autoStartStatus.EntryPath)}");
        builder.AppendLine();

        builder.AppendLine("Queue");
        builder.AppendLine($"- Paused: {FormatBoolean(queueStatus.IsPaused)}");
        builder.AppendLine($"- QueuedCount: {queueStatus.QueuedCount}");
        builder.AppendLine($"- JobsTotal: {orderedJobs.Length}");
        builder.AppendLine($"- JobsPending: {orderedJobs.Count(job => job.Status == PrintJobStatus.Pending)}");
        builder.AppendLine($"- JobsProcessing: {orderedJobs.Count(job => job.Status == PrintJobStatus.Processing)}");
        builder.AppendLine($"- JobsCompleted: {orderedJobs.Count(job => job.Status == PrintJobStatus.Completed)}");
        builder.AppendLine($"- JobsFailed: {orderedJobs.Count(job => job.Status == PrintJobStatus.Failed)}");
        builder.AppendLine($"- JobsCanceled: {orderedJobs.Count(job => job.Status == PrintJobStatus.Canceled)}");
        if (failedJobs.Length > 0)
        {
            builder.AppendLine("- RecentFailures:");
            foreach (var job in failedJobs)
            {
                builder.AppendLine($"  - {job.JobId} | Printer={FormatNullable(job.PrinterName)} | Completed={FormatDate(job.CompletedAt)}");
                builder.AppendLine($"    Error={FormatNullable(job.ErrorMessage)}");
            }
        }
        builder.AppendLine();

        builder.AppendLine("Backend");
        builder.AppendLine($"- Name: {diagnostics.Backend}");
        builder.AppendLine($"- Supported: {FormatBoolean(diagnostics.IsSupported)}");
        builder.AppendLine($"- Summary: {diagnostics.Summary}");
        builder.AppendLine("- Checks:");
        foreach (var check in diagnostics.Checks)
        {
            builder.AppendLine($"  - [{check.Severity}] {check.Title}: {check.Message}");
            if (!string.IsNullOrWhiteSpace(check.Value))
            {
                builder.AppendLine($"    Value: {check.Value}");
            }
        }
        builder.AppendLine("- Printers:");
        if (diagnostics.Printers.Count == 0)
        {
            builder.AppendLine("  - none");
        }
        else
        {
            foreach (var printer in diagnostics.Printers)
            {
                builder.AppendLine($"  - {printer.Name} [{printer.Id}] | Default={FormatBoolean(printer.IsDefault)} | Status={printer.Status}");
                if (!string.IsNullOrWhiteSpace(printer.Detail))
                {
                    builder.AppendLine($"    Detail: {printer.Detail}");
                }
            }
        }
        builder.AppendLine("- Recommendations:");
        if (diagnostics.Recommendations.Count == 0)
        {
            builder.AppendLine("  - none");
        }
        else
        {
            foreach (var recommendation in diagnostics.Recommendations)
            {
                builder.AppendLine($"  - {recommendation}");
            }
        }

        return builder.ToString();
    }

    private static string FormatBoolean(bool value) => value ? "yes" : "no";

    private static string FormatNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToString("O") ?? "—";
}
