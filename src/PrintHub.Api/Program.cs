using PrintHub.Api.Workers;
using PrintHub.Core.Backends;
using PrintHub.Core.Queues;
using PrintHub.Core.Repositories;
using PrintHub.Core.Services;
using PrintHub.Infrastructure.Backends;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPrintJobQueue, InMemoryPrintJobQueue>();
builder.Services.AddSingleton<IPrintJobStore, InMemoryPrintJobStore>();
builder.Services.AddSingleton<IPrintJobService, PrintJobService>();
builder.Services.AddSingleton<IPrintBackend, MockPrintBackend>();
builder.Services.AddHostedService<PrintJobWorker>();

var app = builder.Build();
var summaries =
    new[]
    {
        "Freezing",
        "Bracing",
        "Chilly",
        "Cool",
        "Mild",
        "Warm",
        "Balmy",
        "Hot",
        "Sweltering",
        "Scorching"
    };

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5)
        .Select(index => new WeatherForecast(
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]))
        .ToArray();

    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

internal sealed record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
