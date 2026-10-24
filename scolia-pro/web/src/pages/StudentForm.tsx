import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { AlertTriangle, UserPlus } from 'lucide-react';
import { api, ApiError } from '../api/client';
import type { DuplicateCandidate, Promotion, Student } from '../api/types';
import { Button, ErrorAlert, Modal, SectionTitle, SelectField, TextField } from '../components/ui';
import { money, today } from '../lib/format';

export interface IdentityForm {
  lastName: string; middleName: string; firstName: string; gender: string; birthDate: string; birthPlace: string; nationality: string;
  address: string; phone: string; email: string; guardianName: string; guardianRelation: string; guardianPhone: string; guardianPhone2: string; notes: string;
}

const empty: IdentityForm = {
  lastName: '', middleName: '', firstName: '', gender: '', birthDate: '', birthPlace: '', nationality: '', address: '', phone: '', email: '',
  guardianName: '', guardianRelation: '', guardianPhone: '', guardianPhone2: '', notes: '',
};

export function fromStudent(s: Student): IdentityForm {
  return {
    lastName: s.lastName, middleName: s.middleName, firstName: s.firstName, gender: s.gender, birthDate: s.birthDate ?? '', birthPlace: s.birthPlace ?? '',
    nationality: s.nationality ?? '', address: s.address ?? '', phone: s.phone ?? '', email: s.email ?? '', guardianName: s.guardianName ?? '',
    guardianRelation: s.guardianRelation ?? '', guardianPhone: s.guardianPhone ?? '', guardianPhone2: s.guardianPhone2 ?? '', notes: s.notes ?? '',
  };
}

export function toPayload(f: IdentityForm, version?: number) {
  const n = (v: string) => (v.trim() === '' ? null : v.trim());
  return {
    lastName: f.lastName, middleName: f.middleName, firstName: f.firstName, gender: f.gender, birthDate: n(f.birthDate), birthPlace: n(f.birthPlace),
    nationality: n(f.nationality), address: n(f.address), phone: n(f.phone), email: n(f.email), guardianName: n(f.guardianName),
    guardianRelation: n(f.guardianRelation), guardianPhone: n(f.guardianPhone), guardianPhone2: n(f.guardianPhone2), notes: n(f.notes), version: version ?? null,
  };
}

