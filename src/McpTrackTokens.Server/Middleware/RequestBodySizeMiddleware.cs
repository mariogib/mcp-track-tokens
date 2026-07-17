using Microsoft.Extensions.Options;
using McpTrackTokens.Application.Options;

namespace McpTrackTokens.Server.Middleware;

/// <summary>
/// Rejects requests whose Content-Length exceeds <see cref="TrackingOptions.MaxRequestBytes"/>.
/// </summary>
public sealed class RequestBodySizeMiddleware
{
    private readonly RequestDelegate _next;

    public RequestBodySizeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<TrackingOptions> options)
    {
        var maxBytes = options.Value.MaxRequestBytes;
        if (maxBytes > 0 &&
            context.Request.ContentLength is long length &&
            length > maxBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Request body exceeds configured maximum size.",
                maxRequestBytes = maxBytes
            }).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }
}
