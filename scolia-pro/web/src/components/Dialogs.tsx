import { Modal, Segmented } from './ui';
import type { Theme } from '../lib/session';

type Density = 'confortable' | 'compact';

export function ShortcutsDialog({ onClose }: { onClose: () => void }) {
  const rows: [string, string][] = [
    ['Ctrl + K', 'Rechercher un apprenant, un reçu, une page ou une commande'],
    ['F2', 'Nouvel encaissement'],
    ['Alt + 1 à 4', 'Tableau de bord, apprenants, encaissement, recouvrement'],
    ['Ctrl + F1', 'Réduire ou développer le ruban'],
    ['F5', 'Actualiser les données affichées'],
    ['Alt, puis une lettre', 'Afficher les touches d’accès : la lettre ouvre un onglet, puis une commande'],
    ['Flèches gauche et droite', 'Passer d’un onglet à l’autre'],
    ['Échap', 'Fermer le ruban, les touches d’accès ou la fenêtre ouverte'],
  ];
  return (
    <Modal title="Raccourcis clavier" eyebrow="Aide" onClose={onClose} footer={<button type="button" className="btn primary" onClick={onClose}>Fermer</button>}>
      <table className="data">
        <tbody>
          {rows.map(([keys, text]) => (
            <tr key={keys}><td className="nowrap"><kbd>{keys}</kbd></td><td>{text}</td></tr>
          ))}
        </tbody>
      </table>
    </Modal>
  );
}

export function AboutDialog({ schoolName, onClose }: { schoolName: string; onClose: () => void }) {
  return (
    <Modal title="SCOLIA Pro" eyebrow="Version 1.0.0" onClose={onClose} footer={<button type="button" className="btn primary" onClick={onClose}>Fermer</button>}>
      <p><strong>{schoolName}</strong></p>
      <p className="muted">Gestion scolaire : inscriptions, caisse, reçus numérotés, scolarité, personnel et paie.</p>
      <p className="muted">SCOLIA n’envoie aucune donnée à un tiers. Les liens WhatsApp ne s’ouvrent qu’à votre demande. Le journal d’audit est chaîné et vérifiable.</p>
    </Modal>
  );
}

export function PreferencesDialog({ theme, onTheme, density, onDensity, statusBar, onStatusBar, onClose }: {
  theme: Theme; onTheme: (t: Theme) => void; density: Density; onDensity: (d: Density) => void;
  statusBar: boolean; onStatusBar: (v: boolean) => void; onClose: () => void;
}) {
  return (
    <Modal title="Préférences d’affichage" eyebrow="Affichage" onClose={onClose} footer={<button type="button" className="btn primary" onClick={onClose}>Terminé</button>}>
      <div className="stack">
        <div className="field">
          <span className="label">Thème</span>
          <Segmented value={theme} onChange={onTheme} items={[{ value: 'clair', label: 'Clair' }, { value: 'sombre', label: 'Sombre' }, { value: 'systeme', label: 'Système' }]} />
        </div>
        <div className="field">
          <span className="label">Densité</span>
          <Segmented value={density} onChange={onDensity} items={[{ value: 'confortable', label: 'Confortable' }, { value: 'compact', label: 'Compacte' }]} />
        </div>
        <label className="check"><input type="checkbox" checked={statusBar} onChange={(e) => onStatusBar(e.target.checked)} />Afficher la barre d’état</label>
      </div>
    </Modal>
  );
}
