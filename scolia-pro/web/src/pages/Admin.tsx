import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { CalendarPlus, CheckCircle2, Database, Download, FileUp, KeyRound, Pencil, Plus, Save, ShieldAlert, ShieldCheck, Trash2, Upload, Wallet } from 'lucide-react';
import { useActionParam } from '../lib/commands';
import { api } from '../api/client';
import type { AcademicYear, AuditItem, BackupInfo, ImportReport, Paged, Payroll, Promotion, School, Section, StaffMember, User } from '../api/types';
import { date, dateTime, money, today } from '../lib/format';
import { Badge, Button, Card, Empty, ErrorAlert, Loading, Modal, PageHead, Pager, SectionTitle, SelectField, Tabs, TextField, statusTone, useDebounced, useToast, errorText } from '../components/ui';
import { useSession } from '../lib/session';

// ---------- Paramètres ----------
export function SettingsPage() {
  const [params] = useSearchParams();
  const [tab, setTab] = useState<'etablissement' | 'promotions' | 'annees' | 'sections'>(() => (params.get('onglet') as 'promotions' | 'annees' | 'sections') || 'etablissement');
  useEffect(() => { const t = params.get('onglet'); if (t) setTab(t as 'etablissement' | 'promotions' | 'annees' | 'sections'); }, [params]);
  return (
    <>
      <PageHead title="Paramètres" subtitle="Établissement, promotions et grilles de frais, années scolaires et sections." />
      <Tabs value={tab} onChange={setTab} items={[
        { value: 'etablissement', label: 'Établissement' }, { value: 'promotions', label: 'Promotions et frais' },
        { value: 'annees', label: 'Années scolaires' }, { value: 'sections', label: 'Sections' },
      ]} />
      {tab === 'etablissement' && <SchoolPanel />}
      {tab === 'promotions' && <PromotionsPanel />}
      {tab === 'annees' && <YearsPanel />}
      {tab === 'sections' && <SectionsPanel />}
    </>
  );
}

function SchoolPanel() {
  const q = useQuery({ queryKey: ['school'], queryFn: () => api.get<School>('/api/etablissement') });
  const [f, setF] = useState<School | null>(null);
  const [error, setError] = useState<unknown>();
  const [logoVersion, setLogoVersion] = useState(0);
  const toast = useToast();
  const { refresh } = useSession();
  useEffect(() => { if (q.data) setF(q.data); }, [q.data]);
  if (!f) return <Loading />;
  const set = (k: keyof School) => (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, [k]: e.target.type === 'checkbox' ? e.target.checked : e.target.value });
  const save = async () => {
    setError(undefined);
    try { await api.put('/api/etablissement', f); toast('Paramètres enregistrés.', 'success'); await refresh(); } catch (e) { setError(e); }
  };
  const uploadLogo = async (file: File) => {
    const data = new FormData(); data.append('fichier', file);
    try { await api.put('/api/etablissement/logo', data); setLogoVersion((v) => v + 1); toast('Logo mis à jour.', 'success'); } catch (e) { toast(errorText(e), 'error'); }
  };
  return (
    <div className="grid mobile-split-grid settings-grid">
      <Card title="Identité de l'établissement" actions={<Button variant="primary" icon={<Save />} onClick={save}>Enregistrer</Button>}>
        <SectionTitle num="01">Coordonnées</SectionTitle>
        <div className="grid cols-2">
          <TextField className="span-2" label="Nom" value={f.name} onChange={set('name')} />
          <TextField label="Sigle" value={f.shortName} onChange={set('shortName')} />
          <TextField label="Slogan" value={f.motto ?? ''} onChange={set('motto')} />
          <TextField label="Adresse" value={f.address ?? ''} onChange={set('address')} />
          <TextField label="Ville" value={f.city ?? ''} onChange={set('city')} />
          <TextField label="Pays" value={f.country ?? ''} onChange={set('country')} />
          <TextField label="Téléphone" value={f.phone ?? ''} onChange={set('phone')} />
          <TextField label="E-mail" value={f.email ?? ''} onChange={set('email')} />
          <TextField label="Site web" value={f.website ?? ''} onChange={set('website')} />
        </div>
        <div className="divider" />
        <SectionTitle num="02">Finances et documents</SectionTitle>
        <div className="grid cols-3">
          <TextField label="Monnaie (ISO)" value={f.currencyCode} onChange={set('currencyCode')} maxLength={3} />
          <TextField label="Symbole" value={f.currencySymbol} onChange={set('currencySymbol')} />
          <TextField label="Indicatif téléphonique" value={f.phoneCountryCode} onChange={set('phoneCountryCode')} help="243 pour la RDC (rappels WhatsApp)" />
          <TextField className="span-3" label="Mention en bas des reçus" value={f.receiptFooter ?? ''} onChange={set('receiptFooter')} />
        </div>
        <label className="check" style={{ marginTop: 12 }}><input type="checkbox" checked={f.wholeAmountsOnly} onChange={set('wholeAmountsOnly')} />Montants entiers uniquement (sans centimes)</label>
        <div style={{ marginTop: 12 }}><ErrorAlert error={error} /></div>
      </Card>
      <Card title="Logo">
        <div className="stack" style={{ alignItems: 'center' }}>
          <img src={`/api/etablissement/logo?v=${logoVersion}`} onError={(e) => { (e.target as HTMLImageElement).src = '/scolia.png'; }} alt="Logo" style={{ width: 140, height: 140, objectFit: 'contain', borderRadius: 16, background: 'var(--surface-2)', padding: 8 }} />
          <label className="btn"><Upload />Choisir une image<input type="file" accept="image/png,image/jpeg" hidden onChange={(e) => e.target.files?.[0] && uploadLogo(e.target.files[0])} /></label>
          <p className="small muted" style={{ textAlign: 'center' }}>PNG ou JPEG, 2 Mo maximum. Il apparaît sur les reçus, relevés, bulletins et l’écran de connexion.</p>
        </div>
      </Card>
    </div>
  );
}

