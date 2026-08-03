import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useLocation } from 'react-router-dom';
import {
  API_KEY_CHANGED_EVENT,
  ApiError,
  api,
  getStoredApiKey,
} from '../api/client';

function isApiKeyExemptPath(pathname: string): boolean {
  return pathname === '/settings' || pathname.startsWith('/settings/');
}

/**
 * Tracks the dashboard Bearer key in localStorage and re-reads when it changes
 * (same tab via custom event, other tabs via storage).
 */
export function useStoredApiKey(): string | null {
  const [key, setKey] = useState<string | null>(() => getStoredApiKey());

  useEffect(() => {
    const sync = () => setKey(getStoredApiKey());
    window.addEventListener(API_KEY_CHANGED_EVENT, sync);
    window.addEventListener('storage', sync);
    return () => {
      window.removeEventListener(API_KEY_CHANGED_EVENT, sync);
      window.removeEventListener('storage', sync);
    };
  }, []);

  return key;
}

export type ApiKeyGateReason = 'missing' | 'invalid';

export type ApiKeyAccess =
  | { status: 'ok' }
  | { status: 'missing' }
  | { status: 'invalid' }
  | { status: 'checking' }
  | { status: 'exempt' };

export type ApiKeyGateLocationState = {
  apiKeyGate?: ApiKeyGateReason;
};

export function bearerKeyGateMessage(reason: ApiKeyGateReason): string {
  if (reason === 'missing') {
    return 'You were redirected to Settings because no Bearer API key is saved in this browser. Paste a valid key under Local connection and click Save local key to continue.';
  }

  return 'You were redirected to Settings because the saved Bearer API key was rejected by the API (401 Unauthorized). Replace it with a valid key under Local connection and click Save local key.';
}

/**
 * Requires a present, server-validated Bearer key for non-Settings routes.
 * Missing or 401 → caller should navigate to /settings.
 */
export function useApiKeyAccess(): ApiKeyAccess {
  const location = useLocation();
  const key = useStoredApiKey();
  const exempt = isApiKeyExemptPath(location.pathname);

  const validation = useQuery({
    queryKey: ['api-key-validation', key ?? ''],
    queryFn: ({ signal }) => api.status(signal),
    enabled: Boolean(key) && !exempt,
    retry: false,
    staleTime: 30_000,
  });

  if (exempt) {
    return { status: 'exempt' };
  }

  if (!key) {
    return { status: 'missing' };
  }

  if (validation.isLoading || validation.isFetching) {
    return { status: 'checking' };
  }

  if (
    validation.isError &&
    validation.error instanceof ApiError &&
    validation.error.status === 401
  ) {
    return { status: 'invalid' };
  }

  // Network/server errors should not bounce to Settings — only auth failures.
  return { status: 'ok' };
}
