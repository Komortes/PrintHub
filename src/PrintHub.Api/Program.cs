using PrintHub.Api.Workers;
using PrintHub.Api.Auth;
using PrintHub.Api.Configuration;
using PrintHub.Api.Logging;
using PrintHub.Api.Requests;
using PrintHub.Contracts.Diagnostics;
using PrintHub.Contracts.PrintJobs;
using PrintHub.Contracts.Printers;
using PrintHub.Contracts.Settings;
using PrintHub.Core.Backends;
using PrintHub.Core.Documents;
using PrintHub.Core.Models;
using PrintHub.Core.Platform;
using PrintHub.Core.Queues;
using PrintHub.Core.Repositories;
using PrintHub.Core.Settings;
using PrintHub.Core.Services;
using PrintHub.Infrastructure.Backends;
using PrintHub.Infrastructure.Documents;
using PrintHub.Infrastructure.Paths;
using PrintHub.Infrastructure.Platform;
using PrintHub.Infrastructure.Repositories;
using PrintHub.Infrastructure.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

var appDataPaths = PrintHubAppDataPaths.CreateDefault();

builder.Services.AddOpenApi();
var fileLoggerOptions = builder.Configuration
    .GetSection(PrintHubFileLoggerOptions.SectionName)
    .Get<PrintHubFileLoggerOptions>()
    ?? new PrintHubFileLoggerOptions();

if (fileLoggerOptions.Enabled)
{
    builder.Logging.AddProvider(new PrintHubFileLoggerProvider(appDataPaths.AppDataRootPath, fileLoggerOptions));
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
builder.Services
    .AddOptions<PrintHubApiOptions>()
    .Bind(builder.Configuration.GetSection(PrintHubApiOptions.SectionName));
PrintJobRequestParser.ConfigureFormOptions(builder.Services, builder.Configuration);
builder.Services.AddSingleton(appDataPaths);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPrintJobQueue, InMemoryPrintJobQueue>();
builder.Services.AddSingleton<IPrintJobStore>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<PrintHubApiOptions>>().Value;
    var paths = serviceProvider.GetRequiredService<PrintHubAppDataPaths>();
    return new SqlitePrintJobStore(paths.AppDataRootPath, options.JobsFilePath);
});
builder.Services.AddSingleton<MockPrintBackend>();
builder.Services.AddSingleton<LpPrintBackend>();
builder.Services.AddSingleton<WindowsPrintBackend>();
builder.Services.AddSingleton<IPrintBackend>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<PrintHubApiOptions>>().Value;
    var logger = serviceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("PrintHub.PrintBackend");

    return ResolvePrintBackend(
        options.BackendMode,
        serviceProvider.GetRequiredService<MockPrintBackend>(),
        serviceProvider.GetRequiredService<LpPrintBackend>(),
        serviceProvider.GetRequiredService<WindowsPrintBackend>(),
        logger);
});
builder.Services.AddSingleton<IPrintHubSettingsStore>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<PrintHubApiOptions>>().Value;
    var paths = serviceProvider.GetRequiredService<PrintHubAppDataPaths>();
    return new JsonPrintHubSettingsStore(paths.AppDataRootPath, options.SettingsFilePath);
});
builder.Services.AddSingleton<IPrintHubSettingsService>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<PrintHubApiOptions>>().Value;
    var defaults = PrintHubSettings.CreateDefaults(
        options.ServiceName,
        options.Port,
        options.ApiKeyHeaderName,
        options.ApiKey,
        null,
        options.StorageDirectory,
        options.MaxUploadSizeBytes);

    return new PrintHubSettingsService(
        serviceProvider.GetRequiredService<IPrintHubSettingsStore>(),
        defaults);
});
builder.Services.AddSingleton<IAutoStartService>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<PrintHubApiOptions>>().Value;

    return new PlatformAutoStartService(
        AppContext.BaseDirectory,
        options.AutoStartUnixLauncherPath,
        options.AutoStartWindowsLauncherPath,
        options.AutoStartMacOsLaunchAgentsDirectoryPath,
        options.AutoStartLinuxDirectoryPath,
        options.AutoStartWindowsDirectoryPath);
});
builder.Services.AddHttpClient<IPrintDocumentPipeline, FileSystemPrintDocumentPipeline>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<IPrintJobService, PrintJobService>();
builder.Services.AddHostedService<PrintJobWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();
app.UseStaticFiles();