function PromotionsPanel() {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['promotions'], queryFn: () => api.get<Promotion[]>('/api/etablissement/promotions') });
  const [editing, setEditing] = useState<Promotion | 'new' | null>(null);
  useActionParam('promotion', () => setEditing('new'));
  return (
    <Card title="Promotions de l'année courante" pad={false} actions={<Button variant="primary" icon={<Plus />} onClick={() => setEditing('new')}>Nouvelle promotion</Button>}>
      {!q.data ? <Loading /> : q.data.length === 0 ? <Empty text="Aucune promotion : créez-en une pour commencer les inscriptions." /> : (
        <table className="data">
          <thead><tr><th>Code</th><th>Désignation</th><th>Section</th><th className="num">Mensualité</th><th className="num">Total annuel</th><th className="num">Inscrits</th><th>Suivante</th><th>Statut</th><th /></tr></thead>
          <tbody>{q.data.map((p) => (
            <tr key={p.id}><td className="tag-code">{p.code}</td><td>{p.name}</td><td>{p.sectionName ?? '—'}</td><td className="num">{money(p.monthlyFee)}</td><td className="num">{money(p.annualTotal)}</td>
              <td className="num">{p.studentCount}{p.capacity ? ` / ${p.capacity}` : ''}</td><td>{p.nextPromotionCode ?? '—'}</td><td><Badge tone={p.isActive ? 'green' : 'gray'}>{p.isActive ? 'Active' : 'Inactive'}</Badge></td>
              <td className="right"><Button size="small" icon={<Pencil />} onClick={() => setEditing(p)}>Modifier</Button></td></tr>))}
          </tbody>
        </table>
      )}
      {editing && <PromotionDialog promotion={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} onSaved={() => qc.invalidateQueries({ queryKey: ['promotions'] })} />}
    </Card>
  );
}

