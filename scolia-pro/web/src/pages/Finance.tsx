import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ArrowLeft, Ban, Banknote, FileDown, FileSpreadsheet, Lock, MessageCircle, MessageSquare, Plus, Printer, Search } from 'lucide-react';
import { api, openDocument } from '../api/client';
import type { CashClosing, CashSituation, Debtor, Expense, Paged, Promotion, Receipt, ReceiptRow, SponsorshipListItem } from '../api/types';
import { date, dateTime, money, today } from '../lib/format';
import { Badge, Button, Card, Empty, ErrorAlert, Kpi, Loading, Modal, PageHead, Pager, ReasonDialog, SelectField, TextField, statusTone, useDebounced, useToast } from '../components/ui';
import { P, useSession } from '../lib/session';

function usePromotions() {
  return useQuery({ queryKey: ['promotions'], queryFn: () => api.get<Promotion[]>('/api/etablissement/promotions') });
}

// ---------- Journal des reçus ----------
export function ReceiptsPage() {
  const [search, setSearch] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [method, setMethod] = useState('');
  const [status, setStatus] = useState('');
  const [promotionId, setPromotionId] = useState('');
  const [page, setPage] = useState(1);
  const debounced = useDebounced(search);
  const navigate = useNavigate();
  const promotions = usePromotions();
  const query = { search: debounced, from, to, method, status, promotionId };
  const list = useQuery({ queryKey: ['receipts', query, page], queryFn: () => api.get<Paged<ReceiptRow>>('/api/paiements', { ...query, page, pageSize: 50 }), placeholderData: (p) => p });
  useEffect(() => setPage(1), [debounced, from, to, method, status, promotionId]);
  return (
    <>
      <PageHead title="Journal des reçus" subtitle="Tous les encaissements, numérotés sans interruption. Un reçu erroné s’annule avec un motif : il n’est jamais supprimé."
        actions={<>
          <Button icon={<FileDown />} onClick={() => openDocument('/api/paiements/export', query)}>PDF</Button>
          <Button icon={<FileSpreadsheet />} onClick={() => openDocument('/api/paiements/export', { ...query, format: 'xlsx' })}>Excel</Button>
        </>} />
      <div className="toolbar">
        <div className="field grow"><label htmlFor="rq">Recherche</label><div className="input-group"><Search /><input id="rq" className="input" placeholder="N° de reçu, matricule, nom…" value={search} onChange={(e) => setSearch(e.target.value)} /></div></div>
        <TextField label="Du" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        <TextField label="Au" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        <SelectField label="Promotion" value={promotionId} onChange={(e) => setPromotionId(e.target.value)}><option value="">Toutes</option>{promotions.data?.map((p) => <option key={p.id} value={p.id}>{p.code}</option>)}</SelectField>
        <SelectField label="Mode" value={method} onChange={(e) => setMethod(e.target.value)}><option value="">Tous</option><option value="Especes">Espèces</option><option value="MobileMoney">Mobile Money</option><option value="Banque">Banque</option><option value="Cheque">Chèque</option></SelectField>
        <SelectField label="Statut" value={status} onChange={(e) => setStatus(e.target.value)}><option value="">Tous</option><option>Valide</option><option>Annulé</option></SelectField>
      </div>
      <ErrorAlert error={list.error} />
      <div className="table-wrap">
        {!list.data ? <Loading /> : list.data.items.length === 0 ? <Empty text="Aucun reçu pour ces critères." /> : (
          <table className="data">
            <thead><tr><th>N° reçu</th><th>Date</th><th>Matricule</th><th>Apprenant</th><th>Promotion</th><th>Mode</th><th>Caissier</th><th>Statut</th><th className="num">Montant</th></tr></thead>
            <tbody>{list.data.items.map((r) => (
              <tr key={r.id} className="clickable" onClick={() => navigate(`/paiements/recus/${r.id}`)}>
                <td className="tag-code">{r.number}</td><td>{date(r.date)}</td><td>{r.matricule}</td><td>{r.studentName}</td><td>{r.promotionCode}</td>
                <td>{r.method}{r.reference && <div className="small muted">{r.reference}</div>}</td><td className="muted">{r.cashier}</td>
                <td><Badge tone={statusTone(r.status)}>{r.status}</Badge></td>
                <td className="num" style={{ fontWeight: 700, textDecoration: r.status === 'Annulé' ? 'line-through' : undefined }}>{money(r.amount)}</td>
              </tr>))}
            </tbody>
            <tfoot><tr><td colSpan={8}>Total des reçus valides ({list.data.total} reçu(s) au total)</td><td className="num">{money(list.data.sum)}</td></tr></tfoot>
          </table>
        )}
        {list.data && list.data.total > 0 && <Pager page={list.data.page} pageSize={list.data.pageSize} total={list.data.total} onPage={setPage} />}
      </div>
    </>
  );
}

