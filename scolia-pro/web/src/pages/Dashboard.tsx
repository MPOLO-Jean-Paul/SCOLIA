import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Banknote, HandCoins, TrendingUp, Users, AlertTriangle, CalendarClock } from 'lucide-react';
import { api } from '../api/client';
import type { Dashboard, Promotion } from '../api/types';
import { money, pct, date, int } from '../lib/format';
import { BarChart, HBars, Ring } from '../components/charts';
import { Badge, Button, Card, Empty, ErrorAlert, Kpi, Loading, PageHead, SelectField, statusTone } from '../components/ui';
import { P, useSession } from '../lib/session';

export function DashboardPage() {
  const [promotionId, setPromotionId] = useState('');
  const { can } = useSession();
  const navigate = useNavigate();
  const promotions = useQuery({ queryKey: ['promotions'], queryFn: () => api.get<Promotion[]>('/api/etablissement/promotions') });
  const q = useQuery({ queryKey: ['dashboard', promotionId], queryFn: () => api.get<Dashboard>('/api/rapports/tableau-de-bord', { promotionId }) });
  const d = q.data;
  return (
    <>
      <PageHead title="Tableau de bord" eyebrow={todayLabel()} subtitle={d ? `Année scolaire ${d.academicYear} · situation en temps réel` : 'Chargement…'}
        actions={<>
          {can(P.paymentsCreate) && <Button variant="primary" icon={<HandCoins />} onClick={() => navigate('/encaisser')}>Encaisser (F2)</Button>}
          <Button icon={<AlertTriangle />} onClick={() => navigate('/recouvrement')}>Retards de paiement</Button>
        </>} />
      <div className="toolbar">
        <SelectField label="Promotion" value={promotionId} onChange={(e) => setPromotionId(e.target.value)}>
          <option value="">Toutes les promotions</option>
          {promotions.data?.map((p) => <option key={p.id} value={p.id}>{p.code} — {p.name}</option>)}
        </SelectField>
      </div>
      <ErrorAlert error={q.error} />
      {!d ? <Loading rows={6} /> : (
        <div className="stack">
          <div className="grid cols-4">
            <Kpi icon={<Users size={15} />} label="Apprenants actifs" value={int(d.activeStudents)} hint={`${d.genderFemale} filles · ${d.genderMale} garçons`} />
            <Kpi icon={<Banknote size={15} />} label="Recettes de l'année" value={money(d.collectedTotal)} hint={`Ce mois : ${money(d.collectedThisMonth)} · aujourd’hui : ${money(d.collectedToday)}`} tone="green" />
            <Kpi icon={<AlertTriangle size={15} />} label="Reste exigible" value={money(d.overdue)} hint={`${d.studentsLate} apprenant(s) en retard`} tone="orange" />
            <Kpi icon={<CalendarClock size={15} />} label="Reste à percevoir (année)" value={money(d.outstanding)} hint={`Dépenses du mois : ${money(d.expensesThisMonth)}`} tone="purple" />
          </div>
          <div className="grid" style={{ gridTemplateColumns: 'minmax(0,2fr) minmax(280px,1fr)' }}>
            <Card title="Recettes par mois" actions={<div className="legend"><span><i style={{ background: 'var(--blue)' }} />Encaissé</span><span><i style={{ background: 'var(--table-head)' }} />Attendu</span></div>}>
              <BarChart data={d.monthlyCollections} />
            </Card>
            <Card title="Recouvrement">
              <div className="row" style={{ justifyContent: 'center' }}><Ring value={d.recoveryRate} label="recouvré" /></div>
              <div className="stack" style={{ gap: 6, marginTop: 10 }}>
                <div className="row" style={{ justifyContent: 'space-between' }}><span className="muted small">Attendu à ce jour</span><strong className="mono">{money(d.expectedToDate)}</strong></div>
                <div className="row" style={{ justifyContent: 'space-between' }}><span className="muted small">Apprenants à jour</span><strong>{d.studentsUpToDate} / {d.activeStudents}</strong></div>
                <div className="row" style={{ justifyContent: 'space-between' }}><span className="muted small">Taux d’apprenants à jour</span><strong>{pct(d.activeStudents ? Math.round((d.studentsUpToDate * 1000) / d.activeStudents) / 10 : 100)}</strong></div>
              </div>
            </Card>
          </div>
          <div className="grid" style={{ gridTemplateColumns: 'minmax(0,2fr) minmax(280px,1fr)' }}>
            <Card title="Situation par promotion" pad={false}>
              {d.promotions.length === 0 ? <Empty text="Aucune promotion configurée." /> : (
                <div className="table-wrap" style={{ border: 'none', borderRadius: 0 }}>
                  <table className="data">
                    <thead><tr><th>Promotion</th><th className="num">Effectif</th><th className="num">Encaissé</th><th className="num">Exigible impayé</th><th style={{ width: 170 }}>Recouvrement</th></tr></thead>
                    <tbody>
                      {d.promotions.map((p) => (
                        <tr key={p.id} className="clickable" onClick={() => navigate(`/apprenants?promotionId=${p.id}`)}>
                          <td><span className="tag-code">{p.code}</span> <span className="muted small">{p.name}</span></td>
                          <td className="num">{p.students}</td>
                          <td className="num">{money(p.collected)}</td>
                          <td className="num" style={{ color: p.overdue > 0 ? 'var(--orange)' : undefined }}>{money(p.overdue)}</td>
                          <td><div className="row" style={{ flexWrap: 'nowrap' }}><div className={`progress ${p.recoveryRate >= 80 ? 'green' : p.recoveryRate >= 60 ? 'orange' : ''}`} style={{ flex: 1 }}><span style={{ width: `${p.recoveryRate}%` }} /></div><span className="small mono">{pct(p.recoveryRate)}</span></div></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
            <Card title="Ancienneté des retards" actions={<TrendingUp size={16} color="var(--muted)" />}>
              <HBars data={d.aging} />
              <p className="small muted" style={{ marginBottom: 0 }}>Balance âgée : montant exigible selon l’échéance impayée la plus ancienne.</p>
            </Card>
          </div>
          <Card title="Derniers encaissements" actions={<Link to="/paiements" className="small">Voir le journal</Link>} pad={false}>
            {d.latestReceipts.length === 0 ? <Empty text="Aucun reçu pour cette année." /> : (
              <div className="table-wrap" style={{ border: 'none', borderRadius: 0 }}>
                <table className="data">
                  <thead><tr><th>N° reçu</th><th>Date</th><th>Apprenant</th><th>Mode</th><th>Caissier</th><th>Statut</th><th className="num">Montant</th></tr></thead>
                  <tbody>{d.latestReceipts.map((r) => (
                    <tr key={r.id} className="clickable" onClick={() => navigate(`/paiements/recus/${r.id}`)}>
                      <td className="tag-code">{r.number}</td><td>{date(r.date)}</td><td>{r.allocations}</td><td>{r.method}</td><td className="muted">{r.cashier}</td>
                      <td><Badge tone={statusTone(r.status)}>{r.status}</Badge></td><td className="num">{money(r.amount)}</td>
                    </tr>))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        </div>
      )}
    </>
  );
}

/** Date du jour en français, initiale en majuscule : « Samedi 24 octobre 2026 ». */
function todayLabel(): string {
  const text = new Date().toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  return text.charAt(0).toUpperCase() + text.slice(1);
}
