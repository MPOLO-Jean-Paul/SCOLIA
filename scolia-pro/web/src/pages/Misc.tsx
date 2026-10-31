import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { BadgeCheck, GraduationCap, KeyRound, ShieldX } from 'lucide-react';
import { api } from '../api/client';
import type { AcademicYear, Promotion, ReEnrollmentItem, Session } from '../api/types';
import { longDate, money } from '../lib/format';
import { Button, Card, Empty, ErrorAlert, Loading, PageHead, SelectField, TextField, useToast } from '../components/ui';
import { useSession } from '../lib/session';
import { ScoliaMark } from '../components/ScoliaMark';

export function ProfilePage() {
  const { session, setSession } = useSession();
  const [f, setF] = useState({ currentPassword: '', newPassword: '', confirm: '' });
  const [error, setError] = useState<unknown>();
  const toast = useToast();
  const navigate = useNavigate();
  return (
    <>
      <PageHead title="Mon mot de passe" subtitle={session?.mustChangePassword ? 'Votre mot de passe est provisoire : choisissez-en un nouveau pour continuer.' : `${session?.displayName} · ${session?.roleLabel}`} />
      <div style={{ maxWidth: 520 }}>
        <Card title="Changer le mot de passe">
          <form className="stack" onSubmit={async (e) => {
            e.preventDefault();
            if (f.newPassword !== f.confirm) { setError(new Error('Les deux nouveaux mots de passe ne correspondent pas.')); return; }
            try { const s = await api.post<Session>('/api/session/mot-de-passe', { currentPassword: f.currentPassword, newPassword: f.newPassword }); setSession(s); toast('Mot de passe modifié.', 'success'); navigate('/'); } catch (err) { setError(err); }
          }}>
            <TextField label="Mot de passe actuel" type="password" autoComplete="current-password" value={f.currentPassword} onChange={(e) => setF({ ...f, currentPassword: e.target.value })} />
            <TextField label="Nouveau mot de passe" type="password" autoComplete="new-password" value={f.newPassword} onChange={(e) => setF({ ...f, newPassword: e.target.value })} help="8 caractères minimum, avec des lettres et des chiffres" />
            <TextField label="Confirmer" type="password" autoComplete="new-password" value={f.confirm} onChange={(e) => setF({ ...f, confirm: e.target.value })} />
            <ErrorAlert error={error} />
            <Button type="submit" variant="primary" icon={<KeyRound />}>Enregistrer</Button>
          </form>
        </Card>
      </div>
    </>
  );
}

interface Verification { found: boolean; number?: string; date?: string; amount?: number; student?: string; school?: string; status?: string; currency?: string }

/** Page publique ouverte en scannant le QR code d'un reçu. */
export function VerifyPage() {
  const { code = '' } = useParams();
  const [result, setResult] = useState<Verification | null>(null);
  const [error, setError] = useState<unknown>();
  useEffect(() => {
    document.title = 'Vérification d’un reçu · SCOLIA';
    api.get<Verification>(`/api/verifier/${encodeURIComponent(code)}`).then(setResult).catch(setError);
  }, [code]);
  const valid = result?.found && result.status === 'Valide';
  return (
    <div className="verify-page">
      <div className="auth-card" style={{ textAlign: 'center' }}>
        <ScoliaMark size={56} />
        <h2 style={{ fontSize: 22 }}>Vérification d’un reçu</h2>
        {error ? <ErrorAlert error={error} /> : !result ? <Loading rows={3} /> : !result.found ? (
          <><div className="status-pill ko"><ShieldX />Reçu introuvable</div><p className="muted">Ce code ne correspond à aucun reçu émis par l’établissement. Le document pourrait être falsifié.</p></>
        ) : (
          <>
            <div className={`status-pill ${valid ? 'ok' : 'ko'}`}>{valid ? <BadgeCheck /> : <ShieldX />}{valid ? 'Reçu authentique et valide' : 'Reçu annulé'}</div>
            <dl className="dl" style={{ textAlign: 'left', marginTop: 18 }}>
              <dt>Établissement</dt><dd>{result.school}</dd><dt>N° de reçu</dt><dd className="tag-code">{result.number}</dd>
              <dt>Date</dt><dd>{result.date && longDate(result.date)}</dd><dt>Montant</dt><dd>{result.amount?.toLocaleString('fr-FR')} {result.currency}</dd><dt>Apprenant</dt><dd>{result.student}</dd>
            </dl>
          </>
        )}
        <p className="small muted" style={{ marginTop: 18 }}>Code vérifié : <span className="tag-code">{code}</span></p>
      </div>
    </div>
  );
}