export function ReceiptDetailPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const toast = useToast();
  const { can } = useSession();
  const [cancelling, setCancelling] = useState(false);
  const q = useQuery({ queryKey: ['receipt', id], queryFn: () => api.get<Receipt>(`/api/paiements/${id}`) });
  const r = q.data;
  if (q.error) return <ErrorAlert error={q.error} />;
  if (!r) return <Loading />;
  return (
    <>
      <button className="link-btn small" onClick={() => navigate(-1)} style={{ marginBottom: 10, display: 'inline-flex', gap: 6, alignItems: 'center' }}><ArrowLeft size={14} />Retour</button>
      <PageHead title={`Reçu ${r.number}`} subtitle={`${date(r.date)} · enregistré le ${dateTime(r.createdAtUtc)} par ${r.cashier}`}
        actions={<>
          <Button variant="primary" icon={<Printer />} onClick={() => openDocument(`/api/paiements/${r.id}/recu.pdf`)}>Imprimer</Button>
          {r.status === 'Valide' && can(P.paymentsCancel) && <Button variant="danger" icon={<Ban />} onClick={() => setCancelling(true)}>Annuler ce reçu</Button>}
        </>} />
      {r.status !== 'Valide' && <div className="alert error" style={{ marginBottom: 16 }}><Ban /><div><strong>Reçu annulé</strong> le {dateTime(r.cancelledAtUtc)} par {r.cancelledBy} : {r.cancellationReason}</div></div>}
      <div className="grid cols-2">
        <Card title="Apprenant">
          <dl className="dl">
            <dt>Nom complet</dt><dd><a href={`/apprenants/${r.studentId}`} onClick={(e) => { e.preventDefault(); navigate(`/apprenants/${r.studentId}`); }}>{r.studentName}</a></dd>
            <dt>Matricule</dt><dd className="tag-code">{r.matricule}</dd><dt>Promotion</dt><dd>{r.promotionCode} · {r.academicYear}</dd>
            <dt>Versé par</dt><dd>{r.payerName ?? '—'} {r.payerPhone && `· ${r.payerPhone}`}</dd>
          </dl>
        </Card>
        <Card title="Paiement">
          <dl className="dl">
            <dt>Montant</dt><dd style={{ fontSize: 20 }}>{money(r.amount)}</dd><dt>Mode</dt><dd>{r.method}</dd><dt>Référence</dt><dd>{r.reference ?? '—'}</dd>
            <dt>Code de vérification</dt><dd className="tag-code">{r.verificationCode}</dd><dt>Statut</dt><dd><Badge tone={statusTone(r.status)}>{r.status}</Badge></dd>
            {r.note && <><dt>Observation</dt><dd>{r.note}</dd></>}
          </dl>
        </Card>
      </div>
      <div style={{ marginTop: 16 }} className="table-wrap">
        <table className="data"><thead><tr><th>Affectation</th><th className="num">Montant</th></tr></thead>
          <tbody>{r.allocations.map((a) => <tr key={a.feeItemId}><td>{a.label}</td><td className="num">{money(a.amount)}</td></tr>)}</tbody>
          <tfoot><tr><td>Total</td><td className="num">{money(r.amount)}</td></tr></tfoot></table>
      </div>
      {cancelling && <ReasonDialog title={`Annuler le reçu ${r.number}`} prompt="Le reçu restera visible dans le journal avec la mention « Annulé ». Les montants affectés redeviennent dus." confirmLabel="Annuler le reçu"
        onClose={() => setCancelling(false)} onConfirm={async (reason) => { await api.post(`/api/paiements/${r.id}/annulation`, { reason }); await qc.invalidateQueries(); toast('Reçu annulé.', 'success'); }} />}
    </>
  );
}

