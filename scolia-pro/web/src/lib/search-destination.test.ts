import { describe, expect, it } from 'vitest';
import { validSearchDestination as valid } from './search-destination';

describe('recherche globale et liens directs', () => {
  it.each(['/', '/pilotage', '/apprenants', '/apprenants?nouveau=1', '/notes?action=evaluation',
    '/caisse?action=arreter', '/depenses?action=nouvelle',
    '/parametres?onglet=promotions&action=promotion',
    '/parametres?onglet=annees&action=annee',
    '/apprenants/123e4567-e89b-12d3-a456-426614174000'])('autorise %s', p => expect(valid(p)).toBe(true));
  it.each(['', '//elsewhere', '/unknown', '/apprenants?nouveau=1&nouveau=1',
    '/notes?action=reset', '/parametres?onglet=wrong', '/audit?action=delete',
    '/parametres?onglet=annees&action=promotion'])('refuse %s', p => expect(valid(p)).toBe(false));
});
