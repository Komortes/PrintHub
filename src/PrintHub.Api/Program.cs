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
using PrintHub.Core.Queues;
using PrintHub.Core.Repositories;
using PrintHub.Core.Settings;
using PrintHub.Core.Services;
using PrintHub.Infrastructure.Backends;
using PrintHub.Infrastructure.Documents;
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

builder.Services.AddOpenApi();
var fileLoggerOptions = builder.Configuration
    .GetSection(PrintHubFileLoggerOptions.SectionName)
    .Get<PrintHubFileLoggerOptions>()
    ?? new PrintHubFileLoggerOptions();

if (fileLoggerOptions.Enabled)
{
    builder.Logging.AddProvider(new PrintHubFileLoggerProvider(AppContext.BaseDirectory, fileLoggerOptions));
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
builder.Services
    .AddOptions<PrintHubApiOptions>()
    .Bind(builder.Configuration.GetSection(PrintHubApiOptions.SectionName));
PrintJobRequestParser.ConfigureFormOptions(builder.Services, builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPrintJobQueue, InMemoryPrintJobQueue>();
builder.Services.AddSingleton<IPrintJobStore, InMemoryPrintJobStore>();
builder.Services.AddSingleton<IPrintBackend, MockPrintBackend>();
builder.Services.AddSingleton<IPrintHubSettingsStore>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<PrintHubApiOptions>>().Value;
    return new JsonPrintHubSettingsStore(AppContext.BaseDirectory, options.SettingsFilePath);
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

protectedApi.MapGet("/printers", async (
    IPrintBackend backend,
    IPrintHubSettingsService settingsService,
    CancellationToken cancellationToken) =>
{
    var printers = await backend.GetPrintersAsync(cancellationToken);
    var settings = await settingsService.GetAsync(cancellationToken);
    return TypedResults.Ok(printers.Select(printer => ToDto(printer, settings.DefaultPrinterName)).ToArray());
})
    .WithName("GetPrinters");

protectedApi.MapPut("/printers/{printerId}/default", async Task<IResult> (
    string printerId,
    IPrintBackend backend,
    IPrintHubSettingsService settingsService,
    CancellationToken cancellationToken) =>
{
    var printers = await backend.GetPrintersAsync(cancellationToken);
    var printer = FindPrinter(printers, printerId);

    if (printer is null)
    {
        return TypedResults.NotFound();
    }

    var settings = await settingsService.GetAsync(cancellationToken);
    var updatedSettings = await settingsService.UpdateAsync(
        ToUpdateRequest(settings, printer.Name),
        cancellationToken);

    return TypedResults.Ok(ToDto(printer, updatedSettings.DefaultPrinterName));
})
    .WithName("SetDefaultPrinter");

protectedApi.MapPost("/printers/{printerId}/test-print", async Task<IResult> (
    string printerId,
    IPrintBackend backend,
    IPrintJobService printJobService,
    CancellationToken cancellationToken) =>
{
    var printers = await backend.GetPrintersAsync(cancellationToken);
    var printer = FindPrinter(printers, printerId);

    if (printer is null)
    {
        return TypedResults.NotFound();
    }

    var createdJob = await printJobService.CreateAsync(
        CreateTestPrintJobRequest(printer.Name),
        cancellationToken);

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

static bool IsDefaultPrinter(PrinterInfo printer, string? defaultPrinterName)
{
    if (string.IsNullOrWhiteSpace(defaultPrinterName))
    {
        return printer.IsDefault;
    }

    return string.Equals(printer.Name, defaultPrinterName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(printer.Id, defaultPrinterName, StringComparison.OrdinalIgnoreCase);
}

static PrinterInfo? FindPrinter(IEnumerable<PrinterInfo> printers, string printerId)
{
    if (string.IsNullOrWhiteSpace(printerId))
    {
        return null;
    }

    return printers.FirstOrDefault(printer =>
        string.Equals(printer.Id, printerId, StringComparison.OrdinalIgnoreCase));
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