function PromotionDialog({ promotion, onClose, onSaved }: { promotion?: Promotion; onClose: () => void; onSaved: () => void }) {
  const sections = useQuery({ queryKey: ['sections'], queryFn: () => api.get<Section[]>('/api/etablissement/sections') });
  const [f, setF] = useState({
    code: promotion?.code ?? '', name: promotion?.name ?? '', sectionId: promotion?.sectionId ?? '', level: String(promotion?.level ?? 1), capacity: promotion?.capacity ? String(promotion.capacity) : '',
    isActive: promotion?.isActive ?? true, nextPromotionCode: promotion?.nextPromotionCode ?? '', monthlyFee: String(promotion?.monthlyFee ?? ''),
  });
  const [fees, setFees] = useState(() => (promotion?.fees ?? []).filter((x) => x.kind !== 'Mensualite').map((x) => ({ kind: x.kind, label: x.label, amount: String(x.amount), dueDate: x.dueDate })));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  const toast = useToast();
  const save = async () => {
    setBusy(true); setError(undefined);
    const body = { ...f, sectionId: f.sectionId || null, level: Number(f.level), capacity: f.capacity ? Number(f.capacity) : null, nextPromotionCode: f.nextPromotionCode || null,
      monthlyFee: Number(f.monthlyFee || 0), oneTimeFees: fees.map((x) => ({ ...x, amount: Number(x.amount) })) };
    try { if (promotion) await api.put(`/api/etablissement/promotions/${promotion.id}`, body); else await api.post('/api/etablissement/promotions', body); onSaved(); toast('Promotion enregistrée.', 'success'); onClose(); }
    catch (e) { setError(e); } finally { setBusy(false); }
  };
  return (
    <Modal wide title={promotion ? `Promotion ${promotion.code}` : 'Nouvelle promotion'} subtitle="La mensualité crée une échéance par mois de l’année scolaire. Ajoutez les frais ponctuels (inscription, examen…)." onClose={onClose}
      footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" loading={busy} onClick={save}>Enregistrer</Button></>}>
      <div className="stack">
        <div className="grid cols-4">
          <TextField label="Code (4 caractères)" value={f.code} onChange={(e) => setF({ ...f, code: e.target.value.toUpperCase() })} maxLength={4} help="Préfixe des matricules" />
          <TextField className="span-2" label="Désignation" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} />
          <TextField label="Niveau" type="number" value={f.level} onChange={(e) => setF({ ...f, level: e.target.value })} />
          <SelectField label="Section" value={f.sectionId} onChange={(e) => setF({ ...f, sectionId: e.target.value })}><option value="">—</option>{sections.data?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</SelectField>
          <TextField label="Mensualité" inputMode="numeric" value={f.monthlyFee} onChange={(e) => setF({ ...f, monthlyFee: e.target.value })} />
          <TextField label="Capacité" inputMode="numeric" value={f.capacity} onChange={(e) => setF({ ...f, capacity: e.target.value })} />
          <TextField label="Promotion suivante" value={f.nextPromotionCode} onChange={(e) => setF({ ...f, nextPromotionCode: e.target.value.toUpperCase() })} help="Pour les réinscriptions" />
        </div>
        <label className="check"><input type="checkbox" checked={f.isActive} onChange={(e) => setF({ ...f, isActive: e.target.checked })} />Promotion active (accepte de nouvelles inscriptions)</label>
        <SectionTitle num="02">Frais ponctuels</SectionTitle>
        {fees.map((x, i) => (
          <div key={i} className="grid fee-grid">
            <SelectField label="Nature" value={x.kind} onChange={(e) => setFees(fees.map((y, j) => (j === i ? { ...y, kind: e.target.value } : y)))}><option value="Inscription">Inscription</option><option value="Examen">Examen</option><option value="Autre">Autre</option></SelectField>
            <TextField label="Libellé" value={x.label} onChange={(e) => setFees(fees.map((y, j) => (j === i ? { ...y, label: e.target.value } : y)))} />
            <TextField label="Montant" inputMode="numeric" value={x.amount} onChange={(e) => setFees(fees.map((y, j) => (j === i ? { ...y, amount: e.target.value } : y)))} />
            <TextField label="Échéance" type="date" value={x.dueDate} onChange={(e) => setFees(fees.map((y, j) => (j === i ? { ...y, dueDate: e.target.value } : y)))} />
            <Button variant="ghost" icon={<Trash2 />} onClick={() => setFees(fees.filter((_, j) => j !== i))} aria-label="Retirer" />
          </div>
        ))}
        <div><Button size="small" icon={<Plus />} onClick={() => setFees([...fees, { kind: 'Inscription', label: "Frais d'inscription", amount: '', dueDate: today() }])}>Ajouter un frais</Button></div>
        <ErrorAlert error={error} />
      </div>
    </Modal>
  );
}

function YearsPanel() {
  const qc = useQueryClient();
  const toast = useToast();
  const q = useQuery({ queryKey: ['years'], queryFn: () => api.get<AcademicYear[]>('/api/etablissement/annees') });
  const [creating, setCreating] = useState(false);
  useActionParam('annee', () => setCreating(true));
  const act = async (url: string, message: string) => {
    try { await api.post(url); await qc.invalidateQueries(); toast(message, 'success'); } catch (e) { toast(errorText(e), 'error'); }
  };
  return (
    <Card title="Années scolaires" pad={false} actions={<Button variant="primary" icon={<CalendarPlus />} onClick={() => setCreating(true)}>Préparer une nouvelle année</Button>}>
      {!q.data ? <Loading /> : (
        <table className="data">
          <thead><tr><th>Année</th><th>Du</th><th>Au</th><th>Mois facturés</th><th>Statut</th><th /></tr></thead>
          <tbody>{q.data.map((y) => (
            <tr key={y.id}><td style={{ fontWeight: 700 }}>{y.label} {y.isCurrent && <Badge tone="green">Courante</Badge>}</td><td>{date(y.startDate)}</td><td>{date(y.endDate)}</td>
              <td className="small">{y.periods.map((p) => p.name.slice(0, 3)).join(' · ')}</td><td><Badge tone={y.status === 'Closed' ? 'gray' : 'green'}>{y.status === 'Closed' ? 'Clôturée' : 'Ouverte'}</Badge></td>
              <td className="right">{!y.isCurrent && <Button size="small" onClick={() => act(`/api/etablissement/annees/${y.id}/courante`, `${y.label} est l'année courante.`)}>Rendre courante</Button>}{' '}
                {y.status !== 'Closed' && <Button size="small" variant="ghost" onClick={() => act(`/api/etablissement/annees/${y.id}/cloture`, `${y.label} clôturée.`)}>Clôturer</Button>}</td></tr>))}
          </tbody>
        </table>
      )}
      {creating && <NewYearDialog onClose={() => setCreating(false)} onSaved={() => qc.invalidateQueries()} />}
    </Card>
  );
}

