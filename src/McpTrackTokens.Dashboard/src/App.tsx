import { Navigate, Route, Routes, useSearchParams } from 'react-router-dom';
import { AppLayout } from './layout/AppLayout';
import { OverviewPage } from './pages/OverviewPage';
import { ProjectsPage } from './pages/ProjectsPage';
import { ProjectDetailsPage } from './pages/ProjectDetailsPage';
import { ProjectChartDetailPage } from './pages/ProjectChartDetailPage';
import { OverviewChartDetailPage } from './pages/OverviewChartDetailPage';
import { ReportsPage } from './pages/ReportsPage';
import { TimesheetPage } from './pages/TimesheetPage';
import { TimesheetReportsPage } from './pages/TimesheetReportsPage';
import { ImportedUsagePage } from './pages/ImportedUsagePage';
import { UnallocatedActivityPage } from './pages/UnallocatedActivityPage';
import { SettingsPage } from './pages/SettingsPage';
import { HelpPage } from './pages/HelpPage';
import { McpHelpPage } from './pages/McpHelpPage';

function RedirectTimesheetReports({ scope }: { scope?: 'project' | 'client' }) {
  const [params] = useSearchParams();
  const next = new URLSearchParams(params);
  if (scope) next.set('scope', scope);
  const qs = next.toString();
  return <Navigate to={qs ? `/timesheet/reports?${qs}` : '/timesheet/reports'} replace />;
}

export default function App() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<OverviewPage />} />
        <Route path="charts/:chartKey" element={<OverviewChartDetailPage />} />
        <Route path="projects" element={<ProjectsPage />} />
        <Route path="projects/:projectId" element={<ProjectDetailsPage />} />
        <Route
          path="projects/:projectId/charts/:chartKey"
          element={<ProjectChartDetailPage />}
        />
        <Route path="timesheet" element={<TimesheetPage />} />
        <Route path="timesheet/reports" element={<TimesheetReportsPage />} />
        <Route path="timesheet/reports/overall" element={<RedirectTimesheetReports />} />
        <Route
          path="timesheet/reports/projects"
          element={<RedirectTimesheetReports scope="project" />}
        />
        <Route
          path="timesheet/reports/clients"
          element={<RedirectTimesheetReports scope="client" />}
        />
        <Route path="reports" element={<ReportsPage />} />
        <Route path="imported-usage" element={<ImportedUsagePage />} />
        <Route path="imports" element={<Navigate to="/imported-usage" replace />} />
        <Route path="reconciliation" element={<Navigate to="/imported-usage" replace />} />
        <Route path="unallocated" element={<UnallocatedActivityPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="help" element={<HelpPage />} />
        <Route path="help/mcp" element={<McpHelpPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
