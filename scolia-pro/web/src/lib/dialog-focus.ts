import { useEffect, useRef, useState, type RefObject } from 'react';

const FOCUSABLE = 'button:not(:disabled),a[href],input:not(:disabled):not([type="hidden"]),select:not(:disabled),textarea:not(:disabled),[tabindex]:not([tabindex="-1"])';

/** Garde le clavier dans la fenêtre ouverte, sans déplacer le focus à chaque mise à jour du formulaire. */
export function useDialogFocus(ref: RefObject<HTMLElement | null>, onClose: () => void) {
  const close = useRef(onClose);
  close.current = onClose;
  // Capturé avant le commit : un champ autoFocus peut déjà être actif quand l'effet s'exécute.
  const [previous] = useState(() => document.activeElement as HTMLElement | null);

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    const controls = () => Array.from(dialog.querySelectorAll<HTMLElement>(FOCUSABLE))
      .filter((el) => el.tabIndex >= 0 && !el.matches(':disabled') && el.getClientRects().length > 0 && !el.closest('[inert]'));
    const initial = controls();
    (initial.find((el) => el.matches('input:not([readonly]),select,textarea,button:not(.close)')) ?? initial[0] ?? dialog)
      .focus({ preventScroll: true });

    const onKey = (event: KeyboardEvent) => {
      // Une fenêtre secondaire prend la main sur celle qui est restée ouverte dessous.
      const dialogs = document.querySelectorAll('[aria-modal="true"]');
      if (dialogs[dialogs.length - 1] !== dialog) return;
      if (event.key === 'Escape' && !event.isComposing) {
        event.preventDefault();
        event.stopPropagation();
        close.current();
        return;
      }
      if (event.key !== 'Tab') return;
      const items = controls();
      const active = document.activeElement;
      const first = items[0];
      const last = items[items.length - 1];
      if (!first) {
        event.preventDefault();
        dialog.focus({ preventScroll: true });
      } else if (!items.includes(active as HTMLElement) || (!event.shiftKey && active === last)) {
        event.preventDefault();
        (event.shiftKey ? last : first).focus();
      } else if (event.shiftKey && active === first) {
        event.preventDefault();
        last.focus();
      }
    };
    document.addEventListener('keydown', onKey, true);
    return () => {
      document.removeEventListener('keydown', onKey, true);
      if (previous?.isConnected) previous.focus({ preventScroll: true });
    };
  }, [ref, previous]);
}
