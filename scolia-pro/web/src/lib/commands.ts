import { useEffect, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';

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

/**
 * Ouvre une action demandée par le ruban (paramètre d'URL « action »), une seule fois.
 * Le paramètre est retiré de l'adresse pour qu'un rafraîchissement ne rouvre pas la boîte.
 */
export function useActionParam(action: string, run: () => void) {
  const [params, setParams] = useSearchParams();
  const requested = params.get('action') === action;
  useEffect(() => {
    if (!requested) return;
    run();
    setParams((current) => {
      const next = new URLSearchParams(current);
      next.delete('action');
      return next;
    }, { replace: true });
    // Une seule exécution par demande : run est volontairement exclu des dépendances.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requested]);
}
