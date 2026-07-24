import { type ReactNode } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import type { AdminNavItem } from '@lunarq/frontend-shared/admin';
import { StatusBadge } from '../components/StatusBadge';
import { useHealthQuery, useStatusQuery } from '../api/hooks';
import { getStoredApiKey } from '../api/client';
import { useHistoryKeyboardNavigation } from '../hooks/useHistoryKeyboardNavigation';
import { AdminShell, TextLink, ThemeButton } from '../shared/adminUi';

const navItems: AdminNavItem[] = [
  { to: '/', label: 'Overview', icon: '⌂', end: true },
  { to: '/projects', label: 'Projects', icon: '◫' },
  { to: '/timesheet', label: 'Timesheet', icon: '◷' },
  { to: '/reports', label: 'Reports', icon: '▣' },
  { to: '/imported-usage', label: 'Imported usage', icon: '⇩' },
  { to: '/settings', label: 'Settings', icon: '⚙' },
  { to: '/help', label: 'Help', icon: '?' },
];

function titleForPath(pathname: string, search: string): { title: string; subtitle: string } {
  if (pathname.startsWith('/projects/')) {
    return { title: 'Project details', subtitle: 'Activity, usage, cost, and configuration' };
  }
  switch (pathname) {
    case '/projects':
      return { title: 'Projects', subtitle: 'Tracked repositories and cost rollups' };
    case '/timesheet':
      return {
        title: 'Timesheet',
        subtitle: 'Billable entries and time reports',
      };
    case '/reports':
      return { title: 'Reports', subtitle: 'Client and project cost, activity, and billing reports' };
    case '/imported-usage': {
      const tab = new URLSearchParams(search).get('tab');
      if (tab === 'unallocated' || tab === 'unallocated-prompts') {
        return {
          title: 'Imported usage',
          subtitle: 'Assign or delete unallocated prompts',
        };
      }
      return {
        title: 'Imported usage',
        subtitle: 'Upload Cursor exports and review imported rows',
      };
    }
    case '/settings':
      return { title: 'Settings', subtitle: 'Tracking preferences, privacy, and API keys' };
    case '/help':
      return {
        title: 'Help',
        subtitle: 'Windows setup and MCP tool reference',
      };
    default:
      return { title: 'Overview', subtitle: 'Live tracking health and today’s activity' };
  }
}

export function AppLayout() {
  const location = useLocation();
  useHistoryKeyboardNavigation();
  const health = useHealthQuery();
  const status = useStatusQuery();
  const page = titleForPath(location.pathname, location.search);
  const healthy =
    health.data?.healthy === true ||
    health.data?.status === 'Healthy' ||
    (health.isSuccess && !health.isError);
  const hasApiKey = Boolean(getStoredApiKey());
  const activeProject = status.data?.currentProject?.name;

  return (
    <AdminShell
      navItems={navItems}
      logo={
        <div className="logo">
          <img className="logo-image" src="/brand-icon.png" alt="" width={72} height={72} />
          <div className="logo-text">
            <span className="logo-title">MCP Track Tokens</span>
            <span className="logo-subtitle">Local AI activity &amp; cost</span>
          </div>
        </div>
      }
      userName="Local"
      userEmail={activeProject ? `Active: ${activeProject}` : 'No active project'}
      topBarContent={
        <div className="dashboard-topbar">
          <div className="dashboard-topbar-title">
            <h1>{page.title}</h1>
            <p>{page.subtitle}</p>
          </div>
          <div className="dashboard-topbar-actions">
            <StatusBadge
              label={healthy ? 'Server healthy' : health.isError ? 'Server offline' : 'Checking…'}
              tone={healthy ? 'success' : health.isError ? 'danger' : 'warning'}
            />
            <ThemeButton />
          </div>
        </div>
      }
    >
      {!hasApiKey &&
      location.pathname !== '/settings' &&
      location.pathname !== '/help' ? (
        <div className="warning-banner" role="status">
          No API key saved yet. Open <TextLink to="/settings">Settings</TextLink> and save your
          tracking Bearer key (default for the Windows install is <code>OverTheMoon</code>).
        </div>
      ) : null}
      <Outlet />
    </AdminShell>
  );
}

export function Page({ children }: { children: ReactNode }) {
  return <div className="stack">{children}</div>;
}
