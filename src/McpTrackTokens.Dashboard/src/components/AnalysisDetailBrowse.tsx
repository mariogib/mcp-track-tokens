import { useEffect, useMemo, useState, type ReactNode } from 'react';
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
  sliceBrowsePage,
} from '@lunarq/frontend-shared/components';
import { exportToExcel, type ExcelColumn } from '@lunarq/frontend-shared/utils';
import { TablePanel } from './MetricCard';
import { EmptyState } from './States';
import { BrowseListControls, BrowseScrollSentinel } from '../shared/adminUi';
import { formatNumber } from '../utils/format';

export const STATUS_FILTER_NONE = '__none__';

type AnalysisDetailBrowseProps<T> = {
  heading?: string;
  showHeading?: boolean;
  searchPlaceholder?: string;
  rows: T[];
  getSearchText: (row: T) => string;
  /** When set, adds a Status filter to the browse toolbar. */
  getStatusValue?: (row: T) => string;
  /** Fixed status options. If omitted, options are derived from the current rows. */
  statusOptions?: BrowseFilterOption[];
  exportFilename: string;
  exportTitle: string;
  exportColumns: ExcelColumn[];
  toExportRow: (row: T) => Record<string, unknown>;
  renderTable: (rows: T[]) => ReactNode;
  renderGrid: (rows: T[]) => ReactNode;
  emptyMessage?: string;
  emptySourceMessage?: string;
  /** When true, omit the outer page-section wrapper (for nesting under existing sections). */
  embedded?: boolean;
  /** Extra browse filters merged after the status filter (if any). */
  filters?: BrowseFilterConfig[];
  /** Additional row predicate applied with search/status filters. */
  filterRow?: (row: T) => boolean;
  /** Optional controls rendered under the browse toolbar (e.g. custom date range). */
  filtersExtra?: ReactNode;
  /** Enable paging / lazy / scroll controls. Defaults to `true`. */
  enablePaging?: boolean;
  /**
   * `pages` = classic paging,
   * `lazy` = load each displayed page once,
   * `scroll` = infinite scroll loading each page once.
   * Defaults to `pages`.
   */
  pagingMode?: BrowsePagingMode;
  /** Max rows per page (or per lazy/scroll load). Defaults to `25`. */
  pageSize?: number;
  pageSizeOptions?: number[];
};

function normalizeStatusValue(value: string | null | undefined): string {
  const trimmed = value?.trim() ?? '';
  return trimmed || STATUS_FILTER_NONE;
}

function statusLabel(value: string): string {
  return value === STATUS_FILTER_NONE ? 'None' : value;
}

