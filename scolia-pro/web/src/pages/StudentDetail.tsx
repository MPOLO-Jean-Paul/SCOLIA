import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ArrowLeft, Camera, FileText, HandCoins, MessageCircle, Pencil, Plus, Repeat, Save, ShieldCheck, UserX } from 'lucide-react';
import { api, openDocument } from '../api/client';
import type { Account, Promotion, Sponsorship, Student } from '../api/types';
import { date, money, initials, pct } from '../lib/format';
import { Badge, Button, Card, Empty, ErrorAlert, Kpi, Loading, Modal, ReasonDialog, SelectField, Tabs, TextArea, TextField, statusTone, useToast, errorText } from '../components/ui';
import { IdentityFields, fromStudent, toPayload, type IdentityForm } from './StudentForm';
import { P, useSession } from '../lib/session';
import { useCommand } from '../lib/commands';

type Tab = 'compte' | 'dossier' | 'pec' | 'scolarite';

export function StudentDetailPage() {
  const { id = '' } = useParams();
  const [tab, setTab] = useState<Tab>('compte');
  const navigate = useNavigate();
  const toast = useToast();
  const qc = useQueryClient();
  const { can } = useSession();
  const photoInput = useRef<HTMLInputElement>(null);
  const [photoVersion, setPhotoVersion] = useState(0);
  const student = useQuery({ queryKey: ['student', id], queryFn: () => api.get<Student>(`/api/apprenants/${id}`) });
  const current = student.data?.enrollments[0];
  const account = useQuery({ queryKey: ['account', current?.id], queryFn: () => api.get<Account>(`/api/inscriptions/${current!.id}/compte`), enabled: !!current && can(P.paymentsRead) });
  const s = student.data;
  // Commandes du ruban « Apprenant ».
  useCommand('encaisser-eleve', () => { if (current) navigate(`/encaisser?inscription=${current.id}`); });
  useCommand('releve-eleve', () => { if (current) openDocument(`/api/inscriptions/${current.id}/releve.pdf`); });
  useCommand('modifier-eleve', () => setTab('dossier'));

  const uploadPhoto = async (file: File) => {
    const form = new FormData();
    form.append('fichier', file);
    try { await api.put(`/api/apprenants/${id}/photo`, form); setPhotoVersion((v) => v + 1); await qc.invalidateQueries({ queryKey: ['student', id] }); toast('Photo enregistrée.', 'success'); }
    catch (e) { toast(errorText(e), 'error'); }
  };

  if (student.error) return <ErrorAlert error={student.error} />;
  if (!s) return <Loading rows={6} />;
  const name = `${s.lastName} ${s.middleName} ${s.firstName}`;
  return (
    <>
      <button className="link-btn small" onClick={() => navigate(-1)} style={{ marginBottom: 10, display: 'inline-flex', gap: 6, alignItems: 'center' }}><ArrowLeft size={14} />Retour</button>
      <div className="card card-pad" style={{ marginBottom: 18 }}>
        <div className="row" style={{ gap: 18, alignItems: 'center' }}>
          <div className="identity">
            <button className="photo" style={{ border: 'none', cursor: can(P.studentsWrite) ? 'pointer' : 'default', position: 'relative' }} onClick={() => can(P.studentsWrite) && photoInput.current?.click()} title="Changer la photo">
              {s.hasPhoto ? <img src={`/api/apprenants/${id}/photo?v=${photoVersion}`} alt="" /> : initials(`${s.lastName} ${s.firstName}`)}
              {can(P.studentsWrite) && <Camera size={14} style={{ position: 'absolute', right: 4, bottom: 4, color: 'var(--muted)' }} />}
            </button>
            <input ref={photoInput} type="file" accept="image/jpeg,image/png,image/webp" hidden onChange={(e) => e.target.files?.[0] && uploadPhoto(e.target.files[0])} />
            <div>
              <h1 style={{ fontSize: 24 }}>{name}</h1>
              <div className="row muted" style={{ marginTop: 4 }}>
                <span className="tag-code">{s.matricule}</span>
                {current && <><span>·</span><span>{current.promotionCode} — {current.promotionName}</span><span>·</span><span>{current.academicYear}</span><Badge tone={statusTone(current.status)}>{current.status}</Badge></>}
              </div>
            </div>
          </div>
          <div className="row" style={{ marginLeft: 'auto' }}>
            {account.data?.whatsAppLink && <a className="btn" href={account.data.whatsAppLink} target="_blank" rel="noreferrer"><MessageCircle />Rappel WhatsApp</a>}
            {current && can(P.paymentsRead) && <Button icon={<FileText />} onClick={() => openDocument(`/api/inscriptions/${current.id}/releve.pdf`)}>Relevé de compte</Button>}
            {current && can(P.paymentsCreate) && <Button variant="primary" icon={<HandCoins />} onClick={() => navigate(`/encaisser?inscription=${current.id}`)}>Encaisser</Button>}
          </div>
        </div>
      </div>
      <Tabs value={tab} onChange={setTab} items={[
        { value: 'compte', label: 'Situation financière' }, { value: 'dossier', label: 'Dossier' },
        { value: 'pec', label: 'Prises en charge' }, { value: 'scolarite', label: 'Inscriptions' },
      ]} />
      {tab === 'compte' && (current ? <AccountTab account={account.data} error={account.error} /> : <Empty text="Aucune inscription." />)}
      {tab === 'dossier' && <DossierTab student={s} />}
      {tab === 'pec' && current && <SponsorshipTab enrollmentId={current.id} account={account.data} />}
      {tab === 'scolarite' && <EnrollmentTab student={s} />}
    </>
  );
}

