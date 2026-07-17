import { execFile } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import { promisify } from 'util';
import type { RepoInfo } from './types';

const execFileAsync = promisify(execFile);

export interface WorkspaceFolderLike {
  uri: { fsPath: string };
  name: string;
}

export interface GitResolverDeps {
  getWorkspaceFolders: () => readonly WorkspaceFolderLike[] | undefined;
  getActiveFilePath: () => string | undefined;
  /** Remembered mapping: workspace folder path -> repository path */
  getRememberedRepo?: (workspacePath: string) => string | undefined;
  setRememberedRepo?: (workspacePath: string, repositoryPath: string) => void;
  getLastSelectedRepo?: () => string | undefined;
  setLastSelectedRepo?: (repositoryPath: string) => void;
  askUserToPickRepo?: (candidates: string[]) => Promise<string | undefined>;
  getGitApiRepos?: () => Promise<GitApiLike | undefined>;
  execGit?: (args: string[], cwd: string) => Promise<string>;
  existsSync?: (p: string) => boolean;
  findNearestGit?: (start: string) => string | undefined;
}

export interface GitApiLike {
  repositories: Array<{
    rootUri: { fsPath: string };
    state: {
      HEAD?: { name?: string } | null;
      remotes: Array<{ name: string; fetchUrl?: string; pushUrl?: string }>;
    };
  }>;
}

/**
 * Pure multi-root repository selection policy.
 */
export function selectRepositoryPath(input: {
  candidates: string[];
  activeFilePath?: string;
  lastSelected?: string;
  remembered?: string;
}): { repositoryPath?: string; needsAsk: boolean } {
  const unique = [...new Set(input.candidates.filter(Boolean))];
  if (unique.length === 0) {
    return { needsAsk: false };
  }

  if (input.activeFilePath) {
    const fromActive = unique.find((c) => isPathInsideOrEqual(input.activeFilePath!, c));
    if (fromActive) {
      return { repositoryPath: fromActive, needsAsk: false };
    }
  }

  if (input.remembered && unique.includes(input.remembered)) {
    return { repositoryPath: input.remembered, needsAsk: false };
  }

  if (input.lastSelected && unique.includes(input.lastSelected)) {
    return { repositoryPath: input.lastSelected, needsAsk: false };
  }

  if (unique.length === 1) {
    return { repositoryPath: unique[0], needsAsk: false };
  }

  return { needsAsk: true };
}

export function findNearestGitRoot(
  startPath: string,
  existsSync: (p: string) => boolean = fs.existsSync,
): string | undefined {
  let current = path.resolve(startPath);
  // If start is a file, begin at its directory
  try {
    if (existsSync(current) && !isDirectory(current, existsSync)) {
      current = path.dirname(current);
    }
  } catch {
    current = path.dirname(current);
  }

  for (let i = 0; i < 40; i++) {
    const gitDir = path.join(current, '.git');
    if (existsSync(gitDir)) {
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

function isDirectory(p: string, existsSync: (p: string) => boolean): boolean {
  try {
    return existsSync(p) && fs.statSync(p).isDirectory();
  } catch {
    return false;
  }
}

function normalizePath(p: string): string {
  return path.resolve(p).replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase();
}

function isPathInsideOrEqual(child: string, parent: string): boolean {
  const c = normalizePath(child);
  const p = normalizePath(parent);
  return c === p || c.startsWith(`${p}/`);
}

async function defaultExecGit(args: string[], cwd: string): Promise<string> {
  const { stdout } = await execFileAsync('git', args, {
    cwd,
    windowsHide: true,
    maxBuffer: 1024 * 1024,
  });
  return stdout.trim();
}

export class GitResolver {
  private readonly deps: Required<
    Pick<
      GitResolverDeps,
      'getWorkspaceFolders' | 'getActiveFilePath' | 'execGit' | 'existsSync' | 'findNearestGit'
    >
  > &
    GitResolverDeps;

  constructor(deps: GitResolverDeps) {
    this.deps = {
      ...deps,
      execGit: deps.execGit ?? defaultExecGit,
      existsSync: deps.existsSync ?? fs.existsSync,
      findNearestGit: deps.findNearestGit ?? ((start) => findNearestGitRoot(start, deps.existsSync ?? fs.existsSync)),
    };
  }

  async resolve(): Promise<RepoInfo> {
    const folders = this.deps.getWorkspaceFolders() ?? [];
    const activeFilePath = this.deps.getActiveFilePath();
    const candidates = new Set<string>();

    for (const folder of folders) {
      const fromNearest = this.deps.findNearestGit!(folder.uri.fsPath);
      if (fromNearest) {
        candidates.add(fromNearest);
      } else {
        candidates.add(folder.uri.fsPath);
      }
    }

    if (activeFilePath) {
      const fromFile = this.deps.findNearestGit!(activeFilePath);
      if (fromFile) {
        candidates.add(fromFile);
      }
    }

    const gitApi = await this.deps.getGitApiRepos?.();
    if (gitApi) {
      for (const repo of gitApi.repositories) {
        candidates.add(repo.rootUri.fsPath);
      }
    }

    const workspacePath = folders[0]?.uri.fsPath;
    const remembered = workspacePath
      ? this.deps.getRememberedRepo?.(workspacePath)
      : undefined;
    const lastSelected = this.deps.getLastSelectedRepo?.();

    const selection = selectRepositoryPath({
      candidates: [...candidates],
      activeFilePath,
      lastSelected,
      remembered,
    });

    let repositoryPath = selection.repositoryPath;
    if (selection.needsAsk && this.deps.askUserToPickRepo) {
      const picked = await this.deps.askUserToPickRepo([...candidates]);
      if (picked) {
        repositoryPath = picked;
        this.deps.setLastSelectedRepo?.(picked);
        if (workspacePath) {
          this.deps.setRememberedRepo?.(workspacePath, picked);
        }
      }
    } else if (repositoryPath) {
      this.deps.setLastSelectedRepo?.(repositoryPath);
    }

    const info: RepoInfo = {
      workspacePath,
      repositoryPath,
      activeFilePath,
    };

    if (!repositoryPath) {
      return info;
    }

    // Prefer Git extension API details when matching repo is present
    if (gitApi) {
      const match = gitApi.repositories.find(
        (r) => normalizePath(r.rootUri.fsPath) === normalizePath(repositoryPath!),
      );
      if (match) {
        info.branch = match.state.HEAD?.name;
        const remote =
          match.state.remotes.find((r) => r.name === 'origin') ?? match.state.remotes[0];
        info.remoteUrl = remote?.fetchUrl ?? remote?.pushUrl;
        return info;
      }
    }

    try {
      info.branch = await this.deps.execGit!(['rev-parse', '--abbrev-ref', 'HEAD'], repositoryPath);
      info.remoteUrl = await this.deps.execGit!(
        ['config', '--get', 'remote.origin.url'],
        repositoryPath,
      );
    } catch {
      // CLI fallback failed; leave optional fields empty
    }

    return info;
  }
}