export function IdentityFields({ form, setForm, error }: { form: IdentityForm; setForm: (f: IdentityForm) => void; error?: unknown }) {
  const e = error instanceof ApiError ? error : undefined;
  const set = (k: keyof IdentityForm) => (ev: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => setForm({ ...form, [k]: ev.target.value });
  return (
    <>
      <SectionTitle num="01">Identité</SectionTitle>
      <div className="grid cols-3">
        <TextField label="Nom" value={form.lastName} onChange={set('lastName')} error={e?.fieldError('lastName')} required autoComplete="off" />
        <TextField label="Post-nom" value={form.middleName} onChange={set('middleName')} error={e?.fieldError('middleName')} required autoComplete="off" />
        <TextField label="Prénom" value={form.firstName} onChange={set('firstName')} error={e?.fieldError('firstName')} required autoComplete="off" />
        <SelectField label="Sexe" value={form.gender} onChange={set('gender')} error={e?.fieldError('gender')} required>
          <option value="">Choisir…</option><option value="F">Féminin</option><option value="M">Masculin</option>
        </SelectField>
        <TextField label="Date de naissance" type="date" value={form.birthDate} onChange={set('birthDate')} max={today()} error={e?.fieldError('birthDate')} required />
        <TextField label="Lieu de naissance" value={form.birthPlace} onChange={set('birthPlace')} error={e?.fieldError('birthPlace')} required />
        <TextField label="E-mail" type="email" value={form.email} onChange={set('email')} error={e?.fieldError('email')} />
      </div>
      <div className="divider" />
      <SectionTitle num="02">Parent et adresse</SectionTitle>
      <div className="grid cols-3">
        <TextField label="Parent ou tuteur" value={form.guardianName} onChange={set('guardianName')} />
        <SelectField label="Lien" value={form.guardianRelation} onChange={set('guardianRelation')}>
          <option value="">—</option>{['Père', 'Mère', 'Tuteur', 'Tutrice', 'Oncle', 'Tante', 'Frère', 'Sœur', 'Autre'].map((r) => <option key={r}>{r}</option>)}
        </SelectField>
        <TextField label="Contact parent 1" value={form.guardianPhone} onChange={set('guardianPhone')} error={e?.fieldError('guardianPhone')} inputMode="tel" required help="10 chiffres, ex. 0812345678. Les relances WhatsApp et SMS partent vers ce numéro." />
        <TextField label="Contact parent 2" value={form.guardianPhone2} onChange={set('guardianPhone2')} error={e?.fieldError('guardianPhone2')} inputMode="tel" help="Facultatif. Sert aux relances si le contact 1 est vide." />
        <TextField className="span-2" label="Adresse" value={form.address} onChange={set('address')} help="Quartier, avenue et numéro" />
      </div>
    </>
  );
}

export function NewStudentDialog({ onClose, onCreated, defaultPromotionId }: { onClose: () => void; onCreated: (s: Student) => void; defaultPromotionId?: string }) {
  const [form, setForm] = useState<IdentityForm>(empty);
  const [promotionId, setPromotionId] = useState(defaultPromotionId ?? '');
  const [enrolledOn, setEnrolledOn] = useState(today());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  const [duplicates, setDuplicates] = useState<DuplicateCandidate[]>();
  const promotions = useQuery({ queryKey: ['promotions'], queryFn: () => api.get<Promotion[]>('/api/etablissement/promotions') });
  const selected = promotions.data?.find((p) => p.id === promotionId);

  const submit = async (confirmDuplicate: boolean) => {
    setBusy(true); setError(undefined);
    try {
      const created = await api.post<Student>('/api/apprenants', { identity: toPayload(form), promotionId, enrolledOn, confirmDuplicate });
      onCreated(created);
    } catch (err) {
      if (err instanceof ApiError && err.code === 'doublon') setDuplicates(err.data?.candidates as DuplicateCandidate[]);
      else setError(err);
    } finally { setBusy(false); }
  };

  return (
    <Modal wide eyebrow="Secrétariat" title="Inscription d'un apprenant" subtitle="Identité, coordonnées et affectation à une promotion. Le matricule est attribué automatiquement."
      onClose={onClose}
      footer={<>
        <Button variant="ghost" onClick={onClose}>Annuler</Button>
        <Button variant="primary" icon={<UserPlus />} loading={busy} disabled={!promotionId} onClick={() => submit(false)}>Enregistrer l’inscription</Button>
      </>}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(false); }} className="stack">
        <IdentityFields form={form} setForm={setForm} error={error} />
        <div className="divider" />
        <SectionTitle num="03">Inscription</SectionTitle>
        <div className="grid cols-3">
          <SelectField label="Promotion" value={promotionId} onChange={(e) => setPromotionId(e.target.value)} required>
            <option value="">Choisir…</option>
            {promotions.data?.filter((p) => p.isActive).map((p) => <option key={p.id} value={p.id}>{p.code} — {p.name}</option>)}
          </SelectField>
          <TextField label="Date d'inscription" type="date" value={enrolledOn} onChange={(e) => setEnrolledOn(e.target.value)} max={today()} />
          <div className="field"><span className="label">Frais de l'année</span><div style={{ paddingTop: 8, fontWeight: 700 }}>{selected ? `${money(selected.annualTotal)} · ${money(selected.monthlyFee)} / mois` : '—'}</div></div>
        </div>
        {duplicates && (
          <div className="alert warning">
            <AlertTriangle />
            <div>
              <strong>Homonyme détecté.</strong> Un dossier porte déjà cette identité :
              <ul style={{ margin: '6px 0' }}>{duplicates.map((d) => <li key={d.id}><span className="tag-code">{d.matricule}</span> {d.fullName} {d.promotionCode && `(${d.promotionCode})`}</li>)}</ul>
              <div className="row"><Button size="small" onClick={() => setDuplicates(undefined)}>Vérifier la saisie</Button>
                <Button size="small" variant="primary" onClick={() => submit(true)}>Il s’agit d’une autre personne : inscrire</Button></div>
            </div>
          </div>
        )}
        <ErrorAlert error={error} />
      </form>
    </Modal>
  );
}
