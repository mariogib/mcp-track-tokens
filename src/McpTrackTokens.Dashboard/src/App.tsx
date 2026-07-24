import { Navigate, Route, Routes, useSearchParams } from 'react-router-dom';
import { AppLayout } from './layout/AppLayout';
import { OverviewPage } from './pages/OverviewPage';
import { ProjectsPage } from './pages/ProjectsPage';
import { ProjectDetailsPage } from './pages/ProjectDetailsPage';
import { ProjectChartDetailPage } from './pages/ProjectChartDetailPage';
import { OverviewChartDetailPage } from './pages/OverviewChartDetailPage';
import { ReportsPage } from './pages/ReportsPage';
import { TimesheetLandingPage } from './pages/TimesheetLandingPage';
import { ImportedUsagePage } from './pages/ImportedUsagePage';
import { SettingsPage } from './pages/SettingsPage';
import { HelpLandingPage } from './pages/HelpLandingPage';

/** Old `/timesheet/reports*` URLs → `/timesheet?tab=reports` (keeps scope/range/etc.). */
function RedirectTimesheetReports({ scope }: { scope?: 'project' | 'client' }) {
  const [params] = useSearchParams();
  const next = new URLSearchParams(params);
  next.set('tab', 'reports');
  if (scope) next.set('scope', scope);
  const qs = next.toString();
  return <Navigate to={`/timesheet?${qs}`} replace />;
}

/** Old `/help/mcp` → `/help?view=mcp-help` (keeps nested Tools/Resources/Prompts tab). */
function RedirectMcpHelp() {
  const [params] = useSearchParams();
  const next = new URLSearchParams(params);
  next.set('view', 'mcp-help');
  const qs = next.toString();
  return <Navigate to={`/help?${qs}`} replace />;
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
        <Route path="timesheet" element={<TimesheetLandingPage />} />
        <Route path="timesheet/reports" element={<RedirectTimesheetReports />} />
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
        <Route
          path="unallocated"
          element={<Navigate to="/imported-usage?tab=unallocated-prompts" replace />}
        />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="help" element={<HelpLandingPage />} />
        <Route path="help/mcp" element={<RedirectMcpHelp />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