function AccountTab({ account, error }: { account?: Account; error: unknown }) {
  const navigate = useNavigate();
  if (error) return <ErrorAlert error={error} />;
  if (!account) return <Loading />;
  const rate = account.totalNet ? Math.round((account.totalPaid * 1000) / account.totalNet) / 10 : 100;
  return (
    <div className="stack">
      <div className="grid cols-4">
        <Kpi label="Total net de l'année" value={money(account.totalNet)} hint={account.totalCovered ? `Pris en charge : ${money(account.totalCovered)}` : `Frais : ${money(account.totalFees)}`} />
        <Kpi label="Déjà payé" value={money(account.totalPaid)} hint={`${pct(rate)} de l'année`} tone="green" />
        <Kpi label="Reste exigible" value={money(account.overdue)} hint={account.overdueCount ? `${account.overdueCount} échéance(s) · ${account.agingBucket}` : 'Aucune échéance en retard'} tone={account.overdue > 0 ? 'red' : 'green'} />
        <Kpi label="Reste de l'année" value={money(account.balance)} tone="orange" />
      </div>
      <div className="grid" style={{ gridTemplateColumns: 'minmax(0,3fr) minmax(0,2fr)' }}>
        <div className="table-wrap">
          <table className="data">
            <thead><tr><th>Rubrique</th><th>Échéance</th><th className="num">Net</th><th className="num">Payé</th><th className="num">Reste</th><th>Statut</th></tr></thead>
            <tbody>{account.lines.map((l) => (
              <tr key={l.feeItemId}>
                <td>{l.label}{l.covered > 0 && <div className="small muted">Pris en charge : {money(l.covered)}</div>}</td>
                <td className={l.isOverdue ? '' : 'muted'} style={{ color: l.isOverdue ? 'var(--red)' : undefined }}>{date(l.dueDate)}</td>
                <td className="num">{money(l.net)}</td><td className="num">{money(l.paid)}</td>
                <td className="num" style={{ fontWeight: 700, color: l.isOverdue ? 'var(--red)' : undefined }}>{money(l.remaining)}</td>
                <td><Badge tone={l.isOverdue ? 'red' : statusTone(l.status)}>{l.isOverdue ? 'En retard' : l.status}</Badge></td>
              </tr>))}
            </tbody>
            <tfoot><tr><td colSpan={2}>Total</td><td className="num">{money(account.totalNet)}</td><td className="num">{money(account.totalPaid)}</td><td className="num">{money(account.balance)}</td><td /></tr></tfoot>
          </table>
        </div>
        <Card title="Reçus" pad={false}>
          {account.receipts.length === 0 ? <Empty text="Aucun paiement enregistré." /> : (
            <table className="data">
              <tbody>{account.receipts.map((r) => (
                <tr key={r.id} className="clickable" onClick={() => navigate(`/paiements/recus/${r.id}`)}>
                  <td><div className="tag-code">{r.number}</div><div className="small muted">{date(r.date)} · {r.method}</div><div className="small muted">{r.allocations}</div></td>
                  <td className="num"><div style={{ fontWeight: 700, textDecoration: r.status === 'Annulé' ? 'line-through' : undefined }}>{money(r.amount)}</div>{r.status === 'Annulé' && <Badge tone="red">Annulé</Badge>}</td>
                </tr>))}
              </tbody>
            </table>
          )}
        </Card>
      </div>
    </div>
  );
}

