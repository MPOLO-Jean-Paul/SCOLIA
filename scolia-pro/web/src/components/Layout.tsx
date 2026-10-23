import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import {
  BarChart3, Banknote, BookOpen, ClipboardCheck, Command, FileSpreadsheet, GraduationCap, HandCoins, KeyRound, LayoutDashboard, LogOut, Menu,
  Moon, PanelLeftClose, PanelLeftOpen, Receipt, Search, Settings, ShieldCheck, Sun, UserCog, Users, Wallet, Briefcase, AlertOctagon, DatabaseBackup,
} from 'lucide-react';
import { P, saveTheme, storedTheme, useSession, type Theme } from '../lib/session';
import { initials } from '../lib/format';
import { CommandPalette } from './CommandPalette';

interface NavItem { to: string; label: string; icon: ReactNode; perm?: string; shortcut?: string }
interface NavGroup { title: string; items: NavItem[] }

export const NAV: NavGroup[] = [
  { title: 'Pilotage', items: [
    { to: '/', label: 'Tableau de bord', icon: <LayoutDashboard />, perm: P.reportsRead, shortcut: 'Alt+1' },
  ] },
  { title: 'Secrétariat', items: [
    { to: '/apprenants', label: 'Apprenants', icon: <Users />, perm: P.studentsRead, shortcut: 'Alt+2' },
    { to: '/reinscriptions', label: 'Réinscriptions', icon: <GraduationCap />, perm: P.studentsWrite },
  ] },
  { title: 'Finances', items: [
    { to: '/encaisser', label: 'Encaisser', icon: <HandCoins />, perm: P.paymentsCreate, shortcut: 'Alt+3' },
    { to: '/paiements', label: 'Journal des reçus', icon: <Receipt />, perm: P.paymentsRead },
    { to: '/recouvrement', label: 'Recouvrement', icon: <BarChart3 />, perm: P.reportsRead, shortcut: 'Alt+4' },
    { to: '/prises-en-charge', label: 'Prises en charge', icon: <ShieldCheck />, perm: P.paymentsRead },
    { to: '/depenses', label: 'Dépenses', icon: <Wallet />, perm: P.paymentsRead },
    { to: '/caisse', label: 'Caisse', icon: <Banknote />, perm: P.paymentsRead },
  ] },
  { title: 'Scolarité', items: [
    { to: '/notes', label: 'Cours et notes', icon: <BookOpen />, perm: P.academicsRead },
    { to: '/presences', label: 'Présences', icon: <ClipboardCheck />, perm: P.academicsRead },
    { to: '/discipline', label: 'Discipline', icon: <AlertOctagon />, perm: P.academicsRead },
  ] },
  { title: 'Administration', items: [
    { to: '/personnel', label: 'Personnel et paie', icon: <Briefcase />, perm: P.staffManage },
    { to: '/parametres', label: 'Paramètres', icon: <Settings />, perm: P.settingsWrite },
    { to: '/utilisateurs', label: 'Utilisateurs', icon: <UserCog />, perm: P.usersManage },
    { to: '/donnees', label: 'Données et sauvegardes', icon: <DatabaseBackup />, perm: P.backupManage },
    { to: '/audit', label: 'Journal d’audit', icon: <FileSpreadsheet />, perm: P.auditRead },
  ] },
];

