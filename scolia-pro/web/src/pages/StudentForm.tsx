import { useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { AlertTriangle, UserPlus } from 'lucide-react';
import { api, ApiError } from '../api/client';
import type { DuplicateCandidate, Promotion, Student } from '../api/types';
import { Button, ErrorAlert, Modal, SectionTitle, SelectField, TextField } from '../components/ui';
import { money, today } from '../lib/format';
import { registrationSignature, validateRegistration, type RegistrationErrors } from '../lib/student-validation';

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

/** Identité puis parent ; à l'inscription, ces sections suivent la promotion et sont numérotées à partir de 02. */
export function IdentityFields({ form, setForm, error, clientErrors, numbered }: { form: IdentityForm; setForm: (f: IdentityForm) => void; error?: unknown; clientErrors?: RegistrationErrors; numbered?: boolean }) {
  const first = numbered ? 2 : 1;
  const e = error instanceof ApiError ? error : undefined;
  const set = (k: keyof IdentityForm) => (ev: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => setForm({ ...form, [k]: ev.target.value });
  return (
    <>
      <SectionTitle num={`0${first}`}>Identité</SectionTitle>
      <div className="grid cols-3">
        <TextField label="Nom" value={form.lastName} onChange={set('lastName')} error={clientErrors?.lastName ?? e?.fieldError('lastName')} required autoComplete="off" />
        <TextField label="Post-nom" value={form.middleName} onChange={set('middleName')} error={clientErrors?.middleName ?? e?.fieldError('middleName')} required autoComplete="off" />
        <TextField label="Prénom" value={form.firstName} onChange={set('firstName')} error={clientErrors?.firstName ?? e?.fieldError('firstName')} required autoComplete="off" />
        <SelectField label="Sexe" value={form.gender} onChange={set('gender')} error={clientErrors?.gender ?? e?.fieldError('gender')} required>
          <option value="">Choisir…</option><option value="F">Féminin</option><option value="M">Masculin</option>
        </SelectField>
        <TextField label="Date de naissance" type="date" value={form.birthDate} onChange={set('birthDate')} max={today()} error={clientErrors?.birthDate ?? e?.fieldError('birthDate')} required />
        <TextField label="Lieu de naissance" value={form.birthPlace} onChange={set('birthPlace')} error={clientErrors?.birthPlace ?? e?.fieldError('birthPlace')} required />
      </div>
      <div className="divider" />
      <SectionTitle num={`0${first + 1}`}>Parent et adresse</SectionTitle>
      <div className="grid cols-3">
        <TextField label="Parent ou tuteur" value={form.guardianName} onChange={set('guardianName')} />
        <TextField className="span-2" label="Adresse" value={form.address} onChange={set('address')} help="Quartier, avenue et numéro" />
        <TextField label="Contact parent" value={form.guardianPhone} onChange={set('guardianPhone')} error={clientErrors?.guardianPhone ?? e?.fieldError('guardianPhone')} inputMode="tel" required help="10 chiffres, ex. 0812345678. Les relances WhatsApp et SMS partent vers ce numéro." />
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
  const [duplicateSignature, setDuplicateSignature] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<RegistrationErrors>({});
  const submitting = useRef(false);
  const currentSignature = registrationSignature(form, promotionId, enrolledOn);
  const confirmedCandidates = duplicateSignature === currentSignature ? duplicates : undefined;
  const resetConfirmation = () => { setDuplicates(undefined); setDuplicateSignature(null); setFieldErrors({}); };
  const changeForm = (next: IdentityForm) => { setForm(next); resetConfirmation(); };
  const changePromotion = (next: string) => { setPromotionId(next); resetConfirmation(); };
  const changeDate = (next: string) => { setEnrolledOn(next); resetConfirmation(); };
  const promotions = useQuery({ queryKey: ['promotions'], queryFn: () => api.get<Promotion[]>('/api/etablissement/promotions') });
  const selected = promotions.data?.find((p) => p.id === promotionId);

  const submit = async (confirmDuplicate: boolean) => {
    if (submitting.current) return;
    const errors = validateRegistration(form, promotionId, enrolledOn, today());
    setFieldErrors(errors);
    if (Object.keys(errors).length > 0) {
      setError(new Error('Corrigez les champs indiqués avant d’inscrire cet apprenant.'));
      return;
    }
    // Une identité modifiée doit passer à nouveau par la recherche des homonymes.
    if (confirmDuplicate && (!confirmedCandidates || duplicateSignature !== currentSignature)) {
      setDuplicates(undefined);
      setError(new Error('Les informations ont changé : vérifiez les homonymes avant de confirmer.'));
      return;
    }
    submitting.current = true;
    setBusy(true); setError(undefined);
    try {
      const created = await api.post<Student>('/api/apprenants', {
        identity: toPayload(form), promotionId, enrolledOn, confirmDuplicate,
      });
      onCreated(created);
    } catch (err) {
      if (err instanceof ApiError && err.code === 'doublon') {
        setDuplicates(Array.isArray(err.data?.candidates) ? err.data.candidates as DuplicateCandidate[] : []);
        setDuplicateSignature(currentSignature);
      } else {
        setDuplicates(undefined);
        setDuplicateSignature(null);
        setError(err);
      }
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  };

  return (
    <Modal wide eyebrow="Secrétariat" title="Inscription d'un apprenant" subtitle="Identité, coordonnées et affectation à une promotion. Le matricule est attribué automatiquement."
      onClose={() => { if (!submitting.current) onClose(); }}
      footer={<>
        <Button variant="ghost" disabled={busy} onClick={onClose}>Annuler</Button>
        <Button variant="primary" icon={<UserPlus />} loading={busy} onClick={() => { void submit(false); }}>Enregistrer l’inscription</Button>
      </>}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(false); }} className="stack" noValidate>
        {/* La promotion d'abord, comme dans la version 79 : elle donne le début du matricule. */}
        <SectionTitle num="01">Promotion</SectionTitle>
        <div className="grid cols-3">
          <SelectField label="Promotion" value={promotionId} onChange={(e) => changePromotion(e.target.value)} error={fieldErrors.promotionId} required>
            <option value="">Choisir…</option>
            {promotions.data?.filter((p) => p.isActive).map((p) => <option key={p.id} value={p.id}>{p.code} — {p.name}</option>)}
          </SelectField>
          <TextField label="Date d'inscription" type="date" value={enrolledOn} onChange={(e) => changeDate(e.target.value)} error={fieldErrors.enrolledOn} max={today()} required />
          <div className="field"><span className="label">Frais de l'année</span><div style={{ paddingTop: 8, fontWeight: 700 }}>{selected ? `${money(selected.annualTotal)} · ${money(selected.monthlyFee)} / mois` : '—'}</div></div>
        </div>
        {promotions.data && promotions.data.filter((p) => p.isActive).length === 0 && (
          <div className="alert warning"><AlertTriangle /><div>Aucune promotion ouverte. Créez-en une dans Paramètres, Promotions et frais, avant d'inscrire un apprenant.</div></div>
        )}
        <div className="divider" />
        <IdentityFields form={form} setForm={changeForm} error={error} clientErrors={fieldErrors} numbered />
        {confirmedCandidates && (
          <div className="alert warning" role="alert">
            <AlertTriangle />
            <div>
              <strong>Homonyme détecté.</strong> Un dossier porte déjà cette identité :
              <ul style={{ margin: '6px 0' }}>{confirmedCandidates.map((d) => <li key={d.id}><span className="tag-code">{d.matricule}</span> {d.fullName} {d.promotionCode && `(${d.promotionCode})`}</li>)}</ul>
              <div className="row"><Button size="small" disabled={busy} onClick={() => { setDuplicates(undefined); setDuplicateSignature(null); }}>Vérifier la saisie</Button>
                <Button size="small" variant="primary" disabled={busy} onClick={() => { void submit(true); }}>Il s’agit d’une autre personne : inscrire</Button></div>
            </div>
          </div>
        )}
        <ErrorAlert error={error} />
      </form>
    </Modal>
  );
}
