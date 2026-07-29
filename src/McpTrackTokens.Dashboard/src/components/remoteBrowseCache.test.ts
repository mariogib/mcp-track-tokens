import { describe, expect, it } from 'vitest';
import { createBrowseLoadedPages } from '@lunarq/frontend-shared/components';
import { withCachedRemotePage } from './RemoteAnalysisDetailBrowse';

/**
 * Mirrors RemoteAnalysisDetailBrowse cache-reset behaviour when filters change.
 */
function resetRemoteBrowseCache(args: {
  requestId: number;
  pageCache: Map<number, unknown[]>;
  loadedPages: Set<number>;
}) {
  return {
    requestId: args.requestId + 1,
    pageCache: new Map<number, unknown[]>(),
    loadedPages: createBrowseLoadedPages(0),
    pageIndex: 0,
  };
}

describe('remote browse page cache', () => {
  it('resets cache and page index when filters change', () => {
    const pageCache = new Map<number, unknown[]>([
      [0, [{ id: 'a' }]],
      [1, [{ id: 'b' }]],
    ]);
    const loadedPages = createBrowseLoadedPages(0);
    loadedPages.add(1);

    const next = resetRemoteBrowseCache({
      requestId: 3,
      pageCache,
      loadedPages,
    });

    expect(next.requestId).toBe(4);
    expect(next.pageIndex).toBe(0);
    expect(next.pageCache.size).toBe(0);
    expect([...next.loadedPages]).toEqual([0]);
  });

  it('keeps pages closest to the newly loaded page in lazy mode', () => {
    let cache = new Map<number, unknown[]>();
    for (let page = 0; page < 5; page += 1) {
      cache = withCachedRemotePage(cache, page, [{ id: page }], 'lazy', 3);
    }
    expect(cache.size).toBe(3);
    expect(cache.has(4)).toBe(true);
    expect(cache.has(3)).toBe(true);
    expect(cache.has(2)).toBe(true);
    expect(cache.has(0)).toBe(false);
  });

  it('drops lowest indices first in scroll mode', () => {
    let cache = new Map<number, unknown[]>();
    for (let page = 0; page < 5; page += 1) {
      cache = withCachedRemotePage(cache, page, [{ id: page }], 'scroll', 3);
    }
    expect([...cache.keys()].sort((a, b) => a - b)).toEqual([2, 3, 4]);
  });
});
