// Facturation et restrictions à l'échelle d'un établissement serveur.
// Pas de comptes internes, pas de dossiers scolaires, pas de facturation cachée.
export const OVERRIDES = new Set(["none", "allow", "read_only"]);
export const DAY_MS = 86_400_000;
export const MAX_TRIAL_DAYS = 365;
export const MAX_GRACE_DAYS = 120;
export const DEFAULT_TRIAL_DAYS = 30;

const asMs = value => Number.isSafeInteger(value) && value > 0 ? value : null;

export function evaluateLicense(school, now = Date.now()) {
  if (!school || !Number.isFinite(now)) throw new Error("Établissement ou horloge invalide");
  const created = asMs(school.created_at) ?? now;
  const trialDays = Math.max(0, Math.min(MAX_TRIAL_DAYS,
    Number.isSafeInteger(school.trial_days) ? school.trial_days : DEFAULT_TRIAL_DAYS));
  const trialEnd = created + trialDays * DAY_MS;
  const paidUntil = asMs(school.paid_until);
  const graceUntil = asMs(school.grace_until);
  const overrideUntil = asMs(school.override_until);
  const manual = OVERRIDES.has(school.manual_override) ? school.manual_override : "none";
  const overrideValid = overrideUntil === null || overrideUntil > now;
  let status = "free";
  let reason = "facturation_desactivee";
  let validUntil = null;
  let readOnly = false;

  if (manual === "read_only" && overrideValid) {
    status = "suspended";
    reason = "blocage_manuel";
    readOnly = true;
  } else if (manual === "allow" && overrideValid) {
    status = "derogation";
    reason = "acces_manuel_autorise";
    validUntil = overrideUntil;
  } else if (!school.billing_enabled) {
    status = "free";
  } else if (paidUntil !== null && paidUntil > now) {
    status = "active";
    reason = "paiement_valide";
    validUntil = paidUntil;
  } else if (trialEnd > now) {
    status = "trial";
    reason = "periode_essai";
    validUntil = trialEnd;
  } else if (graceUntil !== null && graceUntil > now) {
    status = "grace";
    reason = "delai_supplementaire";
    validUntil = graceUntil;
  } else {
    status = "expired";
    reason = "echeance_atteinte";
    readOnly = true;
    validUntil = Math.max(paidUntil ?? 0, trialEnd, graceUntil ?? 0) || null;
  }
  // Seules les dates du serveur central sont sources de vérité.
  const remainingDays = validUntil === null ? null :
    Math.max(0, Math.ceil((validUntil - now) / DAY_MS));
  return {
    license_status: status,
    access_mode: readOnly ? "read_only" : "full",
    readonly: readOnly,
    reason,
    billing_enabled: Boolean(school.billing_enabled),
    days_remaining: remainingDays,
    expires_at: validUntil,
    trial_ends_at: trialEnd,
    manual_override: manual,
    issued_at: now,
    // Les postes doivent pouvoir fonctionner hors ligne lors des interruptions.
    // Un renouvellement reste nécessaire pour appliquer une nouvelle restriction.
    offline_grace_seconds: 7 * 86400,
    next_check_seconds: 900
  };
}

function checkedDate(value, allowNull = true) {
  if (value === null && allowNull) return null;
  return Number.isSafeInteger(value) && value > 0 ? value : undefined;
}

export function validateLicensePatch(data) {
  if (!data || typeof data !== "object" || Array.isArray(data)) return null;
  const result = {};
  if ("billing_enabled" in data) {
    if (typeof data.billing_enabled !== "boolean") return null;
    result.billing_enabled = data.billing_enabled ? 1 : 0;
  }
  if ("trial_days" in data) {
    if (!Number.isInteger(data.trial_days) || data.trial_days < 0 ||
        data.trial_days > MAX_TRIAL_DAYS) return null;
    result.trial_days = data.trial_days;
  }
  for (const key of ["paid_until", "grace_until", "override_until"]) {
    if (key in data) {
      const time = checkedDate(data[key]);
      if (time === undefined) return null;
      result[key] = time;
    }
  }
  if ("manual_override" in data) {
    if (!OVERRIDES.has(data.manual_override)) return null;
    result.manual_override = data.manual_override;
  }
  if ("reason" in data) {
    if (typeof data.reason !== "string") return null;
    result.reason = data.reason.trim().slice(0, 180);
  }
  return Object.keys(result).length ? result : null;
}
