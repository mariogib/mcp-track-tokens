using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.Options;

namespace McpTrackTokens.Server.Middleware;

/// <summary>
/// Rejects requests whose Content-Length exceeds the configured maximum
/// (<see cref="TrackingOptions.MaxRequestBytes"/>, or
/// <see cref="TrackingOptions.MaxBackupUploadBytes"/> for database restore uploads).
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
        var tracking = options.Value;
        var path = context.Request.Path.Value ?? string.Empty;
        var isBackupUpload = path.Contains("/database/restore-upload", StringComparison.OrdinalIgnoreCase);
        var maxBytes = isBackupUpload
            ? tracking.MaxBackupUploadBytes
            : tracking.MaxRequestBytes;

        if (isBackupUpload)
        {
            var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
            {
                feature.MaxRequestBodySize = maxBytes > 0 ? maxBytes : null;
            }
        }

        if (maxBytes > 0 &&
            context.Request.ContentLength is long length &&
            length > maxBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new
            {
                error = isBackupUpload
                    ? $"Backup file exceeds the restore upload limit ({maxBytes} bytes)."
                    : "Request body exceeds configured maximum size.",
                maxRequestBytes = maxBytes
            }).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }
}
