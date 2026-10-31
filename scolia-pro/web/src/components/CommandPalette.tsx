import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { Search, User, Receipt, ArrowRight, Palette, Settings } from 'lucide-react';
import { api } from '../api/client';
import type { SearchHit } from '../api/types';
import { useDebounced } from './ui';
import { key } from '../lib/format';
import { useDialogFocus } from '../lib/dialog-focus';
import { validSearchDestination } from '../lib/search-destination';

interface Group { title: string; items: { to: string; label: string; icon: ReactNode }[] }
interface Entry { kind: string; title: string; subtitle?: string; url: string; icon: ReactNode }

/** Palette de commandes (Ctrl+K) : navigation et recherche globale au clavier. */
export function CommandPalette({ groups, onClose, onPreferences, canConfigureSchool }: { groups: Group[]; onClose: () => void; onPreferences: () => void; canConfigureSchool: boolean }) {
  const ref = useRef<HTMLDivElement>(null);
  const listId = useId();
  useDialogFocus(ref, onClose);
  const [text, setText] = useState('');
  const [hits, setHits] = useState<{query: string; items: SearchHit[]}>({ query: '', items: [] });
  const [navigationError, setNavigationError] = useState('');
  const [index, setIndex] = useState(0);
  const navigate = useNavigate();
  const debounced = useDebounced(text, 180);

  useEffect(() => {
    let cancelled = false;
    if (debounced.trim().length < 2) { setHits({ query: '', items: [] }); return; }
    api.get<SearchHit[]>('/api/recherche', { q: debounced }).then((h) => { if (!cancelled) setHits({query: debounced, items: h}); }).catch(() => { if (!cancelled) setHits({query: debounced, items: []}); });
    return () => { cancelled = true; };
  }, [debounced]);

  const entries = useMemo<Entry[]>(() => {
    const k = key(text);
    const pages = groups.flatMap((g) => g.items).filter((i) => !k || key(i.label).includes(k))
      .map((i) => ({ kind: 'Page', title: i.label, url: i.to, icon: i.icon }));
    const settings: Entry[] = [
      {kind: 'Paramètre', title: 'Préférences d’affichage', subtitle: 'Thème, texte, densité et animations', url: 'action:preferences', icon: <Palette />},
      {kind: 'Compte', title: 'Mon compte et mot de passe', url: '/profil', icon: <User />},
      ...(canConfigureSchool ? [
        {kind: 'Paramètre', title: 'Établissement', url: '/parametres?onglet=etablissement', icon: <Settings />},
        {kind: 'Paramètre', title: 'Années scolaires', url: '/parametres?onglet=annees', icon: <Settings />},
        {kind: 'Paramètre', title: 'Sections', url: '/parametres?onglet=sections', icon: <Settings />},
        {kind: 'Paramètre', title: 'Promotions et frais', url: '/parametres?onglet=promotions', icon: <Settings />},
      ] : []),
    ].filter(i => !k || key(i.title + ' ' + (i.subtitle ?? '')).includes(k));
    const found = (hits.query === debounced && key(debounced) === key(text) ? hits.items : []).map((h) => ({ kind: h.type, title: h.title, subtitle: h.subtitle, url: h.url, icon: h.type === 'Reçu' ? <Receipt /> : <User /> }));
    const seen = new Set<string>();
    return [...found, ...settings, ...pages].filter((entry) => {
      if (seen.has(entry.url)) return false;
      seen.add(entry.url);
      return true;
    }).slice(0, 14);
  }, [groups, hits, text, debounced, canConfigureSchool]);

  useEffect(() => setIndex(0), [text, hits.items.length]);
  const activeIndex = entries.length ? Math.min(index, entries.length - 1) : -1;
  useEffect(() => {
    if (activeIndex >= 0) document.getElementById(`${listId}-${activeIndex}`)?.scrollIntoView({ block: 'nearest' });
  }, [activeIndex, entries, listId]);

  const go = (entry?: Entry) => {
    if (!entry) return;
    setNavigationError('');
    if (entry.url === 'action:preferences') { onClose(); onPreferences(); return; }
    if (!validSearchDestination(entry.url)) {
      setNavigationError('Ce résultat ne correspond pas à une destination disponible.');
      return;
    }
    onClose();
    navigate(entry.url);
  };

  return (
    <div className="overlay" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div ref={ref} className="palette" role="dialog" aria-modal="true" aria-label="Recherche globale" tabIndex={-1}>
        <div className="input-group">
          <Search />
          <input autoFocus className="input" role="combobox" aria-label="Rechercher un apprenant, un reçu ou une page" aria-expanded="true" aria-autocomplete="list" aria-controls={listId} aria-activedescendant={activeIndex >= 0 ? `${listId}-${activeIndex}` : undefined} placeholder="Nom, matricule, n° de reçu ou page…" value={text} onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'ArrowDown') { e.preventDefault(); setIndex((i) => Math.min(i + 1, Math.max(0, entries.length - 1))); }
              if (e.key === 'ArrowUp') { e.preventDefault(); setIndex((i) => Math.max(i - 1, 0)); }
              if (e.key === 'Enter') { e.preventDefault(); go(entries[activeIndex]); }
            }} />
        </div>
        {navigationError && <p role="alert" className="small" style={{ margin: '8px 16px', color: 'var(--red)' }}>{navigationError}</p>}
        <ul id={listId} role="listbox" aria-label="Résultats de recherche">
          {entries.map((e, i) => (
            <li key={e.kind + e.url} id={`${listId}-${i}`} role="option" aria-selected={i === activeIndex} className={i === activeIndex ? 'on' : ''} onMouseEnter={() => setIndex(i)} onClick={() => go(e)}>
              {e.icon}
              <div><div style={{ fontWeight: 600 }}>{e.title}</div>{e.subtitle && <div className="small muted">{e.subtitle}</div>}</div>
              <span className="kind">{e.kind}</span>
              {i === activeIndex && <ArrowRight size={16} />}
            </li>
          ))}
          {entries.length === 0 && <li className="muted">Aucun résultat.</li>}
        </ul>
      </div>
    </div>
  );
}
