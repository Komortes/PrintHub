using System.Net;
using Microsoft.AspNetCore.Mvc;
using PrintHub.Core.Settings;

namespace PrintHub.Api.Auth;

public sealed class ApiKeyEndpointFilter : IEndpointFilter
{
    private readonly IPrintHubSettingsService _settingsService;

    public ApiKeyEndpointFilter(IPrintHubSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        // Local requests (browser UI on the same machine) always pass through.
        // API key is only required for external integrations.
        if (IsLocalRequest(context.HttpContext))
        {
            return await next(context);
        }

        var settings = await _settingsService.GetAsync(context.HttpContext.RequestAborted);

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "API key is not configured",
                Detail = "Configure an API key before allowing external access.",
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }

        var providedApiKey = context.HttpContext.Request.Headers[settings.ApiKeyHeaderName].ToString();

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