var protectedApi = app.MapGroup(string.Empty)
    .AddEndpointFilter<ApiKeyEndpointFilter>();
var settingsApi = app.MapGroup("/settings")
    .AddEndpointFilter<ApiKeyEndpointFilter>();

app.MapGet("/health", async (TimeProvider timeProvider, IPrintHubSettingsService settingsService, CancellationToken cancellationToken) =>
{
    var settings = await settingsService.GetAsync(cancellationToken);

    return TypedResults.Ok(new HealthResponse(
        Status: "healthy",
        Service: settings.ServiceName,
        Timestamp: timeProvider.GetUtcNow()));
})
    .WithName("GetHealth");

settingsApi.MapGet(string.Empty, async (IPrintHubSettingsService settingsService, CancellationToken cancellationToken) =>
{
    var settings = await settingsService.GetAsync(cancellationToken);
    return TypedResults.Ok(ToSettingsDto(settings));
})
    .WithName("GetSettings");

settingsApi.MapGet("/auto-start", async (
    IAutoStartService autoStartService,
    CancellationToken cancellationToken) =>
{
    var status = await autoStartService.GetStatusAsync(cancellationToken);
    return TypedResults.Ok(ToAutoStartDto(status));
})
    .WithName("GetAutoStartStatus");

settingsApi.MapGet("/setup-status", async (
    IPrintBackend backend,
    IPrintHubSettingsService settingsService,
    CancellationToken cancellationToken) =>
{
    var settings = await settingsService.GetAsync(cancellationToken);
    IReadOnlyCollection<PrinterInfo> osPrinters;
    try { osPrinters = await backend.GetPrintersAsync(cancellationToken); }
    catch { osPrinters = []; }
    return TypedResults.Ok(ToSetupStatusDto(settings, osPrinters));
})
    .WithName("GetSetupStatus");

settingsApi.MapPut(string.Empty, async Task<IResult> (
    UpdatePrintHubSettingsRequest request,
    IPrintHubSettingsService settingsService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var updatedSettings = await settingsService.UpdateAsync(request, cancellationToken);
        return TypedResults.Ok(ToSettingsDto(updatedSettings));
    }
    catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
    {
        return TypedResults.BadRequest(new ProblemDetails
        {
            Title = "Invalid settings request",
            Detail = exception.Message,
            Status = StatusCodes.Status400BadRequest
        });
    }
})
    .WithName("UpdateSettings");

settingsApi.MapPut("/auto-start", async Task<IResult> (
    UpdateAutoStartRequest request,
    IAutoStartService autoStartService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var status = await autoStartService.SetEnabledAsync(request.Enabled, cancellationToken);
        return TypedResults.Ok(ToAutoStartDto(status));
    }
    catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
    {
        return TypedResults.BadRequest(new ProblemDetails
        {
            Title = "Invalid auto-start request",
            Detail = exception.Message,
            Status = StatusCodes.Status400BadRequest
        });
    }
})
    .WithName("UpdateAutoStart");

settingsApi.MapPost("/onboarding", async Task<IResult> (
    CompleteOnboardingRequest request,
    IPrintBackend backend,
    IPrintHubSettingsService settingsService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.ApiKey))
    {
        return TypedResults.UnprocessableEntity(new ProblemDetails
        {
            Title = "Validation error",
            Detail = "API key is required.",
            Status = StatusCodes.Status422UnprocessableEntity
        });
    }

    try
    {
        var currentSettings = await settingsService.GetAsync(cancellationToken);
        var updateRequest = ToUpdateRequest(currentSettings, currentSettings.DefaultPrinterName) with
        {
            ApiKey = request.ApiKey
        };
        var updatedSettings = await settingsService.UpdateAsync(updateRequest, cancellationToken);

        IReadOnlyCollection<PrinterInfo> osPrinters;
        try { osPrinters = await backend.GetPrintersAsync(cancellationToken); }
        catch { osPrinters = []; }

        return TypedResults.Ok(ToSetupStatusDto(updatedSettings, osPrinters));
    }
    catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
    {
        return TypedResults.BadRequest(new ProblemDetails
        {
            Title = "Invalid onboarding request",
            Detail = exception.Message,
            Status = StatusCodes.Status400BadRequest
        });
    }
})
    .WithName("CompleteOnboarding");

