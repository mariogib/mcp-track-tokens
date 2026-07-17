import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import {
  API_KEY_STORAGE,
  ApiError,
  apiRequest,
  getApiBaseUrl,
  getStoredApiKey,
  setStoredApiKey,
} from '../api/client';

describe('api client', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify({ status: 'Healthy', healthy: true }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('defaults base URL to localhost:5187', () => {
    expect(getApiBaseUrl()).toBe('http://127.0.0.1:5187');
  });

  it('stores and reads bearer key from localStorage', () => {
    setStoredApiKey('test-key-123');
    expect(getStoredApiKey()).toBe('test-key-123');
    expect(localStorage.getItem(API_KEY_STORAGE)).toBe('test-key-123');
    setStoredApiKey(null);
    expect(getStoredApiKey()).toBeNull();
  });

  it('sends Authorization Bearer header when key is present', async () => {
    setStoredApiKey('secret');
    await apiRequest('/health', { auth: true });
    expect(fetch).toHaveBeenCalledWith(
      'http://127.0.0.1:5187/health',
      expect.objectContaining({
        headers: expect.objectContaining({
          Authorization: 'Bearer secret',
        }),
      }),
    );
  });

  it('throws ApiError on non-OK responses', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(JSON.stringify({ title: 'Unauthorized' }), {
          status: 401,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    );

    await expect(apiRequest('/api/v1/projects')).rejects.toBeInstanceOf(ApiError);
  });
});
