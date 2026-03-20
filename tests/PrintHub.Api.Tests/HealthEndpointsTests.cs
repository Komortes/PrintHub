using System.Net;
using System.Net.Http.Json;
using PrintHub.Contracts.Diagnostics;
using PrintHub.Api.Tests.Infrastructure;

namespace PrintHub.Api.Tests;

public sealed class HealthEndpointsTests
{
    [Fact]
    public async Task GetHealth_ReturnsHealthyWithoutAuthentication()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.NotNull(payload);
        Assert.Equal("healthy", payload!.Status);
        Assert.Equal("PrintHub.Test", payload.Service);
    }
}
