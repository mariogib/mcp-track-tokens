import { useEffect, useMemo, useState } from 'react';
import { Panel } from '../components/MetricCard';
import { MCP_PROMPTS, MCP_RESOURCES, MCP_TOOLS } from '../data/mcpCatalog';
import { useTabSearchParam } from '../hooks/useTabSearchParam';

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
}: {
  value: string;
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
    <>
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
              Tools, resources, and prompts exposed by the local MCP Track Tokens server. Use the{' '}
              <strong>Windows setup</strong> tab for connecting Cursor MCP to{' '}
              <code className="mono">http://127.0.0.1:5187</code>.
            </p>
          </div>
        </div>

        {tab === 'Tools' && (
          <div className="stack">
            <p className="muted">Click Copy next to a tool name to put it on the clipboard.</p>
            {toolGroups.map(([group, tools]) => (
              <Panel key={group} className="stack">
                <h3>{group}</h3>
                <div className="table-wrap">
                  <table className="data">
                    <thead>
                      <tr>
                        <th>Tool</th>
                        <th className="cell-wrap">Description</th>
                      </tr>
                    </thead>
                    <tbody>
                      {tools.map((tool) => (
                        <tr key={tool.name}>
                          <td>
                            <div className="mcp-copy-row">
                              <code className="mono">{tool.name}</code>
                              <CopyTextButton value={tool.name} />
                            </div>
                          </td>
                          <td className="cell-wrap">{tool.description}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </Panel>
            ))}
          </div>
        )}

        {tab === 'Resources' && (
          <Panel className="stack">
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
                    <th className="cell-wrap">Description</th>
                  </tr>
                </thead>
                <tbody>
                  {MCP_RESOURCES.map((resource) => (
                    <tr key={resource.uri}>
                      <td>{resource.name}</td>
                      <td>
                        <div className="mcp-copy-row">
                          <code className="mono">{resource.uri}</code>
                          <CopyTextButton value={resource.uri} />
                        </div>
                      </td>
                      <td className="cell-wrap">{resource.description}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Panel>
        )}

        {tab === 'Prompts' && (
          <Panel className="stack">
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
                    <th className="cell-wrap">Description</th>
                    <th className="cell-wrap">Example</th>
                  </tr>
                </thead>
                <tbody>
                  {MCP_PROMPTS.map((prompt) => (
                    <tr key={prompt.name}>
                      <td>
                        <div className="mcp-copy-row">
                          <code className="mono">{prompt.name}</code>
                          <CopyTextButton value={prompt.name} />
                        </div>
                      </td>
                      <td>
                        <code className="mono">{prompt.args}</code>
                      </td>
                      <td className="cell-wrap">{prompt.description}</td>
                      <td className="cell-wrap">
                        <div className="mcp-prompt-example">
                          <p className="mcp-prompt-example-text">{prompt.example}</p>
                          <CopyTextButton value={prompt.example} />
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Panel>
        )}
      </section>
    </>
  );
}
