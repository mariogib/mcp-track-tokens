import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import type {
  BrowseFilterConfig,
  BrowseFilterOption,
  BrowsePagingMode,
  BrowseViewMode,
} from '@lunarq/frontend-shared/components';
import {
  createBrowseLoadedPages,
  getBrowsePageCount,
  getNextBrowsePageToLoad,
  loadBrowsePage,
} from '@lunarq/frontend-shared/components';
import { exportToExcel, type ExcelColumn } from '@lunarq/frontend-shared/utils';
import type { PagedResult } from '../api/types';
import { TablePanel } from './MetricCard';
import { EmptyState } from './States';
import { BrowseListControls, BrowseScrollSentinel } from '../shared/adminUi';
import { formatNumber } from '../utils/format';
import { STATUS_FILTER_NONE } from './AnalysisDetailBrowse';

/** Max rows fetched for Excel export of a remote filtered list. */
export const REMOTE_BROWSE_EXPORT_CAP = 5_000;

/** Max server pages retained in the client cache (bounds memory while browsing). */
export const REMOTE_BROWSE_MAX_CACHED_PAGES = 12;

/**
 * Inserts a page into the cache and drops farthest/oldest pages when over the cap.
 * Scroll mode drops lowest indices first so the newest tail remains visible.
 */
export function withCachedRemotePage<T>(
  cache: Map<number, T[]>,
  page: number,
  items: T[],
  pagingMode: BrowsePagingMode,
  maxPages: number = REMOTE_BROWSE_MAX_CACHED_PAGES,
): Map<number, T[]> {
  const next = new Map(cache);
  next.set(page, items);
  if (next.size <= maxPages) {
    return next;
  }

  if (pagingMode === 'scroll') {
    const keys = [...next.keys()].sort((a, b) => a - b);
    while (next.size > maxPages && keys.length > 0) {
      next.delete(keys.shift()!);
    }
    return next;
  }

  const ranked = [...next.keys()].sort(
    (a, b) => Math.abs(a - page) - Math.abs(b - page) || a - b,
  );
  const keep = new Set(ranked.slice(0, maxPages));
  for (const key of [...next.keys()]) {
    if (!keep.has(key)) {
      next.delete(key);
    }
  }
  return next;
}

export type RemotePageFetchArgs = {
  pageIndex: number;
  pageSize: number;
  search: string;
  status: string;
  signal?: AbortSignal;
};

export type RemotePageFetcher<T> = (args: RemotePageFetchArgs) => Promise<PagedResult<T>>;

type RemoteAnalysisDetailBrowseProps<T> = {
  heading?: string;
  showHeading?: boolean;
  searchPlaceholder?: string;
  /** Reset page cache when this key changes (range / remote filters). */
  filterKey: string;
  fetchPage: RemotePageFetcher<T>;
  getStatusValue?: (row: T) => string;
  statusOptions?: BrowseFilterOption[];
  exportFilename: string;
  exportTitle: string;
  exportColumns: ExcelColumn[];
  toExportRow: (row: T) => Record<string, unknown>;
  renderTable: (rows: T[]) => ReactNode;
  renderGrid: (rows: T[]) => ReactNode;
  emptyMessage?: string;
  emptySourceMessage?: string;
  embedded?: boolean;
  filters?: BrowseFilterConfig[];
  filtersExtra?: ReactNode;
  customControls?: ReactNode[];
  allowCalendarView?: boolean;
  onRequestCalendarView?: () => void;
  pagingMode?: BrowsePagingMode;
  pageSize?: number;
  pageSizeOptions?: number[];
  /** Cap for full-list Excel export. Defaults to {@link REMOTE_BROWSE_EXPORT_CAP}. */
  exportRowCap?: number;
};

function statusLabel(value: string): string {
  return value === STATUS_FILTER_NONE ? 'None' : value;
}

function normalizeStatusValue(value: string | null | undefined): string {
  const trimmed = value?.trim() ?? '';
  return trimmed || STATUS_FILTER_NONE;
}

