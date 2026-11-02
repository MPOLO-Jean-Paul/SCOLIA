import { useQuery } from '@tanstack/react-query';
import { DatabaseBackup, ZoomIn, ZoomOut } from 'lucide-react';
import { api } from '../api/client';
import type { BackupInfo } from '../api/types';
import { dateTime } from '../lib/format';
import { P, useSession } from '../lib/session';

export const ZOOM_MIN = 80;
export const ZOOM_MAX = 140;
export const ZOOM_STEP = 10;

export function StatusBar({ pageTitle, zoom, onZoom }: { pageTitle: string; zoom: number; onZoom: (value: number) => void }) {
  const { session, can } = useSession();
  const backups = useQuery({
    queryKey: ['sauvegardes'],
    queryFn: () => api.get<BackupInfo[]>('/api/sauvegardes'),
    enabled: can(P.backupManage),
    staleTime: 60_000,
  });
  const last = backups.data?.[0];
  return (
    <footer className="statusbar" aria-label="Barre d’état">
      <span className="sb-item">Prêt</span>
      <span className="sb-item sb-page">{pageTitle}</span>
      <span className="sb-spacer" />
      {session?.currentYear && <span className="sb-item">Année {session.currentYear}</span>}
      {can(P.backupManage) && (
        <span className="sb-item" title="Dernière sauvegarde de la base">
          <DatabaseBackup />{last ? `Sauvegarde ${dateTime(last.createdUtc)}` : 'Aucune sauvegarde'}
        </span>
      )}
      <div className="sb-zoom">
        <button type="button" onClick={() => onZoom(zoom - ZOOM_STEP)} disabled={zoom <= ZOOM_MIN} aria-label="Zoom arrière"><ZoomOut /></button>
        <input type="range" min={ZOOM_MIN} max={ZOOM_MAX} step={ZOOM_STEP} value={zoom} onChange={(e) => onZoom(Number(e.target.value))} aria-label="Niveau de zoom" />
        <button type="button" onClick={() => onZoom(zoom + ZOOM_STEP)} disabled={zoom >= ZOOM_MAX} aria-label="Zoom avant"><ZoomIn /></button>
        <button type="button" className="sb-zoom-value" onClick={() => onZoom(100)} title="Zoom 100 %">{zoom} %</button>
      </div>
    </footer>
  );
}
