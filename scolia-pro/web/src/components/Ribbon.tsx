import { useEffect, useRef, useState, type CSSProperties, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react';
import { ArrowDownRight, ChevronDown, ChevronUp } from 'lucide-react';
import type { RibbonCommand, RibbonGroup, RibbonTab, Tone } from '../lib/ribbon';

const TONE: Record<Tone, string> = { blue: 'var(--tone-blue)', green: 'var(--tone-green)', orange: 'var(--tone-orange)', purple: 'var(--tone-purple)', red: 'var(--tone-red)' };

interface RibbonProps {
  tabs: RibbonTab[];
  activeId: string;
  onSelect: (id: string) => void;
  onFile: () => void;
  pressed: Record<string, boolean>;
  collapsed: boolean;
  onToggleCollapsed: () => void;
  onExecute: (command: RibbonCommand) => void;
  onLaunch: (id: string) => void;
}

type Phase = 'off' | 'tabs' | 'commands';

/** Ruban à onglets, sur le modèle des suites bureautiques : groupes de commandes, touches d'accès, onglets contextuels. */
export function Ribbon({ tabs, activeId, onSelect, onFile, pressed, collapsed, onToggleCollapsed, onExecute, onLaunch }: RibbonProps) {
  const [peek, setPeek] = useState(false);
  const [phase, setPhase] = useState<Phase>('off');
  const root = useRef<HTMLDivElement>(null);
  const active = tabs.find((t) => t.id === activeId) ?? tabs[0];
  const open = !collapsed || peek;

  const execute = (command: RibbonCommand) => {
    setPeek(false);
    setPhase('off');
    onExecute(command);
  };

  // Touches d'accès : Alt seul affiche les lettres ; la lettre ouvre un onglet, puis lance une commande.
  useEffect(() => {
    let armed = false;
    const typing = (target: EventTarget | null) =>
      target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName));
    const onKeyDown = (e: KeyboardEvent) => {
      if (document.querySelector('[aria-modal="true"]')) { armed = false; return; }
      if (e.key === 'Alt') { armed = !e.repeat; return; }
      armed = false;
      if (e.key === 'Escape') {
        setPhase('off');
        setPeek(false);
        return;
      }
      if (phase === 'off' || e.ctrlKey || e.metaKey || e.altKey || e.key.length !== 1 || typing(e.target)) return;
      e.preventDefault();
      const letter = e.key.toUpperCase();
      if (letter === 'F') { setPhase('off'); onFile(); return; }
      if (phase === 'commands') {
        const command = active?.groups.flatMap((g) => g.commands).find((c) => c.key === letter);
        if (command) { execute(command); return; }
      }
      const tab = tabs.find((t) => t.key === letter);
      if (tab) {
        onSelect(tab.id);
        if (collapsed) setPeek(true);
        setPhase('commands');
        return;
      }
      setPhase('off');
    };
    const onKeyUp = (e: KeyboardEvent) => {
      if (document.querySelector('[aria-modal="true"]')) { armed = false; return; }
      if (e.key === 'Alt' && armed) {
        armed = false;
        setPhase((p) => (p === 'off' ? 'tabs' : 'off'));
      }
    };
    const onMouseDown = (e: MouseEvent) => {
      if (root.current && !root.current.contains(e.target as Node)) setPeek(false);
    };
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    document.addEventListener('mousedown', onMouseDown);
    return () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('keyup', onKeyUp);
      document.removeEventListener('mousedown', onMouseDown);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tabs, active, phase, collapsed, onSelect, onFile, onExecute]);

  const clickTab = (tab: RibbonTab) => {
    setPhase('off');
    if (collapsed) setPeek(!(tab.id === activeId && peek));
    onSelect(tab.id);
  };

  const onTablistKey = (e: ReactKeyboardEvent<HTMLDivElement>) => {
    const index = tabs.findIndex((t) => t.id === activeId);
    let next = -1;
    if (e.key === 'ArrowRight') next = (index + 1) % tabs.length;
    if (e.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length;
    if (e.key === 'Home') next = 0;
    if (e.key === 'End') next = tabs.length - 1;
    if (next < 0) return;
    e.preventDefault();
    onSelect(tabs[next].id);
    document.getElementById(`tab-${tabs[next].id}`)?.focus();
  };

  return (
    <div className={`ribbon ${collapsed ? 'is-collapsed' : ''}`} ref={root}>
      <div className="ribbon-tabs">
        <button type="button" className="rb-file" onClick={onFile} aria-haspopup="dialog" title="Fichier : informations, sauvegardes et compte (F)">
          {phase === 'tabs' && <KeyTip k="F" />}
          Fichier
        </button>
        <div role="tablist" aria-label="Onglets du ruban" className="rb-tablist" onKeyDown={onTablistKey}>
          {tabs.map((tab) => {
            const selected = tab.id === active?.id;
            const wrapStyle = tab.context ? ({ '--ctx': TONE[tab.context] } as CSSProperties) : undefined;
            return (
              <div key={tab.id} className={`rb-tab-wrap ${tab.context ? 'has-ctx' : ''}`} style={wrapStyle}>
                {tab.context && <span className="rb-ctx-head">{tab.contextLabel}</span>}
                <button
                  type="button"
                  role="tab"
                  id={`tab-${tab.id}`}
                  aria-selected={selected}
                  aria-controls="ribbon-panel"
                  tabIndex={selected ? 0 : -1}
                  className={`rb-tab ${selected ? 'on' : ''}`}
                  onClick={() => clickTab(tab)}
                >
                  {phase === 'tabs' && <KeyTip k={tab.key} />}
                  {tab.label}
                </button>
              </div>
            );
          })}
        </div>
        <span className="rb-spacer" />
        <button
          type="button"
          className="rb-collapse"
          aria-expanded={open}
          aria-label={collapsed ? 'Afficher le ruban' : 'Réduire le ruban'}
          title={collapsed ? 'Afficher le ruban (Ctrl+F1)' : 'Réduire le ruban (Ctrl+F1)'}
          onClick={onToggleCollapsed}
        >
          {open ? <ChevronUp /> : <ChevronDown />}
        </button>
      </div>
      {open && active && (
        <div role="tabpanel" id="ribbon-panel" aria-labelledby={`tab-${active.id}`} className="ribbon-body">
          {active.groups.map((group) => (
            <Group key={group.title} group={group} pressed={pressed} showKeys={phase === 'commands'} onExecute={execute} onLaunch={onLaunch} />
          ))}
        </div>
      )}
    </div>
  );
}

