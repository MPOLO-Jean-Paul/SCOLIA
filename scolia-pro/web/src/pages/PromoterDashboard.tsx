import { useQuery } from '@tanstack/react-query';
import { RefreshCw, School, TrendingUp, Wallet, Users, CircleAlert, CalendarDays } from 'lucide-react';
import { api } from '../api/client';
import { date, int, money } from '../lib/format';
import { Button, Card, Empty, ErrorAlert, Kpi, Loading, PageHead } from '../components/ui';

interface PeriodSummary {
  periodId: string; label: string; collectedOnDates: number; dueNet: number;
  paidAgainstDue: number; remainingDue: number;
}
interface PromotionSummary {
  id: string; code: string; name: string; activeStudents: number;
  studentsWithBalance: number; studentsOverdue: number;
  collectedToday: number; collectedThisMonth: number; collectedTotal: number;
  outstanding: number; overdue: number; periods: PeriodSummary[];
}
interface PromoterSummary {
  academicYear: string; asOf: string; activeStudents: number;
  studentsWithBalance: number; studentsOverdue: number;
  collectedToday: number; collectedThisMonth: number; collectedTotal: number;
  outstanding: number; overdue: number; promotions: PromotionSummary[];
}

/** Aucune donnée individuelle : le Promoteur consulte exclusivement des agrégats. */
export function PromoterDashboardPage() {
  const q = useQuery({
    queryKey: ['pilotage', 'synthese'],
    queryFn: () => api.get<PromoterSummary>('/api/pilotage/synthese'),
    refetchInterval: 60_000,
  });
  const data = q.data;
  return (
    <div className="stack">
      <PageHead title="Vue d'ensemble de l'établissement"
        subtitle={data ? `Année ${data.academicYear} · situation au ${date(data.asOf)} · consultation uniquement` : 'Situation générale des apprenants et des finances, sans dossiers personnels.'}
        actions={<Button icon={<RefreshCw />} loading={q.isFetching} onClick={() => { void q.refetch(); }}>Actualiser les chiffres</Button>} />
      <ErrorAlert error={q.error} />
      {!data ? <Loading rows={5} /> : (
        <>
          <div className="grid cols-3">
            <Kpi icon={<Users size={16} />} label="Apprenants inscrits" value={int(data.activeStudents)} hint="Effectif actif de l'année" />
            <Kpi icon={<CircleAlert size={16} />} label="Dossiers avec un solde" value={int(data.studentsWithBalance)}
              hint={`${int(data.studentsOverdue)} ont des échéances en retard`} tone="orange" />
            <Kpi icon={<Wallet size={16} />} label="Reste à percevoir" value={money(data.outstanding)}
              hint={`Dont ${money(data.overdue)} déjà exigibles`} tone="purple" />
            <Kpi icon={<CalendarDays size={16} />} label="Encaissé aujourd'hui" value={money(data.collectedToday)} />
            <Kpi icon={<TrendingUp size={16} />} label="Encaissé ce mois" value={money(data.collectedThisMonth)} />
            <Kpi icon={<School size={16} />} label="Encaissé cette année" value={money(data.collectedTotal)} tone="green" />
          </div>
          <Card title="Situation par promotion">
            {data.promotions.length === 0 ? <Empty text="Aucune promotion pour l'année en cours." /> : (
              <div className="stack" style={{ gap: 10 }}>
                {data.promotions.map(p => (
                  <details key={p.id} className="card card-pad">
                    <summary style={{ cursor: 'pointer', fontWeight: 650 }}>
                      {p.code} — {p.name} · {int(p.activeStudents)} apprenants · reste {money(p.outstanding)}
                    </summary>
                    <p className="small muted" style={{ marginTop: 12 }}>
                      {int(p.studentsWithBalance)} dossiers à solde · {int(p.studentsOverdue)} en retard
                    </p>
                    <div className="grid cols-3" style={{ marginTop: 8 }}>
                      <Kpi label="Aujourd'hui" value={money(p.collectedToday)} />
                      <Kpi label="Ce mois" value={money(p.collectedThisMonth)} />
                      <Kpi label="Depuis la rentrée" value={money(p.collectedTotal)} />
                    </div>
                    <p className="small muted" style={{ marginTop: 14 }}>
                      Par période : « encaissé » indique la date réelle de réception, tandis que « restant dû » concerne la mensualité prévue.
                    </p>
                    <div className="table-wrap">
                      <table className="data">
                        <thead><tr><th>Période</th><th className="num">Encaissé</th><th className="num">Dû</th><th className="num">Déjà affecté</th><th className="num">Reste</th></tr></thead>
                        <tbody>{p.periods.map(period => (
                          <tr key={period.periodId}><td>{period.label}</td><td className="num">{money(period.collectedOnDates)}</td>
                            <td className="num">{money(period.dueNet)}</td><td className="num">{money(period.paidAgainstDue)}</td>
                            <td className="num">{money(period.remainingDue)}</td></tr>
                        ))}</tbody>
                      </table>
                    </div>
                  </details>
                ))}
              </div>
            )}
          </Card>
          <p className="small muted">Cette page est une consultation : aucune opération financière ni fiche d'apprenant n'est accessible depuis cet espace.</p>
        </>
      )}
    </div>
  );
}