function NewYearDialog({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({ label: '', startMonth: 9, monthCount: 10, dueDay: 6, copyPromotionsFromCurrent: true, makeCurrent: false });
  const [error, setError] = useState<unknown>();
  return (
    <Modal title="Nouvelle année scolaire" subtitle="Les promotions et leurs frais peuvent être recopiés depuis l’année courante." onClose={onClose}
      footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" onClick={async () => { try { await api.post('/api/etablissement/annees', f); onSaved(); onClose(); } catch (e) { setError(e); } }}>Créer</Button></>}>
      <div className="grid cols-2">
        <TextField label="Année" placeholder="2027-2028" value={f.label} onChange={(e) => setF({ ...f, label: e.target.value })} />
        <TextField label="Mois de rentrée (1-12)" type="number" min={1} max={12} value={f.startMonth} onChange={(e) => setF({ ...f, startMonth: Number(e.target.value) })} />
        <TextField label="Mois facturés" type="number" min={1} max={12} value={f.monthCount} onChange={(e) => setF({ ...f, monthCount: Number(e.target.value) })} />
        <TextField label="Jour d'échéance (mois suivant)" type="number" min={1} max={28} value={f.dueDay} onChange={(e) => setF({ ...f, dueDay: Number(e.target.value) })} />
      </div>
      <div className="stack" style={{ marginTop: 12 }}>
        <label className="check"><input type="checkbox" checked={f.copyPromotionsFromCurrent} onChange={(e) => setF({ ...f, copyPromotionsFromCurrent: e.target.checked })} />Recopier les promotions et les frais de l’année courante</label>
        <label className="check"><input type="checkbox" checked={f.makeCurrent} onChange={(e) => setF({ ...f, makeCurrent: e.target.checked })} />En faire immédiatement l’année courante</label>
        <ErrorAlert error={error} />
      </div>
    </Modal>
  );
}

function SectionsPanel() {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['sections'], queryFn: () => api.get<Section[]>('/api/etablissement/sections') });
  const [f, setF] = useState({ code: '', name: '' });
  const [error, setError] = useState<unknown>();
  return (
    <Card title="Sections et options">
      <div className="toolbar">
        <TextField label="Code" value={f.code} onChange={(e) => setF({ ...f, code: e.target.value })} />
        <TextField className="grow" label="Nom" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} />
        <Button variant="primary" icon={<Plus />} onClick={async () => { try { await api.post('/api/etablissement/sections', { ...f, isActive: true }); setF({ code: '', name: '' }); void qc.invalidateQueries({ queryKey: ['sections'] }); } catch (e) { setError(e); } }}>Ajouter</Button>
      </div>
      <ErrorAlert error={error} />
      {q.data && <table className="data"><thead><tr><th>Code</th><th>Nom</th></tr></thead><tbody>{q.data.map((s) => <tr key={s.id}><td className="tag-code">{s.code}</td><td>{s.name}</td></tr>)}</tbody></table>}
    </Card>
  );
}

// ---------- Utilisateurs ----------
export function UsersPage() {
  const qc = useQueryClient();
  const toast = useToast();
  const q = useQuery({ queryKey: ['users'], queryFn: () => api.get<User[]>('/api/utilisateurs') });
  const [editing, setEditing] = useState<User | 'new' | null>(null);
  useActionParam('nouveau', () => setEditing('new'));
  const [resetting, setResetting] = useState<User | null>(null);
  return (
    <>
      <PageHead title="Utilisateurs" subtitle="Un compte nominatif par personne. Les droits dépendent du rôle ; chaque action est tracée dans le journal d’audit."
        actions={<Button variant="primary" icon={<Plus />} onClick={() => setEditing('new')}>Nouveau compte</Button>} />
      <div className="grid cols-2" style={{ marginBottom: 16 }}>
        {[['Secrétariat', 'Administrateur de SCOLIA : apprenants, encaissements, scolarité, personnel, et tout le paramétrage, les utilisateurs et les sauvegardes'], ['Comptabilité', 'Encaissements, annulations, dépenses, arrêt de caisse, journal et rapports. Pas de paramétrage']].map(([r, d]) => (
          <div key={r} className="kpi"><div className="label"><ShieldCheck size={15} />{r}</div><div className="small" style={{ marginTop: 6 }}>{d}</div></div>))}
      </div>
      <div className="table-wrap">
        {!q.data ? <Loading /> : (
          <table className="data">
            <thead><tr><th>Nom</th><th>Identifiant</th><th>Rôle</th><th>Dernière connexion</th><th>Statut</th><th /></tr></thead>
            <tbody>{q.data.map((u) => (
              <tr key={u.id}><td style={{ fontWeight: 700 }}>{u.displayName}</td><td className="tag-code">{u.userName}</td><td>{u.roleLabel}</td><td className="muted">{dateTime(u.lastLoginUtc)}</td>
                <td><Badge tone={!u.isActive ? 'gray' : u.isLocked ? 'red' : 'green'}>{!u.isActive ? 'Désactivé' : u.isLocked ? 'Verrouillé' : 'Actif'}</Badge>{u.mustChangePassword && <Badge tone="orange">Mot de passe à changer</Badge>}</td>
                <td className="right"><Button size="small" icon={<Pencil />} onClick={() => setEditing(u)}>Modifier</Button> <Button size="small" variant="ghost" icon={<KeyRound />} onClick={() => setResetting(u)}>Mot de passe</Button></td></tr>))}
            </tbody>
          </table>
        )}
      </div>
      {editing && <UserDialog user={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} onSaved={() => { void qc.invalidateQueries({ queryKey: ['users'] }); toast('Compte enregistré.', 'success'); }} />}
      {resetting && <ResetDialog user={resetting} onClose={() => setResetting(null)} />}
    </>
  );
}

