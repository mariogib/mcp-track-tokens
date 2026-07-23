import { describe, expect, it } from 'vitest';
import { createBrowseLoadedPages } from '@lunarq/frontend-shared/components';

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
});
