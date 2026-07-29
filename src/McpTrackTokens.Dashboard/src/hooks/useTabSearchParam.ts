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
  aliases?: Readonly<Record<string, T>>,
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
  if (bySlug) {
    return bySlug;
  }

  const aliased = aliases?.[slug] ?? aliases?.[raw];
  if (aliased && tabs.includes(aliased)) {
    return aliased;
  }

  return defaultTab;
}

/**
 * Keeps the active tab in the URL (`?tab=…` by default) so refresh restores it.
 */
export function useTabSearchParam<T extends string>(
  tabs: readonly T[],
  defaultTab: T,
  paramName = 'tab',
  aliases?: Readonly<Record<string, T>>,
): [T, (next: T) => void] {
  const [searchParams, setSearchParams] = useSearchParams();

  const tab = useMemo(
    () => resolveTab(tabs, searchParams.get(paramName), defaultTab, aliases),
    [tabs, searchParams, paramName, defaultTab, aliases],
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