export function AnalysisDetailBrowse<T>({
  heading = 'Detail data',
  showHeading = true,
  searchPlaceholder = 'Search detail rows...',
  rows,
  getSearchText,
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
  filterRow,
  filtersExtra,
  enablePaging = true,
  pagingMode = 'pages',
  pageSize: initialPageSize = 25,
  pageSizeOptions = [10, 25, 50, 100],
}: AnalysisDetailBrowseProps<T>) {
  const [viewMode, setViewMode] = useState<BrowseViewMode>('table');
  const [searchValue, setSearchValue] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [pageIndex, setPageIndex] = useState(0);
  const [pageSize, setPageSize] = useState(initialPageSize);
  const [loadedPages, setLoadedPages] = useState(() => createBrowseLoadedPages(0));

  const derivedStatusOptions = useMemo(() => {
    if (!getStatusValue) {
      return [] as BrowseFilterOption[];
    }
    if (statusOptions?.length) {
      return statusOptions;
    }
    const values = new Set<string>();
    for (const row of rows) {
      values.add(normalizeStatusValue(getStatusValue(row)));
    }
    return [...values]
      .sort((a, b) => statusLabel(a).localeCompare(statusLabel(b)))
      .map((value) => ({ value, label: statusLabel(value) }));
  }, [getStatusValue, rows, statusOptions]);

  const filteredRows = useMemo(() => {
    const query = searchValue.trim().toLowerCase();
    return rows.filter((row) => {
      if (filterRow && !filterRow(row)) {
        return false;
      }
      if (getStatusValue && statusFilter) {
        if (normalizeStatusValue(getStatusValue(row)) !== statusFilter) {
          return false;
        }
      }
      if (!query) {
        return true;
      }
      return getSearchText(row).toLowerCase().includes(query);
    });
  }, [filterRow, getSearchText, getStatusValue, rows, searchValue, statusFilter]);

  useEffect(() => {
    setPageIndex(0);
    setLoadedPages(createBrowseLoadedPages(0));
  }, [searchValue, statusFilter, pagingMode, pageSize]);

  useEffect(() => {
    if (!enablePaging || pagingMode === 'pages') {
      return;
    }
    setLoadedPages((previous) => loadBrowsePage(previous, pageIndex));
  }, [enablePaging, pageIndex, pagingMode]);

  const pagedRows = useMemo(() => {
    if (!enablePaging) {
      return filteredRows;
    }
    return sliceBrowsePage(filteredRows, {
      mode: pagingMode,
      pageIndex,
      pageSize,
      loadedPages: pagingMode === 'pages' ? undefined : loadedPages,
    });
  }, [enablePaging, filteredRows, loadedPages, pageIndex, pageSize, pagingMode]);

  const activeViewMode = viewMode === 'calendar' ? 'table' : viewMode;

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

  const pageCount = getBrowsePageCount(filteredRows.length, pageSize);
  const nextScrollPage = getNextBrowsePageToLoad(loadedPages, pageCount);

  const content = (
    <>
      <BrowseListControls
        heading={heading}
        showHeading={showHeading}
        viewMode={activeViewMode}
        onViewModeChange={(next) => {
          if (next === 'calendar') {
            setViewMode('table');
            return;
          }
          setViewMode(next);
        }}
        allowCalendarView={false}
        searchValue={searchValue}
        searchPlaceholder={searchPlaceholder}
        onSearchChange={setSearchValue}
        filters={browseFilters}
        onExportToExcel={() =>
          void exportToExcel({
            filename: exportFilename.endsWith('.xlsx')
              ? exportFilename
              : `${exportFilename}.xlsx`,
            title: exportTitle,
            timestamp: new Date().toISOString(),
            columns: exportColumns,
            data: filteredRows.map(toExportRow),
          })
        }
        exportLabel="Export to Excel"
        exportDisabled={filteredRows.length === 0}
        paging={
          enablePaging
            ? {
                mode: pagingMode,
                pageSize,
                pageSizeOptions,
                pageIndex,
                totalCount: filteredRows.length,
                loadedPages: pagingMode === 'pages' ? undefined : loadedPages,
                onPageIndexChange: setPageIndex,
                onPageSizeChange: (next) => {
                  setPageSize(next);
                  setPageIndex(0);
                  setLoadedPages(createBrowseLoadedPages(0));
                },
              }
            : null
        }
      />

      {filtersExtra}

      <p className="section-meta">
        {enablePaging
          ? `Showing ${formatNumber(pagedRows.length)} on screen · ${formatNumber(filteredRows.length)} match of ${formatNumber(rows.length)} rows`
          : `Showing ${formatNumber(filteredRows.length)} of ${formatNumber(rows.length)} rows`}
        {statusFilter ? ` · status=${statusLabel(statusFilter)}` : ''}
        {filters
          .filter((filter) => filter.value && filter.value !== '__custom__')
          .map((filter) => ` · ${filter.label.toLowerCase()}=${filter.value}`)
          .join('')}
      </p>

      {rows.length === 0 ? (
        <EmptyState message={emptySourceMessage} />
      ) : filteredRows.length === 0 ? (
        <EmptyState message={emptyMessage} />
      ) : activeViewMode === 'grid' ? (
        <div className="analysis-browse-grid">{renderGrid(pagedRows)}</div>
      ) : (
        <TablePanel>{renderTable(pagedRows)}</TablePanel>
      )}

      {enablePaging && pagingMode === 'scroll' && filteredRows.length > 0 ? (
        <BrowseScrollSentinel
          enabled={nextScrollPage !== null}
          loadKey={`${[...loadedPages].sort((a, b) => a - b).join(',')}:${nextScrollPage ?? 'done'}`}
          onLoadMore={() => {
            if (nextScrollPage === null) {
              return;
            }
            setLoadedPages((previous) => loadBrowsePage(previous, nextScrollPage));
            setPageIndex(nextScrollPage);
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
