/** Last folder chosen by Backup now (absolute path when desktop host is available). */
export const BACKUP_FOLDER_STORAGE_KEY = 'mcp-track-tokens-backup-folder';

const HANDLE_DB_NAME = 'mcp-track-tokens-backup';
const HANDLE_STORE = 'handles';
const HANDLE_KEY = 'backupFolder';

export type BackupFolderRef = {
  path?: string;
  handle?: FileSystemDirectoryHandle;
};

export type LocalBackupFile = {
  fileName: string;
  fullPath?: string;
  sizeBytes: number;
  createdAtUtc: string;
  /** Present when using the File System Access API (browser). */
  handle?: FileSystemFileHandle;
};

type HostResponse = {
  type: string;
  requestId: string;
  path?: string | null;
  cancelled?: boolean;
  error?: string | null;
  fileName?: string | null;
  base64?: string | null;
  files?: Array<{
    fileName: string;
    fullPath: string;
    sizeBytes: number;
    createdAtUtc: string;
  }>;
};

declare global {
  interface Window {
    mcpTrackTokensDesktop?: boolean;
    chrome?: {
      webview?: {
        postMessage: (message: unknown) => void;
        addEventListener: (type: 'message', listener: (event: MessageEvent) => void) => void;
        removeEventListener: (type: 'message', listener: (event: MessageEvent) => void) => void;
      };
    };
  }
}

function isDesktopHost(): boolean {
  return Boolean(window.mcpTrackTokensDesktop && window.chrome?.webview);
}

export function getStoredBackupFolder(): string | null {
  try {
    return localStorage.getItem(BACKUP_FOLDER_STORAGE_KEY);
  } catch {
    return null;
  }
}

export function setStoredBackupFolder(path: string | null) {
  try {
    if (!path) {
      localStorage.removeItem(BACKUP_FOLDER_STORAGE_KEY);
    } else {
      localStorage.setItem(BACKUP_FOLDER_STORAGE_KEY, path);
    }
  } catch {
    /* ignore */
  }
}

function isAbsolutePath(value: string): boolean {
  return /^[a-zA-Z]:[\\/]/.test(value) || value.startsWith('\\\\') || value.startsWith('/');
}

function openHandleDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(HANDLE_DB_NAME, 1);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(HANDLE_STORE)) {
        db.createObjectStore(HANDLE_STORE);
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('Failed to open backup handle DB.'));
  });
}

async function saveDirectoryHandle(handle: FileSystemDirectoryHandle): Promise<void> {
  try {
    const db = await openHandleDb();
    await new Promise<void>((resolve, reject) => {
      const tx = db.transaction(HANDLE_STORE, 'readwrite');
      tx.objectStore(HANDLE_STORE).put(handle, HANDLE_KEY);
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error ?? new Error('Failed to store folder handle.'));
    });
    db.close();
  } catch {
    /* ignore persistence failures */
  }
}

async function loadDirectoryHandle(): Promise<FileSystemDirectoryHandle | null> {
  try {
    const db = await openHandleDb();
    const handle = await new Promise<FileSystemDirectoryHandle | null>((resolve, reject) => {
      const tx = db.transaction(HANDLE_STORE, 'readonly');
      const req = tx.objectStore(HANDLE_STORE).get(HANDLE_KEY);
      req.onsuccess = () => resolve((req.result as FileSystemDirectoryHandle | undefined) ?? null);
      req.onerror = () => reject(req.error ?? new Error('Failed to read folder handle.'));
    });
    db.close();
    if (!handle) {
      return null;
    }

    const withPermission = handle as FileSystemDirectoryHandle & {
      queryPermission?: (descriptor?: { mode?: 'read' | 'readwrite' }) => Promise<PermissionState>;
      requestPermission?: (descriptor?: { mode?: 'read' | 'readwrite' }) => Promise<PermissionState>;
    };

    if (typeof withPermission.queryPermission === 'function') {
      let state = await withPermission.queryPermission({ mode: 'readwrite' });
      if (state === 'prompt' && typeof withPermission.requestPermission === 'function') {
        // May fail without a user gesture; ignore and treat as unavailable.
        try {
          state = await withPermission.requestPermission({ mode: 'readwrite' });
        } catch {
          return null;
        }
      }
      if (state !== 'granted') {
        return null;
      }
    }

    return handle;
  } catch {
    return null;
  }
}

