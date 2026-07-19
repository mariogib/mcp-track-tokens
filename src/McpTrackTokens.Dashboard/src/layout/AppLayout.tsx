import { useState, type ReactNode } from 'react';
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom';
import { ThemeToggle } from '../components/ThemeToggle';
import { StatusBadge } from '../components/StatusBadge';
import { useHealthQuery, useStatusQuery } from '../api/hooks';
import { getStoredApiKey } from '../api/client';
import { useHistoryKeyboardNavigation } from '../hooks/useHistoryKeyboardNavigation';

const navItems = [
  { to: '/', label: 'Overview', end: true },
  { to: '/projects', label: 'Projects' },
  { to: '/imports', label: 'Imports' },
  { to: '/imported-usage', label: 'Imported usage' },
  { to: '/reconciliation', label: 'Reconciliation' },
  { to: '/settings', label: 'Settings' },
  { to: '/help', label: 'Help' },
];

function titleForPath(pathname: string): { title: string; subtitle: string } {
  if (pathname.startsWith('/projects/')) {
    return { title: 'Project details', subtitle: 'Activity, usage, cost, and configuration' };
  }
  switch (pathname) {
    case '/projects':
      return { title: 'Projects', subtitle: 'Tracked repositories and cost rollups' };
    case '/imports':
      return { title: 'Imports', subtitle: 'Upload and map Cursor usage exports' };
    case '/imported-usage':
      return { title: 'Imported usage', subtitle: 'All rows imported from Cursor usage exports' };
    case '/reconciliation':
      return { title: 'Reconciliation', subtitle: 'Allocate unassigned usage with confidence review' };
    case '/unallocated':
      return { title: 'Unallocated activity', subtitle: 'Assign prompt and agent events to projects' };
    case '/settings':
      return { title: 'Settings', subtitle: 'Tracking preferences, privacy, and API keys' };
    case '/help':
      return { title: 'Help', subtitle: 'Windows install and Cursor setup' };
    default:
      return { title: 'Overview', subtitle: 'Live tracking health and today’s activity' };
  }
}

export function AppLayout() {
  const location = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  useHistoryKeyboardNavigation();
  const health = useHealthQuery();
  const status = useStatusQuery();
  const page = titleForPath(location.pathname);
  const healthy =
    health.data?.healthy === true ||
    health.data?.status === 'Healthy' ||
    (health.isSuccess && !health.isError);
  const hasApiKey = Boolean(getStoredApiKey());

  return (
    <div className="app-shell">
      {menuOpen ? (
        <button
          type="button"
          className="backdrop"
          aria-label="Close navigation"
          onClick={() => setMenuOpen(false)}
        />
      ) : null}

      <aside className={`sidebar ${menuOpen ? 'open' : ''}`} aria-label="Primary">
        <div className="brand">
          <span className="brand-mark">MCP Track Tokens</span>
          <span className="brand-sub">Local AI activity & cost</span>
        </div>
        <nav>
          <ul className="nav-list">
            {navItems.map((item) => (
              <li key={item.to}>
                <NavLink
                  to={item.to}
                  end={item.end}
                  className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}
                  onClick={() => setMenuOpen(false)}
                >
                  {item.label}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
        <div className="sidebar-footer">
          {status.data?.currentProject?.name
            ? `Active: ${status.data.currentProject.name}`
            : 'No active project'}
        </div>
      </aside>

      <div className="main">
        <header className="topbar">
          <div className="row">
            <button
              type="button"
              className="btn btn-secondary menu-toggle"
              aria-expanded={menuOpen}
              aria-controls="primary-nav"
              onClick={() => setMenuOpen((v) => !v)}
            >
              Menu
            </button>
            <div className="topbar-title">
              <h1>{page.title}</h1>
              <p>{page.subtitle}</p>
            </div>
          </div>
          <div className="topbar-actions">
            <StatusBadge
              label={healthy ? 'Server healthy' : health.isError ? 'Server offline' : 'Checking…'}
              tone={healthy ? 'success' : health.isError ? 'danger' : 'warning'}
            />
            <ThemeToggle />
          </div>
        </header>
        <main className="content">
          {!hasApiKey && location.pathname !== '/settings' && location.pathname !== '/help' ? (
            <div className="warning-banner" role="status">
              No API key saved yet. Open <Link to="/settings">Settings</Link> and save your tracking
              Bearer key (default for the Windows install is <code>OverTheMoon</code>).
            </div>
          ) : null}
          <Outlet />
        </main>
      </div>
    </div>
  );
}

export function Page({ children }: { children: ReactNode }) {
  return <div className="stack">{children}</div>;
}