// ---------- Recouvrement ----------
export function DebtorsPage() {
  const promotions = usePromotions();
  const [promotionId, setPromotionId] = useState('');
  const [minMonths, setMinMonths] = useState('');
  const [bucket, setBucket] = useState('');
  const [search, setSearch] = useState('');
  const debounced = useDebounced(search);
  const navigate = useNavigate();
  const query = { promotionId, minOverdueMonths: minMonths, bucket, search: debounced };
  const q = useQuery({ queryKey: ['debtors', query], queryFn: () => api.get<Debtor[]>('/api/rapports/debiteurs', query), placeholderData: (p) => p });
  const total = q.data?.reduce((s, d) => s + d.overdue, 0) ?? 0;
  return (
    <>
      <PageHead title="Recouvrement" subtitle="Apprenants ayant des échéances dépassées, avec rappels prêts à envoyer aux parents par WhatsApp ou SMS."
        actions={<>
          <Button icon={<FileDown />} onClick={() => openDocument('/api/rapports/debiteurs/export', query)}>PDF</Button>
          <Button icon={<FileSpreadsheet />} onClick={() => openDocument('/api/rapports/debiteurs/export', { ...query, format: 'xlsx' })}>Excel</Button>
        </>} />
      <div className="grid cols-3" style={{ marginBottom: 16 }}>
        <Kpi label="Dossiers en retard" value={q.data?.length ?? '—'} tone="orange" />
        <Kpi label="Montant exigible" value={money(total)} tone="red" />
        <Kpi label="Retards de plus de 60 jours" value={q.data?.filter((d) => d.agingBucket === '61-90 jours' || d.agingBucket === 'Plus de 90 jours').length ?? '—'} tone="purple" />
      </div>
      <div className="toolbar">
        <div className="field grow"><label htmlFor="dq">Recherche</label><div className="input-group"><Search /><input id="dq" className="input" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Nom ou matricule" /></div></div>
        <SelectField label="Promotion" value={promotionId} onChange={(e) => setPromotionId(e.target.value)}><option value="">Toutes</option>{promotions.data?.map((p) => <option key={p.id} value={p.id}>{p.code}</option>)}</SelectField>
        <SelectField label="Échéances en retard" value={minMonths} onChange={(e) => setMinMonths(e.target.value)}><option value="">Toutes</option><option value="1">1 et plus</option><option value="2">2 et plus</option><option value="3">3 et plus</option></SelectField>
        <SelectField label="Ancienneté" value={bucket} onChange={(e) => setBucket(e.target.value)}><option value="">Toutes</option>{['0-30 jours', '31-60 jours', '61-90 jours', 'Plus de 90 jours'].map((b) => <option key={b}>{b}</option>)}</SelectField>
      </div>
      <ErrorAlert error={q.error} />
      <div className="table-wrap">
        {!q.data ? <Loading /> : q.data.length === 0 ? <Empty text="Aucun retard de paiement : tous les dossiers sont à jour." /> : (
          <table className="data">
            <thead><tr><th>Matricule</th><th>Apprenant</th><th>Promotion</th><th>Périodes en retard</th><th>Ancienneté</th><th>Parent</th><th className="num">Exigible</th><th /></tr></thead>
            <tbody>{q.data.map((d) => (
              <tr key={d.enrollmentId}>
                <td className="tag-code">{d.matricule}</td>
                <td><a href={`/apprenants/${d.studentId}`} onClick={(e) => { e.preventDefault(); navigate(`/apprenants/${d.studentId}`); }} style={{ fontWeight: 700 }}>{d.fullName}</a></td>
                <td>{d.promotionCode}</td><td className="small">{d.periods}</td>
                <td><Badge tone={d.agingBucket === '0-30 jours' ? 'orange' : 'red'}>{d.agingBucket}</Badge></td>
                <td className="small">{d.guardianName ?? '—'}<div className="muted">{d.guardianPhone}</div></td>
                <td className="num" style={{ fontWeight: 800, color: 'var(--red)' }}>{money(d.overdue)}</td>
                <td className="right nowrap">
                  {d.whatsAppLink && <a className="btn small" href={d.whatsAppLink} target="_blank" rel="noreferrer" title="Rappel WhatsApp"><MessageCircle /></a>}{' '}
                  {d.smsLink && <a className="btn small ghost" href={d.smsLink} title="Rappel SMS"><MessageSquare /></a>}{' '}
                  <Button size="small" variant="primary" onClick={() => navigate(`/encaisser?inscription=${d.enrollmentId}`)}>Encaisser</Button>
                </td>
              </tr>))}
            </tbody>
          </table>
        )}
      </div>
    </>
  );
}

