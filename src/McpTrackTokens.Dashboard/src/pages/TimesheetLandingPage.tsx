import { TimesheetPage } from './TimesheetPage';
import { TimesheetReportsPage } from './TimesheetReportsPage';
import { useTabSearchParam } from '../hooks/useTabSearchParam';
import { Page } from '../layout/AppLayout';

const TIMESHEET_TABS = ['Entries', 'Reports'] as const;

export function TimesheetLandingPage() {
  const [tab, setTab] = useTabSearchParam(TIMESHEET_TABS, 'Entries');

  return (
    <Page>
      <div className="tabs" role="tablist" aria-label="Timesheet sections">
        {TIMESHEET_TABS.map((name) => (
          <button
            key={name}
            type="button"
            role="tab"
            aria-selected={tab === name}
            className={`tab${tab === name ? ' active' : ''}`}
            onClick={() => setTab(name)}
          >
            {name}
          </button>
        ))}
      </div>

      {tab === 'Entries' ? <TimesheetPage /> : <TimesheetReportsPage />}
    </Page>
  );
}
