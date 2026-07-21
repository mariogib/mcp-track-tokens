import { useEffect, useState, type ReactNode } from 'react';
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom';
import { ThemeToggle } from '../components/ThemeToggle';
import { StatusBadge } from '../components/StatusBadge';
import { useHealthQuery, useStatusQuery } from '../api/hooks';
import { getStoredApiKey } from '../api/client';
import { useHistoryKeyboardNavigation } from '../hooks/useHistoryKeyboardNavigation';

type NavLeaf = { to: string; label: string; end?: boolean };
type NavGroup = { label: string; children: NavLeaf[] };
type NavEntry = NavLeaf | NavGroup;

function isNavGroup(item: NavEntry): item is NavGroup {
  return 'children' in item;
}

const navItems: NavEntry[] = [
  { to: '/', label: 'Overview', end: true },
  { to: '/projects', label: 'Projects' },
  {
    label: 'Timesheet',
    children: [
      { to: '/timesheet', label: 'Entries', end: true },
      { to: '/timesheet/reports/overall', label: 'Overall report', end: true },
      { to: '/timesheet/reports/projects', label: 'By project', end: true },
      { to: '/timesheet/reports/clients', label: 'By client', end: true },
    ],
  },
  { to: '/reports', label: 'Reports' },
  { to: '/imported-usage', label: 'Imported usage' },
  { to: '/settings', label: 'Settings' },
  {
    label: 'Help',
    children: [
      { to: '/help', label: 'Windows setup', end: true },
      { to: '/help/mcp', label: 'MCP Help' },
    ],
  },
];

function pathMatchesLeaf(pathname: string, leaf: NavLeaf): boolean {
  if (leaf.end) {
    return pathname === leaf.to;
  }
  return pathname === leaf.to || pathname.startsWith(`${leaf.to}/`);
}

function groupHasActiveChild(pathname: string, group: NavGroup): boolean {
  return group.children.some((child) => pathMatchesLeaf(pathname, child));
}

function titleForPath(pathname: string): { title: string; subtitle: string } {
  if (pathname.startsWith('/projects/')) {
    return { title: 'Project details', subtitle: 'Activity, usage, cost, and configuration' };
  }
  switch (pathname) {
    case '/projects':
      return { title: 'Projects', subtitle: 'Tracked repositories and cost rollups' };
    case '/timesheet':
      return {
        title: 'Timesheet',
        subtitle: 'Start, end, and edit billable time across projects',
      };
    case '/timesheet/reports/overall':
      return {
        title: 'Timesheet reports',
        subtitle: 'Overall billable time across all projects',
      };
    case '/timesheet/reports/projects':
      return {
        title: 'Timesheet reports',
        subtitle: 'Billable time for one project',
      };
    case '/timesheet/reports/clients':
      return {
        title: 'Timesheet reports',
        subtitle: 'Billable time rolled up by client',
      };
    case '/reports':
      return { title: 'Reports', subtitle: 'Client and project cost, activity, and billing reports' };
    case '/imported-usage':
      return {
        title: 'Imported usage',
        subtitle: 'Upload Cursor exports, map columns, and review imported rows',
      };
    case '/unallocated':
      return { title: 'Unallocated activity', subtitle: 'Assign prompt and agent events to projects' };
    case '/settings':
      return { title: 'Settings', subtitle: 'Tracking preferences, privacy, and API keys' };
    case '/help/mcp':
      return { title: 'MCP Help', subtitle: 'Tools, resources, and prompts on the MCP server' };
    case '/help':
      return { title: 'Help', subtitle: 'Windows install and Cursor setup' };
    default:
      return { title: 'Overview', subtitle: 'Live tracking health and today’s activity' };
  }
}

function NavGroupItem({
  group,
  pathname,
  onNavigate,
}: {
  group: NavGroup;
  pathname: string;
  onNavigate: () => void;
}) {
  const childActive = groupHasActiveChild(pathname, group);
  const [expanded, setExpanded] = useState(childActive);

  useEffect(() => {
    if (childActive) {
      setExpanded(true);
    }
  }, [childActive]);

  const panelId = `nav-group-${group.label.toLowerCase().replace(/\s+/g, '-')}`;

  return (
    <li className={`nav-group${expanded ? ' nav-group--open' : ''}${childActive ? ' nav-group--active' : ''}`}>
      <button
        type="button"
        className="nav-group-toggle"
        aria-expanded={expanded}
        aria-controls={panelId}
        onClick={() => setExpanded((v) => !v)}
      >
        <span>{group.label}</span>
        <span className="nav-group-chevron" aria-hidden="true">
          {expanded ? '▾' : '▸'}
        </span>
      </button>
      {expanded ? (
        <ul id={panelId} className="nav-sublist">
          {group.children.map((child) => (
            <li key={child.to}>
              <NavLink
                to={child.to}
                end={child.end}
                className={({ isActive }) => `nav-link nav-sublink${isActive ? ' active' : ''}`}
                onClick={onNavigate}
              >
                {child.label}
              </NavLink>
            </li>
          ))}
        </ul>
      ) : null}
    </li>
  );
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
          <img
            className="brand-logo"
            src="/brand-icon.png"
            alt=""
            width={40}
            height={40}
          />
          <div className="brand-text">
            <span className="brand-mark">MCP Track Tokens</span>
            <span className="brand-sub">Local AI activity & cost</span>
          </div>
        </div>
        <nav id="primary-nav">
          <ul className="nav-list">
            {navItems.map((item) =>
              isNavGroup(item) ? (
                <NavGroupItem
                  key={item.label}
                  group={item}
                  pathname={location.pathname}
                  onNavigate={() => setMenuOpen(false)}
                />
              ) : (
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
              ),
            )}
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
          {!hasApiKey &&
          location.pathname !== '/settings' &&
          location.pathname !== '/help' &&
          location.pathname !== '/help/mcp' ? (
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
