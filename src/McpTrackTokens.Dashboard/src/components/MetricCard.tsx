import { Link } from 'react-router-dom';
import type { ReactNode } from 'react';

export function MetricCard({
  label,
  value,
  hint,
  to,
  onClick,
}: {
  label: string;
  value: string;
  hint?: string;
  to?: string;
  onClick?: () => void;
}) {
  const body: ReactNode = (
    <>
      <div className="label">{label}</div>
      <div className="value">{value}</div>
      {hint ? <div className="hint">{hint}</div> : null}
    </>
  );

  if (to) {
    return (
      <Link to={to} className="metric-card metric-card--interactive">
        {body}
      </Link>
    );
  }

  if (onClick) {
    return (
      <button type="button" className="metric-card metric-card--interactive" onClick={onClick}>
        {body}
      </button>
    );
  }

  return <article className="metric-card">{body}</article>;
}
