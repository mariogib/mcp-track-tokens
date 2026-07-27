import React, { type ReactNode, useEffect, useMemo, useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import type { AdminNavItem } from '@lunarq/frontend-shared/admin';
import { createFluentNavIcons } from '@lunarq/frontend-shared/admin';
import type { ThemeLookAndFeel } from '@lunarq/frontend-shared/theme';
import { StatusBadge } from '../components/StatusBadge';
import { useHealthQuery, useStatusQuery } from '../api/hooks';
import { getStoredApiKey } from '../api/client';
import { useHistoryKeyboardNavigation } from '../hooks/useHistoryKeyboardNavigation';
import { AdminShell, TextLink, applyStoredDashboardTheme } from '../shared/adminUi';

const fluentIcons = createFluentNavIcons(React);

const lunarqNavItems: AdminNavItem[] = [
  { to: '/', label: 'Overview', icon: '⌂', end: true },
  { to: '/projects', label: 'Projects', icon: '◫' },
  { to: '/timesheet', label: 'Timesheet', icon: '◷' },
  { to: '/reports', label: 'Reports', icon: '▣' },
  { to: '/imported-usage', label: 'Imported usage', icon: '⇩' },
  { to: '/settings', label: 'Settings', icon: '⚙' },
  { to: '/help', label: 'Help', icon: '?' },
];

const fluentNavItems: AdminNavItem[] = [
  { to: '/', label: 'Overview', icon: fluentIcons.overview, end: true },
  { to: '/projects', label: 'Projects', icon: fluentIcons.projects },
  { to: '/timesheet', label: 'Timesheet', icon: fluentIcons.timesheet },
  { to: '/reports', label: 'Reports', icon: fluentIcons.reports },
  { to: '/imported-usage', label: 'Imported usage', icon: fluentIcons.import },
  { to: '/settings', label: 'Settings', icon: fluentIcons.settings },
  { to: '/help', label: 'Help', icon: fluentIcons.help },
];

function readLookAndFeel(): ThemeLookAndFeel {
  if (typeof document === 'undefined') {
    return 'lunarq';
  }
  return document.documentElement.dataset.lookAndFeel === 'fluent' ? 'fluent' : 'lunarq';
}

function useLookAndFeel(): ThemeLookAndFeel {
  const [lookAndFeel, setLookAndFeel] = useState<ThemeLookAndFeel>(readLookAndFeel);

  useEffect(() => {
    applyStoredDashboardTheme();

    function sync() {
      setLookAndFeel(readLookAndFeel());
    }

    sync();
    document.addEventListener('lunarq:themechange', sync);
    return () => document.removeEventListener('lunarq:themechange', sync);
  }, []);

  return lookAndFeel;
}

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
  const queryClient = useQueryClient();
  const lookAndFeel = useLookAndFeel();
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
  const navItems = useMemo(
    () => (lookAndFeel === 'fluent' ? fluentNavItems : lunarqNavItems),
    [lookAndFeel],
  );

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
      onContentRefresh={() => {
        void queryClient.invalidateQueries();
      }}
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