export function ReEnrollmentPage() {
  const years = useQuery({ queryKey: ['years'], queryFn: () => api.get<AcademicYear[]>('/api/etablissement/annees') });
  const [yearId, setYearId] = useState('');
  const targets = years.data?.filter((y) => y.status !== 'Closed') ?? [];
  useEffect(() => { if (!yearId && targets.length) setYearId(targets[0].id); }, [targets, yearId]);
  const plan = useQuery({ queryKey: ['reenroll', yearId], queryFn: () => api.get<ReEnrollmentItem[]>('/api/reinscriptions/plan', { anneeId: yearId }), enabled: !!yearId, retry: false });
  const promotions = useQuery({ queryKey: ['promotions', yearId], queryFn: () => api.get<Promotion[]>('/api/etablissement/promotions', { anneeId: yearId }), enabled: !!yearId });
  const [choices, setChoices] = useState<Record<string, string>>({});
  const qc = useQueryClient();
  const toast = useToast();
  useEffect(() => {
    if (!plan.data || !promotions.data) return;
    setChoices(Object.fromEntries(plan.data.filter((p) => p.toPromotion).map((p) => [p.studentId, promotions.data!.find((x) => x.code === p.toPromotion)?.id ?? ''])));
  }, [plan.data, promotions.data]);
  const selected = Object.entries(choices).filter(([, v]) => v);
  return (
    <>
      <PageHead title="Réinscriptions" subtitle="Inscrire en lot les apprenants actifs de l’année précédente dans leur promotion suivante. Les soldes antérieurs sont affichés pour décision."
        actions={<Button variant="primary" icon={<GraduationCap />} disabled={!selected.length} onClick={async () => {
          try { const n = await api.post<number>('/api/reinscriptions', { targetYearId: yearId, choices: selected.map(([studentId, promotionId]) => ({ studentId, promotionId })) }); toast(`${n} réinscription(s) effectuée(s).`, 'success'); void qc.invalidateQueries(); }
          catch (e) { toast(e instanceof Error ? e.message : 'Erreur', 'error'); }
        }}>Réinscrire {selected.length} apprenant(s)</Button>} />
      <div className="toolbar">
        <SelectField label="Année cible" value={yearId} onChange={(e) => setYearId(e.target.value)}>{targets.map((y) => <option key={y.id} value={y.id}>{y.label}{y.isCurrent ? ' (courante)' : ''}</option>)}</SelectField>
      </div>
      {plan.error ? <ErrorAlert error={plan.error} /> : !plan.data ? <Loading /> : plan.data.length === 0 ? <Card><Empty text="Aucun apprenant à réinscrire pour cette année." /></Card> : (
        <div className="table-wrap">
          <table className="data">
            <thead><tr><th>Matricule</th><th>Apprenant</th><th>Promotion actuelle</th><th className="num">Solde antérieur</th><th>Promotion cible</th></tr></thead>
            <tbody>{plan.data.map((p) => (
              <tr key={p.studentId}><td className="tag-code">{p.matricule}</td><td>{p.fullName}</td><td>{p.fromPromotion}</td>
                <td className="num" style={{ color: p.previousBalance > 0 ? 'var(--orange)' : 'var(--green)' }}>{money(p.previousBalance)}</td>
                <td><select className="select" value={choices[p.studentId] ?? ''} onChange={(e) => setChoices({ ...choices, [p.studentId]: e.target.value })}>
                  <option value="">Ne pas réinscrire</option>{promotions.data?.map((x) => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}
                </select></td></tr>))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