/**
 * Resolves the last backup folder (or Documents\\MCP Track Tokens on desktop)
 * without opening a picker, for initial list load.
 */
export async function resolveLastBackupFolder(options?: {
  serverDefaultPath?: string;
}): Promise<BackupFolderRef | null> {
  const last = getStoredBackupFolder();

  if (isDesktopHost()) {
    if (last && isAbsolutePath(last)) {
      return { path: last };
    }

    const preferred = last && isAbsolutePath(last) ? last : options?.serverDefaultPath ?? null;
    try {
      const result = await hostRequest<HostResponse>(
        {
          type: 'resolveDefaultBackupFolder',
          defaultPath: preferred,
        },
        'resolveDefaultBackupFolderResult',
      );
      if (result.path) {
        setStoredBackupFolder(result.path);
        return { path: result.path };
      }
    } catch {
      if (last && isAbsolutePath(last)) {
        return { path: last };
      }
    }
    return null;
  }

  const handle = await loadDirectoryHandle();
  if (handle) {
    return { handle };
  }

  return null;
}

function hostRequest<T extends HostResponse>(
  message: Record<string, unknown>,
  expectedType: string,
): Promise<T> {
  const webview = window.chrome?.webview;
  if (!webview) {
    return Promise.reject(new Error('Desktop host bridge is not available.'));
  }

  const requestId = crypto.randomUUID();
  return new Promise<T>((resolve, reject) => {
    const timer = window.setTimeout(() => {
      webview.removeEventListener('message', onMessage);
      reject(new Error('Desktop host request timed out.'));
    }, 120_000);

    const onMessage = (event: MessageEvent) => {
      const data = event.data as HostResponse;
      if (!data || data.requestId !== requestId) {
        return;
      }
      if (data.type !== expectedType) {
        return;
      }
      window.clearTimeout(timer);
      webview.removeEventListener('message', onMessage);
      resolve(data as T);
    };

    webview.addEventListener('message', onMessage);
    webview.postMessage({ ...message, requestId });
  });
}

async function ensureMcpTrackTokensSubfolder(
  handle: FileSystemDirectoryHandle,
): Promise<FileSystemDirectoryHandle> {
  if (handle.name === 'MCP Track Tokens') {
    return handle;
  }
  return handle.getDirectoryHandle('MCP Track Tokens', { create: true });
}

/**
 * Opens a folder selector. Defaults to Documents\\MCP Track Tokens when possible,
 * otherwise the last folder used by Backup now.
 */
export async function pickBackupFolder(options?: {
  defaultPath?: string;
  preferLast?: boolean;
}): Promise<{ path?: string; handle?: FileSystemDirectoryHandle }> {
  const last = getStoredBackupFolder();
  const preferred =
    options?.preferLast && last ? last : last || options?.defaultPath || undefined;

  if (isDesktopHost()) {
    const result = await hostRequest<HostResponse>(
      {
        type: 'pickFolder',
        defaultPath: preferred ?? options?.defaultPath ?? null,
      },
      'pickFolderResult',
    );
    if (result.cancelled || !result.path) {
      throw new Error('Folder selection cancelled.');
    }
    setStoredBackupFolder(result.path);
    return { path: result.path };
  }

  if (typeof window.showDirectoryPicker === 'function') {
    const picked = await window.showDirectoryPicker({
      id: 'mcp-track-tokens-backup',
      mode: 'readwrite',
      startIn: 'documents',
    });
    const handle = await ensureMcpTrackTokensSubfolder(picked);
    // Browser cannot expose absolute paths; keep a friendly label.
    setStoredBackupFolder(handle.name === 'MCP Track Tokens' ? 'MCP Track Tokens' : handle.name);
    await saveDirectoryHandle(handle);
    return { handle };
  }

  throw new Error(
    'Folder picker is not available in this browser. Use the desktop app, or Edge/Chrome.',
  );
}