function Group({ group, pressed, showKeys, onExecute, onLaunch }: {
  group: RibbonGroup; pressed: Record<string, boolean>; showKeys: boolean; onExecute: (c: RibbonCommand) => void; onLaunch: (id: string) => void;
}) {
  const blocks: ReactNode[] = [];
  let smalls: RibbonCommand[] = [];
  const flush = () => {
    if (smalls.length === 0) return;
    blocks.push(
      <div className="rb-smalls" key={`smalls-${smalls[0].id}`}>
        {smalls.map((c) => <Cmd key={c.id} command={c} pressed={pressed} showKey={showKeys} onExecute={onExecute} />)}
      </div>,
    );
    smalls = [];
  };
  for (const command of group.commands) {
    if (command.size === 'small') {
      smalls.push(command);
    } else {
      flush();
      blocks.push(<Cmd key={command.id} command={command} pressed={pressed} showKey={showKeys} onExecute={onExecute} />);
    }
  }
  flush();
  return (
    <section className="rb-group" aria-label={group.title}>
      <div className="rb-cmds">{blocks}</div>
      <div className="rb-group-name">{group.title}</div>
      {group.launcher && (
        <button type="button" className="rb-launcher" title={group.launcher.tip} aria-label={`${group.title} : ${group.launcher.tip}`} onClick={() => onLaunch(group.launcher!.id)}>
          <ArrowDownRight />
        </button>
      )}
    </section>
  );
}

function Cmd({ command, pressed, showKey, onExecute }: { command: RibbonCommand; pressed: Record<string, boolean>; showKey: boolean; onExecute: (c: RibbonCommand) => void }) {
  const on = command.toggle ? !!pressed[command.id] : undefined;
  return (
    <button
      type="button"
      className={`rb-cmd rb-${command.size} tone-${command.tone ?? 'blue'} ${on ? 'is-on' : ''}`}
      aria-label={command.label}
      aria-pressed={on}
      onClick={() => onExecute(command)}
    >
      <span className="rb-icon" aria-hidden="true">{command.icon}</span>
      <span className="rb-label">{command.label}</span>
      {showKey && <KeyTip k={command.key} />}
      <span className="rb-tip" role="presentation">
        <b>{command.label}</b>
        {command.shortcut && <kbd>{command.shortcut}</kbd>}
        <span>{command.tip}</span>
      </span>
    </button>
  );
}

function KeyTip({ k }: { k: string }) {
  return <kbd className="rb-keytip" aria-hidden="true">{k}</kbd>;
}