/**
 * Browse list backed by server OFFSET/LIMIT pages with a load-once page cache (lazy default).
 * Excel export fetches the full filtered match set (capped).
 */
export function RemoteAnalysisDetailBrowse<T>({
  heading = 'Detail data',
  showHeading = true,
  searchPlaceholder = 'Search…',
  filterKey,
  fetchPage,
  getStatusValue,
  statusOptions,
  exportFilename,
  exportTitle,
  exportColumns,
  toExportRow,
  renderTable,
  renderGrid,
  emptyMessage = 'No detail rows match the current search.',
  emptySourceMessage = 'No detail data in this range.',
  embedded = false,
  filters = [],
  filtersExtra,
  customControls,
  allowCalendarView = false,
  onRequestCalendarView,
  pagingMode = 'lazy',
  pageSize: initialPageSize = 25,
  pageSizeOptions = [10, 25, 50, 100],
  exportRowCap = REMOTE_BROWSE_EXPORT_CAP,
}: RemoteAnalysisDetailBrowseProps<T>) {
  const [viewMode, setViewMode] = useState<BrowseViewMode>('table');
  const [searchValue, setSearchValue] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [pageIndex, setPageIndex] = useState(0);
  const [pageSize, setPageSize] = useState(initialPageSize);
  const [loadedPages, setLoadedPages] = useState(() => createBrowseLoadedPages(0));
  const [pageCache, setPageCache] = useState(() => new Map<number, T[]>());
  const [totalCount, setTotalCount] = useState(0);
  const [loadingPage, setLoadingPage] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ready, setReady] = useState(false);

  const pageCacheRef = useRef(pageCache);
  pageCacheRef.current = pageCache;
  const inFlightRef = useRef<Set<number>>(new Set());
  const requestIdRef = useRef(0);
  const fetchPageRef = useRef(fetchPage);
  fetchPageRef.current = fetchPage;

  useEffect(() => {
    const handle = window.setTimeout(() => setDebouncedSearch(searchValue.trim()), 200);
    return () => window.clearTimeout(handle);
  }, [searchValue]);

  const resetPaging = useCallback(() => {
    requestIdRef.current += 1;
    inFlightRef.current = new Set();
    // Clear the ref immediately so a same-tick load does not reuse stale pages.
    pageCacheRef.current = new Map();
    setPageIndex(0);
    setLoadedPages(createBrowseLoadedPages(0));
    setPageCache(new Map());
    setTotalCount(0);
    setReady(false);
    setError(null);
  }, []);

  const ensurePageLoaded = useCallback(
    async (targetPage: number, requestId: number) => {
      if (targetPage < 0) {
        return;
      }
      if (requestId !== requestIdRef.current) {
        return;
      }
      if (pageCacheRef.current.has(targetPage)) {
        setLoadedPages((previous) => loadBrowsePage(previous, targetPage));
        setReady(true);
        return;
      }
      if (inFlightRef.current.has(targetPage)) {
        return;
      }

      inFlightRef.current.add(targetPage);
      setLoadingPage(true);
      try {
        const result = await fetchPageRef.current({
          pageIndex: targetPage,
          pageSize,
          search: debouncedSearch,
          status: statusFilter,
        });
        if (requestId !== requestIdRef.current) {
          return;
        }
        setTotalCount(result.totalCount);
        const next = withCachedRemotePage(
          pageCacheRef.current,
          targetPage,
          result.items,
          pagingMode,
        );
        pageCacheRef.current = next;
        setPageCache(next);
        setLoadedPages((previous) => {
          let updated = loadBrowsePage(previous, targetPage);
          if (updated.size !== next.size) {
            updated = new Set([...updated].filter((page) => next.has(page)));
            if (!updated.has(targetPage) && next.has(targetPage)) {
              updated.add(targetPage);
            }
          }
          return updated;
        });
        setReady(true);
        setError(null);
      } catch (cause) {
        if (requestId !== requestIdRef.current) {
          return;
        }
        if (cause instanceof DOMException && cause.name === 'AbortError') {
          return;
        }
        setError(cause instanceof Error ? cause.message : 'Failed to load page');
        setReady(true);
      } finally {
        inFlightRef.current.delete(targetPage);
        if (requestId === requestIdRef.current) {
          setLoadingPage(false);
        }
      }
    },
    [debouncedSearch, pageSize, pagingMode, statusFilter],
  );

  const filterToken = `${filterKey}|${debouncedSearch}|${statusFilter}|${pageSize}|${pagingMode}`;
  // Empty initial value so the first mount counts as a filter change and loads page 0.
  const filterTokenRef = useRef('');

  // One effect: filter changes always reset + load page 0; pageIndex changes load that page.
  useEffect(() => {
    const filtersChanged = filterTokenRef.current !== filterToken;
    filterTokenRef.current = filterToken;

    if (filtersChanged) {
      resetPaging();
      void ensurePageLoaded(0, requestIdRef.current);
      return;
    }

    void ensurePageLoaded(pageIndex, requestIdRef.current);
  }, [ensurePageLoaded, filterToken, pageIndex, resetPaging]);

  useEffect(() => {
    if (pagingMode !== 'lazy' && pagingMode !== 'scroll') {
      return;
    }
    setLoadedPages((previous) => loadBrowsePage(previous, pageIndex));
  }, [pageIndex, pagingMode]);

  const visibleRows = useMemo(() => {
    if (pagingMode === 'scroll') {
      const pages = [...loadedPages].sort((a, b) => a - b);
      return pages.flatMap((page) => pageCache.get(page) ?? []);
    }
    // `pages` and `lazy`: show only the current page (lazy still caches visited pages).
    return pageCache.get(pageIndex) ?? [];
  }, [loadedPages, pageCache, pageIndex, pagingMode]);

  const cachedRows = useMemo(() => {
    const pages = [...pageCache.keys()].sort((a, b) => a - b);
    return pages.flatMap((page) => pageCache.get(page) ?? []);
  }, [pageCache]);

  const derivedStatusOptions = useMemo(() => {
    if (!getStatusValue) {
      return [] as BrowseFilterOption[];
    }
    if (statusOptions?.length) {
      return statusOptions;
    }
    const values = new Set<string>();
    for (const row of cachedRows) {
      values.add(normalizeStatusValue(getStatusValue(row)));
    }
    return [...values]
      .sort((a, b) => statusLabel(a).localeCompare(statusLabel(b)))
      .map((value) => ({ value, label: statusLabel(value) }));
  }, [cachedRows, getStatusValue, statusOptions]);

  const browseFilters = useMemo(() => {
    const next: BrowseFilterConfig[] = [];
    if (getStatusValue) {
      next.push({
        id: 'browse-status-filter',
        label: 'Status',
        value: statusFilter,
        onChange: setStatusFilter,
        options: [
          { value: '', label: 'All statuses' },
          ...derivedStatusOptions,
        ],
      });
    }
    next.push(...filters);
    return next;
  }, [derivedStatusOptions, filters, getStatusValue, statusFilter]);

  const fetchAllMatchingRows = useCallback(async (): Promise<{ rows: T[]; total: number }> => {
    const exportPageSize = 100;
    const rows: T[] = [];
    let total = totalCount;
    let index = 0;

    while (rows.length < exportRowCap) {
      const result = await fetchPageRef.current({
        pageIndex: index,
        pageSize: exportPageSize,
        search: debouncedSearch,
        status: statusFilter,
      });
      total = result.totalCount;
      if (result.items.length === 0) {
        break;
      }
      rows.push(...result.items);
      if (rows.length >= total || result.items.length < exportPageSize) {
        break;
      }
      index += 1;
    }

    return {
      rows: rows.slice(0, exportRowCap),
      total,
    };
  }, [debouncedSearch, exportRowCap, statusFilter, totalCount]);

  const onExportToExcel = useCallback(async () => {
    if (exporting || totalCount === 0) {
      return;
    }
    setExporting(true);
    try {
      const { rows, total } = await fetchAllMatchingRows();
      const capped = rows.length < total;
      await exportToExcel({
        filename: exportFilename.endsWith('.xlsx')
          ? exportFilename
          : `${exportFilename}.xlsx`,
        title: capped
          ? `${exportTitle} (first ${rows.length} of ${total})`
          : exportTitle,
        timestamp: new Date().toISOString(),
        columns: exportColumns,
        data: rows.map(toExportRow),
      });
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Export failed');
    } finally {
      setExporting(false);
    }
  }, [
    exportColumns,
    exportFilename,
    exportTitle,
    exporting,
    fetchAllMatchingRows,
    toExportRow,
    totalCount,
  ]);

  const pageCount = getBrowsePageCount(totalCount, pageSize);
  const nextScrollPage = getNextBrowsePageToLoad(loadedPages, pageCount);
  const activeViewMode = viewMode === 'calendar' ? 'table' : viewMode;

  const content = (
    <>
      <BrowseListControls
        heading={heading}
        showHeading={showHeading}
        viewMode={activeViewMode}
        onViewModeChange={(next) => {
          if (next === 'calendar') {
            onRequestCalendarView?.();
            return;
          }
          setViewMode(next);
        }}
        allowCalendarView={allowCalendarView}
        searchValue={searchValue}
        searchPlaceholder={searchPlaceholder}
        onSearchChange={setSearchValue}
        filters={browseFilters}
        onExportToExcel={() => void onExportToExcel()}
        exportLabel={exporting ? 'Exporting…' : 'Export to Excel'}
        exportDisabled={totalCount === 0 || exporting}
        customControls={customControls}
        paging={{
          mode: pagingMode,
          pageSize,
          pageSizeOptions,
          pageIndex,
          totalCount,
          loadedPages: pagingMode === 'pages' ? undefined : loadedPages,
          onPageIndexChange: setPageIndex,
          onPageSizeChange: (next) => {
            setPageSize(next);
            setPageIndex(0);
            setLoadedPages(createBrowseLoadedPages(0));
            setPageCache(new Map());
          },
        }}
      />

      {filtersExtra}

      <p className="section-meta">
        {exporting
          ? 'Exporting filtered rows…'
          : loadingPage
            ? 'Loading page…'
            : `Showing ${formatNumber(visibleRows.length)} on screen · ${formatNumber(totalCount)} match`}
        {statusFilter ? ` · status=${statusLabel(statusFilter)}` : ''}
        {filters
          .filter((filter) => filter.value && filter.value !== '__custom__')
          .map((filter) => ` · ${filter.label.toLowerCase()}=${filter.value}`)
          .join('')}
      </p>

      {error ? (
        <EmptyState message={error} />
      ) : !ready && loadingPage ? (
        <EmptyState message="Loading…" />
      ) : totalCount === 0 ? (
        <EmptyState message={debouncedSearch || statusFilter ? emptyMessage : emptySourceMessage} />
      ) : activeViewMode === 'grid' ? (
        <div className="analysis-browse-grid">{renderGrid(visibleRows)}</div>
      ) : (
        <TablePanel>{renderTable(visibleRows)}</TablePanel>
      )}

      {pagingMode === 'scroll' && totalCount > 0 ? (
        <BrowseScrollSentinel
          enabled={nextScrollPage !== null && !loadingPage}
          loadKey={`${[...loadedPages].sort((a, b) => a - b).join(',')}:${nextScrollPage ?? 'done'}`}
          onLoadMore={() => {
            if (nextScrollPage === null) {
              return;
            }
            setPageIndex(nextScrollPage);
            void ensurePageLoaded(nextScrollPage, requestIdRef.current);
          }}
        />
      ) : null}
    </>
  );

  if (embedded) {
    return <div className="browse-list-block">{content}</div>;
  }

  return <section className="page-section">{content}</section>;
}
