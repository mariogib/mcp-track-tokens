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
  getSearchText: (row: T) => string;
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
 * Export uses currently cached rows only.
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
    setPageIndex(0);
    setLoadedPages(createBrowseLoadedPages(0));
    setPageCache(new Map());
    setTotalCount(0);
    setReady(false);
    setError(null);
  }, []);

  useEffect(() => {
    resetPaging();
  }, [filterKey, debouncedSearch, statusFilter, pageSize, pagingMode, resetPaging]);

  const ensurePageLoaded = useCallback(
    async (targetPage: number) => {
      if (targetPage < 0) {
        return;
      }
      if (pageCacheRef.current.has(targetPage)) {
        setLoadedPages((previous) => loadBrowsePage(previous, targetPage));
        return;
      }
      if (inFlightRef.current.has(targetPage)) {
        return;
      }

      const requestId = requestIdRef.current;
      inFlightRef.current.add(targetPage);
      setLoadingPage(true);
      const controller = new AbortController();
      try {
        const result = await fetchPageRef.current({
          pageIndex: targetPage,
          pageSize,
          search: debouncedSearch,
          status: statusFilter,
          signal: controller.signal,
        });
        if (requestId !== requestIdRef.current) {
          return;
        }
        setTotalCount(result.totalCount);
        setPageCache((previous) => {
          const next = new Map(previous);
          next.set(targetPage, result.items);
          return next;
        });
        setLoadedPages((previous) => loadBrowsePage(previous, targetPage));
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
    [debouncedSearch, pageSize, statusFilter],
  );

  useEffect(() => {
    void ensurePageLoaded(pageIndex);
  }, [ensurePageLoaded, pageIndex, filterKey, debouncedSearch, statusFilter, pageSize]);

  useEffect(() => {
    if (pagingMode !== 'lazy' && pagingMode !== 'scroll') {
      return;
    }
    setLoadedPages((previous) => loadBrowsePage(previous, pageIndex));
  }, [pageIndex, pagingMode]);

  const visibleRows = useMemo(() => {
    if (pagingMode === 'pages') {
      return pageCache.get(pageIndex) ?? [];
    }
    const pages = [...loadedPages].sort((a, b) => a - b);
    return pages.flatMap((page) => pageCache.get(page) ?? []);
  }, [loadedPages, pageCache, pageIndex, pagingMode]);

  const cachedExportRows = useMemo(() => {
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
    for (const row of cachedExportRows) {
      values.add(normalizeStatusValue(getStatusValue(row)));
    }
    return [...values]
      .sort((a, b) => statusLabel(a).localeCompare(statusLabel(b)))
      .map((value) => ({ value, label: statusLabel(value) }));
  }, [cachedExportRows, getStatusValue, statusOptions]);

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
        onExportToExcel={() =>
          void exportToExcel({
            filename: exportFilename.endsWith('.xlsx')
              ? exportFilename
              : `${exportFilename}.xlsx`,
            title: `${exportTitle} (loaded pages)`,
            timestamp: new Date().toISOString(),
            columns: exportColumns,
            data: cachedExportRows.map(toExportRow),
          })
        }
        exportLabel="Export loaded"
        exportDisabled={cachedExportRows.length === 0}
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
        {loadingPage
          ? 'Loading page…'
          : `Showing ${formatNumber(visibleRows.length)} on screen · ${formatNumber(totalCount)} match`}
        {statusFilter ? ` · status=${statusLabel(statusFilter)}` : ''}
        {filters
          .filter((filter) => filter.value && filter.value !== '__custom__')
          .map((filter) => ` · ${filter.label.toLowerCase()}=${filter.value}`)
          .join('')}
        {cachedExportRows.length > 0 && cachedExportRows.length < totalCount
          ? ` · export covers ${formatNumber(cachedExportRows.length)} loaded rows`
          : ''}
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
            void ensurePageLoaded(nextScrollPage);
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
