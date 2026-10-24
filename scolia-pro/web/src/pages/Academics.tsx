import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { CheckCheck, FileText, Plus, Save } from 'lucide-react';
import { useActionParam } from '../lib/commands';
import { api, openDocument } from '../api/client';
import type { Assessment, AttendanceRow, AttendanceSummary, Course, GradeRow, Incident, Paged, Promotion, Results, StudentListItem } from '../api/types';
import { date, num, today } from '../lib/format';
import { Badge, Button, Card, Empty, ErrorAlert, Loading, Modal, PageHead, Segmented, SelectField, Tabs, TextArea, TextField, useToast, errorText } from '../components/ui';
import { P, useSession } from '../lib/session';

function PromotionPicker({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const promotions = useQuery({ queryKey: ['promotions'], queryFn: () => api.get<Promotion[]>('/api/etablissement/promotions') });
  useEffect(() => { if (!value && promotions.data?.[0]) onChange(promotions.data[0].id); }, [promotions.data, value, onChange]);
  return (
    <SelectField label="Promotion" value={value} onChange={(e) => onChange(e.target.value)}>
      {promotions.data?.map((p) => <option key={p.id} value={p.id}>{p.code} — {p.name}</option>)}
    </SelectField>
  );
}

// ---------- Cours et notes ----------
export function GradesPage() {
  const [promotionId, setPromotionId] = useState('');
  const [tab, setTab] = useState<'notes' | 'resultats'>('notes');
  const [params, setParams] = useSearchParams();
  const request = params.get('action');
  const handled = () => setParams((current) => { const next = new URLSearchParams(current); next.delete('action'); return next; }, { replace: true });
  return (
    <>
      <PageHead title="Cours et notes" subtitle="Cours par promotion, évaluations, saisie des notes, moyennes pondérées, classement et bulletins." />
      <div className="toolbar"><PromotionPicker value={promotionId} onChange={setPromotionId} /></div>
      <Tabs value={tab} onChange={setTab} items={[{ value: 'notes', label: 'Cours et évaluations' }, { value: 'resultats', label: 'Résultats et bulletins' }]} />
      {promotionId && (tab === 'notes' ? <CoursesPanel promotionId={promotionId} request={request} onHandled={handled} /> : <ResultsPanel promotionId={promotionId} />)}
    </>
  );
}

function CoursesPanel({ promotionId, request, onHandled }: { promotionId: string; request?: string | null; onHandled: () => void }) {
  const { can } = useSession();
  const qc = useQueryClient();
  const courses = useQuery({ queryKey: ['courses', promotionId], queryFn: () => api.get<Course[]>('/api/scolarite/cours', { promotionId }) });
  const [courseId, setCourseId] = useState('');
  const [editingCourse, setEditingCourse] = useState<Course | 'new' | null>(null);
  const [newAssessment, setNewAssessment] = useState(false);
  const toastNotice = useToast();
  // Demandes venues du ruban : nouveau cours, nouvelle évaluation.
  useEffect(() => {
    if (request === 'cours') { setEditingCourse('new'); onHandled(); }
    else if (request === 'evaluation') {
      if (courseId) setNewAssessment(true); else toastNotice('Choisissez d’abord un cours pour y ajouter l’évaluation.', 'error');
      onHandled();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [request]);
  const [assessmentId, setAssessmentId] = useState('');
  useEffect(() => { setCourseId(''); setAssessmentId(''); }, [promotionId]);
  useEffect(() => { if (!courseId && courses.data?.[0]) setCourseId(courses.data[0].id); }, [courses.data, courseId]);
  const assessments = useQuery({ queryKey: ['assessments', courseId], queryFn: () => api.get<Assessment[]>(`/api/scolarite/cours/${courseId}/evaluations`), enabled: !!courseId });
  useEffect(() => { setAssessmentId(assessments.data?.[0]?.id ?? ''); }, [assessments.data]);
  return (
    <div className="grid" style={{ gridTemplateColumns: 'minmax(260px, 1fr) minmax(0, 2.2fr)', alignItems: 'start' }}>
      <Card title="Cours" pad={false} actions={can(P.academicsWrite) && <Button size="small" icon={<Plus />} onClick={() => setEditingCourse('new')}>Cours</Button>}>
        {!courses.data ? <Loading /> : courses.data.length === 0 ? <Empty text="Aucun cours dans cette promotion." /> : (
          <table className="data"><tbody>{courses.data.map((c) => (
            <tr key={c.id} className="clickable" onClick={() => setCourseId(c.id)} style={{ outline: c.id === courseId ? '2px solid var(--blue)' : undefined, outlineOffset: -2 }}>
              <td><div style={{ fontWeight: 700 }}>{c.name}</div><div className="small muted">{c.code} · coef. {num(c.coefficient)} · {c.hours} h{c.teacherName ? ` · ${c.teacherName}` : ''}</div></td>
              <td className="right">{can(P.academicsWrite) && <Button size="small" variant="ghost" onClick={(e) => { e.stopPropagation(); setEditingCourse(c); }}>Modifier</Button>}</td>
            </tr>))}</tbody></table>
        )}
      </Card>
      <div className="stack">
        {courseId && (
          <Card title="Évaluations" actions={can(P.academicsWrite) && <Button size="small" icon={<Plus />} onClick={() => setNewAssessment(true)}>Évaluation</Button>}>
            {!assessments.data ? <Loading rows={2} /> : assessments.data.length === 0 ? <p className="muted">Aucune évaluation pour ce cours.</p> : (
              <Segmented value={assessmentId} onChange={setAssessmentId} items={assessments.data.map((a) => ({ value: a.id, label: `${a.title} · P${a.term} · /${num(a.maxScore)}` }))} />
            )}
          </Card>
        )}
        {assessmentId && <GradeSheet assessmentId={assessmentId} />}
      </div>
      {editingCourse && <CourseDialog promotionId={promotionId} course={editingCourse === 'new' ? undefined : editingCourse} onClose={() => setEditingCourse(null)} onSaved={() => qc.invalidateQueries({ queryKey: ['courses', promotionId] })} />}
      {newAssessment && <AssessmentDialog courseId={courseId} onClose={() => setNewAssessment(false)} onSaved={() => qc.invalidateQueries({ queryKey: ['assessments', courseId] })} />}
    </div>
  );
}

function GradeSheet({ assessmentId }: { assessmentId: string }) {
  const sheet = useQuery({ queryKey: ['grades', assessmentId], queryFn: () => api.get<{ assessment: Assessment; rows: GradeRow[] }>(`/api/scolarite/evaluations/${assessmentId}/notes`) });
  const [rows, setRows] = useState<GradeRow[]>([]);
  const [busy, setBusy] = useState(false);
  const toast = useToast();
  const { can } = useSession();
  useEffect(() => { if (sheet.data) setRows(sheet.data.rows); }, [sheet.data]);
  if (!sheet.data) return <Loading />;
  const max = sheet.data.assessment.maxScore;
  const graded = rows.filter((r) => r.score !== null && r.score !== undefined);
  const average = graded.length ? graded.reduce((s, r) => s + (r.score ?? 0), 0) / graded.length : null;
  const save = async () => {
    setBusy(true);
    try { const r = await api.put<{ modifiees: number }>(`/api/scolarite/evaluations/${assessmentId}/notes`, rows.map((x) => ({ enrollmentId: x.enrollmentId, score: x.score ?? null, comment: x.comment || null }))); toast(`${r.modifiees} note(s) enregistrée(s).`, 'success'); void sheet.refetch(); }
    catch (e) { toast(errorText(e), 'error'); } finally { setBusy(false); }
  };
  return (
    <Card title={`Notes · ${sheet.data.assessment.title} (sur ${num(max)})`} pad={false} actions={<>
      <span className="small muted">{graded.length}/{rows.length} notée(s) · moyenne {num(average)}</span>
      {can(P.academicsWrite) && <Button variant="primary" size="small" icon={<Save />} loading={busy} onClick={save}>Enregistrer</Button>}
    </>}>
      <table className="data">
        <thead><tr><th>Matricule</th><th>Apprenant</th><th style={{ width: 120 }}>Note</th><th>Appréciation</th></tr></thead>
        <tbody>{rows.map((r, i) => (
          <tr key={r.enrollmentId}>
            <td className="tag-code">{r.matricule}</td><td>{r.fullName}</td>
            <td><input className={`input grade-input ${r.score !== null && r.score !== undefined && (r.score < 0 || r.score > max) ? 'invalid' : ''}`} inputMode="decimal" value={r.score ?? ''} placeholder="Absent"
              disabled={!can(P.academicsWrite)}
              onKeyDown={(e) => { if (e.key === 'Enter' || e.key === 'ArrowDown') { e.preventDefault(); (document.querySelectorAll<HTMLInputElement>('.grade-input')[i + 1])?.focus(); } }}
              onChange={(e) => { const v = e.target.value.replace(',', '.'); const next = [...rows]; next[i] = { ...r, score: v === '' ? null : Number(v) }; setRows(next); }} /></td>
            <td><input className="input" value={r.comment ?? ''} disabled={!can(P.academicsWrite)} onChange={(e) => { const next = [...rows]; next[i] = { ...r, comment: e.target.value }; setRows(next); }} /></td>
          </tr>))}
        </tbody>
      </table>
    </Card>
  );
}

function CourseDialog({ promotionId, course, onClose, onSaved }: { promotionId: string; course?: Course; onClose: () => void; onSaved: () => void }) {
  const teachers = useQuery({ queryKey: ['teachers'], queryFn: () => api.get<{ id: string; nom: string; function: string }[]>('/api/enseignants') });
  const [f, setF] = useState({ code: course?.code ?? '', name: course?.name ?? '', coefficient: String(course?.coefficient ?? 1), hours: String(course?.hours ?? 30), teacherId: course?.teacherId ?? '', isActive: course?.isActive ?? true });
  const [error, setError] = useState<unknown>();
  return (
    <Modal title={course ? 'Modifier le cours' : 'Nouveau cours'} onClose={onClose} footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" onClick={async () => {
      try {
        const body = { promotionId, code: f.code, name: f.name, coefficient: Number(f.coefficient), hours: Number(f.hours), teacherId: f.teacherId || null, isActive: f.isActive };
        if (course) await api.put(`/api/scolarite/cours/${course.id}`, body); else await api.post('/api/scolarite/cours', body);
        onSaved(); onClose();
      } catch (e) { setError(e); }
    }}>Enregistrer</Button></>}>
      <div className="grid cols-2">
        <TextField label="Code" value={f.code} onChange={(e) => setF({ ...f, code: e.target.value })} />
        <TextField label="Intitulé" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} />
        <TextField label="Coefficient" inputMode="decimal" value={f.coefficient} onChange={(e) => setF({ ...f, coefficient: e.target.value })} />
        <TextField label="Volume horaire" inputMode="numeric" value={f.hours} onChange={(e) => setF({ ...f, hours: e.target.value })} />
        <SelectField className="span-2" label="Enseignant" value={f.teacherId} onChange={(e) => setF({ ...f, teacherId: e.target.value })}>
          <option value="">Non attribué</option>{teachers.data?.map((t) => <option key={t.id} value={t.id}>{t.nom} — {t.function}</option>)}
        </SelectField>
      </div>
      <div style={{ marginTop: 12 }}><ErrorAlert error={error} /></div>
    </Modal>
  );
}

