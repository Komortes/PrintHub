using System.Net;
using System.Net.Http.Json;
using PrintHub.Api.Tests.Infrastructure;
using PrintHub.Contracts.Printers;

namespace PrintHub.Api.Tests;

public sealed class BackendSelectionTests
{
    [Fact]
    public async Task PrintersEndpoint_WithAutoBackend_ReturnsOk()
    {
        using var factory = new PrintHubApiFactory(new Dictionary<string, string?>
        {
            ["PrintHub:BackendMode"] = "Auto"
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/printers");
        request.Headers.Add("X-PrintHub-Api-Key", "test-api-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var printers = await response.Content.ReadFromJsonAsync<PrinterDto[]>(TestJson.SerializerOptions);

        Assert.NotNull(printers);
    }
}
