import { useCallback, useEffect, useMemo, useState } from 'react';

/** Resolved appearance applied to the UI. */
export type ThemeMode = 'light' | 'dark';

/** Stored preference — `system` follows the OS (Windows) theme. */
export type ThemePreference = ThemeMode | 'system';

const STORAGE_KEY = 'mcp-track-tokens-theme';

function systemTheme(): ThemeMode {
  if (
    typeof window !== 'undefined' &&
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-color-scheme: dark)').matches
  ) {
    return 'dark';
  }
  return 'light';
}

function readPreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'light' || stored === 'dark' || stored === 'system') {
      return stored;
    }
  } catch {
    /* ignore */
  }
  return 'system';
}

function resolveTheme(preference: ThemePreference): ThemeMode {
  return preference === 'system' ? systemTheme() : preference;
}

export function useThemeState() {
  const [preference, setPreference] = useState<ThemePreference>(readPreference);
  const [theme, setThemeState] = useState<ThemeMode>(() => resolveTheme(readPreference()));

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
  }, [theme]);

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, preference);
    } catch {
      /* ignore */
    }
  }, [preference]);

  useEffect(() => {
    setThemeState(resolveTheme(preference));
    if (preference !== 'system' || typeof window === 'undefined') {
      return;
    }

    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = () => setThemeState(systemTheme());
    const onHostSync = () => setThemeState(systemTheme());
    media.addEventListener('change', onChange);
    window.addEventListener('mcp-track-tokens-theme-sync', onHostSync);
    return () => {
      media.removeEventListener('change', onChange);
      window.removeEventListener('mcp-track-tokens-theme-sync', onHostSync);
    };
  }, [preference]);

  const setTheme = useCallback((next: ThemeMode) => {
    setPreference(next);
    setThemeState(next);
  }, []);

  const toggleTheme = useCallback(() => {
    setPreference((prev) => {
      const order: ThemePreference[] = ['system', 'light', 'dark'];
      const next = order[(order.indexOf(prev) + 1) % order.length] ?? 'system';
      setThemeState(resolveTheme(next));
      return next;
    });
  }, []);

  return useMemo(
    () => ({ theme, preference, toggleTheme, setTheme }),
    [theme, preference, toggleTheme, setTheme],
  );
}
