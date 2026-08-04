import type { DataTableSortState } from '@lunarq/frontend-shared/components';

/** Map DataTable sort state to paged API query params (clears cache via RemoteAnalysisDetailBrowse). */
export function browseSortQuery(sort: DataTableSortState | undefined): {
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
} {
  if (!sort) {
    return {};
  }
  return {
    sortBy: sort.columnId,
    sortDirection: sort.direction,
  };
}
