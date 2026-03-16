using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PrintHub.Api.Configuration;

namespace PrintHub.Api.Auth;

public sealed class ApiKeyEndpointFilter : IEndpointFilter
{
    private readonly IOptions<PrintHubApiOptions> _options;
    private readonly ILogger<ApiKeyEndpointFilter> _logger;

    public ApiKeyEndpointFilter(
        IOptions<PrintHubApiOptions> options,
        ILogger<ApiKeyEndpointFilter> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var settings = _options.Value;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            _logger.LogError("API key is not configured. Protected endpoints are unavailable.");

            return TypedResults.Problem(new ProblemDetails
            {
                Title = "API key is not configured",
                Detail = "Set PrintHub:ApiKey in configuration before calling protected endpoints.",
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }

        var request = context.HttpContext.Request;
        var providedApiKey = request.Headers[settings.ApiKeyHeaderName].ToString();

        if (string.IsNullOrWhiteSpace(providedApiKey) ||
            !string.Equals(providedApiKey, settings.ApiKey, StringComparison.Ordinal))
        {
            return TypedResults.Unauthorized();
        }

        return await next(context);
    }
}
