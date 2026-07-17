import * as fs from 'fs';
import * as path from 'path';
import { getDiagnosticsPath, readStdinJson } from './shared';

const SENSITIVE_KEYS = new Set([
  'prompt',
  'text',
  'content',
  'promptContent',
  'response',
  'responseContent',
  'messages',
  'completion',
]);

/**
 * Write sanitized payload samples to a local diagnostics file.
 * Never saves prompt content by default.
 */
export function sanitizeForDiagnostics(
  value: unknown,
  storePromptContent = false,
): unknown {
  if (Array.isArray(value)) {
    return value.map((v) => sanitizeForDiagnostics(v, storePromptContent));
  }
  if (value && typeof value === 'object') {
    const out: Record<string, unknown> = {};
    for (const [key, child] of Object.entries(value as Record<string, unknown>)) {
      if (SENSITIVE_KEYS.has(key) && !storePromptContent) {
        if (typeof child === 'string') {
          out[key] = `[redacted length=${child.length}]`;
        } else {
          out[key] = '[redacted]';
        }
        continue;
      }
      out[key] = sanitizeForDiagnostics(child, storePromptContent);
    }
    return out;
  }
  return value;
}

export function writeDiagnosticsSample(
  payload: unknown,
  options: { filePath?: string; storePromptContent?: boolean } = {},
): string {
  const filePath = options.filePath ?? getDiagnosticsPath();
  const dir = path.dirname(filePath);
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  const sample = {
    capturedAtUtc: new Date().toISOString(),
    payload: sanitizeForDiagnostics(payload, options.storePromptContent === true),
  };
  fs.appendFileSync(filePath, `${JSON.stringify(sample)}\n`, 'utf8');
  return filePath;
}

async function main(): Promise<void> {
  try {
    const raw = await readStdinJson();
    const store = process.env.MCP_TRACK_TOKENS_STORE_PROMPT_CONTENT === 'true';
    const file = writeDiagnosticsSample(raw, { storePromptContent: store });
    if (process.env.MCP_TRACK_TOKENS_DEBUG === '1') {
      console.error(`Wrote diagnostics sample to ${file}`);
    }
  } catch (err) {
    if (process.env.MCP_TRACK_TOKENS_DEBUG === '1') {
      console.error(err);
    }
  }
  process.exitCode = 0;
}

void main();
