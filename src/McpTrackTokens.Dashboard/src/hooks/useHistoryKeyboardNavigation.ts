import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';

/**
 * Alt+← / Alt+→, BrowserBack/Forward, and Backspace (outside inputs) navigate React Router history.
 * Needed in the desktop WebView where browser accelerator keys are disabled.
 */
export function useHistoryKeyboardNavigation() {
  const navigate = useNavigate();

  useEffect(() => {
    const isEditableTarget = (target: EventTarget | null): boolean => {
      if (!(target instanceof HTMLElement)) {
        return false;
      }
      if (target.isContentEditable) {
        return true;
      }
      const tag = target.tagName;
      return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
    };

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.ctrlKey || event.metaKey) {
        return;
      }

      if (event.key === 'BrowserBack') {
        event.preventDefault();
        navigate(-1);
        return;
      }

      if (event.key === 'BrowserForward') {
        event.preventDefault();
        navigate(1);
        return;
      }

      // Common “back” key outside text fields (desktop WebView / some keyboards).
      if (event.key === 'Backspace' && !event.altKey && !isEditableTarget(event.target)) {
        event.preventDefault();
        navigate(-1);
        return;
      }

      if (!event.altKey) {
        return;
      }

      if (event.key === 'ArrowLeft') {
        event.preventDefault();
        navigate(-1);
      } else if (event.key === 'ArrowRight') {
        event.preventDefault();
        navigate(1);
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [navigate]);
}