// Deux comptes, comme la V79 : le secrétariat administre SCOLIA, la comptabilité tient la caisse.
const ROLES = [['Secretariat', 'Secrétariat'], ['Comptabilite', 'Comptabilité']];

function UserDialog({ user, onClose, onSaved }: { user?: User; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({ userName: user?.userName ?? '', displayName: user?.displayName ?? '', role: user && ROLES.some(([v]) => v === user.role) ? user.role : 'Secretariat', password: '', isActive: user?.isActive ?? true });
  const [error, setError] = useState<unknown>();
  return (
    <Modal title={user ? `Compte de ${user.displayName}` : 'Nouveau compte'} onClose={onClose} footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" onClick={async () => {
      try {
        if (user) await api.put(`/api/utilisateurs/${user.id}`, { displayName: f.displayName, role: f.role, isActive: f.isActive });
        else await api.post('/api/utilisateurs', { userName: f.userName, displayName: f.displayName, role: f.role, password: f.password });
        onSaved(); onClose();
      } catch (e) { setError(e); }
    }}>Enregistrer</Button></>}>
      <div className="grid cols-2">
        <TextField label="Nom complet" value={f.displayName} onChange={(e) => setF({ ...f, displayName: e.target.value })} />
        <TextField label="Identifiant" value={f.userName} disabled={!!user} onChange={(e) => setF({ ...f, userName: e.target.value })} />
        <SelectField label="Rôle" value={f.role} onChange={(e) => setF({ ...f, role: e.target.value })}>{ROLES.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</SelectField>
        {!user && <TextField label="Mot de passe provisoire" type="password" value={f.password} onChange={(e) => setF({ ...f, password: e.target.value })} help="À changer à la première connexion" />}
        {user && <label className="check"><input type="checkbox" checked={f.isActive} onChange={(e) => setF({ ...f, isActive: e.target.checked })} />Compte actif</label>}
      </div>
      <div style={{ marginTop: 12 }}><ErrorAlert error={error} /></div>
    </Modal>
  );
}

function ResetDialog({ user, onClose }: { user: User; onClose: () => void }) {
  const [password, setPassword] = useState('');
  const [error, setError] = useState<unknown>();
  const toast = useToast();
  return (
    <Modal title={`Réinitialiser le mot de passe de ${user.displayName}`} subtitle="Le compte est déverrouillé et l’utilisateur devra choisir un nouveau mot de passe." onClose={onClose}
      footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" onClick={async () => { try { await api.post(`/api/utilisateurs/${user.id}/mot-de-passe`, { newPassword: password }); toast('Mot de passe réinitialisé.', 'success'); onClose(); } catch (e) { setError(e); } }}>Réinitialiser</Button></>}>
      <TextField label="Mot de passe provisoire" type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
      <div style={{ marginTop: 12 }}><ErrorAlert error={error} /></div>
    </Modal>
  );
}

