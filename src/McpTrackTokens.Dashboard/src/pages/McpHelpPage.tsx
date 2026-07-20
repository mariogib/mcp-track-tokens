import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { MCP_PROMPTS, MCP_RESOURCES, MCP_TOOLS } from '../data/mcpCatalog';
import { Page } from '../layout/AppLayout';

const TABS = ['Tools', 'Resources', 'Prompts'] as const;
type Tab = (typeof TABS)[number];

export function McpHelpPage() {
  const [tab, setTab] = useState<Tab>('Tools');

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
                            <code className="mono">{tool.name}</code>
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
              last 30 days.
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
                        <code className="mono">{resource.uri}</code>
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
            </p>
            <div className="table-wrap">
              <table className="data">
                <thead>
                  <tr>
                    <th>Prompt</th>
                    <th>Arguments</th>
                    <th>Description</th>
                  </tr>
                </thead>
                <tbody>
                  {MCP_PROMPTS.map((prompt) => (
                    <tr key={prompt.name}>
                      <td>
                        <code className="mono">{prompt.name}</code>
                      </td>
                      <td>
                        <code className="mono">{prompt.args}</code>
                      </td>
                      <td>{prompt.description}</td>
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
