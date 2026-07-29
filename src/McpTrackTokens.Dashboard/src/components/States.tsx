import { ApiError } from '../api/client';
import { EmptyState, LoadingState, TextLink } from '../shared/adminUi';

export { EmptyState, LoadingState };

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
          <TextLink to="/settings">Settings → API key management</TextLink>, paste your tracking
          key, and click <strong>Save local key</strong>.
        </p>
      ) : null}
    </div>
  );
}