// ---------- Données : sauvegardes, import, intégrité ----------
export function DataPage() {
  const qc = useQueryClient();
  const toast = useToast();
  const backups = useQuery({ queryKey: ['backups'], queryFn: () => api.get<BackupInfo[]>('/api/sauvegardes') });
  const [integrity, setIntegrity] = useState<boolean | null>(null);
  const [restoring, setRestoring] = useState<BackupInfo | null>(null);
  const [files, setFiles] = useState<File[]>([]);
  useActionParam('importer', () => document.getElementById('import-v79')?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  const [report, setReport] = useState<ImportReport | null>(null);
  const [busy, setBusy] = useState(false);
  const runImport = async (apply: boolean) => {
    const data = new FormData();
    files.forEach((f) => data.append('fichiers', f));
    setBusy(true);
    try { const r = await api.post<ImportReport>(`/api/import/scolia?appliquer=${apply}`, data); setReport(r); if (apply) { toast('Import terminé.', 'success'); void qc.invalidateQueries(); } }
    catch (e) { toast(errorText(e), 'error'); } finally { setBusy(false); }
  };
  return (
    <>
      <PageHead title="Données et sauvegardes" subtitle="Sauvegarde automatique quotidienne, contrôle d’intégrité, restauration et migration depuis SCOLIA V79 (Excel)." />
      <div className="grid cols-2" style={{ alignItems: 'start' }}>
        <Card title="Sauvegardes" actions={<>
          <Button icon={<ShieldCheck />} onClick={async () => setIntegrity((await api.get<{ intacte: boolean }>('/api/sauvegardes/integrite')).intacte)}>Contrôler l’intégrité</Button>
          <Button variant="primary" icon={<Database />} onClick={async () => { try { await api.post('/api/sauvegardes'); void backups.refetch(); toast('Sauvegarde créée.', 'success'); } catch (e) { toast(errorText(e), 'error'); } }}>Sauvegarder maintenant</Button>
        </>} pad={false}>
          {integrity !== null && <div className={`alert ${integrity ? 'success' : 'error'}`} style={{ margin: 14 }}>{integrity ? <CheckCircle2 /> : <ShieldAlert />}{integrity ? 'Base de données intègre.' : 'Anomalie détectée : restaurez la dernière sauvegarde.'}</div>}
          {!backups.data ? <Loading /> : backups.data.length === 0 ? <Empty text="Aucune sauvegarde pour le moment (la première est automatique)." /> : (
            <table className="data">
              <tbody>{backups.data.map((b) => (
                <tr key={b.name}><td><div className="tag-code small">{b.name}</div><div className="small muted">{dateTime(b.createdUtc)} · {(b.sizeBytes / 1048576).toFixed(1)} Mo</div></td>
                  <td className="right nowrap"><a className="btn small ghost" href={`/api/sauvegardes/${encodeURIComponent(b.name)}`}><Download />Télécharger</a> <Button size="small" variant="danger" onClick={() => setRestoring(b)}>Restaurer</Button></td></tr>))}
              </tbody>
            </table>
          )}
        </Card>
        <div id="import-v79">
        <Card title="Importer depuis SCOLIA V79 (Excel)">
          <p className="small muted" style={{ marginTop: 0 }}>Choisissez le classeur principal et, le cas échéant, les segments de data. Les matricules sont conservés ; chaque ancien reçu devient un reçu numéroté, réparti par mois. Lancez d’abord une simulation : rien n’est enregistré.</p>
          <label className="btn"><FileUp />Choisir les classeurs<input type="file" multiple accept=".xlsm,.xlsx" hidden onChange={(e) => { setFiles(Array.from(e.target.files ?? [])); setReport(null); }} /></label>
          {files.length > 0 && <ul className="small">{files.map((f) => <li key={f.name}>{f.name} ({(f.size / 1024).toFixed(0)} Ko)</li>)}</ul>}
          <div className="row" style={{ marginTop: 10 }}>
            <Button disabled={!files.length} loading={busy} onClick={() => runImport(false)}>Simuler l’import</Button>
            <Button variant="primary" disabled={!files.length || !report || report.applied} loading={busy} onClick={() => runImport(true)}>Importer définitivement</Button>
          </div>
          {report && (
            <div className="stack" style={{ marginTop: 14 }}>
              <div className={`alert ${report.errors.length ? 'warning' : 'success'}`}><CheckCircle2 /><div><strong>{report.applied ? 'Import effectué' : 'Simulation'}</strong> : {report.promotions} promotion(s), {report.students} apprenant(s), {report.receipts} reçu(s), {report.allocations} affectation(s).</div></div>
              {report.errors.length > 0 && <details open><summary><strong>{report.errors.length} erreur(s) à corriger</strong></summary><ul className="small">{report.errors.map((e, i) => <li key={i}>{e}</li>)}</ul></details>}
              {report.warnings.length > 0 && <details><summary><strong>{report.warnings.length} avertissement(s)</strong></summary><ul className="small">{report.warnings.map((e, i) => <li key={i}>{e}</li>)}</ul></details>}
            </div>
          )}
        </Card>
        </div>
      </div>
      {restoring && <Modal title="Restaurer cette sauvegarde ?" subtitle={restoring.name} onClose={() => setRestoring(null)}
        footer={<><Button variant="ghost" onClick={() => setRestoring(null)}>Annuler</Button><Button variant="danger" onClick={async () => {
          try { await api.post(`/api/sauvegardes/${encodeURIComponent(restoring.name)}/restauration`); toast('Base restaurée. Reconnectez-vous.', 'success'); setTimeout(() => window.location.assign('/'), 1200); } catch (e) { toast(errorText(e), 'error'); }
          setRestoring(null);
        }}>Restaurer</Button></>}>
        <p>Toutes les données saisies après cette sauvegarde seront remplacées. Une sauvegarde de l’état actuel est créée automatiquement avant la restauration.</p>
      </Modal>}
    </>
  );
}

// ---------- Journal d'audit ----------
export function AuditPage() {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const debounced = useDebounced(search);
  const [verification, setVerification] = useState<{ isIntact: boolean; message: string } | null>(null);
  const q = useQuery({ queryKey: ['audit', debounced, page], queryFn: () => api.get<Paged<AuditItem>>('/api/audit', { q: debounced, page, taille: 100 }), placeholderData: (p) => p });
  return (
    <>
      <PageHead title="Journal d’audit" subtitle="Chaque opération sensible est enregistrée et chaînée par empreinte SHA-256 : une modification du journal en dehors de SCOLIA est détectable."
        actions={<Button icon={<ShieldCheck />} onClick={async () => setVerification(await api.get('/api/audit/verification'))}>Vérifier l’intégrité</Button>} />
      {verification && <div className={`alert ${verification.isIntact ? 'success' : 'error'}`} style={{ marginBottom: 14 }}>{verification.isIntact ? <CheckCircle2 /> : <ShieldAlert />}{verification.message}</div>}
      <div className="toolbar"><TextField className="grow" label="Recherche" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} placeholder="Utilisateur, action, matricule, n° de reçu…" /></div>
      <div className="table-wrap">
        {!q.data ? <Loading /> : (
          <table className="data">
            <thead><tr><th>N°</th><th>Date</th><th>Utilisateur</th><th>Action</th><th>Description</th><th>Poste</th></tr></thead>
            <tbody>{q.data.items.map((a) => <tr key={a.id}><td className="muted">{a.id}</td><td className="nowrap">{dateTime(a.atUtc)}</td><td>{a.userName}</td><td><span className="tag-code small">{a.action}</span></td><td>{a.summary}</td><td className="small muted">{a.ipAddress}</td></tr>)}</tbody>
          </table>
        )}
        {q.data && <Pager page={q.data.page} pageSize={q.data.pageSize} total={q.data.total} onPage={setPage} />}
      </div>
    </>
  );
}