export function SponsorshipsPage() {
  const q = useQuery({ queryKey: ['sponsorships-all'], queryFn: () => api.get<SponsorshipListItem[]>('/api/prises-en-charge') });
  const navigate = useNavigate();
  return (
    <>
      <PageHead title="Prises en charge" subtitle="Bourses, sponsors et exonérations de l’année. Elles se créent depuis la fiche de l’apprenant." />
      <div className="table-wrap">
        {!q.data ? <Loading /> : q.data.length === 0 ? <Empty text="Aucune prise en charge cette année." /> : (
          <table className="data">
            <thead><tr><th>Matricule</th><th>Apprenant</th><th>Promotion</th><th>Garant</th><th>Motif</th><th>Depuis</th><th>Statut</th><th className="num">Couvert</th></tr></thead>
            <tbody>{q.data.map((s) => (
              <tr key={s.id} className="clickable" onClick={() => navigate(`/apprenants?q=${s.matricule}`)}>
                <td className="tag-code">{s.matricule}</td><td>{s.fullName}</td><td>{s.promotion}</td><td>{s.sponsorName}</td><td className="small muted">{s.reason}</td>
                <td>{date(s.effectiveFrom)}</td><td><Badge tone={s.isActive ? 'purple' : 'gray'}>{s.isActive ? 'Active' : 'Terminée'}</Badge></td><td className="num">{money(s.covered)}</td>
              </tr>))}
            </tbody>
            <tfoot><tr><td colSpan={7}>Total couvert</td><td className="num">{money(q.data.reduce((a, s) => a + s.covered, 0))}</td></tr></tfoot>
          </table>
        )}
      </div>
    </>
  );
}

// ---------- Dépenses ----------
export function ExpensesPage() {
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [category, setCategory] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const [cancelling, setCancelling] = useState<Expense | null>(null);
  const debounced = useDebounced(search);
  const qc = useQueryClient();
  const toast = useToast();
  const { can } = useSession();
  const categories = useQuery({ queryKey: ['expense-categories'], queryFn: () => api.get<string[]>('/api/depenses/categories') });
  const list = useQuery({ queryKey: ['expenses', from, to, category, debounced, page], queryFn: () => api.get<Paged<Expense>>('/api/depenses', { du: from, au: to, categorie: category, q: debounced, page, taille: 50 }), placeholderData: (p) => p });
  return (
    <>
      <PageHead title="Dépenses" subtitle="Sorties de caisse et de banque, numérotées et justifiées."
        actions={can(P.expensesWrite) && <Button variant="primary" icon={<Plus />} onClick={() => setCreating(true)}>Nouvelle dépense</Button>} />
      <div className="toolbar">
        <div className="field grow"><label htmlFor="eq">Recherche</label><div className="input-group"><Search /><input id="eq" className="input" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Bénéficiaire, description, n°" /></div></div>
        <TextField label="Du" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        <TextField label="Au" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        <SelectField label="Catégorie" value={category} onChange={(e) => setCategory(e.target.value)}><option value="">Toutes</option>{categories.data?.map((c) => <option key={c}>{c}</option>)}</SelectField>
      </div>
      <div className="table-wrap">
        {!list.data ? <Loading /> : list.data.items.length === 0 ? <Empty text="Aucune dépense." /> : (
          <table className="data">
            <thead><tr><th>N°</th><th>Date</th><th>Catégorie</th><th>Bénéficiaire</th><th>Description</th><th>Mode</th><th>Statut</th><th className="num">Montant</th><th /></tr></thead>
            <tbody>{list.data.items.map((e) => (
              <tr key={e.id}>
                <td className="tag-code">{e.number}</td><td>{date(e.date)}</td><td>{e.category}</td><td>{e.beneficiary}</td><td className="small">{e.description}{e.cancellationReason && <div className="muted">Annulée : {e.cancellationReason}</div>}</td>
                <td>{e.method}</td><td><Badge tone={statusTone(e.status)}>{e.status}</Badge></td><td className="num" style={{ fontWeight: 700 }}>{money(e.amount)}</td>
                <td>{e.status === 'Valide' && can(P.expensesWrite) && <Button size="small" variant="ghost" icon={<Ban />} onClick={() => setCancelling(e)} aria-label="Annuler" />}</td>
              </tr>))}
            </tbody>
            <tfoot><tr><td colSpan={7}>Total des dépenses valides</td><td className="num">{money(list.data.sum)}</td><td /></tr></tfoot>
          </table>
        )}
        {list.data && list.data.total > 0 && <Pager page={list.data.page} pageSize={list.data.pageSize} total={list.data.total} onPage={setPage} />}
      </div>
      {creating && <ExpenseDialog categories={categories.data ?? []} onClose={() => setCreating(false)} onSaved={() => { void qc.invalidateQueries({ queryKey: ['expenses'] }); void qc.invalidateQueries({ queryKey: ['cash'] }); toast('Dépense enregistrée.', 'success'); }} />}
      {cancelling && <ReasonDialog title={`Annuler la dépense ${cancelling.number}`} prompt="La dépense restera visible avec la mention « Annulée »." confirmLabel="Annuler la dépense"
        onClose={() => setCancelling(null)} onConfirm={async (reason) => { await api.post(`/api/depenses/${cancelling.id}/annulation`, { reason }); await qc.invalidateQueries({ queryKey: ['expenses'] }); }} />}
    </>
  );
}

