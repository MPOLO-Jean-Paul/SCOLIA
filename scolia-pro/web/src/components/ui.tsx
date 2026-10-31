import { createContext, useCallback, useContext, useEffect, useId, useRef, useState, type ReactNode, type InputHTMLAttributes, type SelectHTMLAttributes, type TextareaHTMLAttributes } from 'react';
import { createPortal } from 'react-dom';
import { AlertTriangle, CheckCircle2, ChevronLeft, ChevronRight, Info, Inbox, X } from 'lucide-react';
import { useLocation } from 'react-router-dom';
import { ApiError } from '../api/client';
import { moduleOf } from '../lib/ribbon';
import { useDialogFocus } from '../lib/dialog-focus';

// ---------- Notifications ----------
type ToastKind = 'info' | 'success' | 'error';
interface ToastItem { id: number; kind: ToastKind; text: string }
const ToastContext = createContext<(text: string, kind?: ToastKind) => void>(() => {});

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const push = useCallback((text: string, kind: ToastKind = 'info') => {
    const id = Date.now() + Math.random();
    setItems((list) => [...list.slice(-3), { id, kind, text }]);
    setTimeout(() => setItems((list) => list.filter((t) => t.id !== id)), kind === 'error' ? 7000 : 4000);
  }, []);
  return (
    <ToastContext.Provider value={push}>
      {children}
      <div className="toasts" role="status" aria-live="polite">
        {items.map((t) => (
          <div key={t.id} className={`toast ${t.kind}`}>
            {t.kind === 'success' ? <CheckCircle2 color="var(--green)" /> : t.kind === 'error' ? <AlertTriangle color="var(--red)" /> : <Info color="var(--blue)" />}
            <div>{t.text}</div>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}
export const useToast = () => useContext(ToastContext);

export function errorText(error: unknown) {
  if (error instanceof ApiError) {
    const details = error.errors ? Object.values(error.errors).flat() : [];
    return details.length > 0 && !details.includes(error.message) ? `${error.message} ${details.join(' ')}` : error.message;
  }
  return error instanceof Error ? error.message : 'Erreur inattendue.';
}

// ---------- Boutons et champs ----------
export function Button({ variant, size, icon, children, loading, ...rest }: {
  variant?: 'primary' | 'ghost' | 'danger' | 'success'; size?: 'small'; icon?: ReactNode; loading?: boolean;
} & React.ButtonHTMLAttributes<HTMLButtonElement>) {
  const cls = ['btn', variant, size, !children && icon ? 'icon' : undefined, rest.className].filter(Boolean).join(' ');
  return (
    <button type="button" {...rest} className={cls} disabled={rest.disabled || loading}>
      {icon}
      {loading ? 'Patientez…' : children}
    </button>
  );
}

export function Field({ label, error, help, children, className, required }: { label: string; error?: string; help?: string; children: (id: string) => ReactNode; className?: string; required?: boolean }) {
  const id = useId();
  return (
    <div className={`field ${className ?? ''}`}>
      {/* L'astérisque signale un champ obligatoire ; il reste hors du nom lu par les lecteurs d'écran. */}
      <label htmlFor={id}>{label}{required && <span className="req" aria-hidden="true"> *</span>}</label>
      {children(id)}
      {error ? <span className="error">{error}</span> : help ? <span className="help">{help}</span> : null}
    </div>
  );
}

export function TextField({ label, error, help, className, ...rest }: { label: string; error?: string; help?: string } & InputHTMLAttributes<HTMLInputElement>) {
  return <Field label={label} error={error} help={help} className={className} required={rest.required}>{(id) => <input id={id} {...rest} className={`input ${error ? 'invalid' : ''}`} />}</Field>;
}

export function SelectField({ label, error, help, className, children, ...rest }: { label: string; error?: string; help?: string } & SelectHTMLAttributes<HTMLSelectElement>) {
  return <Field label={label} error={error} help={help} className={className} required={rest.required}>{(id) => <select id={id} {...rest} className={`select ${error ? 'invalid' : ''}`}>{children}</select>}</Field>;
}

export function TextArea({ label, error, className, ...rest }: { label: string; error?: string } & TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <Field label={label} error={error} className={className}>{(id) => <textarea id={id} {...rest} className="textarea" />}</Field>;
}

export function SectionTitle({ num, children }: { num?: string; children: ReactNode }) {
  return <h3 className="section-title">{num && <span className="num">{num}</span>}{children}</h3>;
}

export function Badge({ tone, children }: { tone?: 'green' | 'orange' | 'red' | 'gray' | 'purple'; children: ReactNode }) {
  return <span className={`badge ${tone ?? ''}`}>{children}</span>;
}

export function statusTone(status?: string): 'green' | 'orange' | 'red' | 'gray' | 'purple' | undefined {
  switch (status) {
    case 'Actif': case 'Valide': case 'Payé': case 'Paye': case 'Présent': return 'green';
    case 'Partiel': case 'Suspendu': case 'Retard': case 'Brouillon': return 'orange';
    case 'Impayé': case 'Annulé': case 'Absent': case 'Sorti': case 'Grave': return 'red';
    case 'Pris en charge': case 'Diplômé': return 'purple';
    default: return 'gray';
  }
}

export function Kpi({ label, value, hint, tone, icon }: { label: string; value: ReactNode; hint?: ReactNode; tone?: 'orange' | 'green' | 'red' | 'purple'; icon?: ReactNode }) {
  return (
    <div className={`kpi ${tone ?? ''}`}>
      <div className="label">{icon}{label}</div>
      <div className="value">{value}</div>
      {hint && <div className="hint">{hint}</div>}
    </div>
  );
}

export function Empty({ text, title, children }: { text: string; title?: string; children?: ReactNode }) {
  return (
    <div className="empty">
      <span className="empty-icon"><Inbox /></span>
      {title && <strong className="empty-title">{title}</strong>}
      <div className="empty-text">{text}</div>
      {children}
    </div>
  );
}

export function Loading({ rows = 4 }: { rows?: number }) {
  return <div className="stack" style={{ padding: 18 }}>{Array.from({ length: rows }, (_, i) => <div key={i} className="skeleton" style={{ width: `${90 - i * 12}%` }} />)}</div>;
}

export function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  return (
    <div className="pager">
      <span>{total} résultat(s) · page {page} / {pages}</span>
      <Button size="small" variant="ghost" icon={<ChevronLeft />} disabled={page <= 1} onClick={() => onPage(page - 1)} aria-label="Page précédente" />
      <Button size="small" variant="ghost" icon={<ChevronRight />} disabled={page >= pages} onClick={() => onPage(page + 1)} aria-label="Page suivante" />
    </div>
  );
}

export function Tabs<T extends string>({ value, onChange, items }: { value: T; onChange: (v: T) => void; items: { value: T; label: ReactNode }[] }) {
  return (
    <div className="tabs" role="tablist">
      {items.map((i) => <button key={i.value} role="tab" aria-selected={value === i.value} className={value === i.value ? 'on' : ''} onClick={() => onChange(i.value)}>{i.label}</button>)}
    </div>
  );
}

export function Segmented<T extends string>({ value, onChange, items }: { value: T; onChange: (v: T) => void; items: { value: T; label: string }[] }) {
  return <div className="segmented">{items.map((i) => <button key={i.value} type="button" className={value === i.value ? 'on' : ''} onClick={() => onChange(i.value)}>{i.label}</button>)}</div>;
}

// ---------- Fenêtres modales ----------
export function Modal({ title, eyebrow, subtitle, onClose, children, footer, wide }: {
  title: string; eyebrow?: string; subtitle?: string; onClose: () => void; children: ReactNode; footer?: ReactNode; wide?: boolean;
}) {
  const ref = useRef<HTMLDivElement>(null);
  useDialogFocus(ref, onClose);
  return createPortal(
    <div className="overlay" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div ref={ref} className={`modal ${wide ? 'wide' : ''}`} role="dialog" aria-modal="true" aria-label={title} tabIndex={-1}>
        <div className="modal-head">
          {eyebrow && <div className="eyebrow">{eyebrow}</div>}
          <h2>{title}</h2>
          {subtitle && <p>{subtitle}</p>}
          <button className="close" onClick={onClose} aria-label="Fermer"><X size={18} /></button>
        </div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-foot">{footer}</div>}
      </div>
    </div>,
    document.body,
  );
}

/** Demande un motif obligatoire (annulations, fins de prise en charge…). */
export function ReasonDialog({ title, prompt, confirmLabel, onConfirm, onClose }: {
  title: string; prompt: string; confirmLabel: string; onConfirm: (reason: string) => Promise<void>; onClose: () => void;
}) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  return (
    <Modal title={title} onClose={onClose} footer={<>
      <Button variant="ghost" onClick={onClose}>Retour</Button>
      <Button variant="danger" loading={busy} disabled={reason.trim().length < 5} onClick={async () => {
        setBusy(true); setError(undefined);
        try { await onConfirm(reason.trim()); onClose(); } catch (e) { setError(errorText(e)); } finally { setBusy(false); }
      }}>{confirmLabel}</Button>
    </>}>
      <div className="stack">
        <p className="muted" style={{ margin: 0 }}>{prompt}</p>
        <TextArea label="Motif (obligatoire)" value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} />
        {error && <div className="alert error"><AlertTriangle />{error}</div>}
      </div>
    </Modal>
  );
}