// ---------- Personnel et paie ----------
export function StaffPage() {
  const [tab, setTab] = useState<'personnel' | 'paie'>('personnel');
  return (
    <>
      <PageHead title="Personnel et paie" subtitle="Dossiers du personnel, bulletins mensuels et paiement des salaires (enregistrés en dépenses)." />
      <Tabs value={tab} onChange={setTab} items={[{ value: 'personnel', label: 'Personnel' }, { value: 'paie', label: 'Paie mensuelle' }]} />
      {tab === 'personnel' ? <StaffList /> : <PayrollPanel />}
    </>
  );
}

function StaffList() {
  const qc = useQueryClient();
  const q = useQuery({ queryKey: ['staff'], queryFn: () => api.get<StaffMember[]>('/api/personnel') });
  const [editing, setEditing] = useState<StaffMember | 'new' | null>(null);
  useActionParam('nouveau', () => setEditing('new'));
  return (
    <Card title="Membres du personnel" pad={false} actions={<Button variant="primary" icon={<Plus />} onClick={() => setEditing('new')}>Ajouter</Button>}>
      {!q.data ? <Loading /> : q.data.length === 0 ? <Empty text="Aucun membre du personnel." /> : (
        <table className="data">
          <thead><tr><th>N°</th><th>Nom</th><th>Fonction</th><th>Téléphone</th><th>Embauche</th><th className="num">Salaire de base</th><th>Statut</th><th /></tr></thead>
          <tbody>{q.data.map((s) => (
            <tr key={s.id}><td className="tag-code">{s.number}</td><td style={{ fontWeight: 700 }}>{s.lastName} {s.middleName} {s.firstName}</td><td>{s.function}</td><td>{s.phone ?? '—'}</td><td>{date(s.hireDate)}</td>
              <td className="num">{money(s.baseSalary)}</td><td><Badge tone={s.isActive ? 'green' : 'gray'}>{s.isActive ? 'Actif' : 'Inactif'}</Badge></td><td><Button size="small" icon={<Pencil />} onClick={() => setEditing(s)}>Modifier</Button></td></tr>))}
          </tbody>
        </table>
      )}
      {editing && <StaffDialog member={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} onSaved={() => qc.invalidateQueries({ queryKey: ['staff'] })} />}
    </Card>
  );
}

function StaffDialog({ member, onClose, onSaved }: { member?: StaffMember; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({ lastName: member?.lastName ?? '', middleName: member?.middleName ?? '', firstName: member?.firstName ?? '', gender: member?.gender ?? 'M', function: member?.function ?? 'Enseignant',
    phone: member?.phone ?? '', email: member?.email ?? '', hireDate: member?.hireDate ?? today(), baseSalary: String(member?.baseSalary ?? ''), isActive: member?.isActive ?? true });
  const [error, setError] = useState<unknown>();
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: e.target.value });
  return (
    <Modal wide title={member ? `${member.number} · ${member.lastName} ${member.firstName}` : 'Nouveau membre du personnel'} onClose={onClose}
      footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" onClick={async () => {
        try { const body = { ...f, baseSalary: Number(f.baseSalary || 0), phone: f.phone || null, email: f.email || null }; if (member) await api.put(`/api/personnel/${member.id}`, body); else await api.post('/api/personnel', body); onSaved(); onClose(); } catch (e) { setError(e); }
      }}>Enregistrer</Button></>}>
      <div className="grid cols-3">
        <TextField label="Nom" value={f.lastName} onChange={set('lastName')} /><TextField label="Post-nom" value={f.middleName} onChange={set('middleName')} /><TextField label="Prénom" value={f.firstName} onChange={set('firstName')} />
        <SelectField label="Sexe" value={f.gender} onChange={set('gender')}><option value="M">Masculin</option><option value="F">Féminin</option></SelectField>
        <TextField label="Fonction" value={f.function} onChange={set('function')} /><TextField label="Téléphone" value={f.phone} onChange={set('phone')} />
        <TextField label="E-mail" value={f.email} onChange={set('email')} /><TextField label="Date d'embauche" type="date" value={f.hireDate} onChange={set('hireDate')} />
        <TextField label="Salaire de base" inputMode="numeric" value={f.baseSalary} onChange={set('baseSalary')} />
      </div>
      {member && <label className="check" style={{ marginTop: 12 }}><input type="checkbox" checked={f.isActive} onChange={(e) => setF({ ...f, isActive: e.target.checked })} />Actif</label>}
      <div style={{ marginTop: 12 }}><ErrorAlert error={error} /></div>
    </Modal>
  );
}

