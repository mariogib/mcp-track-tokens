import { currentUtcYearMonth, type RangePreset } from '../utils/dateRange';

export type MonthWithData = {
  year: number;
  month: number;
  entryCount?: number;
};

type Props = {
  preset: RangePreset;
  fromDate: string;
  toDate: string;
  onPresetChange: (preset: RangePreset) => void;
  onFromDateChange: (value: string) => void;
  onToDateChange: (value: string) => void;
  /** Selected calendar month when preset is `month` (defaults to current UTC month). */
  year?: number;
  month?: number;
  onYearMonthChange?: (year: number, month: number) => void;
  /** When set with onMonthSelect, custom mode shows months that have data. */
  monthsWithData?: MonthWithData[];
  onMonthSelect?: (year: number, month: number) => void;
  idPrefix?: string;
};

const MONTH_OPTIONS = Array.from({ length: 12 }, (_, index) => {
  const month = index + 1;
  const label = new Intl.DateTimeFormat(undefined, {
    month: 'long',
    timeZone: 'UTC',
  }).format(new Date(Date.UTC(2000, index, 1)));
  return { value: month, label };
});

function monthKey(year: number, month: number): string {
  return `${year}-${String(month).padStart(2, '0')}`;
}

function formatMonthLabel(year: number, month: number, entryCount?: number): string {
  const label = new Intl.DateTimeFormat(undefined, {
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(Date.UTC(year, month - 1, 1)));
  if (entryCount == null) return label;
  return `${label} (${entryCount})`;
}

/** Detect if from/to span exactly one UTC calendar month. */
function selectedMonthKey(fromDate: string, toDate: string): string {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(fromDate) || !/^\d{4}-\d{2}-\d{2}$/.test(toDate)) {
    return '';
  }
  const [fy, fm, fd] = fromDate.split('-').map(Number);
  const [ty, tm, td] = toDate.split('-').map(Number);
  if (fd !== 1 || fy !== ty || fm !== tm) return '';
  const lastDay = new Date(Date.UTC(fy, fm, 0)).getUTCDate();
  if (td !== lastDay) return '';
  return monthKey(fy, fm);
}

function yearOptions(extraYears: number[] = []): number[] {
  const { year: current } = currentUtcYearMonth();
  const years = new Set<number>();
  for (let y = current - 5; y <= current + 1; y += 1) years.add(y);
  for (const y of extraYears) {
    if (y >= 2000 && y <= 2100) years.add(y);
  }
  return [...years].sort((a, b) => b - a);
}

export function DateRangeFilters({
  preset,
  fromDate,
  toDate,
  onPresetChange,
  onFromDateChange,
  onToDateChange,
  year,
  month,
  onYearMonthChange,
  monthsWithData,
  onMonthSelect,
  idPrefix = 'date-range',
}: Props) {
  const showCustom = preset === 'custom';
  const showChooseMonth = preset === 'month' && Boolean(onYearMonthChange);
  const showMonthsWithData = showCustom && Boolean(monthsWithData && onMonthSelect);
  const monthValue = showMonthsWithData ? selectedMonthKey(fromDate, toDate) : '';
  const defaults = currentUtcYearMonth();
  const selectedYear = year ?? defaults.year;
  const selectedMonth = month ?? defaults.month;
  const years = yearOptions([
    selectedYear,
    ...(monthsWithData ?? []).map((row) => row.year),
  ]);

  return (
    <div className="field-row chart-detail-filters">
      <div className="field">
        <label htmlFor={`${idPrefix}-preset`}>Date range</label>
        <select
          id={`${idPrefix}-preset`}
          value={preset}
          onChange={(e) => onPresetChange(e.target.value as RangePreset)}
        >
          <option value="7d">Last 7 days</option>
          <option value="30d">Last 30 days</option>
          <option value="90d">Last 90 days</option>
          <option value="month">Choose month</option>
          <option value="custom">Custom</option>
        </select>
      </div>
      {showChooseMonth ? (
        <>
          <div className="field">
            <label htmlFor={`${idPrefix}-year`}>Year</label>
            <select
              id={`${idPrefix}-year`}
              value={selectedYear}
              onChange={(e) => onYearMonthChange?.(Number(e.target.value), selectedMonth)}
            >
              {years.map((y) => (
                <option key={y} value={y}>
                  {y}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor={`${idPrefix}-month-pick`}>Month</label>
            <select
              id={`${idPrefix}-month-pick`}
              value={selectedMonth}
              onChange={(e) => onYearMonthChange?.(selectedYear, Number(e.target.value))}
            >
              {MONTH_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>
        </>
      ) : null}
      {showCustom ? (
        <>
          <div className="field">
            <label htmlFor={`${idPrefix}-from`}>From</label>
            <input
              id={`${idPrefix}-from`}
              type="date"
              value={fromDate}
              onChange={(e) => onFromDateChange(e.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor={`${idPrefix}-to`}>To</label>
            <input
              id={`${idPrefix}-to`}
              type="date"
              value={toDate}
              onChange={(e) => onToDateChange(e.target.value)}
            />
          </div>
        </>
      ) : null}
      {showMonthsWithData ? (
        <div className="field">
          <label htmlFor={`${idPrefix}-month`}>Month with timesheets</label>
          <select
            id={`${idPrefix}-month`}
            value={monthValue}
            onChange={(e) => {
              const value = e.target.value;
              if (!value || !onMonthSelect) return;
              const [y, m] = value.split('-').map(Number);
              if (!y || !m) return;
              onMonthSelect(y, m);
            }}
          >
            <option value="">Select month…</option>
            {(monthsWithData ?? []).map((row) => {
              const key = monthKey(row.year, row.month);
              return (
                <option key={key} value={key}>
                  {formatMonthLabel(row.year, row.month, row.entryCount)}
                </option>
              );
            })}
          </select>
        </div>
      ) : null}
    </div>
  );
}
