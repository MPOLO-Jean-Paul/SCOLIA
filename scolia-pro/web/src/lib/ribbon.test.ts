import { describe, expect, it } from 'vitest';
import { allCommands, buildTabs } from './ribbon';
import { P } from './session';

const everything = () => true;
const nothing = () => false;
const base = { recents: [{ to: '/apprenants', label: 'Apprenants' }, { to: '/caisse', label: 'Caisse' }], pathname: '/' };

describe('ruban', () => {
  it('donne une touche d’accès unique à chaque onglet', () => {
    const keys = buildTabs({ ...base, can: everything }).map((t) => t.key);
    expect(new Set(keys).size).toBe(keys.length);
    expect(keys).not.toContain('F'); // réservé à Fichier
  });

  it('donne une touche d’accès unique à chaque commande d’un même onglet', () => {
    for (const tab of buildTabs({ ...base, can: everything })) {
      const keys = tab.groups.flatMap((g) => g.commands).map((c) => c.key);
      expect(new Set(keys).size, `onglet ${tab.label}`).toBe(keys.length);
    }
  });

  it('masque les commandes dont l’utilisateur n’a pas la permission', () => {
    const commands = buildTabs({ ...base, can: (p) => p === P.reportsRead }).flatMap((t) => t.groups.flatMap((g) => g.commands));
    expect(commands.every((c) => !c.perm || c.perm === P.reportsRead)).toBe(true);
    expect(commands.some((c) => c.id === 'encaisser')).toBe(false);
  });

  it('n’affiche aucun onglet vide sans permission', () => {
    const labels = buildTabs({ ...base, can: nothing }).map((t) => t.label);
    expect(labels).toEqual(['Accueil', 'Affichage', 'Aide']);
  });

  it('ajoute l’onglet contextuel du reçu et de la fiche apprenant seulement sur leur page', () => {
    const receipt = buildTabs({ ...base, pathname: '/paiements/recus/0190f2a4-0000-7000-8000-000000000001', can: everything });
    expect(receipt.find((t) => t.context)?.contextLabel).toBe('Outils du reçu');
    const student = buildTabs({ ...base, pathname: '/apprenants/0190f2a4-0000-7000-8000-000000000002', can: everything });
    expect(student.find((t) => t.context)?.contextLabel).toBe('Outils de l’apprenant');
    expect(buildTabs({ ...base, pathname: '/paiements', can: everything }).some((t) => t.context)).toBe(false);
  });

  it('limite le groupe Récents à six pages', () => {
    const recents = Array.from({ length: 9 }, (_, i) => ({ to: `/page-${i}`, label: `Page ${i}` }));
    const accueil = buildTabs({ pathname: '/', recents, can: everything })[0];
    const recentGroup = accueil.groups.find((g) => g.title === 'Récents');
    expect(recentGroup?.commands).toHaveLength(6);
  });

  it('ne propose que des navigations vers des chemins absolus', () => {
    const targets = allCommands().filter((c) => c.to).map((c) => c.to!);
    expect(targets.length).toBeGreaterThan(10);
    expect(targets.every((t) => t.startsWith('/'))).toBe(true);
  });
});