function PayrollPanel() {
  const [period, setPeriod] = useState(today().slice(0, 7));
  const qc = useQueryClient();
  const toast = useToast();
  const q = useQuery({ queryKey: ['payroll', period], queryFn: () => api.get<Payroll[]>(`/api/personnel/paie/${period}`) });
  const [editing, setEditing] = useState<Payroll | null>(null);
  const refresh = () => qc.invalidateQueries({ queryKey: ['payroll', period] });
  const total = q.data?.reduce((s, p) => s + p.net, 0) ?? 0;
  return (
    <Card title={`Paie de ${period}`} pad={false} actions={<>
      <input className="input" type="month" value={period} onChange={(e) => setPeriod(e.target.value)} style={{ width: 170 }} />
      <Button variant="primary" icon={<Plus />} onClick={async () => { try { const r = await api.post<{ crees: number }>(`/api/personnel/paie/${period}/preparation`); toast(`${r.crees} bulletin(s) préparé(s).`, 'success'); void refresh(); } catch (e) { toast(errorText(e), 'error'); } }}>Préparer les bulletins</Button>
    </>}>
      {!q.data ? <Loading /> : q.data.length === 0 ? <Empty text="Aucun bulletin pour ce mois : cliquez sur « Préparer les bulletins »." /> : (
        <table className="data">
          <thead><tr><th>Agent</th><th>Fonction</th><th className="num">Base</th><th className="num">Primes</th><th className="num">Retenues</th><th className="num">Avances</th><th className="num">Net à payer</th><th>Statut</th><th /></tr></thead>
          <tbody>{q.data.map((p) => (
            <tr key={p.id}><td style={{ fontWeight: 700 }}>{p.fullName}<div className="small tag-code">{p.number}</div></td><td>{p.function}</td><td className="num">{money(p.baseSalary)}</td><td className="num">{money(p.allowances)}</td>
              <td className="num">{money(p.deductions)}</td><td className="num">{money(p.advances)}</td><td className="num" style={{ fontWeight: 800 }}>{money(p.net)}</td>
              <td><Badge tone={p.status === 'Paye' ? 'green' : statusTone(p.status)}>{p.status === 'Paye' ? `Payé le ${date(p.paidOn)}` : p.status === 'Valide' ? 'Validé' : 'Brouillon'}</Badge></td>
              <td className="right nowrap">{p.status !== 'Paye' && <><Button size="small" icon={<Pencil />} onClick={() => setEditing(p)}>Ajuster</Button> <Button size="small" variant="success" icon={<Wallet />} onClick={async () => { try { await api.post(`/api/personnel/paie/bulletins/${p.id}/paiement`); toast('Salaire payé et enregistré en dépense.', 'success'); void refresh(); } catch (e) { toast(errorText(e), 'error'); } }}>Payer</Button></>}</td></tr>))}
          </tbody>
          <tfoot><tr><td colSpan={6}>Masse salariale nette</td><td className="num">{money(total)}</td><td colSpan={2} /></tr></tfoot>
        </table>
      )}
      {editing && <PayrollDialog entry={editing} onClose={() => setEditing(null)} onSaved={refresh} />}
    </Card>
  );
}

function PayrollDialog({ entry, onClose, onSaved }: { entry: Payroll; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({ allowances: String(entry.allowances), deductions: String(entry.deductions), advances: String(entry.advances), note: entry.note ?? '' });
  const [error, setError] = useState<unknown>();
  const net = entry.baseSalary + Number(f.allowances || 0) - Number(f.deductions || 0) - Number(f.advances || 0);
  return (
    <Modal title={`Bulletin de ${entry.fullName}`} subtitle={`${entry.period} · salaire de base ${money(entry.baseSalary)}`} onClose={onClose}
      footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" onClick={async () => {
        try { await api.put(`/api/personnel/paie/bulletins/${entry.id}`, { allowances: Number(f.allowances || 0), deductions: Number(f.deductions || 0), advances: Number(f.advances || 0), note: f.note || null }); onSaved(); onClose(); } catch (e) { setError(e); }
      }}>Valider le bulletin</Button></>}>
      <div className="grid cols-3">
        <TextField label="Primes et indemnités" inputMode="numeric" value={f.allowances} onChange={(e) => setF({ ...f, allowances: e.target.value })} />
        <TextField label="Retenues" inputMode="numeric" value={f.deductions} onChange={(e) => setF({ ...f, deductions: e.target.value })} />
        <TextField label="Avances déjà versées" inputMode="numeric" value={f.advances} onChange={(e) => setF({ ...f, advances: e.target.value })} />
        <TextField className="span-3" label="Observation" value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} />
      </div>
      <p style={{ fontSize: 18, fontWeight: 800 }}>Net à payer : {money(net)}</p>
      <ErrorAlert error={error} />
    </Modal>
  );
}
