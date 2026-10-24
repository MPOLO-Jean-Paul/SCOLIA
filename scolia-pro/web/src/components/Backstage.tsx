import { useEffect, useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ArrowLeft, Database, DatabaseBackup, FileSpreadsheet, Info, Keyboard, KeyRound, LogOut, Save, Settings, ShieldCheck } from 'lucide-react';
import { api } from '../api/client';
import type { BackupInfo } from '../api/types';
import { dateTime } from '../lib/format';
import { P, useSession } from '../lib/session';

interface BackstageProps {
  onClose: () => void;
  onNavigate: (to: string) => void;
  onRun: (id: string) => void;
}

/** Vue « Fichier » : informations sur l'établissement, sauvegardes, contrôles et compte, comme le menu Fichier des suites bureautiques. */
export function Backstage({ onClose, onNavigate, onRun }: BackstageProps) {
  const { session, can } = useSession();
  const [section, setSection] = useState('info');
  const backups = useQuery({
    queryKey: ['sauvegardes'],
    queryFn: () => api.get<BackupInfo[]>('/api/sauvegardes'),
    enabled: can(P.backupManage),
    staleTime: 60_000,
  });

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const last = backups.data?.[0];
  const menu: { id: string; label: string; icon: ReactNode; go?: string; run?: string; visible: boolean; danger?: boolean }[] = [
    { id: 'info', label: 'Informations', icon: <Info />, visible: true },
    { id: 'parametres', label: 'Paramètres de l’établissement', icon: <Settings />, go: '/parametres', visible: can(P.settingsWrite) },
    { id: 'donnees', label: 'Données et sauvegardes', icon: <DatabaseBackup />, go: '/donnees', visible: can(P.backupManage) },
    { id: 'audit', label: 'Journal d’audit', icon: <FileSpreadsheet />, go: '/audit', visible: can(P.auditRead) },
    { id: 'profil', label: 'Mon compte et mot de passe', icon: <KeyRound />, go: '/profil', visible: true },
    { id: 'aide', label: 'Raccourcis clavier', icon: <Keyboard />, run: 'raccourcis', visible: true },
    { id: 'quitter', label: 'Se déconnecter', icon: <LogOut />, run: 'deconnexion', visible: true, danger: true },
  ];

  return (
    <div className="backstage" role="dialog" aria-modal="true" aria-label="Fichier">
      <aside className="bs-side">
        <button type="button" className="bs-back" onClick={onClose} aria-label="Retour à SCOLIA">
          <ArrowLeft />
        </button>
        <nav>
          {menu.filter((m) => m.visible).map((m) => (
            <button
              key={m.id}
              type="button"
              className={`bs-item ${section === m.id ? 'on' : ''} ${m.danger ? 'danger' : ''}`}
              aria-current={section === m.id ? 'true' : undefined}
              onClick={() => {
                if (m.go) onNavigate(m.go);
                else if (m.run) onRun(m.run);
                else setSection(m.id);
              }}
            >
              {m.icon}<span>{m.label}</span>
            </button>
          ))}
        </nav>
      </aside>
      <main className="bs-main">
        {section === 'info' && (
          <>
            <h1>{session?.schoolName ?? 'SCOLIA Pro'}</h1>
            <p className="bs-sub">{session?.roleLabel} · {session?.displayName}{session?.currentYear && ` · année ${session.currentYear}`}</p>

            <h2>Protection des données</h2>
            <div className="bs-cards">
              <div className="bs-card">
                <span className="bs-card-label">Dernière sauvegarde</span>
                <strong>{can(P.backupManage) ? (last ? dateTime(last.createdUtc) : 'Aucune sauvegarde') : 'Réservé à l’administration'}</strong>
                <span className="bs-card-hint">Sauvegarde quotidienne automatique, vérifiée avant conservation.</span>
              </div>
              <div className="bs-card">
                <span className="bs-card-label">Base de données</span>
                <strong>SQLite, mode WAL</strong>
                <span className="bs-card-hint">Un fichier unique dans le dossier donnees, transactionnel.</span>
              </div>
            </div>

            <h2>Actions</h2>
            <div className="bs-actions">
              {can(P.backupManage) && <button type="button" className="btn primary" onClick={() => onRun('sauvegarder')}><Save />Sauvegarder maintenant</button>}
              {can(P.backupManage) && <button type="button" className="btn" onClick={() => onRun('diag-base')}><Database />Vérifier la base</button>}
              {can(P.auditRead) && <button type="button" className="btn" onClick={() => onRun('diag-audit')}><ShieldCheck />Vérifier le journal d’audit</button>}
            </div>
          </>
        )}
      </main>
    </div>
  );
}