function AssessmentDialog({ courseId, onClose, onSaved }: { courseId: string; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({ title: 'Interrogation', kind: 'Interrogation', term: '1', date: today(), maxScore: '20', weight: '1' });
  const [error, setError] = useState<unknown>();
  return (
    <Modal title="Nouvelle évaluation" onClose={onClose} footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" onClick={async () => {
      try { await api.post('/api/scolarite/evaluations', { courseId, title: f.title, kind: f.kind, term: Number(f.term), date: f.date, maxScore: Number(f.maxScore), weight: Number(f.weight) }); onSaved(); onClose(); } catch (e) { setError(e); }
    }}>Créer</Button></>}>
      <div className="grid cols-2">
        <TextField label="Intitulé" value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} />
        <SelectField label="Type" value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })}>
          <option value="Interrogation">Interrogation</option><option value="Devoir">Devoir</option><option value="TravauxPratiques">Travaux pratiques</option><option value="Examen">Examen</option>
        </SelectField>
        <SelectField label="Période" value={f.term} onChange={(e) => setF({ ...f, term: e.target.value })}>{[1, 2, 3, 4].map((t) => <option key={t} value={t}>Période {t}</option>)}</SelectField>
        <TextField label="Date" type="date" value={f.date} onChange={(e) => setF({ ...f, date: e.target.value })} />
        <TextField label="Note maximale" inputMode="decimal" value={f.maxScore} onChange={(e) => setF({ ...f, maxScore: e.target.value })} />
        <TextField label="Poids dans la moyenne" inputMode="decimal" value={f.weight} onChange={(e) => setF({ ...f, weight: e.target.value })} help="Ex. 2 pour un examen qui compte double" />
      </div>
      <div style={{ marginTop: 12 }}><ErrorAlert error={error} /></div>
    </Modal>
  );
}

