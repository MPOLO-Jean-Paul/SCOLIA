import { useEffect, useRef } from 'react';

const EVENT = 'scolia:commande';

/** Envoie une commande aux pages : seule la page concernée la traite (ex. imprimer le reçu affiché). */
export function runCommand(id: string) {
  window.dispatchEvent(new CustomEvent<string>(EVENT, { detail: id }));
}

/** Enregistre le traitement d'une commande pour la page courante. */
export function useCommand(id: string, handler: () => void) {
  const latest = useRef(handler);
  useEffect(() => {
    latest.current = handler;
  });
  useEffect(() => {
    const listener = (event: Event) => {
      if ((event as CustomEvent<string>).detail === id) latest.current();
    };
    window.addEventListener(EVENT, listener);
    return () => window.removeEventListener(EVENT, listener);
  }, [id]);
}