protectedApi.MapGet("/printers", async (
    IPrintBackend backend,
    IPrintHubSettingsService settingsService,
    CancellationToken cancellationToken) =>
{
    var settings = await settingsService.GetAsync(cancellationToken);
    if (settings.Printers.Count == 0)
    {
        return TypedResults.Ok(Array.Empty<PrinterDto>());
    }

    IReadOnlyCollection<PrinterInfo> osPrinters;
    try { osPrinters = await backend.GetPrintersAsync(cancellationToken); }
    catch { osPrinters = []; }

    var dtos = settings.Printers.Select(registered =>
    {
        var osMatch = osPrinters.FirstOrDefault(os =>
            string.Equals(os.Id, registered.Id, StringComparison.OrdinalIgnoreCase));
        var status = osMatch?.Status ?? PrinterStatus.Offline;
        return ToDto(new PrinterInfo(registered.Id, registered.Name, false, status),
                     settings.DefaultPrinterName);
    }).ToArray();

    return TypedResults.Ok(dtos);
})
    .WithName("GetPrinters");

protectedApi.MapGet("/printers/discover", async (
    IPrintBackend backend,
    IPrintHubSettingsService settingsService,
    CancellationToken cancellationToken) =>
{
    var settings = await settingsService.GetAsync(cancellationToken);

    IReadOnlyCollection<PrinterInfo> osPrinters;
    try { osPrinters = await backend.GetPrintersAsync(cancellationToken); }
    catch { osPrinters = []; }

    var registeredIds = settings.Printers
        .Select(p => p.Id)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var unregistered = osPrinters
        .Where(p => !registeredIds.Contains(p.Id))
        .Select(p => ToDto(p, settings.DefaultPrinterName))
        .ToArray();

    return TypedResults.Ok(unregistered);
})
.WithName("DiscoverPrinters");

protectedApi.MapPost("/printers", async Task<IResult> (
    AddPrinterRequest request,
    IPrintBackend backend,
    IPrintHubSettingsService settingsService,
    CancellationToken cancellationToken) =>
{
    IReadOnlyCollection<PrinterInfo> osPrinters;
    try { osPrinters = await backend.GetPrintersAsync(cancellationToken); }
    catch { osPrinters = []; }

    var osMatch = osPrinters.FirstOrDefault(p =>
        string.Equals(p.Id, request.Id, StringComparison.OrdinalIgnoreCase));

    if (osMatch is null)
    {
        return TypedResults.NotFound(new ProblemDetails
        {
            Title = "Printer not found",
            Detail = $"Printer '{request.Id}' was not found in the OS printer list.",
            Status = StatusCodes.Status404NotFound
        });
    }

    var settings = await settingsService.AddPrinterAsync(osMatch.Id, osMatch.Name, cancellationToken);
    var dto = ToDto(new PrinterInfo(osMatch.Id, osMatch.Name, false, osMatch.Status),
                    settings.DefaultPrinterName);
    return TypedResults.Ok(dto);
})
.WithName("AddPrinter");

protectedApi.MapDelete("/printers/{printerId}", async Task<IResult> (
    string printerId,
    IPrintHubSettingsService settingsService,
    CancellationToken cancellationToken) =>
{
    var settings = await settingsService.GetAsync(cancellationToken);
    var exists = settings.Printers.Any(p =>
        string.Equals(p.Id, printerId, StringComparison.OrdinalIgnoreCase));

    if (!exists)
    {
        return TypedResults.NotFound(new ProblemDetails
        {
            Title = "Printer not found",
            Detail = $"Printer '{printerId}' is not registered.",
            Status = StatusCodes.Status404NotFound
        });
    }

    await settingsService.RemovePrinterAsync(printerId, cancellationToken);
    return TypedResults.NoContent();
})
.WithName("RemovePrinter");

protectedApi.MapPut("/printers/{printerId}/default", async Task<IResult> (
    string printerId,
    IPrintBackend backend,
    IPrintHubSettingsService settingsService,
    CancellationToken cancellationToken) =>
{
    var settings = await settingsService.GetAsync(cancellationToken);
    var registeredPrinter = settings.Printers.FirstOrDefault(p =>
        string.Equals(p.Id, printerId, StringComparison.OrdinalIgnoreCase));

    if (registeredPrinter is null)
    {
        return TypedResults.NotFound(new ProblemDetails
        {
            Title = "Printer not found",
            Detail = $"Printer '{printerId}' is not registered.",
            Status = StatusCodes.Status404NotFound
        });
    }

    var updatedSettings = await settingsService.UpdateAsync(
        ToUpdateRequest(settings, registeredPrinter.Name), cancellationToken);

    IReadOnlyCollection<PrinterInfo> osPrinters;
    try { osPrinters = await backend.GetPrintersAsync(cancellationToken); }
    catch { osPrinters = []; }

    var osMatch = osPrinters.FirstOrDefault(p =>
        string.Equals(p.Id, registeredPrinter.Id, StringComparison.OrdinalIgnoreCase));
    var status = osMatch?.Status ?? PrinterStatus.Offline;
    var dto = ToDto(
        new PrinterInfo(registeredPrinter.Id, registeredPrinter.Name, false, status),
        updatedSettings.DefaultPrinterName);

    return TypedResults.Ok(dto);
})
    .WithName("SetDefaultPrinter");

