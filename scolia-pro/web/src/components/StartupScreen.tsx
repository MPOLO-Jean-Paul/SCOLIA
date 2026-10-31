import { ScoliaMark } from './ScoliaMark';

/** Même repère visuel pendant le chargement et à l'ouverture de la session. */
export function StartupScreen({ welcome }: { welcome?: string }) {
  return (
    <main className="signin-screen startup-screen" role="status" aria-live="polite" aria-busy={!welcome}>
      <div className="startup-content">
        <div className="startup-mark"><ScoliaMark size={64} /></div>
        <h1 className="signin-title">{welcome ? 'Bienvenue' : 'SCOLIA'}</h1>
        <p className="signin-sub">{welcome || 'Préparation de votre espace…'}</p>
        <div className="loading-dots" aria-hidden="true"><span /><span /><span /></div>
      </div>
    </main>
  );
}
