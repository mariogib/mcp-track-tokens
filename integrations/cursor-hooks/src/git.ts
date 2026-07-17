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

export function findGitRoot(
  start: string,
  existsSync: (p: string) => boolean = fs.existsSync,
): string | undefined {
  let current = path.resolve(start);
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
  ].filter((p): p is string => Boolean(p));

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
    info.branch = await git(['rev-parse', '--abbrev-ref', 'HEAD'], repositoryPath);
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
