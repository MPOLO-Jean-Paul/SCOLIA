import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { CheckCircle2, HandCoins, MessageCircle, Printer, RotateCcw, Search } from 'lucide-react';
import { api, ApiError, openDocument } from '../api/client';
import type { Account, Paged, PaymentPreview, Receipt, StudentListItem } from '../api/types';
import { date, money, newKey, today } from '../lib/format';
import { useSession } from '../lib/session';
import { Badge, Button, Card, Empty, ErrorAlert, Kpi, Loading, PageHead, Segmented, SectionTitle, TextField, statusTone, useDebounced, useToast } from '../components/ui';

const METHODS = [
  { value: 'Especes', label: 'Espèces' }, { value: 'MobileMoney', label: 'Mobile Money' }, { value: 'Banque', label: 'Banque' }, { value: 'Cheque', label: 'Chèque' },
];

export function CashierPage() {
  const [params, setParams] = useSearchParams();
  const enrollmentId = params.get('inscription') ?? '';
  const [search, setSearch] = useState('');
  const debounced = useDebounced(search, 180);
  const results = useQuery({
    queryKey: ['cashier-search', debounced],
    queryFn: () => api.get<Paged<StudentListItem>>('/api/apprenants', { q: debounced, taille: 12 }),
    enabled: debounced.trim().length >= 2,
  });
  const account = useQuery({ queryKey: ['account', enrollmentId], queryFn: () => api.get<Account>(`/api/inscriptions/${enrollmentId}/compte`), enabled: !!enrollmentId });
  const [receipt, setReceipt] = useState<Receipt | null>(null);
  const select = (id?: string) => { setReceipt(null); setParams(id ? { inscription: id } : {}); };

  return (
    <>
      <PageHead title="Encaisser" subtitle="Rechercher un apprenant, saisir le versement et imprimer le reçu validé à la caisse." />
      <div className="grid mobile-split-grid cashier-grid">
        <Card title="01 · Rechercher un apprenant">
          <div className="input-group"><Search /><input className="input" placeholder="Matricule ou nom (2 caractères minimum)" value={search} onChange={(e) => setSearch(e.target.value)} /></div>
          <div style={{ marginTop: 12 }}>
            {debounced.trim().length < 2 ? <p className="small muted">Saisissez au moins deux caractères. Astuce : F2 ouvre cet écran depuis n’importe où.</p>
              : !results.data ? <Loading rows={3} /> : results.data.items.length === 0 ? <Empty text="Aucun apprenant trouvé." /> : (
                <div className="stack" style={{ gap: 6 }}>
                  {results.data.items.map((s) => (
                    <button key={s.id} className="btn ghost" disabled={!s.enrollmentId} style={{ justifyContent: 'space-between', height: 'auto', padding: '10px 12px', borderColor: s.enrollmentId === enrollmentId ? 'var(--blue)' : undefined, background: s.enrollmentId === enrollmentId ? 'var(--blue-soft)' : undefined }}
                      onClick={() => select(s.enrollmentId)}>
                      <span style={{ textAlign: 'left' }}><span className="tag-code">{s.matricule}</span><br /><span style={{ fontWeight: 700 }}>{s.lastName} {s.middleName} {s.firstName}</span><br /><span className="small muted">{s.promotionCode} · {s.status}</span></span>
                      <span className="small" style={{ color: s.overdue > 0 ? 'var(--orange)' : 'var(--green)' }}>{s.overdue > 0 ? money(s.overdue) : 'À jour'}</span>
                    </button>
                  ))}
                </div>
              )}
          </div>
        </Card>
        <div>
          {!enrollmentId ? <Card><Empty text="Sélectionnez un apprenant pour enregistrer un paiement." /></Card>
            : account.error ? <ErrorAlert error={account.error} />
            : !account.data ? <Card><Loading rows={5} /></Card>
            : receipt ? <ReceiptDone receipt={receipt} account={account.data} onNew={() => setReceipt(null)} onOther={() => { select(undefined); setSearch(''); }} />
            : <PaymentForm key={enrollmentId} account={account.data} onDone={setReceipt} />}
        </div>
      </div>
    </>
  );
}

/** Référence de la transaction : date, heure et quatre signes au hasard, comme sur l'application de bureau. */
function newReference(now = new Date()) {
  const p = (n: number) => String(n).padStart(2, '0');
  const hex = Array.from(crypto.getRandomValues(new Uint8Array(2)), (b) => b.toString(16).padStart(2, '0')).join('').toUpperCase();
  return `TR${p(now.getFullYear() % 100)}${p(now.getMonth() + 1)}${p(now.getDate())}-${p(now.getHours())}${p(now.getMinutes())}-${hex}`;
}

