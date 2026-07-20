import type { RangePreset } from '../utils/dateRange';

type Props = {
  preset: RangePreset;
  fromDate: string;
  toDate: string;
  onPresetChange: (preset: RangePreset) => void;
  onFromDateChange: (value: string) => void;
  onToDateChange: (value: string) => void;
  idPrefix?: string;
};

export function DateRangeFilters({
  preset,
  fromDate,
  toDate,
  onPresetChange,
  onFromDateChange,
  onToDateChange,
  idPrefix = 'date-range',
}: Props) {
  const showCustom = preset === 'custom';

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
          <option value="month">This month</option>
          <option value="custom">Custom</option>
        </select>
      </div>
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
              max={fromDate ? undefined : undefined}
              onChange={(e) => onToDateChange(e.target.value)}
            />
          </div>
        </>
      ) : null}
    </div>
  );
}
