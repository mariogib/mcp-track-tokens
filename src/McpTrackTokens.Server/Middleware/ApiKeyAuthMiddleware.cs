using McpTrackTokens.Application.Interfaces;

namespace McpTrackTokens.Server.Middleware;

/// <summary>
/// Requires a valid Bearer tracking API key for protected API routes.
/// </summary>
public sealed class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IApiKeyService apiKeyService)
    {
        if (!RequiresAuthentication(context.Request))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await WriteUnauthorizedAsync(context).ConfigureAwait(false);
            return;
        }

        var key = header["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(key) ||
            !await apiKeyService.VerifyAsync(key, context.RequestAborted).ConfigureAwait(false))
        {
            await WriteUnauthorizedAsync(context).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    private static bool RequiresAuthentication(HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            if (path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/ready", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Static dashboard assets under wwwroot remain public.
            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static Task WriteUnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return context.Response.WriteAsJsonAsync(new
        {
            error = "A valid Authorization Bearer tracking API key is required."
        });
    }
}
