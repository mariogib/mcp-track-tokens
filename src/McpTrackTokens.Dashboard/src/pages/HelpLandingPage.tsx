import { HelpPage } from './HelpPage';
import { McpHelpPage } from './McpHelpPage';
import { useTabSearchParam } from '../hooks/useTabSearchParam';
import { Page } from '../layout/AppLayout';

const HELP_VIEWS = ['Windows setup', 'MCP Help'] as const;

export function HelpLandingPage() {
  const [view, setView] = useTabSearchParam(HELP_VIEWS, 'Windows setup', 'view');

  return (
    <Page>
      <div className="tabs" role="tablist" aria-label="Help sections">
        {HELP_VIEWS.map((name) => (
          <button
            key={name}
            type="button"
            role="tab"
            aria-selected={view === name}
            className={`tab${view === name ? ' active' : ''}`}
            onClick={() => setView(name)}
          >
            {name}
          </button>
        ))}
      </div>

      {view === 'Windows setup' ? <HelpPage /> : <McpHelpPage />}
    </Page>
  );
}
