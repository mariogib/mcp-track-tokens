import { type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useTabSearchParam } from '../hooks/useTabSearchParam';
import { Page } from '../layout/AppLayout';

const HELP_TABS = ['Overview', 'Cursor setup'] as const;

function CodeBlock({ children }: { children: string }) {
  return (
    <pre className="mono" style={{ whiteSpace: 'pre-wrap', overflowX: 'auto' }}>
      <code>{children}</code>
    </pre>
  );
}

function Step({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="stack" style={{ gap: '0.5rem' }}>
      <h3>{title}</h3>
      <div className="stack">{children}</div>
    </div>
  );
}

export function HelpPage() {
  const [tab, setTab] = useTabSearchParam(HELP_TABS, 'Cursor setup');

  return (
    <Page>
      <div className="tabs" role="tablist" aria-label="Help sections">
        {HELP_TABS.map((name) => (
          <button
            key={name}
            type="button"
            role="tab"
            aria-selected={tab === name}
            className={`tab${tab === name ? ' active' : ''}`}
            onClick={() => setTab(name)}
          >
            {name}
          </button>
        ))}
      </div>

      {tab === 'Overview' && (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Help</h2>
              <p>
                This guide is for the Windows installer (tray host + desktop dashboard). MCP Track
                Tokens correlates editor activity with imported Cursor usage so you can attribute
                time and cost to projects.
              </p>
            </div>
          </div>
          <div className="panel stack">
            <p>
              Start with the <strong>Cursor setup</strong> tab to connect Cursor. Then open{' '}
              <Link to="/settings">Settings</Link> to confirm your API key, and use{' '}
              <Link to="/imported-usage">Imported usage</Link> for Cursor usage exports. For the MCP tool,
              resource, and prompt catalog, open <Link to="/help/mcp">MCP Help</Link>.
            </p>
            <ul>
              <li>
                Installer: <code className="mono">MCP-Track-Tokens-Setup.msi</code>
              </li>
              <li>
                Tray host keeps the API and dashboard available at{' '}
                <code className="mono">http://127.0.0.1:5187</code>
              </li>
              <li>
                Desktop app: open from the Start Menu or tray <strong>Open dashboard</strong>
              </li>
              <li>
                Default API key: <code className="mono">OverTheMoon</code> (change under Settings if
                you create your own keys)
              </li>
            </ul>
          </div>
        </section>
      )}

      {tab === 'Cursor setup' && (
        <section className="page-section">
          <div className="section-header">
            <div>
              <h2>Cursor setup</h2>
              <p>
                For the Windows install. Install activity hooks, set environment variables, and
                point Cursor MCP at the local tray host. Cursor settings are never rewritten
                automatically — merge the examples below yourself.
              </p>
            </div>
          </div>

          <div className="panel stack">
            <Step title="1. Install and start the Windows host">
              <p>
                Run <code className="mono">MCP-Track-Tokens-Setup.msi</code>. Leave these options
                enabled if prompted:
              </p>
              <ul>
                <li>Start MCP Track Tokens when Windows starts</li>
                <li>Install Cursor hooks</li>
                <li>Install VS Code / Cursor extension</li>
                <li>Start MCP Track Tokens now</li>
              </ul>
              <p>
                Confirm the tray icon is running and{' '}
                <code className="mono">http://127.0.0.1:5187/health</code> returns healthy. Open the
                dashboard from the tray menu or the desktop app.
              </p>
            </Step>

            <Step title="2. Cursor hooks on disk">
              <p>
                With <strong>Install Cursor hooks</strong> selected, setup copies scripts to{' '}
                <code className="mono">%USERPROFILE%\.cursor\mcp-track-tokens-hooks\</code> and
                writes{' '}
                <code className="mono">%USERPROFILE%\.cursor\mcp-track-tokens-hooks.example.json</code>
                .
              </p>
              <p className="hint">
                If hooks were skipped during setup, re-run the installer with that option checked,
                or copy the hooks package from{' '}
                <code className="mono">Program Files\MCP Track Tokens\integrations\cursor-hooks</code>{' '}
                into <code className="mono">%USERPROFILE%\.cursor\mcp-track-tokens-hooks</code>.
              </p>
            </Step>

            <Step title="3. Merge hooks into Cursor">
              <p>
                Open Cursor Settings → Hooks (or your Cursor hooks config file) and wire the paths.
                Paths are relative to <code className="mono">%USERPROFILE%\.cursor</code>:
              </p>
              <CodeBlock>{`{
  "version": 1,
  "serverUrl": "http://127.0.0.1:5187",
  "apiKeyEnv": "MCP_TRACK_TOKENS_API_KEY",
  "hooks": {
    "promptSubmitted": "./mcp-track-tokens-hooks/dist/prompt-submitted.js",
    "agentStarted": "./mcp-track-tokens-hooks/dist/agent-started.js",
    "agentCompleted": "./mcp-track-tokens-hooks/dist/agent-completed.js",
    "sessionStarted": "./mcp-track-tokens-hooks/dist/session-started.js",
    "sessionEnded": "./mcp-track-tokens-hooks/dist/session-ended.js"
  }
}`}</CodeBlock>
              <p className="hint">
                Adjust event names if your Cursor version uses different hook keys. Include
                agentFailed / agentCancelled when available.
              </p>
            </Step>

            <Step title="4. Set Windows environment variables for hooks">
              <p>
                Hooks need a Bearer key to POST events. Set these as <strong>User</strong>{' '}
                environment variables (Windows Settings → System → About → Advanced system settings
                → Environment Variables), then restart Cursor:
              </p>
              <CodeBlock>{`MCP_TRACK_TOKENS_API_KEY=OverTheMoon
MCP_TRACK_TOKENS_SERVER_URL=http://127.0.0.1:5187`}</CodeBlock>
              <p>
                Use the same key under <Link to="/settings">Settings → Connection</Link> in the
                desktop dashboard.
              </p>
            </Step>

            <Step title="5. Configure Cursor MCP (HTTP to the tray host)">
              <p>
                With the tray host running, add this MCP server entry in Cursor (HTTP MCP shares the
                same local database as the dashboard):
              </p>
              <CodeBlock>{`{
  "mcpServers": {
    "mcp-track-tokens": {
      "url": "http://127.0.0.1:5187/mcp",
      "headers": {
        "Authorization": "Bearer OverTheMoon"
      }
    }
  }
}`}</CodeBlock>
              <p className="hint">
                Replace the Bearer value if you created a different API key. The tray host must be
                running for MCP tools and hooks to reach the API.
              </p>
            </Step>

            <Step title="6. Verify">
              <ul>
                <li>
                  <Link to="/settings">Settings → Integrations</Link> shows Cursor hooks configured
                  or inferred from activity.
                </li>
                <li>
                  Submit a prompt in Cursor, then check <Link to="/">Overview</Link> for new
                  activity.
                </li>
              </ul>
            </Step>

            <Step title="7. Import Cursor usage (for costs)">
              <p>
                Hooks track activity. Token and dollar costs come from Cursor usage exports you
                download and import.
              </p>
              <ol>
                <li>
                  In Cursor, open usage / billing and export your usage data (CSV or JSON), or use
                  the export Cursor provides for the period you care about.
                </li>
                <li>
                  Open the dashboard <Link to="/imported-usage">Imported usage</Link> page.
                </li>
                <li>
                  In <strong>Upload &amp; map</strong>, choose the export file and run{' '}
                  <strong>Preview</strong>.
                </li>
                <li>
                  Confirm or adjust column mappings, then import (use dry-run first if you want a
                  safe check).
                </li>
                <li>
                  Review imported rows below the upload section, then use{' '}
                  <strong>Allocate all</strong> when needed and check project costs under{' '}
                  <Link to="/projects">Projects</Link>.
                </li>
              </ol>
              <p className="hint">
                Related pages:{' '}
                <Link to="/imported-usage">Imported usage</Link>
                {' · '}
                <Link to="/projects">Projects</Link>
                {' · '}
                <Link to="/">Overview</Link>
              </p>
            </Step>
          </div>
        </section>
      )}
    </Page>
  );
}