function DossierTab({ student }: { student: Student }) {
  const [form, setForm] = useState<IdentityForm>(fromStudent(student));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  const qc = useQueryClient();
  const toast = useToast();
  const { can } = useSession();
  const editable = can(P.studentsWrite);
  const save = async () => {
    setBusy(true); setError(undefined);
    try {
      await api.put(`/api/apprenants/${student.id}`, toPayload(form, student.version));
      await qc.invalidateQueries({ queryKey: ['student', student.id] });
      toast('Dossier enregistré.', 'success');
    } catch (e) { setError(e); } finally { setBusy(false); }
  };
  return (
    <Card title="Informations du dossier" actions={editable && <Button variant="primary" icon={<Save />} loading={busy} onClick={save}>Enregistrer</Button>}>
      <fieldset disabled={!editable} style={{ border: 'none', padding: 0, margin: 0 }}>
        <IdentityFields form={form} setForm={setForm} error={error} />
      </fieldset>
      <div style={{ marginTop: 14 }}><ErrorAlert error={error} /></div>
    </Card>
  );
}

function SponsorshipTab({ enrollmentId, account }: { enrollmentId: string; account?: Account }) {
  const list = useQuery({ queryKey: ['sponsorships', enrollmentId], queryFn: () => api.get<Sponsorship[]>(`/api/inscriptions/${enrollmentId}/prises-en-charge`) });
  const [editing, setEditing] = useState<Sponsorship | 'new' | null>(null);
  const [ending, setEnding] = useState<Sponsorship | null>(null);
  const qc = useQueryClient();
  const { can } = useSession();
  const refresh = () => Promise.all([qc.invalidateQueries({ queryKey: ['sponsorships', enrollmentId] }), qc.invalidateQueries({ queryKey: ['account', enrollmentId] })]);
  return (
    <Card title="Prises en charge" actions={can(P.studentsWrite) && <Button variant="primary" icon={<Plus />} onClick={() => setEditing('new')}>Nouvelle prise en charge</Button>}>
      {!list.data ? <Loading /> : list.data.length === 0 ? <Empty text="Aucune prise en charge : l’apprenant paie la totalité de ses frais." /> : (
        <div className="stack">
          {list.data.map((s) => (
            <div key={s.id} className="card card-pad" style={{ boxShadow: 'none' }}>
              <div className="row">
                <ShieldCheck color="var(--purple)" />
                <div><strong>{s.sponsorName}</strong><div className="small muted">{s.reason} · depuis le {date(s.effectiveFrom)}</div></div>
                <Badge tone={s.isActive ? 'purple' : 'gray'}>{s.isActive ? 'Active' : 'Terminée'}</Badge>
                <strong style={{ marginLeft: 'auto' }}>{money(s.coveredTotal)}</strong>
                {s.isActive && can(P.studentsWrite) && <><Button size="small" icon={<Pencil />} onClick={() => setEditing(s)}>Modifier</Button><Button size="small" variant="danger" onClick={() => setEnding(s)}>Terminer</Button></>}
              </div>
              <div className="row small muted" style={{ marginTop: 8 }}>{s.lines.map((l) => <Badge key={l.feeItemId} tone="gray">{l.label} · {l.percent} %</Badge>)}</div>
            </div>
          ))}
        </div>
      )}
      {editing && account && <SponsorshipDialog enrollmentId={enrollmentId} account={account} existing={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} onSaved={refresh} />}
      {ending && <ReasonDialog title="Terminer la prise en charge" prompt="Les prochaines échéances redeviennent payantes. Les mois déjà couverts restent couverts." confirmLabel="Terminer"
        onClose={() => setEnding(null)} onConfirm={async (reason) => { await api.post(`/api/inscriptions/${enrollmentId}/prises-en-charge/${ending.id}/fin`, { reason }); await refresh(); }} />}
    </Card>
  );
}

