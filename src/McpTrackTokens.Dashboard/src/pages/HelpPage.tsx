import { useEffect, useState, type ReactNode } from 'react';
import { Panel } from '../components/MetricCard';
import { useTabSearchParam } from '../hooks/useTabSearchParam';
import { TextLink } from '../shared/adminUi';

const HELP_TABS = ['Overview', 'Cursor setup'] as const;

const HOOKS_JSON = `{
  "version": 1,
  "hooks": {
    "beforeSubmitPrompt": [
      { "command": "./mcp-track-tokens-hooks/dist/prompt-submitted.js", "timeout": 5 }
    ],
    "sessionStart": [
      { "command": "./mcp-track-tokens-hooks/dist/session-started.js", "timeout": 5 }
    ],
    "sessionEnd": [
      { "command": "./mcp-track-tokens-hooks/dist/session-ended.js", "timeout": 5 }
    ],
    "subagentStart": [
      { "command": "./mcp-track-tokens-hooks/dist/agent-started.js", "timeout": 5 }
    ],
    "subagentStop": [
      { "command": "./mcp-track-tokens-hooks/dist/agent-completed.js", "timeout": 5 }
    ],
    "stop": [
      { "command": "./mcp-track-tokens-hooks/dist/agent-completed.js", "timeout": 5 }
    ]
  }
}`;

const MCP_SERVER_JSON = `{
  "mcpServers": {
    "mcp-track-tokens": {
      "url": "http://127.0.0.1:5187/mcp",
      "headers": {
        "Authorization": "Bearer OverTheMoon"
      }
    }
  }
}`;

const ENV_VARS = `MCP_TRACK_TOKENS_API_KEY=OverTheMoon
MCP_TRACK_TOKENS_SERVER_URL=http://127.0.0.1:5187`;

async function copyText(value: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(value);
      return true;
    }
  } catch {
    // fall through
  }

  try {
    const input = document.createElement('textarea');
    input.value = value;
    input.setAttribute('readonly', '');
    input.style.position = 'fixed';
    input.style.left = '-9999px';
    document.body.appendChild(input);
    input.select();
    const ok = document.execCommand('copy');
    document.body.removeChild(input);
    return ok;
  } catch {
    return false;
  }
}

function CodeBlock({ children, label = 'Copy' }: { children: string; label?: string }) {
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) return;
    const timer = window.setTimeout(() => setCopied(false), 1500);
    return () => window.clearTimeout(timer);
  }, [copied]);

  return (
    <div className="copyable-code-block">
      <div className="copyable-code-block-toolbar">
        <button
          type="button"
          className="btn btn-secondary btn-copy-inline"
          aria-label={copied ? 'Copied to clipboard' : label}
          title={copied ? 'Copied' : label}
          onClick={async () => {
            const ok = await copyText(children);
            if (ok) setCopied(true);
          }}
        >
          {copied ? 'Copied' : 'Copy'}
        </button>
      </div>
      <pre className="mono copyable-code-block-pre">
        <code>{children}</code>
      </pre>
    </div>
  );
}

function Step({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Panel className="stack help-step-card">
      <h3>{title}</h3>
      <div className="stack">{children}</div>
    </Panel>
  );
}

export function HelpPage() {
  const [tab, setTab] = useTabSearchParam(HELP_TABS, 'Cursor setup');

  return (
    <>
      <div className="tabs" role="tablist" aria-label="Windows setup sections">
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
              <h2>Windows setup</h2>
              <p>
                This guide is for the Windows installer (tray host + desktop dashboard). MCP Track
                Tokens correlates editor activity with imported Cursor usage so you can attribute
                time and cost to projects.
              </p>
            </div>
          </div>
          <Panel className="stack">
            <p>
              Start with the <strong>Cursor setup</strong> tab to connect Cursor. Then open{' '}
              <TextLink to="/settings">Settings</TextLink> to confirm your API key, and use{' '}
              <TextLink to="/imported-usage">Imported usage</TextLink> for Cursor usage exports. For the MCP
              tool, resource, and prompt catalog, open the <strong>MCP Help</strong> tab.
            </p>
            <ul>
              <li>
                Installer: <code className="mono">MCP-Track-Tokens-Setup.msi</code> (deploys API,
                HTTP MCP, and dashboard)
              </li>
              <li>
                Tray host keeps API (<code className="mono">/api/v1</code>), MCP (
                <code className="mono">/mcp</code>), and the dashboard available at{' '}
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
          </Panel>
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

          <div className="stack help-setup-steps">
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
              <CodeBlock label="Copy hooks JSON">{HOOKS_JSON}</CodeBlock>
              <p className="hint">
                Use current Cursor event names (<code className="mono">beforeSubmitPrompt</code>,{' '}
                <code className="mono">sessionStart</code>, <code className="mono">stop</code>, …).
                After a Cursor upgrade, run MCP tool{' '}
                <code className="mono">check_cursor_hooks</code> (see{' '}
                <TextLink to="/help?view=mcp-help&tab=tools">MCP Help → Tools</TextLink>) to confirm the mapping still works.
              </p>
            </Step>

            <Step title="4. Set Windows environment variables for hooks">
              <p>
                Hooks need a Bearer key to POST events. Set these as <strong>User</strong>{' '}
                environment variables (Windows Settings → System → About → Advanced system settings
                → Environment Variables), then restart Cursor:
              </p>
              <CodeBlock label="Copy environment variables">{ENV_VARS}</CodeBlock>
              <p>
                Use the same key under <TextLink to="/settings">Settings → Connection</TextLink> in the
                desktop dashboard.
              </p>
            </Step>

            <Step title="5. Configure Cursor MCP (HTTP to the tray host)">
              <p>
                With the tray host running, add this MCP server entry in Cursor (HTTP MCP shares the
                same local database as the dashboard):
              </p>
              <CodeBlock label="Copy MCP server JSON">{MCP_SERVER_JSON}</CodeBlock>
              <p className="hint">
                Replace the Bearer value if you created a different API key. The tray host must be
                running for MCP tools and hooks to reach the API.
              </p>
            </Step>

            <Step title="6. Verify">
              <ul>
                <li>
                  <TextLink to="/settings?tab=integrations">Settings → Integrations</TextLink> shows Cursor hooks configured
                  or inferred from activity.
                </li>
                <li>
                  Submit a prompt in Cursor, then check <TextLink to="/">Overview</TextLink> for new
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
                  Open the dashboard <TextLink to="/imported-usage">Imported usage</TextLink> page.
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
                  <TextLink to="/projects">Projects</TextLink>.
                </li>
              </ol>
              <p className="hint">
                Related pages:{' '}
                <TextLink to="/imported-usage">Imported usage</TextLink>
                {' · '}
                <TextLink to="/projects">Projects</TextLink>
                {' · '}
                <TextLink to="/">Overview</TextLink>
              </p>
            </Step>
          </div>
        </section>
      )}
    </>
  );
}
