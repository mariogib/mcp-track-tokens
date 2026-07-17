type StatusTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral';

const toneClass: Record<StatusTone, string> = {
  success: 'badge badge-success',
  warning: 'badge badge-warning',
  danger: 'badge badge-danger',
  info: 'badge badge-info',
  neutral: 'badge',
};

export function StatusBadge({
  label,
  tone = 'neutral',
}: {
  label: string;
  tone?: StatusTone;
}) {
  return <span className={toneClass[tone]}>{label}</span>;
}
