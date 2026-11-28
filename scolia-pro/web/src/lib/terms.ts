/**
 * Vocabulaire du profil d'établissement : « apprenant » dans un ITM/IM, « élève » dans une école,
 * « étudiant » dans une université. Les adresses de l'API (/api/apprenants) ne changent jamais.
 */
const SCHOOL = new Set(['Maternelle', 'Primaire', 'Secondaire', 'ComplexeScolaire']);

export function isItm(type?: string | null): boolean {
  return !type || (!SCHOOL.has(type) && type !== 'SuperieurLmd');
}

export function terms(type?: string | null): { singular: string; plural: string } {
  if (type && SCHOOL.has(type)) return { singular: 'élève', plural: 'élèves' };
  if (type === 'SuperieurLmd') return { singular: 'étudiant', plural: 'étudiants' };
  return { singular: 'apprenant', plural: 'apprenants' };
}

const capital = (w: string) => w.charAt(0).toUpperCase() + w.slice(1);

/** Remplace « apprenant(s) » en gardant la majuscule et le pluriel du texte d'origine. */
export function adapt(text: string, type?: string | null): string {
  const t = terms(type);
  if (t.singular === 'apprenant') return text;
  return text.replace(/\b([Aa])pprenant(s?)\b/g, (_m, a: string, s: string) => {
    const word = s ? t.plural : t.singular;
    return a === 'A' ? capital(word) : word;
  });
}
