import { describe, expect, it } from 'vitest';
import { accountHome, canOpenPath, requiredPermission } from './access';

describe('Navigation intuitive selon le rôle', () => {
  const promoter = ['pilotage.lire'];
  const cashier = ['apprenants.lire', 'paiements.lire', 'paiements.encaisser', 'rapports.lire'];
  it('dirige le Promoteur vers la synthèse et non les dossiers', () => {
    expect(accountHome(promoter)).toBe('/pilotage');
    expect(canOpenPath('/pilotage', promoter)).toBe(true);
    expect(canOpenPath('/apprenants', promoter)).toBe(false);
    expect(canOpenPath('/apprenants/1234', promoter)).toBe(false);
    expect(canOpenPath('/paiements/recus/1234', promoter)).toBe(false);
    expect(canOpenPath('/encaisser', promoter)).toBe(false);
  });
  it('permet aux caissiers les pages nécessaires', () => {
    expect(accountHome(cashier)).toBe('/');
    expect(canOpenPath('/encaisser', cashier)).toBe(true);
    expect(canOpenPath('/paiements/recus/1234', cashier)).toBe(true);
    expect(canOpenPath('/utilisateurs', cashier)).toBe(false);
  });
  it('n’autorise pas les chemins inconnus ni les commandes mal dirigées', () => {
    expect(requiredPermission('/inconnue')).toBeUndefined();
    expect(canOpenPath('/inconnue', ['pilotage.lire'])).toBe(false);
    expect(canOpenPath('/profil', promoter)).toBe(true);
  });
});
