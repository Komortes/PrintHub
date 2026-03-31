using System.Net;
using System.Net.Http.Json;
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
}