protectedApi.MapPost("/printers/{printerId}/test-print", async Task<IResult> (
    string printerId,
    IPrintJobService printJobService,
    IPrintHubSettingsService settingsService,
    CancellationToken cancellationToken) =>
{
    var settings = await settingsService.GetAsync(cancellationToken);
    var registeredPrinter = settings.Printers.FirstOrDefault(p =>
        string.Equals(p.Id, printerId, StringComparison.OrdinalIgnoreCase));

    if (registeredPrinter is null)
    {
        return TypedResults.NotFound(new ProblemDetails
        {
            Title = "Printer not found",
            Detail = $"Printer '{printerId}' is not registered.",
            Status = StatusCodes.Status404NotFound
        });
    }

    var createdJob = await printJobService.CreateAsync(
        CreateTestPrintJobRequest(registeredPrinter.Name), cancellationToken);

    return TypedResults.Created($"/print-jobs/{createdJob.JobId}", createdJob);
})
    .WithName("CreateTestPrintJob");

protectedApi.MapPost("/print-jobs", async Task<IResult> (
    HttpRequest request,
    IPrintJobService printJobService,
    IPrintHubSettingsService settingsService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var createRequest = await PrintJobRequestParser.ParseAsync(request, settingsService, cancellationToken);
        var createdJob = await printJobService.CreateAsync(createRequest, cancellationToken);

        return TypedResults.Created($"/print-jobs/{createdJob.JobId}", createdJob);
    }
    catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or JsonException or ArgumentException or HttpRequestException)
    {
        return TypedResults.BadRequest(new ProblemDetails
        {
            Title = "Invalid print job request",
            Detail = exception.Message,
            Status = StatusCodes.Status400BadRequest
        });
    }
})
    .WithName("CreatePrintJob");

protectedApi.MapGet("/print-jobs", async Task<IResult> (
    [FromQuery] string? status,
    [FromQuery] bool? activeOnly,
    [FromQuery] int? limit,
    IPrintJobService printJobService,
    CancellationToken cancellationToken) =>
{
    PrintJobStatus? statusFilter = null;

    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!Enum.TryParse<PrintJobStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Invalid print jobs query",
                Detail = $"Query parameter 'status' has unsupported value '{status}'.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        statusFilter = parsedStatus;
    }

    if (limit is <= 0)
    {
        return TypedResults.BadRequest(new ProblemDetails
        {
            Title = "Invalid print jobs query",
            Detail = "Query parameter 'limit' must be greater than 0.",
            Status = StatusCodes.Status400BadRequest
        });
    }

    var jobs = await printJobService.ListAsync(cancellationToken);
    IEnumerable<PrintJobDto> filteredJobs = jobs;

    if (activeOnly == true)
    {
        filteredJobs = filteredJobs.Where(job => job.Status is PrintJobStatus.Pending or PrintJobStatus.Processing);
    }

    if (statusFilter is not null)
    {
        filteredJobs = filteredJobs.Where(job => job.Status == statusFilter.Value);
    }

    if (limit is not null)
    {
        filteredJobs = filteredJobs.Take(limit.Value);
    }

    return TypedResults.Ok(filteredJobs.ToArray());
})
    .WithName("ListPrintJobs");

protectedApi.MapGet("/print-jobs/queue", async (
    IPrintJobService printJobService,
    CancellationToken cancellationToken) =>
{
    var queueStatus = await printJobService.GetQueueStatusAsync(cancellationToken);
    return TypedResults.Ok(queueStatus);
})
    .WithName("GetPrintQueueStatus");

protectedApi.MapPost("/print-jobs/queue/pause", async (
    IPrintJobService printJobService,
    CancellationToken cancellationToken) =>
{
    var queueStatus = await printJobService.PauseQueueAsync(cancellationToken);
    return TypedResults.Ok(queueStatus);
})
    .WithName("PausePrintQueue");

