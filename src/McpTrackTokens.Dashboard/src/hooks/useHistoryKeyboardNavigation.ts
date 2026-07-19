import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';

/**
 * Alt+← / Alt+→ (and browser Back/Forward keys) navigate React Router history.
 * Needed in the desktop WebView where browser accelerator keys are disabled.
 */
export function useHistoryKeyboardNavigation() {
  const navigate = useNavigate();

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented) {
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
