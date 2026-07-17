import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { OverviewPage } from '../pages/OverviewPage';
import { ThemeProvider } from '../theme/ThemeProvider';

function renderOverview() {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={client}>
      <ThemeProvider>
        <MemoryRouter>
          <OverviewPage />
        </MemoryRouter>
      </ThemeProvider>
    </QueryClientProvider>,
  );
}

describe('OverviewPage', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        if (url.includes('/health')) {
          return new Response(JSON.stringify({ status: 'Healthy', healthy: true }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          });
        }
        if (url.includes('/api/v1/status')) {
          return new Response(
            JSON.stringify({
              isHealthy: true,
              databasePath: '~/.mcp-track-tokens/mcp-track-tokens.db',
              databaseProvider: 'Sqlite',
              currentProject: {
                id: '11111111-1111-1111-1111-111111111111',
                name: 'Demo Project',
                slug: 'demo-project',
                currency: 'USD',
                isActive: true,
                createdAtUtc: '2026-07-01T00:00:00Z',
                updatedAtUtc: '2026-07-17T00:00:00Z',
                repositoryCount: 1,
              },
              activeSessionId: '22222222-2222-2222-2222-222222222222',
              activeSessionEditor: 'Cursor',
              queuedEventCount: 0,
              unallocatedEventCount: 2,
              unallocatedUsageCount: 1,
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          );
        }
        if (url.includes('/api/v1/reports/summary')) {
          return new Response(
            JSON.stringify({
              year: 2026,
              month: 7,
              fromUtc: '2026-07-01T00:00:00Z',
              toUtc: '2026-08-01T00:00:00Z',
              currency: 'USD',
              activity: {
                promptCount: 42,
                agentRuns: 10,
                agentDurationMilliseconds: 600000,
                activeProjectTimeSeconds: 7200,
                sessionCount: 3,
                failureCount: 0,
                cancellationCount: 0,
              },
              usage: {
                inputTokens: 1000,
                outputTokens: 500,
                cachedInputTokens: 0,
                reasoningTokens: 0,
                totalTokens: 1500,
                requestCount: 20,
                reportedCost: 12.5,
                currency: 'USD',
              },
              cost: {
                usageBasedCost: 10,
                subscriptionAllocation: 5,
                otherProviderCost: 0,
                unallocatedCost: 1.25,
                totalAiCost: 15,
                currency: 'USD',
              },
              projects: [],
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          );
        }
        if (url.includes('/api/v1/sessions/active')) {
          return new Response(
            JSON.stringify({
              id: '22222222-2222-2222-2222-222222222222',
              projectName: 'Demo Project',
              editor: 'Cursor',
              startedAtUtc: '2026-07-17T08:00:00Z',
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          );
        }
        if (url.includes('/api/v1/unallocated')) {
          return new Response(JSON.stringify([]), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          });
        }
        return new Response('not found', { status: 404 });
      }),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders overview metrics from the API', async () => {
    renderOverview();

    await waitFor(() => {
      expect(screen.getByText('Demo Project')).toBeInTheDocument();
    });

    expect(screen.getByText('Prompts (month)')).toBeInTheDocument();
    expect(screen.getByText('42')).toBeInTheDocument();
    expect(screen.getByText('Cursor cost (month)')).toBeInTheDocument();
    expect(screen.getByText('Server health')).toBeInTheDocument();
    expect(screen.getByText(/mcp-track-tokens\.db/)).toBeInTheDocument();
  });
});