export function ErrorAlert({ error }: { error: unknown }) {
  if (!error) return null;
  return <div className="alert error"><AlertTriangle /><div>{errorText(error)}</div></div>;
}

export function Card({ title, actions, children, pad = true }: { title?: ReactNode; actions?: ReactNode; children: ReactNode; pad?: boolean }) {
  return (
    <section className="card">
      {(title || actions) && <div className="card-head">{typeof title === 'string' ? <h2>{title}</h2> : title}<div className="actions">{actions}</div></div>}
      <div className={pad ? 'card-pad' : undefined}>{children}</div>
    </section>
  );
}

export function PageHead({ title, subtitle, actions, eyebrow }: { title: string; subtitle?: ReactNode; actions?: ReactNode; eyebrow?: string }) {
  const { pathname } = useLocation();
  const module = eyebrow ?? moduleOf(pathname);
  useEffect(() => { document.title = `${title} · SCOLIA Pro`; }, [title]);
  return (
    <header className="page-head">
      <div className="page-title">
        {module && <div className="page-eyebrow">{module}</div>}
        <h1>{title}</h1>
        {subtitle && <p>{subtitle}</p>}
      </div>
      {actions && <div className="actions">{actions}</div>}
    </header>
  );
}

export function useDebounced<T>(value: T, delay = 250) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => { const t = setTimeout(() => setDebounced(value), delay); return () => clearTimeout(t); }, [value, delay]);
  return debounced;
}
