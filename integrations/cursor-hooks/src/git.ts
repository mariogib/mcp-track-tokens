import { execFile } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import { promisify } from 'util';

const execFileAsync = promisify(execFile);

export interface GitInfo {
  repositoryPath?: string;
  remoteUrl?: string;
  branch?: string;
}

/**
 * Cursor often emits Windows roots as "/d:/Dev/...". Normalize those so
 * path.resolve / fs.existsSync find the real repo on disk.
 */
export function normalizeWorkspacePath(input?: string): string | undefined {
  if (!input || !input.trim()) {
    return undefined;
  }

  let value = input.trim().replace(/\\/g, '/');
  if (
    value.length >= 3 &&
    value[0] === '/' &&
    /[a-zA-Z]/.test(value[1]!) &&
    value[2] === ':'
  ) {
    value = value.slice(1);
  }

  if (value.length >= 2 && /[a-zA-Z]/.test(value[0]!) && value[1] === ':') {
    value = value[0]!.toUpperCase() + value.slice(1);
  }

  return value;
}

export function findGitRoot(
  start: string,
  existsSync: (p: string) => boolean = fs.existsSync,
): string | undefined {
  const normalized = normalizeWorkspacePath(start) ?? start;
  let current = path.resolve(normalized);
  for (let i = 0; i < 40; i++) {
    if (existsSync(path.join(current, '.git'))) {
      return current;
    }
    const parent = path.dirname(current);
    if (parent === current) {
      break;
    }
    current = parent;
  }
  return undefined;
}

async function git(args: string[], cwd: string): Promise<string> {
  const { stdout } = await execFileAsync('git', args, {
    cwd,
    windowsHide: true,
    maxBuffer: 1024 * 1024,
  });
  return stdout.trim();
}

/**
 * Resolve Git root/remote/branch from cwd and/or payload hints.
 */
export async function resolveGit(input: {
  cwd?: string;
  workspaceRoots?: string[];
  repositoryPath?: string;
}): Promise<GitInfo> {
  const candidates = [
    input.repositoryPath,
    ...(input.workspaceRoots ?? []),
    input.cwd,
    process.cwd(),
  ]
    .map((p) => normalizeWorkspacePath(p) ?? p)
    .filter((p): p is string => Boolean(p));

  let repositoryPath: string | undefined;
  for (const candidate of candidates) {
    const root = findGitRoot(candidate);
    if (root) {
      repositoryPath = root;
      break;
    }
  }

  if (!repositoryPath) {
    return {};
  }

  const info: GitInfo = { repositoryPath };
  try {
    const branch = await git(['rev-parse', '--abbrev-ref', 'HEAD'], repositoryPath);
    if (branch && branch !== 'HEAD') {
      info.branch = branch;
    }
  } catch {
    // ignore
  }
  try {
    info.remoteUrl = await git(['config', '--get', 'remote.origin.url'], repositoryPath);
  } catch {
    // ignore
  }
  return info;
}