protectedApi.MapPost("/print-jobs/queue/resume", async (
    IPrintJobService printJobService,
    CancellationToken cancellationToken) =>
{
    var queueStatus = await printJobService.ResumeQueueAsync(cancellationToken);
    return TypedResults.Ok(queueStatus);
})
    .WithName("ResumePrintQueue");

protectedApi.MapPost("/print-jobs/queue/clear", async (
    IPrintJobService printJobService,
    CancellationToken cancellationToken) =>
{
    var response = await printJobService.ClearQueueAsync(cancellationToken);
    return TypedResults.Ok(response);
})
    .WithName("ClearPrintQueue");

protectedApi.MapGet("/print-jobs/{jobId}", async Task<IResult> (
    string jobId,
    IPrintJobService printJobService,
    CancellationToken cancellationToken) =>
{
    var job = await printJobService.GetAsync(jobId, cancellationToken);
    return job is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(job);
})
    .WithName("GetPrintJob");

protectedApi.MapDelete("/print-jobs/{jobId}", async Task<IResult> (
    string jobId,
    IPrintJobService printJobService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var deletedJob = await printJobService.DeleteAsync(jobId, cancellationToken);

        return deletedJob is null
            ? TypedResults.NotFound(new ProblemDetails
            {
                Title = "Print job not found",
                Detail = $"Print job '{jobId}' was not found.",
                Status = StatusCodes.Status404NotFound
            })
            : TypedResults.NoContent();
    }
    catch (InvalidOperationException exception)
    {
        return TypedResults.Conflict(new ProblemDetails
        {
            Title = "Print job cannot be deleted",
            Detail = exception.Message,
            Status = StatusCodes.Status409Conflict
        });
    }
})
    .WithName("DeletePrintJob");

protectedApi.MapPost("/print-jobs/cleanup", async (
    IPrintJobService printJobService,
    CancellationToken cancellationToken) =>
{
    var response = await printJobService.CleanupAsync(cancellationToken);
    return TypedResults.Ok(response);
})
    .WithName("CleanupPrintJobs");

protectedApi.MapPost("/print-jobs/{jobId}/cancel", async Task<IResult> (
    string jobId,
    IPrintJobService printJobService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var canceledJob = await printJobService.CancelAsync(jobId, cancellationToken);

        return canceledJob is null
            ? TypedResults.NotFound(new ProblemDetails
            {
                Title = "Print job not found",
                Detail = $"Print job '{jobId}' was not found.",
                Status = StatusCodes.Status404NotFound
            })
            : TypedResults.Ok(canceledJob);
    }
    catch (InvalidOperationException exception)
    {
        return TypedResults.Conflict(new ProblemDetails
        {
            Title = "Print job cannot be canceled",
            Detail = exception.Message,
            Status = StatusCodes.Status409Conflict
        });
    }
})
    .WithName("CancelPrintJob");

protectedApi.MapPost("/print-jobs/{jobId}/retry", async Task<IResult> (
    string jobId,
    IPrintJobService printJobService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var retriedJob = await printJobService.RetryAsync(jobId, cancellationToken);

        return retriedJob is null
            ? TypedResults.NotFound(new ProblemDetails
            {
                Title = "Print job not found",
                Detail = $"Print job '{jobId}' was not found.",
                Status = StatusCodes.Status404NotFound
            })
            : TypedResults.Created($"/print-jobs/{retriedJob.JobId}", retriedJob);
    }
    catch (InvalidOperationException exception)
    {
        return TypedResults.Conflict(new ProblemDetails
        {
            Title = "Print job cannot be retried",
            Detail = exception.Message,
            Status = StatusCodes.Status409Conflict
        });
    }
})
    .WithName("RetryPrintJob");

app.Run();

const string TestPrintDocumentBase64 = "JVBERi0xLjQKMSAwIG9iajw8Pj5lbmRvYmoKdHJhaWxlcjw8Pj4KJSVFT0YK";

static PrinterDto ToDto(PrinterInfo printer, string? defaultPrinterName = null) =>
    new(
        printer.Id,
        printer.Name,
        IsDefaultPrinter(printer, defaultPrinterName),
        printer.Status);