function SponsorshipDialog({ enrollmentId, account, existing, onClose, onSaved }: { enrollmentId: string; account: Account; existing?: Sponsorship; onClose: () => void; onSaved: () => Promise<unknown> }) {
  const [sponsorName, setSponsorName] = useState(existing?.sponsorName ?? '');
  const [reason, setReason] = useState(existing?.reason ?? '');
  const [from, setFrom] = useState(existing?.effectiveFrom ?? new Date().toISOString().slice(0, 10));
  const [percent, setPercent] = useState<Record<string, number>>(() => Object.fromEntries((existing?.lines ?? []).map((l) => [l.feeItemId, l.percent])));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  const toast = useToast();
  const setAll = (p: number) => setPercent(Object.fromEntries(account.lines.filter((l) => l.remaining > 0 || percent[l.feeItemId]).map((l) => [l.feeItemId, p])));
  const save = async () => {
    setBusy(true); setError(undefined);
    try {
      const lines = Object.entries(percent).filter(([, p]) => p > 0).map(([feeItemId, p]) => ({ feeItemId, percent: p }));
      const body = { sponsorName, reason, effectiveFrom: from, lines };
      if (existing) await api.put(`/api/inscriptions/${enrollmentId}/prises-en-charge/${existing.id}`, body);
      else await api.post(`/api/inscriptions/${enrollmentId}/prises-en-charge`, body);
      await onSaved();
      toast('Prise en charge enregistrée.', 'success');
      onClose();
    } catch (e) { setError(e); } finally { setBusy(false); }
  };
  return (
    <Modal wide eyebrow={account.fullName} title={existing ? 'Modifier la prise en charge' : 'Nouvelle prise en charge'} subtitle="Bourse, sponsor ou exonération : la part couverte est déduite du reste à payer, sans créer de reçu."
      onClose={onClose} footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" loading={busy} onClick={save}>Enregistrer</Button></>}>
      <div className="stack">
        <div className="grid cols-3">
          <TextField label="Garant ou organisme" value={sponsorName} onChange={(e) => setSponsorName(e.target.value)} />
          <TextField label="Date d'effet" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          <div className="field"><span className="label">Appliquer à toutes les rubriques</span><div className="row"><Button size="small" onClick={() => setAll(100)}>100 %</Button><Button size="small" onClick={() => setAll(50)}>50 %</Button><Button size="small" variant="ghost" onClick={() => setPercent({})}>Aucune</Button></div></div>
          <TextArea className="span-3" label="Motif" value={reason} onChange={(e) => setReason(e.target.value)} rows={2} />
        </div>
        <div className="table-wrap">
          <table className="data">
            <thead><tr><th>Rubrique</th><th>Échéance</th><th className="num">Montant</th><th className="num">Déjà payé</th><th style={{ width: 140 }}>Couverture %</th></tr></thead>
            <tbody>{account.lines.map((l) => (
              <tr key={l.feeItemId}>
                <td>{l.label}</td><td>{date(l.dueDate)}</td><td className="num">{money(l.amount)}</td><td className="num">{money(l.paid)}</td>
                <td><input className="input" type="number" min={0} max={100} value={percent[l.feeItemId] ?? 0} onChange={(e) => setPercent({ ...percent, [l.feeItemId]: Number(e.target.value) })} /></td>
              </tr>))}
            </tbody>
          </table>
        </div>
        <ErrorAlert error={error} />
      </div>
    </Modal>
  );
}

