import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowRight, Eye, EyeOff, LoaderCircle, Rocket } from 'lucide-react';
import { api } from '../api/client';
import type { Session } from '../api/types';
import { useSession } from '../lib/session';
import { Button, ErrorAlert, SectionTitle, SelectField, TextField, errorText } from '../components/ui';

export function LoginPage() {
  const { setSession } = useSession();
  const navigate = useNavigate();
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [show, setShow] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  useEffect(() => {
    document.title = 'Connexion · SCOLIA';
  }, []);
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true); setError(undefined);
    try {
      const s = await api.post<Session>('/api/session', { userName, password });
      setSession(s);
      navigate(s.mustChangePassword ? '/profil' : '/', { replace: true });
    } catch (err) { setError(err); } finally { setBusy(false); }
  };
  return (
    <div className="signin-screen">
      <main className="signin">
        <img className="signin-avatar" src="/scolia.png" alt="" />
        <h1 className="signin-title">SCOLIA</h1>
        <p className="signin-sub">Connectez-vous pour continuer</p>
        <form className="signin-form" onSubmit={submit}>
          <label className="sr-only" htmlFor="login-user">Identifiant</label>
          <input id="login-user" className="signin-input" placeholder="Identifiant" autoComplete="username" autoFocus value={userName} onChange={(e) => setUserName(e.target.value)} required />
          <label className="sr-only" htmlFor="login-pwd">Mot de passe</label>
          <div className="signin-pass">
            <input id="login-pwd" className="signin-input" type={show ? 'text' : 'password'} placeholder="Mot de passe" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
            <button type="button" className="signin-icon" onClick={() => setShow((v) => !v)} aria-label={show ? 'Masquer la saisie' : 'Afficher la saisie'} title={show ? 'Masquer la saisie' : 'Afficher la saisie'}>
              {show ? <EyeOff /> : <Eye />}
            </button>
            <button type="submit" className="signin-go" aria-label="Se connecter" title="Se connecter" disabled={busy}>
              {busy ? <LoaderCircle className="spin" /> : <ArrowRight />}
            </button>
          </div>
          {error ? <p className="signin-error" role="alert">{errorText(error)}</p> : null}
        </form>
        <p className="signin-help">Après 5 erreurs, le compte est verrouillé 15 minutes. Mot de passe oublié : contactez l’administrateur.</p>
      </main>
      <footer className="signin-foot">SCOLIA Pro 1.1.0</footer>
    </div>
  );
}

export function SetupPage() {
  const { setSession, refresh } = useSession();
  const navigate = useNavigate();
  const [form, setForm] = useState({
    schoolName: '', shortName: '', motto: 'Intégrité · Honnêteté · Excellence', city: 'Kinshasa', phone: '', currencyCode: 'CDF', currencySymbol: 'FC',
    yearLabel: '', startMonth: 9, monthCount: 10, adminUserName: 'admin', adminDisplayName: '', adminPassword: '', confirm: '', loadDemoData: false,
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  useEffect(() => {
    document.title = 'Installation · SCOLIA';
    api.get<{ label: string }>('/api/installation/annee-proposee').then((r) => setForm((f) => ({ ...f, yearLabel: f.yearLabel || r.label }))).catch(() => undefined);
  }, []);
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.type === 'checkbox' ? (e.target as HTMLInputElement).checked : e.target.type === 'number' || k === 'startMonth' ? Number(e.target.value) : e.target.value }));
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (form.adminPassword !== form.confirm) { setError(new Error('Les deux mots de passe ne correspondent pas.')); return; }
    setBusy(true); setError(undefined);
    try {
      const { confirm: _confirm, ...payload } = form;
      void _confirm;
      const s = await api.post<Session>('/api/installation', payload);
      setSession(s);
      await refresh();
      navigate('/', { replace: true });
    } catch (err) { setError(err); } finally { setBusy(false); }
  };
  const months = ['Janvier', 'Février', 'Mars', 'Avril', 'Mai', 'Juin', 'Juillet', 'Août', 'Septembre', 'Octobre', 'Novembre', 'Décembre'];
  return (
    <div className="signin-screen signin-setup">
      <div className="signin-wide">
        <form className="auth-card" onSubmit={submit}>
          <div className="row"><Rocket color="var(--blue)" /><strong>Première installation</strong></div>
          <h2 style={{ fontSize: 26 }}>Configurer SCOLIA</h2>
          <p className="muted" style={{ marginTop: 0 }}>Ces informations figurent sur les reçus et les documents. Elles restent modifiables dans Paramètres.</p>
          <SectionTitle num="01">Établissement</SectionTitle>
          <div className="grid cols-2">
            <TextField className="span-2" label="Nom de l'établissement" value={form.schoolName} onChange={set('schoolName')} required />
            <TextField label="Sigle" value={form.shortName} onChange={set('shortName')} required help="Ex. ITM CD" />
            <TextField label="Devise" value={form.motto} onChange={set('motto')} />
            <TextField label="Ville" value={form.city} onChange={set('city')} />
            <TextField label="Téléphone" value={form.phone} onChange={set('phone')} />
            <TextField label="Monnaie (code ISO)" value={form.currencyCode} onChange={set('currencyCode')} maxLength={3} />
            <TextField label="Symbole affiché" value={form.currencySymbol} onChange={set('currencySymbol')} maxLength={6} />
          </div>
          <div className="divider" />
          <SectionTitle num="02">Année scolaire</SectionTitle>
          <div className="grid cols-3">
            <TextField label="Année" value={form.yearLabel} onChange={set('yearLabel')} required help="Format 2026-2027" />
            <SelectField label="Mois de rentrée" value={form.startMonth} onChange={set('startMonth')}>
              {months.map((m, i) => <option key={m} value={i + 1}>{m}</option>)}
            </SelectField>
            <TextField label="Mois facturés" type="number" min={1} max={12} value={form.monthCount} onChange={set('monthCount')} />
          </div>
          <div className="divider" />
          <SectionTitle num="03">Compte administrateur</SectionTitle>
          <div className="grid cols-2">
            <TextField label="Nom complet" value={form.adminDisplayName} onChange={set('adminDisplayName')} required />
            <TextField label="Identifiant" value={form.adminUserName} onChange={set('adminUserName')} required autoComplete="username" />
            <TextField label="Mot de passe" type="password" value={form.adminPassword} onChange={set('adminPassword')} required autoComplete="new-password" help="8 caractères minimum, lettres et chiffres" />
            <TextField label="Confirmer le mot de passe" type="password" value={form.confirm} onChange={set('confirm')} required autoComplete="new-password" />
          </div>
          <label className="check" style={{ marginTop: 16 }}>
            <input type="checkbox" checked={form.loadDemoData} onChange={set('loadDemoData')} />
            Ajouter des données de démonstration (apprenants et paiements fictifs) pour découvrir le logiciel
          </label>
          <div style={{ marginTop: 16 }}><ErrorAlert error={error} /></div>
          <Button type="submit" variant="primary" loading={busy} style={{ marginTop: 16, width: '100%', height: 46 }}>Installer SCOLIA</Button>
        </form>
      </div>
      <footer className="signin-foot">SCOLIA Pro 1.1.0</footer>
    </div>
  );
}
