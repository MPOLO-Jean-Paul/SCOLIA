/** Validation commune aux formulaires d'inscription web (bureau et écran étroit). */
export type RegistrationIdentity = {
  lastName: string; middleName: string; firstName: string; gender: string;
  birthDate: string; birthPlace: string; guardianPhone: string;
};

export type RegistrationErrors = Partial<Record<keyof RegistrationIdentity | 'promotionId' | 'enrolledOn', string>>;

function validIsoDate(value: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const parsed = new Date(value + 'T00:00:00Z');
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}

/** Indique le champ à corriger AVANT l'envoi réseau ; le serveur reste l'autorité finale. */
export function validateRegistration(
  form: RegistrationIdentity, promotionId: string, enrolledOn: string, currentDate: string,
): RegistrationErrors {
  const errors: RegistrationErrors = {};
  for (const key of ['lastName', 'middleName', 'firstName', 'birthPlace'] as const) {
    if (!form[key].trim()) errors[key] = 'Ce champ est obligatoire.';
  }
  if (form.gender !== 'F' && form.gender !== 'M') errors.gender = 'Choisissez le sexe.';
  if (!validIsoDate(form.birthDate) || form.birthDate > currentDate)
    errors.birthDate = 'Indiquez une date de naissance valide, non future.';
  if (!/^0\d{9}$/.test(form.guardianPhone.trim()))
    errors.guardianPhone = 'Saisissez 10 chiffres commençant par 0 (ex. 0812345678).';
  if (!promotionId) errors.promotionId = 'Choisissez une promotion.';
  if (!validIsoDate(enrolledOn) || enrolledOn > currentDate)
    errors.enrolledOn = 'Choisissez une date d’inscription valide, non future.';
  return errors;
}

/** Une confirmation d'homonyme ne doit jamais valoir pour une identité modifiée. */
export function registrationSignature(form: RegistrationIdentity, promotionId: string, enrolledOn: string) {
  return JSON.stringify([promotionId, enrolledOn, form.lastName.trim().toUpperCase(),
    form.middleName.trim().toUpperCase(), form.firstName.trim().toUpperCase(),
    form.gender, form.birthDate, form.birthPlace.trim().toUpperCase(), form.guardianPhone.trim()]);
}