function ResultsPanel({ promotionId }: { promotionId: string }) {
  const [term, setTerm] = useState('');
  const q = useQuery({ queryKey: ['results', promotionId, term], queryFn: () => api.get<Results>('/api/scolarite/resultats', { promotionId, periode: term }) });
  const r = q.data;
  return (
    <Card title={r ? `${r.promotion} · moyenne de classe ${num(r.classAverage)} · ${r.passed} admis sur ${r.rows.length}` : 'Résultats'} pad={false}
      actions={<SelectField label="" value={term} onChange={(e) => setTerm(e.target.value)}><option value="">Toutes périodes</option>{[1, 2, 3, 4].map((t) => <option key={t} value={t}>Période {t}</option>)}</SelectField>}>
      {!r ? <Loading /> : r.rows.length === 0 ? <Empty text="Aucun apprenant." /> : (
        <div className="table-wrap" style={{ border: 'none', borderRadius: 0 }}>
          <table className="data">
            <thead><tr><th>Rang</th><th>Apprenant</th>{r.courses.map((c, i) => <th key={c} className="num" title={c}>{c.length > 14 ? c.slice(0, 13) + '…' : c}<div className="small muted">coef. {num(r.coefficients[i])}</div></th>)}<th className="num">Moyenne</th><th>Décision</th><th /></tr></thead>
            <tbody>{r.rows.map((row) => (
              <tr key={row.enrollmentId}>
                <td style={{ fontWeight: 800 }}>{row.rank ?? '—'}</td><td style={{ minWidth: 220 }}><div style={{ fontWeight: 600 }}>{row.fullName}</div><div className="small tag-code">{row.matricule}</div></td>
                {row.courseAverages.map((a, i) => <td key={i} className="num" style={{ color: a !== null && a < 10 ? 'var(--red)' : undefined }}>{num(a)}</td>)}
                <td className="num" style={{ fontWeight: 800 }}>{num(row.average)}</td>
                <td><Badge tone={row.average === null || row.average === undefined ? 'gray' : row.average >= 10 ? 'green' : 'red'}>{row.decision}</Badge></td>
                <td><Button size="small" variant="ghost" icon={<FileText />} onClick={() => openDocument('/api/scolarite/bulletin.pdf', { promotionId, inscriptionId: row.enrollmentId, periode: term })}>Bulletin</Button></td>
              </tr>))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}

// ---------- Présences ----------
const ATTENDANCE = ['Présent', 'Retard', 'Absent', 'Absence justifiée'];

export function AttendancePage() {
  const [promotionId, setPromotionId] = useState('');
  const [day, setDay] = useState(today());
  const [tab, setTab] = useState<'appel' | 'bilan'>('appel');
  const { can } = useSession();
  const toast = useToast();
  const sheet = useQuery({ queryKey: ['attendance', promotionId, day], queryFn: () => api.get<{ rows: AttendanceRow[] }>('/api/scolarite/presences', { promotionId, date: day }), enabled: !!promotionId && tab === 'appel' });
  const [rows, setRows] = useState<AttendanceRow[]>([]);
  useEffect(() => { if (sheet.data) setRows(sheet.data.rows); }, [sheet.data]);
  const from = new Date(Date.now() - 30 * 864e5).toISOString().slice(0, 10);
  const summary = useQuery({ queryKey: ['attendance-summary', promotionId], queryFn: () => api.get<AttendanceSummary[]>('/api/scolarite/presences/bilan', { promotionId, du: from, au: today() }), enabled: !!promotionId && tab === 'bilan' });
  const save = async () => {
    try {
      await api.put(`/api/scolarite/presences?promotionId=${promotionId}&date=${day}`, rows.map((r) => ({ enrollmentId: r.enrollmentId, status: r.status ?? 'Présent', note: r.note ?? null })));
      toast('Appel enregistré.', 'success');
      void sheet.refetch();
    } catch (e) { toast(errorText(e), 'error'); }
  };
  const counts = ATTENDANCE.map((s) => [s, rows.filter((r) => (r.status ?? 'Présent') === s).length] as const);
  return (
    <>
      <PageHead title="Présences" subtitle="Appel quotidien par promotion et taux d’assiduité sur 30 jours." />
      <div className="toolbar"><PromotionPicker value={promotionId} onChange={setPromotionId} /><TextField label="Date" type="date" max={today()} value={day} onChange={(e) => setDay(e.target.value)} /></div>
      <Tabs value={tab} onChange={setTab} items={[{ value: 'appel', label: 'Faire l’appel' }, { value: 'bilan', label: 'Bilan 30 jours' }]} />
      {tab === 'appel' ? (
        <Card title={`Appel du ${date(day)}`} pad={false} actions={<>
          <span className="small muted">{counts.map(([s, n]) => `${s} : ${n}`).join(' · ')}</span>
          {can(P.academicsWrite) && <><Button size="small" icon={<CheckCheck />} onClick={() => setRows(rows.map((r) => ({ ...r, status: 'Présent' })))}>Tous présents</Button><Button size="small" variant="primary" icon={<Save />} onClick={save}>Enregistrer</Button></>}
        </>}>
          {!sheet.data ? <Loading /> : rows.length === 0 ? <Empty text="Aucun apprenant actif." /> : (
            <table className="data">
              <thead><tr><th>Matricule</th><th>Apprenant</th><th>Statut</th><th>Observation</th></tr></thead>
              <tbody>{rows.map((r, i) => (
                <tr key={r.enrollmentId}>
                  <td className="tag-code">{r.matricule}</td><td>{r.fullName}{!r.status && <span className="small muted"> · non saisi</span>}</td>
                  <td><Segmented value={r.status ?? 'Présent'} onChange={(v) => { const n = [...rows]; n[i] = { ...r, status: v }; setRows(n); }} items={ATTENDANCE.map((a) => ({ value: a, label: a === 'Absence justifiée' ? 'Justifié' : a }))} /></td>
                  <td><input className="input" value={r.note ?? ''} onChange={(e) => { const n = [...rows]; n[i] = { ...r, note: e.target.value }; setRows(n); }} /></td>
                </tr>))}
              </tbody>
            </table>
          )}
        </Card>
      ) : (
        <div className="table-wrap">
          {!summary.data ? <Loading /> : (
            <table className="data">
              <thead><tr><th>Matricule</th><th>Apprenant</th><th className="num">Présent</th><th className="num">Retard</th><th className="num">Absent</th><th className="num">Justifié</th><th style={{ width: 200 }}>Assiduité</th></tr></thead>
              <tbody>{summary.data.map((s) => (
                <tr key={s.enrollmentId}><td className="tag-code">{s.matricule}</td><td>{s.fullName}</td><td className="num">{s.present}</td><td className="num">{s.late}</td><td className="num" style={{ color: s.absent ? 'var(--red)' : undefined }}>{s.absent}</td><td className="num">{s.justified}</td>
                  <td><div className="row" style={{ flexWrap: 'nowrap' }}><div className={`progress ${s.rate >= 90 ? 'green' : 'orange'}`} style={{ flex: 1 }}><span style={{ width: `${s.rate}%` }} /></div><span className="small mono">{num(s.rate, 1)} %</span></div></td></tr>))}
              </tbody>
            </table>
          )}
        </div>
      )}
    </>
  );
}

// ---------- Discipline ----------
export function DisciplinePage() {
  const [promotionId, setPromotionId] = useState('');
  const [creating, setCreating] = useState(false);
  useActionParam('incident', () => setCreating(true));
  const { can } = useSession();
  const qc = useQueryClient();
  const list = useQuery({ queryKey: ['incidents', promotionId], queryFn: () => api.get<Incident[]>('/api/scolarite/discipline', { promotionId }), enabled: !!promotionId });
  return (
    <>
      <PageHead title="Discipline" subtitle="Incidents, sanctions et suivi du comportement." actions={can(P.academicsWrite) && <Button variant="primary" icon={<Plus />} onClick={() => setCreating(true)}>Signaler un incident</Button>} />
      <div className="toolbar"><PromotionPicker value={promotionId} onChange={setPromotionId} /></div>
      <div className="table-wrap">
        {!list.data ? <Loading /> : list.data.length === 0 ? <Empty text="Aucun incident enregistré pour cette promotion." /> : (
          <table className="data">
            <thead><tr><th>Date</th><th>Apprenant</th><th>Catégorie</th><th>Description</th><th>Sanction</th><th>Gravité</th><th>Par</th></tr></thead>
            <tbody>{list.data.map((i) => (
              <tr key={i.id}><td>{date(i.date)}</td><td>{i.fullName}<div className="small tag-code">{i.matricule}</div></td><td>{i.category}</td><td className="small">{i.description}</td><td className="small">{i.sanction ?? '—'}</td>
                <td><Badge tone={i.severity === 'Grave' ? 'red' : i.severity === 'Moyen' ? 'orange' : 'gray'}>{i.severity}</Badge></td><td className="muted small">{i.recordedBy}</td></tr>))}
            </tbody>
          </table>
        )}
      </div>
      {creating && <IncidentDialog promotionId={promotionId} onClose={() => setCreating(false)} onSaved={() => qc.invalidateQueries({ queryKey: ['incidents'] })} />}
    </>
  );
}

function IncidentDialog({ promotionId, onClose, onSaved }: { promotionId: string; onClose: () => void; onSaved: () => void }) {
  const students = useQuery({ queryKey: ['students-of', promotionId], queryFn: () => api.get<Paged<StudentListItem>>('/api/apprenants', { promotionId, taille: 500 }) });
  const [f, setF] = useState({ enrollmentId: '', date: today(), category: 'Retard répété', description: '', sanction: '', severity: 'Mineur' });
  const [error, setError] = useState<unknown>();
  return (
    <Modal title="Signaler un incident" onClose={onClose} footer={<><Button variant="ghost" onClick={onClose}>Annuler</Button><Button variant="primary" disabled={!f.enrollmentId} onClick={async () => {
      try { await api.post('/api/scolarite/discipline', { ...f, sanction: f.sanction || null }); onSaved(); onClose(); } catch (e) { setError(e); }
    }}>Enregistrer</Button></>}>
      <div className="grid cols-2">
        <SelectField className="span-2" label="Apprenant" value={f.enrollmentId} onChange={(e) => setF({ ...f, enrollmentId: e.target.value })}>
          <option value="">Choisir…</option>{students.data?.items.map((s) => <option key={s.enrollmentId} value={s.enrollmentId}>{s.matricule} — {s.lastName} {s.firstName}</option>)}
        </SelectField>
        <TextField label="Date" type="date" max={today()} value={f.date} onChange={(e) => setF({ ...f, date: e.target.value })} />
        <SelectField label="Catégorie" value={f.category} onChange={(e) => setF({ ...f, category: e.target.value })}>{['Retard répété', 'Absence injustifiée', 'Indiscipline', 'Tenue non conforme', 'Fraude', 'Violence', 'Autre'].map((c) => <option key={c}>{c}</option>)}</SelectField>
        <TextArea className="span-2" label="Description" value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} />
        <TextField label="Sanction" value={f.sanction} onChange={(e) => setF({ ...f, sanction: e.target.value })} />
        <SelectField label="Gravité" value={f.severity} onChange={(e) => setF({ ...f, severity: e.target.value })}><option value="Mineur">Mineure</option><option value="Moyen">Moyenne</option><option value="Grave">Grave</option></SelectField>
      </div>
      <div style={{ marginTop: 12 }}><ErrorAlert error={error} /></div>
    </Modal>
  );
}