function EnrollmentTab({ student }: { student: Student }) {
  const { can } = useSession();
  const qc = useQueryClient();
  const navigate = useNavigate();
  const toast = useToast();
  const [statusFor, setStatusFor] = useState<string | null>(null);
  const [promotionFor, setPromotionFor] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);
  const refresh = () => qc.invalidateQueries({ queryKey: ['student', student.id] });
  return (
    <Card title="Historique des inscriptions" pad={false} actions={can(P.settingsWrite) && <Button variant="danger" size="small" icon={<UserX />} onClick={() => setDeleting(true)}>Supprimer le dossier</Button>}>
      <table className="data">
        <thead><tr><th>Année</th><th>Promotion</th><th>Inscrit le</th><th>Statut</th><th>Motif</th><th /></tr></thead>
        <tbody>{student.enrollments.map((e) => (
          <tr key={e.id}>
            <td>{e.academicYear}{e.isReEnrollment && <span className="small muted"> · réinscription</span>}</td>
            <td><span className="tag-code">{e.promotionCode}</span> {e.promotionName}</td><td>{date(e.enrolledOn)}</td>
            <td><Badge tone={statusTone(e.status)}>{e.status}</Badge>{e.exitDate && <div className="small muted">le {date(e.exitDate)}</div>}</td>
            <td className="muted small">{e.exitReason ?? '—'}</td>
            <td className="right">{can(P.studentsWrite) && <div className="row" style={{ justifyContent: 'flex-end' }}>
              <Button size="small" icon={<Repeat />} onClick={() => setPromotionFor(e.id)}>Promotion</Button>
              <Button size="small" onClick={() => setStatusFor(e.id)}>Statut</Button></div>}</td>
          </tr>))}
        </tbody>
      </table>
      {statusFor && <StatusDialog enrollmentId={statusFor} onClose={() => setStatusFor(null)} onSaved={refresh} />}
      {promotionFor && <PromotionDialog enrollmentId={promotionFor} onClose={() => setPromotionFor(null)} onSaved={() => { void qc.invalidateQueries(); }} />}
      {deleting && <Modal title="Supprimer ce dossier ?" onClose={() => setDeleting(false)} footer={<><Button variant="ghost" onClick={() => setDeleting(false)}>Annuler</Button>
        <Button variant="danger" onClick={async () => { try { await api.del(`/api/apprenants/${student.id}`); toast('Dossier supprimé.', 'success'); navigate('/apprenants'); } catch (err) { toast(errorText(err), 'error'); setDeleting(false); } }}>Supprimer définitivement</Button></>}>
        <p>La suppression n’est possible que pour un dossier saisi par erreur, sans reçu, note ni présence. Sinon, changez le statut en « Sorti ».</p>
      </Modal>}
    </Card>
  );
}

function StatusDialog({ enrollmentId, onClose, onSaved }: { enrollmentId: string; onClose: () => void; onSaved: () => Promise<unknown> }) {
  const [status, setStatus] = useState('Suspendu');
  const [reason, setReason] = useState('');
  const [when, setWhen] = useState(new Date().toISOString().slice(0, 10));
  const [error, setError] = useState<unknown>();
  return (
    <Modal title="Changer le statut" onClose={onClose} footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" onClick={async () => {
      try { await api.put(`/api/inscriptions/${enrollmentId}/statut`, { status, date: when, reason }); await onSaved(); onClose(); } catch (e) { setError(e); }
    }}>Enregistrer</Button></>}>
      <div className="stack">
        <div className="grid cols-2">
          <SelectField label="Nouveau statut" value={status} onChange={(e) => setStatus(e.target.value)}>{['Actif', 'Suspendu', 'Sorti', 'Diplômé', 'Transféré'].map((s) => <option key={s}>{s}</option>)}</SelectField>
          <TextField label="Date" type="date" value={when} onChange={(e) => setWhen(e.target.value)} />
        </div>
        <TextArea label="Motif" value={reason} onChange={(e) => setReason(e.target.value)} />
        <ErrorAlert error={error} />
      </div>
    </Modal>
  );
}

function PromotionDialog({ enrollmentId, onClose, onSaved }: { enrollmentId: string; onClose: () => void; onSaved: () => void }) {
  const promotions = useQuery({ queryKey: ['promotions'], queryFn: () => api.get<Promotion[]>('/api/etablissement/promotions') });
  const [promotionId, setPromotionId] = useState('');
  const [error, setError] = useState<unknown>();
  return (
    <Modal title="Changer de promotion" subtitle="Les paiements déjà enregistrés sont reportés sur les mêmes mois de la nouvelle promotion." onClose={onClose}
      footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" disabled={!promotionId} onClick={async () => {
        try { await api.put(`/api/inscriptions/${enrollmentId}/promotion`, { promotionId }); onSaved(); onClose(); } catch (e) { setError(e); }
      }}>Confirmer</Button></>}>
      <div className="stack">
        <SelectField label="Nouvelle promotion" value={promotionId} onChange={(e) => setPromotionId(e.target.value)}>
          <option value="">Choisir…</option>{promotions.data?.map((p) => <option key={p.id} value={p.id}>{p.code} — {p.name} ({money(p.monthlyFee)}/mois)</option>)}
        </SelectField>
        <ErrorAlert error={error} />
      </div>
    </Modal>
  );
}
