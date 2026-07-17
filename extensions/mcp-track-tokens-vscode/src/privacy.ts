import { createHash } from 'crypto';
import type { PrivacyOptions, PrivacyResult } from './types';

/**
 * Privacy helpers: never send prompt content by default.
 * Only length (+ optional hash) are recorded unless storePromptContent is enabled.
 */
export function sanitizePrompt(
  prompt: string | undefined | null,
  options: PrivacyOptions,
): PrivacyResult {
  const text = prompt ?? '';
  const result: PrivacyResult = {
    promptLength: text.length,
  };

  if (options.enablePromptHashing && text.length > 0) {
    const salt = options.hashSalt ?? 'mcp-track-tokens';
    result.promptHash = createHash('sha256').update(`${salt}:${text}`).digest('hex');
  }

  if (options.storePromptContent && text.length > 0) {
    result.promptContent = text;
  }

  return result;
}

export function assertNoPromptLeak(
  payload: Record<string, unknown>,
  storePromptContent: boolean,
): void {
  if (!storePromptContent && typeof payload.promptContent === 'string') {
    throw new Error('Privacy violation: promptContent present while storePromptContent is false');
  }
}