export function Layout() {
  const { session, logout, can } = useSession();
  const navigate = useNavigate();
  const [collapsed, setCollapsed] = useState(() => { try { return localStorage.getItem('scolia.sidebar') === '1'; } catch { return false; } });
  const [menuOpen, setMenuOpen] = useState(false);
  const [palette, setPalette] = useState(false);
  const [theme, setTheme] = useState<Theme>(storedTheme());
  const groups = useMemo(() => NAV.map((g) => ({ ...g, items: g.items.filter((i) => !i.perm || can(i.perm)) })).filter((g) => g.items.length > 0), [can]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); setPalette(true); }
      if (e.altKey && /^[1-4]$/.test(e.key)) {
        const item = groups.flatMap((g) => g.items).find((i) => i.shortcut === `Alt+${e.key}`);
        if (item) { e.preventDefault(); navigate(item.to); }
      }
      if (e.key === 'F2' && can(P.paymentsCreate)) { e.preventDefault(); navigate('/encaisser'); }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [groups, navigate, can]);

  const toggleCollapsed = () => {
    setCollapsed((c) => { try { localStorage.setItem('scolia.sidebar', c ? '0' : '1'); } catch { /* ignoré */ } return !c; });
  };
  const nextTheme = () => {
    const t: Theme = theme === 'sombre' ? 'clair' : 'sombre';
    setTheme(t);
    saveTheme(t);
  };

  return (
    <div className={`shell ${collapsed ? 'collapsed' : ''} ${menuOpen ? 'menu-open' : ''}`}>
      <aside className="sidebar" aria-label="Navigation principale">
        <div className="brand">
          <img src="/api/etablissement/logo" onError={(e) => { (e.target as HTMLImageElement).src = '/scolia.png'; }} alt="" />
          <div className="brand-text">
            <div className="brand-name">{session?.schoolShortName || 'SCOLIA'}</div>
            <div className="brand-school">{session?.schoolName}</div>
          </div>
        </div>
        <nav className="nav" onClick={() => setMenuOpen(false)}>
          {groups.map((g) => (
            <div key={g.title}>
              <div className="nav-group-title">{g.title}</div>
              {g.items.map((i) => (
                <NavLink key={i.to} to={i.to} end={i.to === '/'} title={i.shortcut ? `${i.label} (${i.shortcut})` : i.label}>
                  {i.icon}<span className="nav-label">{i.label}</span>
                </NavLink>
              ))}
            </div>
          ))}
        </nav>
        <div className="sidebar-foot">
          <button className="btn small ghost" style={{ color: '#fff', borderColor: 'rgba(255,255,255,.15)' }} onClick={toggleCollapsed} aria-label="Réduire le menu">
            {collapsed ? <PanelLeftOpen /> : <PanelLeftClose />}
          </button>
          <span className="sidebar-foot-text">SCOLIA Pro · v1.0</span>
        </div>
      </aside>
      <div className="main">
        <header className="topbar">
          <button className="btn icon ghost menu-btn" onClick={() => setMenuOpen((o) => !o)} aria-label="Menu" style={{ display: 'none' }}><Menu /></button>
          <button className="search-trigger" onClick={() => setPalette(true)}>
            <Search size={16} /><span>Rechercher un apprenant, un reçu, une page…</span><kbd>Ctrl K</kbd>
          </button>
          <div className="spacer" />
          {session?.currentYear && <span className="year-chip" title="Année scolaire courante">{session.currentYear}</span>}
          <button className="btn icon ghost" onClick={nextTheme} title="Thème clair ou sombre" aria-label="Changer de thème">{theme === 'sombre' ? <Sun /> : <Moon />}</button>
          <UserMenu name={session?.displayName ?? ''} role={session?.roleLabel ?? ''} onLogout={async () => { await logout(); navigate('/connexion'); }} />
        </header>
        <main className="content" id="contenu">
          <Outlet />
        </main>
      </div>
      {palette && <CommandPalette groups={groups} onClose={() => setPalette(false)} />}
      <style>{`@media (max-width: 820px) { .menu-btn { display: inline-flex !important; } }`}</style>
    </div>
  );
}

function UserMenu({ name, role, onLogout }: { name: string; role: string; onLogout: () => void }) {
  const [open, setOpen] = useState(false);
  const navigate = useNavigate();
  return (
    <div style={{ position: 'relative' }}>
      <button className="user-chip" onClick={() => setOpen((o) => !o)} aria-haspopup="menu" aria-expanded={open}>
        <span className="avatar">{initials(name)}</span>
        <span className="who"><div style={{ fontWeight: 700, fontSize: 13 }}>{name}</div><small>{role}</small></span>
      </button>
      {open && (
        <div className="card" role="menu" style={{ position: 'absolute', right: 0, top: 46, width: 230, zIndex: 30, padding: 6 }} onMouseLeave={() => setOpen(false)}>
          <button className="btn ghost" style={{ width: '100%', justifyContent: 'flex-start', border: 'none' }} onClick={() => { setOpen(false); navigate('/profil'); }}><KeyRound />Mon mot de passe</button>
          <button className="btn ghost" style={{ width: '100%', justifyContent: 'flex-start', border: 'none' }} onClick={() => { setOpen(false); window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true })); }}><Command />Commandes (Ctrl K)</button>
          <button className="btn ghost" style={{ width: '100%', justifyContent: 'flex-start', border: 'none', color: 'var(--red)' }} onClick={onLogout}><LogOut />Se déconnecter</button>
        </div>
      )}
    </div>
  );
}
