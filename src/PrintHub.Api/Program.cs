using PrintHub.Api.Workers;
using PrintHub.Api.Auth;
using PrintHub.Api.Configuration;
using PrintHub.Api.Requests;
using PrintHub.Contracts.Diagnostics;
using PrintHub.Contracts.Printers;
using PrintHub.Core.Backends;
using PrintHub.Core.Models;
using PrintHub.Core.Queues;
using PrintHub.Core.Repositories;
using PrintHub.Core.Services;
using PrintHub.Infrastructure.Backends;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddOpenApi();
builder.Services
    .AddOptions<PrintHubApiOptions>()
    .Bind(builder.Configuration.GetSection(PrintHubApiOptions.SectionName));
PrintJobRequestParser.ConfigureFormOptions(builder.Services, builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPrintJobQueue, InMemoryPrintJobQueue>();
builder.Services.AddSingleton<IPrintJobStore, InMemoryPrintJobStore>();
builder.Services.AddSingleton<IPrintJobService, PrintJobService>();
builder.Services.AddSingleton<IPrintBackend, MockPrintBackend>();
builder.Services.AddHostedService<PrintJobWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var protectedApi = app.MapGroup(string.Empty)
    .AddEndpointFilter<ApiKeyEndpointFilter>();

app.MapGet("/health", (TimeProvider timeProvider, IOptions<PrintHubApiOptions> options) =>
    TypedResults.Ok(new HealthResponse(
        Status: "healthy",
        Service: options.Value.ServiceName,
        Timestamp: timeProvider.GetUtcNow())))
    .WithName("GetHealth");

protectedApi.MapGet("/printers", async (IPrintBackend backend, CancellationToken cancellationToken) =>
{
    var printers = await backend.GetPrintersAsync(cancellationToken);
    return TypedResults.Ok(printers.Select(ToDto).ToArray());
})
    .WithName("GetPrinters");

protectedApi.MapPost("/print-jobs", async Task<IResult> (
    HttpRequest request,
    IPrintJobService printJobService,
    IOptions<PrintHubApiOptions> options,
    CancellationToken cancellationToken) =>
{
    try
    {
        var createRequest = await PrintJobRequestParser.ParseAsync(request, options, cancellationToken);
        var createdJob = await printJobService.CreateAsync(createRequest, cancellationToken);

        return TypedResults.Created($"/print-jobs/{createdJob.JobId}", createdJob);
    }
    catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or JsonException or ArgumentException)
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

static PrinterDto ToDto(PrinterInfo printer) =>
    new(printer.Id, printer.Name, printer.IsDefault, printer.Status);