function ExpenseDialog({ categories, onClose, onSaved }: { categories: string[]; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({ date: today(), category: categories[0] ?? 'Divers', beneficiary: '', description: '', amount: '', method: 'Especes', reference: '' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: e.target.value });
  return (
    <Modal eyebrow="Comptabilité" title="Nouvelle dépense" onClose={onClose} footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" loading={busy} onClick={async () => {
      setBusy(true); setError(undefined);
      try { await api.post('/api/depenses', { ...f, amount: Number(f.amount), reference: f.reference || null }); onSaved(); onClose(); } catch (e) { setError(e); } finally { setBusy(false); }
    }}>Enregistrer</Button></>}>
      <div className="grid cols-2">
        <TextField label="Date" type="date" value={f.date} max={today()} onChange={set('date')} />
        <SelectField label="Catégorie" value={f.category} onChange={set('category')}>{categories.map((c) => <option key={c}>{c}</option>)}</SelectField>
        <TextField label="Bénéficiaire" value={f.beneficiary} onChange={set('beneficiary')} />
        <TextField label="Montant" inputMode="numeric" value={f.amount} onChange={set('amount')} />
        <TextField className="span-2" label="Description" value={f.description} onChange={set('description')} />
        <SelectField label="Mode" value={f.method} onChange={set('method')}><option value="Especes">Espèces</option><option value="MobileMoney">Mobile Money</option><option value="Banque">Banque</option><option value="Cheque">Chèque</option></SelectField>
        <TextField label="Référence / pièce" value={f.reference} onChange={set('reference')} />
      </div>
      <div style={{ marginTop: 14 }}><ErrorAlert error={error} /></div>
    </Modal>
  );
}