export async function saveBackupToFolder(
  folder: { path?: string; handle?: FileSystemDirectoryHandle },
  fileName: string,
  bytes: ArrayBuffer,
): Promise<string> {
  if (folder.path && isDesktopHost()) {
    const base64 = arrayBufferToBase64(bytes);
    const result = await hostRequest<HostResponse>(
      {
        type: 'saveFile',
        directory: folder.path,
        fileName,
        base64,
      },
      'saveFileResult',
    );
    if (result.error || !result.path) {
      throw new Error(result.error || 'Failed to save backup file.');
    }
    setStoredBackupFolder(folder.path);
    return result.path;
  }

  if (folder.handle) {
    const fileHandle = await folder.handle.getFileHandle(fileName, { create: true });
    const writable = await fileHandle.createWritable();
    await writable.write(bytes);
    await writable.close();
    return fileName;
  }

  throw new Error('No writable folder was selected.');
}

export async function listLocalBackupFiles(folder: {
  path?: string;
  handle?: FileSystemDirectoryHandle;
}): Promise<LocalBackupFile[]> {
  if (folder.path && isDesktopHost()) {
    const result = await hostRequest<HostResponse>(
      { type: 'listBackupFiles', directory: folder.path },
      'listBackupFilesResult',
    );
    return (result.files ?? []).map((f) => ({
      fileName: f.fileName,
      fullPath: f.fullPath,
      sizeBytes: f.sizeBytes,
      createdAtUtc:
        typeof f.createdAtUtc === 'string'
          ? f.createdAtUtc
          : new Date(f.createdAtUtc).toISOString(),
    }));
  }

  if (folder.handle) {
    const files: LocalBackupFile[] = [];
    for await (const [name, entry] of folder.handle.entries()) {
      if (entry.kind !== 'file' || !name.startsWith('mcp-track-tokens-backup-') || !name.endsWith('.db')) {
        continue;
      }
      const file = await entry.getFile();
      files.push({
        fileName: name,
        sizeBytes: file.size,
        createdAtUtc: new Date(file.lastModified).toISOString(),
        handle: entry,
      });
    }
    files.sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc));
    return files;
  }

  return [];
}

export async function readLocalBackupFile(file: LocalBackupFile): Promise<File> {
  if (file.handle) {
    return file.handle.getFile();
  }

  if (file.fullPath && isDesktopHost()) {
    const result = await hostRequest<HostResponse>(
      { type: 'readFile', path: file.fullPath },
      'readFileResult',
    );
    if (result.error || !result.base64 || !result.fileName) {
      throw new Error(result.error || 'Failed to read backup file.');
    }
    const bytes = Uint8Array.from(atob(result.base64), (c) => c.charCodeAt(0));
    return new File([bytes], result.fileName, { type: 'application/x-sqlite3' });
  }

  throw new Error('Cannot read the selected backup file.');
}

export async function deleteLocalBackupFile(
  folder: { path?: string; handle?: FileSystemDirectoryHandle },
  file: LocalBackupFile,
): Promise<void> {
  if (
    !file.fileName.startsWith('mcp-track-tokens-backup-') ||
    !file.fileName.endsWith('.db')
  ) {
    throw new Error('Only mcp-track-tokens-backup-*.db files can be deleted.');
  }

  if (file.fullPath && isDesktopHost()) {
    const result = await hostRequest<HostResponse>(
      { type: 'deleteFile', path: file.fullPath },
      'deleteFileResult',
    );
    if (result.error) {
      throw new Error(result.error);
    }
    return;
  }

  if (folder.handle) {
    await folder.handle.removeEntry(file.fileName);
    return;
  }

  throw new Error('Cannot delete the selected backup file.');
}

function arrayBufferToBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
  }
  return btoa(binary);
}
