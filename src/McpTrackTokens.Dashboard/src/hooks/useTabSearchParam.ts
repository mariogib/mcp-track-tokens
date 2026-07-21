import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';

function slugifyTab(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/&/g, 'and')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

function resolveTab<T extends string>(
  tabs: readonly T[],
  raw: string | null,
  defaultTab: T,
): T {
  if (!raw) {
    return defaultTab;
  }

  const exact = tabs.find((tab) => tab === raw);
  if (exact) {
    return exact;
  }

  const slug = slugifyTab(raw);
  const bySlug = tabs.find((tab) => slugifyTab(tab) === slug);
  return bySlug ?? defaultTab;
}

/**
 * Keeps the active tab in the URL (`?tab=…` by default) so refresh restores it.
 */
export function useTabSearchParam<T extends string>(
  tabs: readonly T[],
  defaultTab: T,
  paramName = 'tab',
): [T, (next: T) => void] {
  const [searchParams, setSearchParams] = useSearchParams();

  const tab = useMemo(
    () => resolveTab(tabs, searchParams.get(paramName), defaultTab),
    [tabs, searchParams, paramName, defaultTab],
  );

  const setTab = useCallback(
    (next: T) => {
      setSearchParams(
        (prev) => {
          const nextParams = new URLSearchParams(prev);
          nextParams.set(paramName, slugifyTab(next));
          return nextParams;
        },
        { replace: true },
      );
    },
    [paramName, setSearchParams],
  );

  return [tab, setTab];
}
