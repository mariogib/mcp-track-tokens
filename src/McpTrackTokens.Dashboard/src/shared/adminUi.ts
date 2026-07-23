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
  const isLight = theme.bgColor.trim().toLowerCase().startsWith('#')
    ? luminanceHint(theme.bgColor) > 0.55
    : false;

  root.style.setProperty('--bg-base', theme.bgColor);
  root.style.setProperty('--bg-elevated', theme.cardBgColor);
  root.style.setProperty(
    '--bg-muted',
    isLight ? mixHex(theme.bgColor, theme.borderColor, 0.45) : mixHex(theme.cardBgColor, '#000000', 0.22),
  );
  root.style.setProperty('--text-primary', theme.textColor);
  root.style.setProperty(
    '--text-secondary',
    isLight ? mixHex(theme.textColor, theme.textMutedColor, 0.35) : mixHex(theme.textColor, theme.textMutedColor, 0.55),
  );
  root.style.setProperty('--text-muted', theme.textMutedColor);
  root.style.setProperty('--border', theme.borderColor);
  root.style.setProperty(
    '--border-strong',
    isLight ? mixHex(theme.borderColor, theme.textColor, 0.35) : mixHex(theme.borderColor, '#ffffff', 0.2),
  );
  root.style.setProperty('--accent', theme.primaryColor);
  root.style.setProperty('--accent-hover', theme.secondaryColor);
  root.style.setProperty('--accent-soft', hexToRgba(theme.primaryColor, isLight ? 0.16 : 0.18));
  root.style.setProperty('--accent-contrast', isLight ? '#ffffff' : theme.bgColor);
  root.style.setProperty('--success', theme.successColor);
  root.style.setProperty('--warning', theme.warningColor);
  root.style.setProperty('--danger', theme.dangerColor);
  root.style.setProperty('--shadow-md', theme.shadowColor);
}

function luminanceHint(cssColor: string): number {
  const hex = cssColor.trim().replace('#', '');
  const full =
    hex.length === 3
      ? hex
          .split('')
          .map((part) => `${part}${part}`)
          .join('')
      : hex;
  if (!/^[0-9a-fA-F]{6}$/.test(full)) {
    return 0;
  }
  const r = Number.parseInt(full.slice(0, 2), 16) / 255;
  const g = Number.parseInt(full.slice(2, 4), 16) / 255;
  const b = Number.parseInt(full.slice(4, 6), 16) / 255;
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function mixHex(a: string, b: string, amountB: number): string {
  const left = parseHex(a);
  const right = parseHex(b);
  if (!left || !right) {
    return a;
  }
  const t = Math.min(1, Math.max(0, amountB));
  const channel = (from: number, to: number) => Math.round(from + (to - from) * t);
  return `#${[channel(left[0], right[0]), channel(left[1], right[1]), channel(left[2], right[2])]
    .map((value) => value.toString(16).padStart(2, '0'))
    .join('')}`;
}

function parseHex(cssColor: string): [number, number, number] | null {
  const hex = cssColor.trim().replace('#', '');
  const full =
    hex.length === 3
      ? hex
          .split('')
          .map((part) => `${part}${part}`)
          .join('')
      : hex;
  if (!/^[0-9a-fA-F]{6}$/.test(full)) {
    return null;
  }
  return [
    Number.parseInt(full.slice(0, 2), 16),
    Number.parseInt(full.slice(2, 4), 16),
    Number.parseInt(full.slice(4, 6), 16),
  ];
}

function hexToRgba(cssColor: string, alpha: number): string {
  const rgb = parseHex(cssColor);
  if (!rgb) {
    return cssColor;
  }
  return `rgba(${rgb[0]}, ${rgb[1]}, ${rgb[2]}, ${alpha})`;
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
