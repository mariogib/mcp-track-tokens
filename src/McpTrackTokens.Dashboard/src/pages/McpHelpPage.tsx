import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { MCP_PROMPTS, MCP_RESOURCES, MCP_TOOLS } from '../data/mcpCatalog';
import { useTabSearchParam } from '../hooks/useTabSearchParam';
import { Page } from '../layout/AppLayout';

const TABS = ['Tools', 'Resources', 'Prompts'] as const;

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

function CopyTextButton({
  value,
  label = 'Copy',
}: {
  value: string;
  label?: string;
}) {
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) return;
    const timer = window.setTimeout(() => setCopied(false), 1500);
    return () => window.clearTimeout(timer);
  }, [copied]);

  return (
    <button
      type="button"
      className="btn btn-secondary btn-copy-inline"
      aria-label={copied ? `${value} copied` : `Copy ${value}`}
      title={copied ? 'Copied' : label}
      onClick={async () => {
        const ok = await copyText(value);
        if (ok) setCopied(true);
      }}
    >
      {copied ? 'Copied' : 'Copy'}
    </button>
  );
}

export function McpHelpPage() {
  const [tab, setTab] = useTabSearchParam(TABS, 'Tools');

  const toolGroups = useMemo(() => {
    const map = new Map<string, typeof MCP_TOOLS>();
    for (const tool of MCP_TOOLS) {
      const list = map.get(tool.group) ?? [];
      list.push(tool);
      map.set(tool.group, list);
    }
    return [...map.entries()];
  }, []);

  return (
    <Page>
      <div className="tabs" role="tablist" aria-label="MCP help sections">
        {TABS.map((name) => (
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

      <section className="page-section">
        <div className="section-header">
          <div>
            <h2>MCP reference</h2>
            <p>
              Tools, resources, and prompts exposed by the local MCP Track Tokens server. See{' '}
              <Link to="/help">Windows setup</Link> for connecting Cursor MCP to{' '}
              <code className="mono">http://127.0.0.1:5187</code>.
            </p>
          </div>
        </div>

        {tab === 'Tools' && (
          <div className="stack">
            <p className="muted">Click Copy next to a tool name to put it on the clipboard.</p>
            {toolGroups.map(([group, tools]) => (
              <div key={group} className="panel stack">
                <h3>{group}</h3>
                <div className="table-wrap">
                  <table className="data">
                    <thead>
                      <tr>
                        <th>Tool</th>
                        <th>Description</th>
                      </tr>
                    </thead>
                    <tbody>
                      {tools.map((tool) => (
                        <tr key={tool.name}>
                          <td>
                            <div className="mcp-copy-row">
                              <code className="mono">{tool.name}</code>
                              <CopyTextButton value={tool.name} label="Copy tool name" />
                            </div>
                          </td>
                          <td>{tool.description}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            ))}
          </div>
        )}

        {tab === 'Resources' && (
          <div className="panel stack">
            <p className="muted">
              JSON snapshots you can read from the MCP client. Most time-based resources cover the
              last 30 days. Click Copy next to a URI to put it on the clipboard.
            </p>
            <div className="table-wrap">
              <table className="data">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>URI</th>
                    <th>Description</th>
                  </tr>
                </thead>
                <tbody>
                  {MCP_RESOURCES.map((resource) => (
                    <tr key={resource.uri}>
                      <td>{resource.name}</td>
                      <td>
                        <div className="mcp-copy-row">
                          <code className="mono">{resource.uri}</code>
                          <CopyTextButton value={resource.uri} label="Copy resource URI" />
                        </div>
                      </td>
                      <td>{resource.description}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {tab === 'Prompts' && (
          <div className="panel stack">
            <p className="muted">
              Prompt templates that guide the agent to call the right tools for common analyses.
              Copy the prompt name or a filled-in example to paste into chat.
            </p>
            <div className="table-wrap">
              <table className="data">
                <thead>
                  <tr>
                    <th>Prompt</th>
                    <th>Arguments</th>
                    <th>Description</th>
                    <th>Example</th>
                  </tr>
                </thead>
                <tbody>
                  {MCP_PROMPTS.map((prompt) => (
                    <tr key={prompt.name}>
                      <td>
                        <div className="mcp-copy-row">
                          <code className="mono">{prompt.name}</code>
                          <CopyTextButton value={prompt.name} label="Copy prompt name" />
                        </div>
                      </td>
                      <td>
                        <code className="mono">{prompt.args}</code>
                      </td>
                      <td>{prompt.description}</td>
                      <td>
                        <div className="mcp-prompt-example">
                          <p className="mcp-prompt-example-text">{prompt.example}</p>
                          <CopyTextButton value={prompt.example} label="Copy example" />
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </section>
    </Page>
  );
}
