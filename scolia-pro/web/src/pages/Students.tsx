import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { FileDown, RotateCcw, Search, UserPlus } from 'lucide-react';
import { api, openDocument } from '../api/client';
import type { Paged, Promotion, StudentListItem } from '../api/types';
import { money } from '../lib/format';
import { Badge, Button, Empty, ErrorAlert, Loading, PageHead, Pager, SelectField, statusTone, useDebounced, useToast } from '../components/ui';
import { NewStudentDialog } from './StudentForm';
import { P, useSession } from '../lib/session';

export function StudentsPage() {
  const [params, setParams] = useSearchParams();
  const [search, setSearch] = useState(params.get('q') ?? '');
  const [promotionId, setPromotionId] = useState(params.get('promotionId') ?? '');
  const [status, setStatus] = useState(params.get('statut') ?? 'Actif');
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(params.get('nouveau') === '1');
  // Depuis le ruban (Secrétariat > Nouvelle inscription) : la page est déjà ouverte, il faut réagir au paramètre.
  useEffect(() => { if (params.get('nouveau') === '1') setCreating(true); }, [params]);
  const debounced = useDebounced(search, 200);
  const navigate = useNavigate();
  const toast = useToast();
  const { can } = useSession();
  const promotions = useQuery({ queryKey: ['promotions'], queryFn: () => api.get<Promotion[]>('/api/etablissement/promotions') });
  const list = useQuery({
    queryKey: ['students', debounced, promotionId, status, page],
    queryFn: () => api.get<Paged<StudentListItem>>('/api/apprenants', { q: debounced, promotionId, statut: status, page, taille: 50 }),
    placeholderData: (previous) => previous,
  });
  useEffect(() => { setPage(1); }, [debounced, promotionId, status]);
  useEffect(() => {
    const next = new URLSearchParams();
    if (debounced) next.set('q', debounced);
    if (promotionId) next.set('promotionId', promotionId);
    if (status !== 'Actif') next.set('statut', status);
    setParams(next, { replace: true });
  }, [debounced, promotionId, status, setParams]);

  return (
    <>
      <PageHead title="Apprenants" subtitle="Retrouver un dossier, consulter sa situation financière et sa scolarité."
        actions={<>
          {promotionId && <Button icon={<FileDown />} onClick={() => openDocument(`/api/rapports/liste-classe/${promotionId}`)}>Liste nominative</Button>}
          {can(P.studentsWrite) && <Button variant="primary" icon={<UserPlus />} onClick={() => setCreating(true)}>Nouvelle inscription</Button>}
        </>} />
      <div className="toolbar">
        <div className="field grow">
          <label htmlFor="q">Recherche</label>
          <div className="input-group"><Search /><input id="q" className="input" placeholder="Nom, post-nom, prénom, matricule ou téléphone…" value={search} onChange={(e) => setSearch(e.target.value)} autoFocus /></div>
        </div>
        <SelectField label="Promotion" value={promotionId} onChange={(e) => setPromotionId(e.target.value)}>
          <option value="">Toutes</option>
          {promotions.data?.map((p) => <option key={p.id} value={p.id}>{p.code} — {p.name}</option>)}
        </SelectField>
        <SelectField label="Statut" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Tous</option>{['Actif', 'Suspendu', 'Sorti', 'Diplômé', 'Transféré'].map((s) => <option key={s}>{s}</option>)}
        </SelectField>
        {(search || promotionId || status !== 'Actif') && <Button icon={<RotateCcw />} onClick={() => { setSearch(''); setPromotionId(''); setStatus('Actif'); setPage(1); }}>Effacer les filtres</Button>}
      </div>
      <ErrorAlert error={list.error} />
      <div className="table-wrap">
        {!list.data ? (list.isError ? <Empty text="Impossible de charger la liste. Vérifiez la connexion, puis réessayez."><Button onClick={() => { void list.refetch(); }}>Réessayer</Button></Empty> : <Loading />)
        : list.data.items.length === 0 ? <Empty text={search || promotionId || status !== 'Actif' ? "Aucun apprenant ne correspond à ces critères." : "Aucun apprenant actif n'est encore inscrit."}>
          {(search || promotionId || status !== 'Actif') && <Button icon={<RotateCcw />} onClick={() => { setSearch(''); setPromotionId(''); setStatus('Actif'); }}>Réinitialiser la recherche</Button>}
          {!search && !promotionId && status === 'Actif' && can(P.studentsWrite) && <Button variant="primary" icon={<UserPlus />} onClick={() => setCreating(true)}>Inscrire le premier apprenant</Button>}
        </Empty> : (
          <table className="data">
            <thead><tr><th>Matricule</th><th>Nom</th><th>Post-nom</th><th>Prénom</th><th>Sexe</th><th>Promotion</th><th>Contact parent</th><th>Statut</th><th className="num">Reste exigible</th><th className="num">Reste année</th></tr></thead>
            <tbody>
              {list.data.items.map((s) => (
                <tr key={s.id} className="clickable" onClick={() => navigate(`/apprenants/${s.id}`)}>
                  <td><Link className="tag-code" to={`/apprenants/${s.id}`} onClick={(e) => e.stopPropagation()}>{s.matricule}</Link></td><td style={{ fontWeight: 700 }}>{s.lastName}</td><td>{s.middleName}</td><td>{s.firstName}</td><td>{s.gender}</td>
                  <td>{s.promotionCode}</td><td className="muted">{s.guardianPhone ?? '—'}</td><td><Badge tone={statusTone(s.status)}>{s.status}</Badge></td>
                  <td className="num" style={{ color: s.overdue > 0 ? 'var(--orange)' : 'var(--green)', fontWeight: 700 }}>{s.overdue > 0 ? money(s.overdue) : 'À jour'}</td>
                  <td className="num">{money(s.balance)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {list.data && list.data.total > 0 && <Pager page={list.data.page} pageSize={list.data.pageSize} total={list.data.total} onPage={setPage} />}
      </div>
      {creating && <NewStudentDialog defaultPromotionId={promotionId || undefined} onClose={() => setCreating(false)}
        onCreated={(s) => { setCreating(false); toast(`${s.matricule} — ${s.lastName} ${s.firstName} inscrit(e).`, 'success'); navigate(`/apprenants/${s.id}`); }} />}
    </>
  );
}
