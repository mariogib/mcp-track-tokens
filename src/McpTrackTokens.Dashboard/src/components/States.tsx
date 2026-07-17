import { Link } from 'react-router-dom';
import { ApiError } from '../api/client';

export function LoadingState({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="loading-box" role="status" aria-live="polite">
      {label}
    </div>
  );
}

export function ErrorState({ message, error }: { message: string; error?: unknown }) {
  const unauthorized =
    error instanceof ApiError
      ? error.status === 401
      : /unauthorized|401/i.test(message);

  return (
    <div className="error-box" role="alert">
      <p>{message}</p>
      {unauthorized ? (
        <p>
          API routes require <code>Authorization: Bearer …</code>. Open{' '}
          <Link to="/settings">Settings → API key management</Link>, paste your tracking key, and
          click <strong>Save local key</strong>.
        </p>
      ) : null}
    </div>
  );
}

export function EmptyState({ message }: { message: string }) {
  return <div className="empty">{message}</div>;
}
