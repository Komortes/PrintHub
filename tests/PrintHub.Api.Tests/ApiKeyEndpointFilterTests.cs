using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PrintHub.Api.Auth;
using PrintHub.Core.Settings;

namespace PrintHub.Api.Tests;

public sealed class ApiKeyEndpointFilterTests
{
    [Fact]
    public async Task ExternalRequest_IsAuthorized_WithBearerHeader()
    {
        var filter = CreateFilter();
        var httpContext = CreateExternalHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer test-api-key";
        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        var result = await filter.InvokeAsync(
            invocationContext,
            _ => ValueTask.FromResult<object?>("next-called"));

        Assert.Equal("next-called", result);
    }

    [Fact]
    public async Task ExternalRequest_IsAuthorized_WithLegacyApiKeyHeader()
    {
        var filter = CreateFilter();
        var httpContext = CreateExternalHttpContext();
        httpContext.Request.Headers["X-PrintHub-Api-Key"] = "test-api-key";
        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        var result = await filter.InvokeAsync(
            invocationContext,
            _ => ValueTask.FromResult<object?>("next-called"));

        Assert.Equal("next-called", result);
    }

    [Fact]
    public async Task ExternalRequest_ReturnsUnauthorized_WithInvalidBearerHeader()
    {
        var filter = CreateFilter();
        var httpContext = CreateExternalHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer wrong-key";
        var invocationContext = new TestEndpointFilterInvocationContext(httpContext);

        var result = await filter.InvokeAsync(
            invocationContext,
            _ => ValueTask.FromResult<object?>("next-called"));

        var unauthorizedResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
        Assert.Equal("Bearer realm=\"PrintHub\"", httpContext.Response.Headers.WWWAuthenticate);
    }

    private static ApiKeyEndpointFilter CreateFilter() =>
        new(new StubPrintHubSettingsService(
            PrintHubSettings.CreateDefaults(
                "PrintHub.Test",
                5051,
                "X-PrintHub-Api-Key",
                "test-api-key",
                null,
                "data/documents",
                1024)));

    private static DefaultHttpContext CreateExternalHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        httpContext.RequestServices = new ServiceCollection().BuildServiceProvider();
        return httpContext;
    }

    private sealed class TestEndpointFilterInvocationContext(HttpContext httpContext) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext => httpContext;

        public override IList<object?> Arguments { get; } = [];

        public override T GetArgument<T>(int index) =>
            (T)Arguments[index]!;
    }

    private sealed class StubPrintHubSettingsService(PrintHubSettings settings) : IPrintHubSettingsService
    {
        public ValueTask<PrintHubSettings> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(settings);

        public ValueTask<PrintHubSettings> UpdateAsync(
            PrintHub.Contracts.Settings.UpdatePrintHubSettingsRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<PrintHubSettings> AddPrinterAsync(
            string id,
            string name,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<PrintHubSettings> RemovePrinterAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
