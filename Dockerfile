# Multi-stage build for MCP Track Tokens (Server + Dashboard)
# Build: docker build -t mcp-track-tokens .
# Run:   docker compose up

# ---- Dashboard (Vite) ----
FROM node:22-alpine AS dashboard
WORKDIR /src/dashboard
COPY src/McpTrackTokens.Dashboard/package.json src/McpTrackTokens.Dashboard/package-lock.json ./
RUN npm ci
COPY src/McpTrackTokens.Dashboard/ ./
RUN npm run build

# ---- .NET publish ----
# Restore/publish the CLI project only (tests/ are excluded via .dockerignore).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/McpTrackTokens.Domain/ src/McpTrackTokens.Domain/
COPY src/McpTrackTokens.Application/ src/McpTrackTokens.Application/
COPY src/McpTrackTokens.Infrastructure/ src/McpTrackTokens.Infrastructure/
COPY src/McpTrackTokens.Server/ src/McpTrackTokens.Server/
COPY src/McpTrackTokens.Cli/ src/McpTrackTokens.Cli/
RUN dotnet restore src/McpTrackTokens.Cli/McpTrackTokens.Cli.csproj
RUN dotnet publish src/McpTrackTokens.Cli/McpTrackTokens.Cli.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Copy dashboard into Server wwwroot inside publish output
COPY --from=dashboard /src/dashboard/dist /app/publish/wwwroot

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system --gid 10001 mtt \
    && useradd --system --uid 10001 --gid mtt --home-dir /data --create-home mtt

COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://0.0.0.0:5187 \
    MCP_TRACK_TOKENS_BIND_ADDRESS=http://0.0.0.0:5187 \
    MCP_TRACK_TOKENS_SERVER_URL=http://127.0.0.1:5187 \
    MCP_TRACK_TOKENS_DATABASE_PATH=/data/mcp-track-tokens.db \
    MCP_TRACK_TOKENS_EXPORT_PATH=/data/exports/ \
    MCP_TRACK_TOKENS_LOG_PATH=/data/logs/ \
    MCP_TRACK_TOKENS_QUEUE_PATH=/data/queue/ \
    MCP_TRACK_TOKENS_ENCRYPTION_KEY_PATH=/data/encryption.key \
    MCP_TRACK_TOKENS_MIGRATE_ON_STARTUP=true

VOLUME ["/data"]
EXPOSE 5187

USER mtt
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fsS http://127.0.0.1:5187/health || exit 1

ENTRYPOINT ["dotnet", "mcp-track-tokens.dll", "serve", "--http", "--migrate"]
