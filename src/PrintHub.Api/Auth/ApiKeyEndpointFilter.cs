using System.Net;
using Microsoft.AspNetCore.Mvc;
using PrintHub.Core.Settings;

namespace PrintHub.Api.Auth;

public sealed class ApiKeyEndpointFilter : IEndpointFilter
{
    private readonly IPrintHubSettingsService _settingsService;
    private readonly ILogger<ApiKeyEndpointFilter> _logger;

    public ApiKeyEndpointFilter(
        IPrintHubSettingsService settingsService,
        ILogger<ApiKeyEndpointFilter> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var settings = await _settingsService.GetAsync(context.HttpContext.RequestAborted);
        var request = context.HttpContext.Request;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            if (request.Path.StartsWithSegments("/settings") && IsLocalRequest(context.HttpContext))
            {
                return await next(context);
            }

            _logger.LogError("API key is not configured. Protected endpoints are unavailable.");

            return TypedResults.Problem(new ProblemDetails
            {
                Title = "API key is not configured",
                Detail = "Configure an API key via /settings from localhost before calling protected endpoints.",
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }

        var providedApiKey = request.Headers[settings.ApiKeyHeaderName].ToString();

        if (string.IsNullOrWhiteSpace(providedApiKey) ||
            !string.Equals(providedApiKey, settings.ApiKey, StringComparison.Ordinal))
        {
            return TypedResults.Unauthorized();
        }

        return await next(context);
    }

    private static bool IsLocalRequest(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress is null ||
        IPAddress.IsLoopback(httpContext.Connection.RemoteIpAddress);
}
