using PrintHub.Api.Configuration;
using PrintHub.Api.Logging;
using PrintHub.Contracts.PrintJobs;
using PrintHub.Core.Backends;
using PrintHub.Core.Platform;
using PrintHub.Core.Services;
using PrintHub.Core.Settings;
using PrintHub.Infrastructure.Paths;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrintHub.Api.Diagnostics;

public sealed class PrintHubSupportBundleService : IPrintHubSupportBundleService
{
    private const string ZipContentType = "application/zip";
    private const int MaxRecentJobs = 100;
    private const int MaxLogFiles = 8;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly IPrintBackend _backend;
    private readonly IPrintHubSettingsService _settingsService;
    private readonly IPrintJobService _printJobService;
    private readonly IAutoStartService _autoStartService;
    private readonly PrintHubAppDataPaths _appDataPaths;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<PrintHubApiOptions> _apiOptions;
    private readonly IOptions<PrintHubFileLoggerOptions> _fileLoggerOptions;

    public PrintHubSupportBundleService(
        IPrintBackend backend,
        IPrintHubSettingsService settingsService,
        IPrintJobService printJobService,
        IAutoStartService autoStartService,
        PrintHubAppDataPaths appDataPaths,
        TimeProvider timeProvider,
        IOptions<PrintHubApiOptions> apiOptions,
        IOptions<PrintHubFileLoggerOptions> fileLoggerOptions)
    {
        _backend = backend;
        _settingsService = settingsService;
        _printJobService = printJobService;
        _autoStartService = autoStartService;
        _appDataPaths = appDataPaths;
        _timeProvider = timeProvider;
        _apiOptions = apiOptions;
        _fileLoggerOptions = fileLoggerOptions;
    }

    public async ValueTask<PrintHubSupportBundle> CreateAsync(CancellationToken cancellationToken = default)
    {
        var generatedAtUtc = _timeProvider.GetUtcNow();
        var settings = await _settingsService.GetAsync(cancellationToken);
        var queueStatus = await _printJobService.GetQueueStatusAsync(cancellationToken);
        var jobs = await _printJobService.ListAsync(cancellationToken);
        var backendDiagnostics = await _backend.GetDiagnosticsAsync(cancellationToken);
        var autoStartStatus = await _autoStartService.GetStatusAsync(cancellationToken);
        var orderedJobs = jobs
            .OrderByDescending(job => job.CreatedAt)
            .ToArray();
        var recentJobs = orderedJobs
            .Take(MaxRecentJobs)
            .ToArray();
        var diagnosticsDto = PrintHubDiagnosticsFormatter.ToDto(backendDiagnostics);
        var diagnosticsReport = PrintHubDiagnosticsFormatter.BuildReport(
            settings,
            queueStatus,
            jobs,
            backendDiagnostics,
            autoStartStatus,
            _appDataPaths,
            generatedAtUtc);

        var settingsPath = _appDataPaths.ResolveDataPath(
            _apiOptions.Value.SettingsFilePath,
            PrintHubApiOptions.DefaultSettingsFilePath);
        var jobsPath = _appDataPaths.ResolveDataPath(
            _apiOptions.Value.JobsFilePath,
            PrintHubApiOptions.DefaultJobsFilePath);
        var storageDirectoryPath = _appDataPaths.ResolveDataPath(
            settings.StorageDirectory,
            PrintHubApiOptions.DefaultStorageDirectory);
        var logsDirectoryPath = _appDataPaths.ResolveDataPath(
            _fileLoggerOptions.Value.DirectoryPath,
            PrintHubFileLoggerOptions.DefaultDirectoryPath);
        var logFiles = GetLogFiles(logsDirectoryPath);

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(archive, "diagnostics/report.txt", diagnosticsReport);
            WriteJsonEntry(archive, "diagnostics/backend-diagnostics.json", diagnosticsDto);
            WriteJsonEntry(archive, "queue/status.json", queueStatus);
            WriteJsonEntry(archive, "jobs/recent-jobs.json", recentJobs);
            WriteJsonEntry(archive, "settings/settings.public.json", new
            {
                settings.ServiceName,
                settings.Port,
                settings.ApiKeyHeaderName,
                apiKeyConfigured = !string.IsNullOrWhiteSpace(settings.ApiKey),
                apiKey = (string?)null,
                settings.DefaultPrinterName,
                settings.StorageDirectory,
                settings.MaxUploadSizeBytes,
                printers = settings.Printers.Select(printer => new
                {
                    printer.Id,
                    printer.Name
                }).ToArray()
            });
            WriteJsonEntry(archive, "runtime/paths.json", new
            {
                appDataRoot = _appDataPaths.AppDataRootPath,
                settingsPath,
                settingsFileExists = File.Exists(settingsPath),
                jobsPath,
                jobsFileExists = File.Exists(jobsPath),
                storageDirectory = storageDirectoryPath,
                storageDirectoryExists = Directory.Exists(storageDirectoryPath),
                logsDirectory = logsDirectoryPath,
                logsDirectoryExists = Directory.Exists(logsDirectoryPath)
            });
            WriteJsonEntry(archive, "manifest.json", new
            {
                generatedAtUtc,
                contentVersion = 1,
                serviceName = settings.ServiceName,
                appVersion = typeof(Program).Assembly.GetName().Version?.ToString(),
                runtime = new
                {
                    os = RuntimeInformation.OSDescription,
                    framework = RuntimeInformation.FrameworkDescription,
                    processArchitecture = RuntimeInformation.ProcessArchitecture.ToString()
                },
                queue = new
                {
                    queueStatus.IsPaused,
                    queueStatus.QueuedCount,
                    totalJobs = orderedJobs.Length
                },
                logFiles = logFiles.Select(file => new
                {
                    name = Path.GetFileName(file),
                    sizeBytes = new FileInfo(file).Length
                }).ToArray()
            });

            foreach (var logFile in logFiles)
            {
                await WriteFileEntryAsync(
                    archive,
                    Path.Combine("logs", Path.GetFileName(logFile)).Replace('\\', '/'),
                    logFile,
                    cancellationToken);
            }
        }

        var fileName = $"printhub-support-{generatedAtUtc:yyyyMMdd-HHmmss}-utc.zip";
        return new PrintHubSupportBundle(buffer.ToArray(), fileName, ZipContentType);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static string[] GetLogFiles(string logsDirectoryPath)
    {
        if (!Directory.Exists(logsDirectoryPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(logsDirectoryPath)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .Take(MaxLogFiles)
            .ToArray();
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
        writer.Write(content);
    }

    private static void WriteJsonEntry(ZipArchive archive, string entryName, object value)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, value, value.GetType(), JsonOptions);
    }

    private static async Task WriteFileEntryAsync(
        ZipArchive archive,
        string entryName,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        await using var destination = entry.Open();
        await source.CopyToAsync(destination, cancellationToken);
    }
}
