import { describe, expect, it } from 'vitest';
import { date, fullName, initials, int, key, money, pct, setCurrency } from './format';

// Séparateur des milliers en français : espace fine insécable (U+202F).
const s = ' ';

describe('formatage français', () => {
  it('affiche les montants entiers avec la devise de l’établissement', () => {
    setCurrency('FC');
    expect(money(1500000)).toBe(`1${s}500${s}000 FC`);
    expect(money(0)).toBe('0 FC');
    expect(money(null)).toBe('—');
    setCurrency('$');
    expect(money(25)).toBe('25 $');
    setCurrency('');
    expect(money(10)).toBe('10 FC');
  });

  it('formate entiers et pourcentages', () => {
    expect(int(12345)).toBe(`12${s}345`);
    expect(pct(87.5)).toBe('87,5 %');
    expect(pct(undefined)).toBe('—');
  });

  it('présente les dates ISO au format jour/mois/année', () => {
    expect(date('2026-10-06')).toBe('06/10/2026');
    expect(date('2026-10-06T08:15:00Z')).toBe('06/10/2026');
    expect(date(null)).toBe('—');
  });
});

describe('identité des apprenants', () => {
  it('normalise comme le serveur : majuscules, sans accents', () => {
    expect(key('  Éléonore Ngalula ')).toBe('ELEONORE NGALULA');
    expect(key('çàü')).toBe('CAU');
  });

  it('compose le nom complet et les initiales', () => {
    expect(fullName({ lastName: 'MATONDO', middleName: '', firstName: 'Plamedie' })).toBe('MATONDO Plamedie');
    expect(initials('MATONDO NGALULA Plamedie')).toBe('MN');
  });
});
