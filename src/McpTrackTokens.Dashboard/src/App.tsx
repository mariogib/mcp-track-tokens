import { Navigate, Route, Routes } from 'react-router-dom';
import { AppLayout } from './layout/AppLayout';
import { OverviewPage } from './pages/OverviewPage';
import { ProjectsPage } from './pages/ProjectsPage';
import { ProjectDetailsPage } from './pages/ProjectDetailsPage';
import { ReportsPage } from './pages/ReportsPage';
import { ImportsPage } from './pages/ImportsPage';
import { ImportedUsagePage } from './pages/ImportedUsagePage';
import { ReconciliationPage } from './pages/ReconciliationPage';
import { UnallocatedActivityPage } from './pages/UnallocatedActivityPage';
import { SettingsPage } from './pages/SettingsPage';
import { HelpPage } from './pages/HelpPage';
import { McpHelpPage } from './pages/McpHelpPage';

export default function App() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<OverviewPage />} />
        <Route path="projects" element={<ProjectsPage />} />
        <Route path="projects/:projectId" element={<ProjectDetailsPage />} />
        <Route path="reports" element={<ReportsPage />} />
        <Route path="imports" element={<ImportsPage />} />
        <Route path="imported-usage" element={<ImportedUsagePage />} />
        <Route path="reconciliation" element={<ReconciliationPage />} />
        <Route path="unallocated" element={<UnallocatedActivityPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="help" element={<HelpPage />} />
        <Route path="help/mcp" element={<McpHelpPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
