using System.Net;
using Microsoft.AspNetCore.Mvc;
using PrintHub.Core.Settings;

namespace PrintHub.Api.Auth;

public sealed class ApiKeyEndpointFilter : IEndpointFilter
{
    private const string AuthorizationHeaderName = "Authorization";
    private const string BearerScheme = "Bearer";
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

        var providedApiKey = ResolveProvidedApiKey(
            context.HttpContext.Request,
            settings.ApiKeyHeaderName);

        if (string.IsNullOrWhiteSpace(providedApiKey) ||
            !string.Equals(providedApiKey, settings.ApiKey, StringComparison.Ordinal))
        {
            context.HttpContext.Response.Headers.WWWAuthenticate = $"{BearerScheme} realm=\"PrintHub\"";
            return TypedResults.Unauthorized();
        }

        return await next(context);
    }

    private static string? ResolveProvidedApiKey(
        HttpRequest request,
        string configuredHeaderName)
    {
        var bearerToken = TryGetBearerToken(request.Headers[AuthorizationHeaderName].ToString());

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            return bearerToken;
        }

        if (string.IsNullOrWhiteSpace(configuredHeaderName) ||
            string.Equals(configuredHeaderName, AuthorizationHeaderName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return request.Headers[configuredHeaderName].ToString();
    }

    private static string? TryGetBearerToken(string authorizationHeaderValue)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeaderValue))
        {
            return null;
        }

        var parts = authorizationHeaderValue.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 2 ||
            !string.Equals(parts[0], BearerScheme, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(parts[1]))
        {
            return null;
        }

        return parts[1];
    }

    private static bool IsLocalRequest(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress is null ||
        IPAddress.IsLoopback(httpContext.Connection.RemoteIpAddress);
}
