import React from 'react';
import { Link, NavLink, useNavigate } from 'react-router-dom';
import { createAdminShell } from '@lunarq/frontend-shared/admin';
import {
  createBreadcrumb,
  createBrowseListControls,
  createBrowseScrollSentinel,
  createCard,
  createChartCard,
  createTextLink,
  type ChartCardProps,
} from '@lunarq/frontend-shared/components';
import {
  applyThemeVariables,
  BUILTIN_THEME_PRESETS,
  createThemeButton,
  type ThemeResponseBase,
} from '@lunarq/frontend-shared/theme';

/** Shared admin shell bound to this app's React + React Router. */
export const AdminShell = createAdminShell(React, NavLink);

/** Shared breadcrumb trail bound to React Router Link. */
export const Breadcrumb = createBreadcrumb(React, Link);

/** Shared theme-aware text link bound to React Router Link. */
export const TextLink = createTextLink(React, Link);

/** Shared browse toolbar (table/grid; calendar optional per page). */
export const BrowseListControls = createBrowseListControls(React);

/** Sentinel for BrowseListControls `paging.mode === "scroll"`. */
export const BrowseScrollSentinel = createBrowseScrollSentinel(React);

/** Keep app-local tokens in sync when ThemeButton applies a preset. */
function applyDashboardTheme(theme: ThemeResponseBase): void {
  applyThemeVariables(theme);
  const root = document.documentElement;
  root.style.setProperty('--bg-base', theme.bgColor);
  root.style.setProperty('--bg-elevated', theme.cardBgColor);
  root.style.setProperty('--text-primary', theme.textColor);
  root.style.setProperty('--text-secondary', theme.textMutedColor);
  root.style.setProperty('--text-muted', theme.textMutedColor);
  root.style.setProperty('--border', theme.borderColor);
  root.style.setProperty('--accent', theme.primaryColor);
  root.style.setProperty('--accent-hover', theme.secondaryColor);
  root.style.setProperty('--success', theme.successColor);
  root.style.setProperty('--warning', theme.warningColor);
  root.style.setProperty('--danger', theme.dangerColor);
  root.style.setProperty('--shadow-md', theme.shadowColor);
}

/** Shared theme picker (LunarQ / LunarQ Light / Midnight). */
export const ThemeButton = createThemeButton(React, {
  themes: BUILTIN_THEME_PRESETS,
  defaultThemeId: 'lunarq-light',
  storageKey: 'mcp-track-tokens-theme-id',
  applyTheme: applyDashboardTheme,
});

/** Shared card surface. */
export const Card = createCard(React);

const SharedChartCard = createChartCard(React, Card);

/** Shared chart card with React Router navigation for analysis links. */
export function ChartCard(props: Omit<ChartCardProps, 'onNavigate'>) {
  const navigate = useNavigate();
  return React.createElement(SharedChartCard, {
    ...props,
    onNavigate: props.to ? (to: string) => void navigate(to) : undefined,
  });
}
