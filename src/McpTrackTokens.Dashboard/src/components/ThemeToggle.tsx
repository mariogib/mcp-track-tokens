import { useTheme } from '../theme/ThemeProvider';

export function ThemeToggle() {
  const { preference, toggleTheme } = useTheme();
  const label =
    preference === 'system' ? 'System' : preference === 'light' ? 'Light' : 'Dark';
  const nextHint =
    preference === 'system' ? 'Switch to light theme' : preference === 'light' ? 'Switch to dark theme' : 'Use Windows theme';

  return (
    <button
      type="button"
      className="btn btn-secondary"
      onClick={toggleTheme}
      aria-label={nextHint}
      title={`${nextHint} (current: ${label})`}
    >
      {label}
    </button>
  );
}
