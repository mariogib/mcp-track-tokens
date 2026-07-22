import type { CSSProperties, ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { Card } from '../shared/adminUi';

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
  const navigate = useNavigate();
  const interactive = Boolean(to || onClick);

  return (
    <Card
      className={`metric-card${interactive ? ' metric-card--interactive' : ''}`}
      onClick={to ? () => void navigate(to) : onClick}
    >
      <div className="label">{label}</div>
      <div className="value">{value}</div>
      {hint ? <div className="hint">{hint}</div> : null}
    </Card>
  );
}

/** Shared module card used as a general content panel. */
export function Panel({
  children,
  className = '',
  onClick,
}: {
  children: ReactNode;
  className?: string;
  onClick?: () => void;
}) {
  return (
    <Card className={['panel', className].filter(Boolean).join(' ')} onClick={onClick}>
      {children}
    </Card>
  );
}

/** Table surface on shared Card (zero padding; scroll lives in .table-wrap). */
export function TablePanel({
  children,
  className = '',
  style,
}: {
  children: ReactNode;
  className?: string;
  style?: CSSProperties;
}) {
  return (
    <div
      className={['card', 'panel', 'table-panel', className].filter(Boolean).join(' ')}
      style={style}
    >
      <div className="table-wrap">{children}</div>
    </div>
  );
}
