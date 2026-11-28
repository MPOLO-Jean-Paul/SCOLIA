// SCOLIA Pro: metadata-only licensing. Never accept names of pupils,
// internal school users, grades, payments or document bodies in clear text.
// Online continuity (cloud.mjs) stores only blobs encrypted by the school,
// with a key JP-Services never receives.
export const CONSENT_VERSION = "2026-11-08";
export const STATES = new Set(["trial", "active", "suspended", "expired"]);
export const EVENTS = new Set(["install", "login", "online", "logout"]);

export function cleanLabel(value, max = 100) {
  if (typeof value !== "string") return "";
  return value.trim().replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g, " ").slice(0, max);
}
export function parseRegistration(value) {
  if (!value || typeof value !== "object" || value.consent !== true ||
      value.consent_version !== CONSENT_VERSION)
    return { ok: false, error: "Information et accord explicite de l'établissement requis." };
  const school = cleanLabel(value.school_name, 140);
  const device = cleanLabel(value.server_device_name, 70);
  const code = cleanLabel(value.invite_code, 140);
  const deviceId = cleanLabel(value.installation_id, 96);
  if (school.length < 3 || device.length < 2 || code.length < 20 ||
      !/^[a-zA-Z0-9_-]{16,96}$/.test(deviceId))
    return { ok: false, error: "Inscription établissement incomplète." };
  return { ok: true, school, device, code, deviceId };
}
export function parseHeartbeat(value) {
  const event = cleanLabel(value?.event, 20);
  const deviceId = cleanLabel(value?.installation_id, 96);
  const device = cleanLabel(value?.server_device_name, 70);
  const eventId = cleanLabel(value?.event_id, 36);
  const occurredAt = Date.parse(value?.occurred_at ?? "");
  // La date hors ligne doit être explicite et limitée à 90 jours de reprise.
  if (!EVENTS.has(event) || !/^[a-zA-Z0-9_-]{16,96}$/.test(deviceId)
      || device.length < 2 || !/^[a-f0-9-]{36}$/i.test(eventId)
      || !Number.isFinite(occurredAt))
    return null;
  return { event, deviceId, device, eventId, occurredAt };
}
export function licenseState(state, expiresAt, now = Date.now()) {
  if (state === "suspended") return "suspended";
  if (state === "expired") return "expired";
  if (expiresAt !== null && expiresAt !== undefined && Number(expiresAt) <= now)
    return "expired";
  return STATES.has(state) ? state : "trial";
}
export function normalizeIp(input) {
  // Addresses are retained for 30 days only, shown to the owner only.
  if (typeof input !== "string") return null;
  const ip = input.trim();
  return /^[0-9a-fA-F:.]{2,45}$/.test(ip) ? ip : null;
}
export function clientPolicy(state) {
  return {
    license_status: state,
    // Do not make offline educational records inaccessible because a cloud
    // control service is unavailable; access control requires a separate,
    // carefully tested license policy on the institution's main server.
    local_data_access: "unaffected",
    next_check_seconds: 3600
  };
}