function PaymentForm({ account, onDone }: { account: Account; onDone: (r: Receipt) => void }) {
  const { session } = useSession();
  const [method, setMethod] = useState('Especes');
  // Remplis par SCOLIA : la référence, la personne connectée (« Visé par ») et le téléphone du tuteur.
  const [reference] = useState(() => newReference());
  const payerPhone = account.guardianPhone ?? '';
  const [paidOn, setPaidOn] = useState(today());
  const [note, setNote] = useState('');
  const [selected, setSelected] = useState<string[]>([]);
  const [preview, setPreview] = useState<PaymentPreview | null>(null);
  const [previewError, setPreviewError] = useState<unknown>();
  const [busy, setBusy] = useState(false);
  const submitting = useRef(false);
  const [previewing, setPreviewing] = useState(false);
  const [error, setError] = useState<unknown>();
  const [idempotencyKey, setIdempotencyKey] = useState(newKey);
  const qc = useQueryClient();
  const toast = useToast();
  // Un paiement se fait par mois complet : seules les mensualités de scolarité se cochent, à leur montant dû.
  const months = account.lines.filter((l) => l.remaining > 0 && l.kind === 'Mensualite');
  const chosen = months.filter((l) => selected.includes(l.feeItemId));
  const total = chosen.reduce((sum, l) => sum + l.remaining, 0);
  const hasSelection = chosen.length > 0;
  const toggle = (id: string, on: boolean) => setSelected(on ? [...selected, id] : selected.filter((x) => x !== id));

  useEffect(() => {
    setIdempotencyKey(newKey());
    if (!hasSelection) { setPreview(null); setPreviewError(undefined); setPreviewing(false); return; }
    setPreviewing(true);
    let cancelled = false;
    api.post<PaymentPreview>('/api/paiements/apercu', { enrollmentId: account.enrollmentId, amount: total, method, feeItemIds: selected })
      .then((p) => { if (!cancelled) { setPreview(p); setPreviewError(undefined); } })
      .catch((e) => { if (!cancelled) { setPreview(null); setPreviewError(e); } })
      .finally(() => { if (!cancelled) setPreviewing(false); });
    return () => { cancelled = true; };
  }, [total, account.enrollmentId, selected, method, hasSelection]);

  const submit = async () => {
    if (submitting.current || previewing || !preview || !hasSelection) return;
    submitting.current = true;
    setBusy(true); setError(undefined);
    try {
      const receipt = await api.post<Receipt>('/api/paiements', {
        enrollmentId: account.enrollmentId, amount: total, date: paidOn, method, reference,
        payerPhone: payerPhone || null, note: note.trim() || 'OK', feeItemIds: selected, idempotencyKey,
      });
      await Promise.all([qc.invalidateQueries({ queryKey: ['account'] }), qc.invalidateQueries({ queryKey: ['dashboard'] }), qc.invalidateQueries({ queryKey: ['students'] })]);
      toast(`Reçu ${receipt.number} enregistré.`, 'success');
      onDone(receipt);
    } catch (e) { setError(e); } finally { submitting.current = false; setBusy(false); }
  };

  return (
    <div className="stack payment-form">
      <div className="card card-pad">
        <div className="row" style={{ justifyContent: 'space-between' }}>
          <div><div className="tag-code">{account.matricule}</div><h2 style={{ fontSize: 20 }}>{account.fullName}</h2><div className="small muted">{account.promotionCode} · {account.academicYear} · <Badge tone={statusTone(account.enrollmentStatus)}>{account.enrollmentStatus}</Badge></div></div>
          {account.whatsAppLink && <a className="btn small" href={account.whatsAppLink} target="_blank" rel="noreferrer"><MessageCircle />Rappel</a>}
        </div>
        <div className="grid cols-3" style={{ marginTop: 14 }}>
          <Kpi label="Reste exigible" value={money(account.overdue)} tone={account.overdue > 0 ? 'red' : 'green'} hint={account.overdueCount ? `${account.overdueCount} échéance(s)` : 'À jour'} />
          <Kpi label="Prochain mois dû" value={months[0] ? money(months[0].remaining) : '—'} hint={months[0] ? `${months[0].label} · ${date(months[0].dueDate)}` : 'Année soldée'} />
          <Kpi label="Reste de l'année" value={money(account.balance)} tone="orange" hint={`Déjà payé : ${money(account.totalPaid)}`} />
        </div>
      </div>
      <div className="card card-pad">
        <SectionTitle num="02">Mois payés et versement</SectionTitle>
        <div className="field span-2">
          <span className="label">Mois payés</span>
          <p className="small muted" style={{ margin: '2px 0 8px' }}>Chaque mois se règle en entier, à son montant. Les paiements partiels ne sont pas enregistrés.</p>
          {months.length === 0 ? <Empty text="Aucun mois à payer pour cet apprenant." /> : (
            <div className="row" style={{ flexWrap: 'wrap', gap: 8 }}>
              {months.map((l) => (
                <label key={l.feeItemId} className="check badge payment-month" style={{ padding: '6px 10px', background: selected.includes(l.feeItemId) ? 'var(--blue-soft)' : undefined }}>
                  <input type="checkbox" checked={selected.includes(l.feeItemId)} disabled={busy} onChange={(e) => toggle(l.feeItemId, e.target.checked)} />
                  {l.label} · <strong>{money(l.remaining)}</strong>{l.isOverdue && <span style={{ color: 'var(--red)', marginLeft: 6 }}>en retard</span>}
                </label>
              ))}
            </div>
          )}
          <div className="small" style={{ marginTop: 8 }}>{hasSelection ? `${chosen.length} mois · total ${money(total)}` : 'Aucun mois choisi'}</div>
        </div>
        <div className="grid cols-2" style={{ marginTop: 14 }}>
          <div className="field span-2"><span className="label">Mode de paiement</span><Segmented value={method} onChange={(value) => { if (!busy) setMethod(value); }} items={METHODS} /></div>
          <TextField label="Date du reçu" type="date" value={paidOn} max={today()} onChange={(e) => setPaidOn(e.target.value)} />
          <TextField label="Référence de la transaction" value={reference} readOnly help="Donnée par SCOLIA" />
          <TextField label="Visé par" value={session?.displayName ?? ''} readOnly help="La personne connectée" />
          <TextField label="Téléphone du payeur" value={payerPhone || 'Aucun numéro au dossier'} readOnly help="Repris du dossier de l’apprenant" />
          <TextField className="span-2" label="Observation (facultative)" value={note} onChange={(e) => setNote(e.target.value)} placeholder="Laissée vide, le reçu portera « OK »" />
        </div>
      </div>
      <div className="card card-pad">
        <SectionTitle num="03">Répartition du paiement</SectionTitle>
        {!hasSelection ? <p className="muted small">Cochez les mois payés : la répartition s’affiche avant validation.</p>
          : previewError ? <ErrorAlert error={previewError} />
          : !preview ? <Loading rows={2} /> : (
            <div className="payment-allocation">
              <table className="data">
                <thead><tr><th>Rubrique</th><th className="num">Affecté</th><th className="num">Reste après</th></tr></thead>
                <tbody>{preview.allocations.map((a) => <tr key={a.feeItemId}><td>{a.label}</td><td className="num" style={{ fontWeight: 700 }}>{money(a.amount)}</td><td className="num">{a.remainingAfter === 0 ? <Badge tone="green">Soldé</Badge> : money(a.remainingAfter)}</td></tr>)}</tbody>
                <tfoot><tr><td>Reste de l’année après ce reçu</td><td className="num">{money(total)}</td><td className="num">{money(preview.balanceAfter)}</td></tr></tfoot>
              </table>
            </div>
          )}
        <div style={{ marginTop: 14 }}><ErrorAlert error={error instanceof ApiError && error.status === 409 ? new Error(error.message) : error} /></div>
        <div className="row" style={{ justifyContent: 'flex-end', marginTop: 14 }}>
          <Button variant="primary" className="payment-submit" icon={<HandCoins />} loading={busy} disabled={!preview || !hasSelection || previewing} onClick={submit}>Valider le paiement · {money(total)}</Button>
        </div>
      </div>
    </div>
  );
}