// ---------- Caisse ----------
export function CashPage() {
  const [day, setDay] = useState(today());
  const [closing, setClosing] = useState(false);
  const { can } = useSession();
  const situation = useQuery({ queryKey: ['cash', day], queryFn: () => api.get<CashSituation>('/api/caisse/situation', { date: day }) });
  const closings = useQuery({ queryKey: ['cash-closings'], queryFn: () => api.get<CashClosing[]>('/api/caisse/arretes') });
  const s = situation.data;
  return (
    <>
      <PageHead title="Caisse" subtitle={s?.lastClosingDate ? `Dernier arrêté : ${date(s.lastClosingDate)}. Les opérations antérieures sont verrouillées.` : 'Aucun arrêté de caisse pour le moment.'}
        actions={can(P.cashClose) && <Button variant="primary" icon={<Lock />} onClick={() => setClosing(true)}>Arrêter la caisse</Button>} />
      <div className="toolbar"><TextField label="Situation au" type="date" value={day} max={today()} onChange={(e) => setDay(e.target.value)} /></div>
      {!s ? <Loading /> : (
        <div className="stack">
          <div className="grid cols-4">
            <Kpi icon={<Banknote size={15} />} label="Solde d'ouverture" value={money(s.openingBalance)} hint={`Depuis le ${date(s.periodStart)}`} />
            <Kpi label="Recettes en espèces" value={money(s.cashReceipts)} tone="green" />
            <Kpi label="Dépenses en espèces" value={money(s.cashExpenses)} tone="red" />
            <Kpi label="Espèces attendues en caisse" value={money(s.expectedCash)} tone="purple" hint={`Hors caisse (Mobile Money, banque) : ${money(s.otherReceipts)}`} />
          </div>
          <Card title="Mouvements de la période" pad={false}>
            {s.movements.length === 0 ? <Empty text="Aucun mouvement depuis le dernier arrêté." /> : (
              <div className="table-wrap" style={{ border: 'none', borderRadius: 0, maxHeight: 460 }}>
                <table className="data">
                  <thead><tr><th>Date</th><th>Type</th><th>N°</th><th>Libellé</th><th>Mode</th><th className="num">Entrée</th><th className="num">Sortie</th></tr></thead>
                  <tbody>{s.movements.map((m) => (
                    <tr key={m.number}><td>{date(m.date)}</td><td><Badge tone={m.type === 'Recette' ? 'green' : 'red'}>{m.type}</Badge></td><td className="tag-code">{m.number}</td><td>{m.label}</td><td>{m.method}</td>
                      <td className="num">{m.in ? money(m.in) : ''}</td><td className="num">{m.out ? money(m.out) : ''}</td></tr>))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
          <Card title="Arrêtés de caisse" pad={false}>
            {!closings.data || closings.data.length === 0 ? <Empty text="Aucun arrêté." /> : (
              <table className="data">
                <thead><tr><th>Date</th><th>Période</th><th className="num">Attendu</th><th className="num">Compté</th><th className="num">Écart</th><th>Observation</th><th>Par</th></tr></thead>
                <tbody>{closings.data.map((c) => (
                  <tr key={c.id}><td>{date(c.date)}</td><td className="small muted">du {date(c.periodStart)}</td><td className="num">{money(c.expectedCash)}</td><td className="num">{money(c.countedCash)}</td>
                    <td className="num" style={{ color: c.difference === 0 ? 'var(--green)' : 'var(--red)', fontWeight: 700 }}>{money(c.difference)}</td><td className="small">{c.note ?? '—'}</td><td className="muted">{c.closedBy}</td></tr>))}
                </tbody>
              </table>
            )}
          </Card>
        </div>
      )}
      {closing && s && <CloseDialog defaultDate={day} onClose={() => setClosing(false)} />}
    </>
  );
}

function CloseDialog({ defaultDate, onClose }: { defaultDate: string; onClose: () => void }) {
  const [day, setDay] = useState(defaultDate);
  const [counted, setCounted] = useState('');
  const [note, setNote] = useState('');
  const [error, setError] = useState<unknown>();
  const qc = useQueryClient();
  const toast = useToast();
  const situation = useQuery({ queryKey: ['cash', day], queryFn: () => api.get<CashSituation>('/api/caisse/situation', { date: day }) });
  const diff = (Number(counted) || 0) - (situation.data?.expectedCash ?? 0);
  return (
    <Modal eyebrow="Comptabilité" title="Arrêté de caisse" subtitle="Comptez les espèces présentes. Après l’arrêté, les opérations de la période ne peuvent plus être modifiées."
      onClose={onClose} footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" icon={<Lock />} disabled={counted === ''} onClick={async () => {
        try { await api.post('/api/caisse/arretes', { date: day, countedCash: Number(counted), note: note || null }); await qc.invalidateQueries({ queryKey: ['cash'] }); await qc.invalidateQueries({ queryKey: ['cash-closings'] }); toast('Caisse arrêtée.', 'success'); onClose(); } catch (e) { setError(e); }
      }}>Arrêter la caisse</Button></>}>
      <div className="stack">
        <div className="grid cols-2">
          <TextField label="Date de l'arrêté" type="date" value={day} max={today()} onChange={(e) => setDay(e.target.value)} />
          <div className="field"><span className="label">Espèces attendues</span><div style={{ fontSize: 22, fontWeight: 800, paddingTop: 4 }}>{money(situation.data?.expectedCash)}</div></div>
          <TextField label="Espèces comptées" inputMode="numeric" value={counted} onChange={(e) => setCounted(e.target.value)} autoFocus />
          <div className="field"><span className="label">Écart</span><div style={{ fontSize: 22, fontWeight: 800, paddingTop: 4, color: counted === '' ? undefined : diff === 0 ? 'var(--green)' : 'var(--red)' }}>{counted === '' ? '—' : money(diff)}</div></div>
          <TextField className="span-2" label="Observation (obligatoire en cas d'écart)" value={note} onChange={(e) => setNote(e.target.value)} />
        </div>
        <ErrorAlert error={error} />
      </div>
    </Modal>
  );
}
