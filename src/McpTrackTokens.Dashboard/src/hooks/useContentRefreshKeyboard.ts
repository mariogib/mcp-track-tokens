import { useEffect } from 'react';

function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  if (target.isContentEditable) {
    return true;
  }
  const tag = target.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
}

/**
 * F5 / Ctrl+R trigger the shared AdminShell Refresh control (remount + query invalidation).
 * Needed in the desktop WebView where browser accelerator keys are disabled, and in the
 * browser to avoid a full document reload.
 */
export function useContentRefreshKeyboard() {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.altKey || event.metaKey) {
        return;
      }

      const isF5 = event.key === 'F5';
      const isCtrlR = event.key.toLowerCase() === 'r' && event.ctrlKey;
      if (!isF5 && !isCtrlR) {
        return;
      }

      if (isEditableTarget(event.target)) {
        return;
      }

      const refreshButton = document.querySelector<HTMLButtonElement>('.content-refresh-btn');
      if (!refreshButton) {
        return;
      }

      event.preventDefault();
      refreshButton.click();
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);
}