function ReceiptDone({ receipt, account, onNew, onOther }: { receipt: Receipt; account: Account; onNew: () => void; onOther: () => void }) {
  const navigate = useNavigate();
  const { session } = useSession();
  const phone = account.guardianPhone?.replace(/\D/g, '');
  const text = encodeURIComponent(`Reçu ${receipt.number} du ${date(receipt.date)} : ${money(receipt.amount)} versés pour ${receipt.studentName} (${receipt.matricule}). Reste de l'année : ${money(account.balance)}. Code de vérification : ${receipt.verificationCode}.`);
  const country = session?.phoneCountryCode ?? '243';
  const wa = phone ? `https://wa.me/${phone.startsWith('0') ? country + phone.slice(1) : phone}?text=${text}` : undefined;
  return (
    <div className="card card-pad" style={{ textAlign: 'center', padding: 36 }}>
      <CheckCircle2 size={54} color="var(--green)" />
      <h2 style={{ marginTop: 10 }}>Paiement enregistré</h2>
      <p className="muted">Reçu <span className="tag-code">{receipt.number}</span> · {money(receipt.amount)} · {receipt.method}</p>
      <div className="stack" style={{ maxWidth: 420, margin: '18px auto', textAlign: 'left', gap: 6 }}>
        {receipt.allocations.map((a) => <div key={a.feeItemId} className="row" style={{ justifyContent: 'space-between' }}><span>{a.label}</span><strong>{money(a.amount)}</strong></div>)}
        <div className="divider" style={{ margin: '8px 0' }} />
        <div className="row" style={{ justifyContent: 'space-between' }}><span>Reste de l’année</span><strong>{money(account.balance)}</strong></div>
      </div>
      <div className="row" style={{ justifyContent: 'center' }}>
        <Button variant="primary" icon={<Printer />} onClick={() => openDocument(`/api/paiements/${receipt.id}/recu.pdf`)}>Imprimer le reçu</Button>
        {wa && <a className="btn" href={wa} target="_blank" rel="noreferrer"><MessageCircle />Envoyer par WhatsApp</a>}
        <Button icon={<RotateCcw />} onClick={onNew}>Autre paiement pour cet apprenant</Button>
        <Button variant="ghost" onClick={onOther}>Autre apprenant</Button>
        <Button variant="ghost" onClick={() => navigate(`/paiements/recus/${receipt.id}`)}>Détail du reçu</Button>
      </div>
    </div>
  );
}