static PrintHubSettingsDto ToSettingsDto(PrintHubSettings settings) =>
    new(
        settings.ServiceName,
        settings.Port,
        settings.ApiKeyHeaderName,
        settings.ApiKey,
        settings.DefaultPrinterName,
        settings.StorageDirectory,
        settings.MaxUploadSizeBytes);

static SetupStatusDto ToSetupStatusDto(
    PrintHubSettings settings,
    IReadOnlyCollection<PrinterInfo> osPrinters)
{
    var hasApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
    var isOnboardingRequired = !hasApiKey;

    var registeredDtos = settings.Printers.Select(registered =>
    {
        var osMatch = osPrinters.FirstOrDefault(os =>
            string.Equals(os.Id, registered.Id, StringComparison.OrdinalIgnoreCase));
        var status = osMatch?.Status ?? PrinterStatus.Offline;
        return ToDto(new PrinterInfo(registered.Id, registered.Name, false, status),
                     settings.DefaultPrinterName);
    }).ToArray();

    var hasDefaultPrinter = HasResolvedDefaultPrinter(settings, registeredDtos);

    return new SetupStatusDto(
        isOnboardingRequired,
        hasApiKey,
        hasDefaultPrinter,
        registeredDtos);
}

static AutoStartStatusDto ToAutoStartDto(AutoStartRegistration registration) =>
    new(
        registration.IsSupported,
        registration.IsEnabled,
        registration.Provider,
        registration.EntryPath);

static bool IsDefaultPrinter(PrinterInfo printer, string? defaultPrinterName)
{
    if (string.IsNullOrWhiteSpace(defaultPrinterName))
    {
        return printer.IsDefault;
    }

    return string.Equals(printer.Name, defaultPrinterName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(printer.Id, defaultPrinterName, StringComparison.OrdinalIgnoreCase);
}

static bool HasResolvedDefaultPrinter(PrintHubSettings settings, PrinterDto[] printers)
{
    if (printers.Length == 0)
        return !string.IsNullOrWhiteSpace(settings.DefaultPrinterName);

    return printers.Any(p => p.IsDefault);
}

static UpdatePrintHubSettingsRequest ToUpdateRequest(PrintHubSettings settings, string? defaultPrinterName) =>
    new(
        settings.ServiceName,
        settings.Port,
        settings.ApiKeyHeaderName,
        settings.ApiKey,
        defaultPrinterName,
        settings.StorageDirectory,
        settings.MaxUploadSizeBytes);

static CreatePrintJobRequest CreateTestPrintJobRequest(string printerName) =>
    new(
        printerName,
        1,
        new PrintDocumentRequest(
            DocumentSourceType.Base64,
            PrintDocumentFormat.Pdf,
            Url: null,
            Data: TestPrintDocumentBase64,
            FileName: "print-hub-test-page.pdf"));

static IPrintBackend ResolvePrintBackend(
    PrintBackendMode mode,
    MockPrintBackend mockBackend,
    LpPrintBackend cupsBackend,
    WindowsPrintBackend windowsBackend,
    ILogger logger)
{
    if (!Enum.IsDefined(mode))
    {
        logger.LogWarning("Unsupported print backend mode '{BackendMode}'. Falling back to automatic selection.", mode);
        mode = PrintBackendMode.Auto;
    }

    switch (mode)
    {
        case PrintBackendMode.Mock:
            logger.LogInformation("Using mock print backend.");
            return mockBackend;
        case PrintBackendMode.System:
            if (TryResolveSystemPrintBackend(cupsBackend, windowsBackend, logger) is not { } systemBackend)
            {
                throw new InvalidOperationException(
                    "PrintHub is configured to use the system print backend, but no supported platform backend is available on this machine.");
            }

            return systemBackend;
        case PrintBackendMode.Auto:
        default:
            if (TryResolveSystemPrintBackend(cupsBackend, windowsBackend, logger) is { } availableSystemBackend)
            {
                return availableSystemBackend;
            }

            logger.LogWarning("System print backend is unavailable. Falling back to the mock backend.");
            return mockBackend;
    }
}

static IPrintBackend? TryResolveSystemPrintBackend(
    LpPrintBackend cupsBackend,
    WindowsPrintBackend windowsBackend,
    ILogger logger)
{
    if (WindowsPrintBackend.IsSupported())
    {
        logger.LogInformation("Using Windows print backend.");
        return windowsBackend;
    }

    if (LpPrintBackend.IsSupported())
    {
        logger.LogInformation("Using system print backend via lp/cups.");
        return cupsBackend;
    }

    return null;
}

public partial class Program
{
}
