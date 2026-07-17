# MCP Track Tokens Dashboard

React + TypeScript + Vite UI for local AI activity, usage imports, and cost allocation.

## Stack

- React 18
- TypeScript
- Vite
- TanStack Query
- React Router v6
- Recharts

## Setup

```bash
cd src/McpTrackTokens.Dashboard
npm install
cp .env.example .env   # optional; defaults to http://127.0.0.1:5187
```

## Scripts

| Script | Description |
| --- | --- |
| `npm run dev` | Vite dev server (proxies `/api`, `/health`, `/ready`) |
| `npm run build` | Typecheck + production build to `dist/` |
| `npm run preview` | Preview the production build |
| `npm run lint` | ESLint |
| `npm run test` | Vitest + React Testing Library |

## API client

- Base URL: `import.meta.env.VITE_API_URL` (default `http://127.0.0.1:5187`)
- Auth: `Authorization: Bearer <key>` from `localStorage` key `mcp-track-tokens-api-key`
- Endpoints under `/api/v1/*` plus `/health` and `/ready`

Store or create an API key on the **Settings** page.

## Themes

Light and dark themes via CSS variables (`data-theme`). Typography uses IBM Plex Sans / Mono.

## Post-build: copy to Server wwwroot

`vite` writes assets to `src/McpTrackTokens.Dashboard/dist` (`outDir: 'dist'`, `emptyOutDir: true`).

The ASP.NET Server serves the dashboard from `src/McpTrackTokens.Server/wwwroot` when present. After a successful build, copy the output:

```bash
# from repo root (PowerShell)
npm --prefix src/McpTrackTokens.Dashboard run build
Remove-Item -Recurse -Force src/McpTrackTokens.Server/wwwroot -ErrorAction SilentlyContinue
Copy-Item -Recurse src/McpTrackTokens.Dashboard/dist src/McpTrackTokens.Server/wwwroot
```

Install scripts should perform this copy step so a packaged Server includes the latest dashboard.
