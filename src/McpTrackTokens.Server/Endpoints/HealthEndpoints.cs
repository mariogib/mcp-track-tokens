using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using McpTrackTokens.Application.Options;
using McpTrackTokens.Infrastructure.Persistence;

namespace McpTrackTokens.Server.Endpoints;

/// <summary>
/// Public health and readiness endpoints.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            service = "mcp-track-tokens",
            timestampUtc = DateTimeOffset.UtcNow
        }));

        app.MapGet("/ready", async (
            TrackingDbContext db,
            IOptions<TrackingOptions> options,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var canConnect = await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
                if (!canConnect)
                {
                    return Results.Json(new
                    {
                        status = "not_ready",
                        database = "unreachable",
                        path = options.Value.GetResolvedDatabasePath()
                    }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                return Results.Ok(new
                {
                    status = "ready",
                    databaseProvider = options.Value.DatabaseProvider,
                    databasePath = options.Value.GetResolvedDatabasePath()
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    status = "not_ready",
                    error = ex.Message
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        return app;
    }
}
