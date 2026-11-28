import { describe, expect, it } from 'vitest';
import { adapt, isItm } from './terms';

describe('vocabulaire du profil', () => {
  it('garde « apprenant » dans un ITM/IM', () => {
    expect(adapt('Liste des apprenants', 'ItmIm')).toBe('Liste des apprenants');
    expect(isItm(undefined)).toBe(true);
  });
  it('dit « élève » dans une école et « étudiant » dans une université', () => {
    expect(adapt('Apprenants actifs', 'Primaire')).toBe('Élèves actifs');
    expect(adapt("Inscrit un apprenant. Fiche de l'apprenant", 'Secondaire')).toBe("Inscrit un élève. Fiche de l'élève");
    expect(adapt('Apprenant', 'SuperieurLmd')).toBe('Étudiant');
    expect(isItm('SuperieurLmd')).toBe(false);
  });
});
