using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PrintHub.Api.Tests.Infrastructure;
using PrintHub.Contracts.Printers;

namespace PrintHub.Api.Tests;

public sealed class PrinterDiagnosticsTests
{
    private static HttpRequestMessage Authed(HttpMethod method, string url) =>
        new(method, url) { Headers = { { "X-PrintHub-Api-Key", "test-api-key" } } };

    [Fact]
    public async Task GetPrinterDiagnostics_ReturnsMockBackendDiagnostics()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Authed(HttpMethod.Get, "/printers/diagnostics"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var diagnostics = await response.Content.ReadFromJsonAsync<PrintBackendDiagnosticsDto>(TestJson.SerializerOptions);

        Assert.NotNull(diagnostics);
        Assert.Equal("mock", diagnostics!.Backend);
        Assert.True(diagnostics.IsSupported);
        Assert.Equal(2, diagnostics.Printers.Count);
        Assert.Contains(diagnostics.Checks, check => check.Code == "backend-mode");
        Assert.Contains(diagnostics.Recommendations, recommendation => recommendation.Contains("BackendMode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetDiagnosticsReport_ReturnsPlainText_WithoutApiKeyValue()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Authed(HttpMethod.Get, "/diagnostics/report"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);

        var report = await response.Content.ReadAsStringAsync();

        Assert.Contains("PrintHub Diagnostics Report", report);
        Assert.Contains("ServiceName: PrintHub.Test", report);
        Assert.Contains("Backend", report);
        Assert.Contains("Name: mock", report);
        Assert.Contains("ApiKeyConfigured: yes", report);
        Assert.DoesNotContain("test-api-key", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSupportBundle_ReturnsZip_WithoutApiKeyValue()
    {
        using var factory = new PrintHubApiFactory();
        var logsDirectoryPath = Path.Combine(factory.TempRootPath, "logs");
        Directory.CreateDirectory(logsDirectoryPath);
        await File.WriteAllTextAsync(
            Path.Combine(logsDirectoryPath, "printhub.log"),
            "support-bundle-log-line");

        using var client = factory.CreateClient();

        var response = await client.SendAsync(Authed(HttpMethod.Get, "/diagnostics/support-bundle"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

        await using var archiveStream = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("diagnostics/report.txt"));
        Assert.NotNull(archive.GetEntry("diagnostics/backend-diagnostics.json"));
        Assert.NotNull(archive.GetEntry("settings/settings.public.json"));
        Assert.NotNull(archive.GetEntry("queue/status.json"));
        Assert.NotNull(archive.GetEntry("jobs/recent-jobs.json"));
        Assert.NotNull(archive.GetEntry("runtime/paths.json"));
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.NotNull(archive.GetEntry("logs/printhub.log"));

        var reportEntry = archive.GetEntry("diagnostics/report.txt")!;
        using var reportReader = new StreamReader(reportEntry.Open());
        var report = await reportReader.ReadToEndAsync();
        Assert.Contains("PrintHub Diagnostics Report", report);
        Assert.DoesNotContain("test-api-key", report, StringComparison.Ordinal);

        var settingsEntry = archive.GetEntry("settings/settings.public.json")!;
        using var settingsReader = new StreamReader(settingsEntry.Open());
        using var settingsDocument = JsonDocument.Parse(await settingsReader.ReadToEndAsync());
        Assert.True(settingsDocument.RootElement.GetProperty("apiKeyConfigured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, settingsDocument.RootElement.GetProperty("apiKey").ValueKind);

        var logEntry = archive.GetEntry("logs/printhub.log")!;
        using var logReader = new StreamReader(logEntry.Open());
        var logContent = await logReader.ReadToEndAsync();
        Assert.Contains("support-bundle-log-line", logContent);
    }
}
