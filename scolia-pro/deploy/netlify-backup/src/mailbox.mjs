import { createHash, timingSafeEqual } from "node:crypto";

export const SERVICE = "SCOLIA Netlify HTTPS Backup";
export const PROTOCOL = "scolia-https-mailbox-v1";
const TTL = 90_000;
const MESSAGE_TTL = 90_000;
const HEX64 = /^[a-f0-9]{64}$/i;
const ID = /^[a-f0-9]{32}$/i;
const MAX_MAIL = 24;
const MAX_FRAME = 8192;
const safeReply = (data, status = 200) => Response.json(data, {
  status, headers: { "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" }
});
const reject = (status, error) => safeReply({ error }, status);
const hash = value => createHash("sha256").update(value, "utf8").digest("hex");
const matches = (a, b) => {
  if (!HEX64.test(a || "") || !HEX64.test(b || "")) return false;
  return timingSafeEqual(Buffer.from(a, "hex"), Buffer.from(b, "hex"));
};
const token = (request, kind) => request.headers.get(kind === "host"
  ? "X-Scolia-Host" : "X-Scolia-Client") || "";
const validToken = value => typeof value === "string" && HEX64.test(value);
const roomKey = room => "rooms/" + room.toLowerCase();
const messagePrefix = (room, role) => "mail/" + room.toLowerCase() + "/" + role + "/";
const validEpoch = v => {
  if (typeof v !== "string" || v.length > 64 ||
      !/^[A-Za-z0-9+/]+={0,2}$/.test(v)) return false;
  try { return Buffer.from(v, "base64").length === 32; } catch { return false; }
};
const validFrame = value => {
  if (typeof value !== "string" || value.length > 12000 ||
      !/^[A-Za-z0-9+/]+={0,2}$/.test(value)) return false;
  try {
    const bytes = Buffer.from(value, "base64");
    return bytes.length >= 40 && bytes.length <= MAX_FRAME;
  } catch { return false; }
};
async function readBody(request) {
  if (Number(request.headers.get("Content-Length") || "0") > 13000) return null;
  const raw = await request.text();
  if (raw.length > 13000) return null;
  try { return JSON.parse(raw); } catch { return null; }
}
const onlyRoom = value => HEX64.test(value || "") ? value.toLowerCase() : null;
const onlyRole = value => value === "host" || value === "client" ? value : null;
const authRoom = (session, req, role) => {
  const provided = token(req, role);
  return validToken(provided) && matches(hash(provided), role === "host"
    ? session.room : session.client_hash);
};

/**
 * Experimental, opt-in HTTPS ciphertext mailbox.
 * It is NOT a WebSocket or TURN endpoint. The SCOLIA desktop/Android clients
 * must implement an audited adapter before this channel may be activated.
 * Data are ciphertext frames only; no student, account or institution labels.
 */
export async function handleMailbox(request, {
  store,
  now = () => Date.now(),
  enabled = false
} = {}) {
  const url = new URL(request.url);
  const path = url.pathname;
  if (request.method === "GET" && path === "/api/scolia-backup/health")
    return safeReply({
      service: SERVICE, protocol: PROTOCOL, transport: "https",
      enabled: Boolean(enabled), websocket: false, turn: false,
      production_ready: false
    });
  if (!path.startsWith("/api/scolia-backup/")) return reject(404, "Route inconnue.");
  if (!enabled || !store) return reject(503, "Canal de secours expérimental désactivé.");
  if (request.headers.has("Origin")) return reject(403, "Clients Web non autorisés.");
  const room = onlyRoom(url.searchParams.get("room"));
  if (!room) return reject(400, "Salle invalide.");

  if (path === "/api/scolia-backup/session" && request.method === "POST") {
    const host = token(request, "host");
    if (!validToken(host) || !matches(hash(host), room))
      return reject(401, "Hôte non autorisé.");
    const body = await readBody(request);
    if (!HEX64.test(body?.client_hash || "") || !validEpoch(body?.epoch))
      return reject(400, "Session ou époque invalide.");
    const key = roomKey(room);
    const previous = await store.getWithMetadata(key, { type: "json", consistency: "strong" });
    // If an old message is in flight, the caller must retry after session expiry.
    if (previous?.data && previous.data.expires_at > now() &&
        (!matches(previous.data.client_hash, body.client_hash) ||
         previous.data.epoch !== body.epoch))
      return reject(409, "Session encore active.");
    const session = {
      room, client_hash: body.client_hash.toLowerCase(), epoch: body.epoch,
      expires_at: now() + TTL
    };
    const write = await store.setJSON(key, session,
      previous ? { onlyIfMatch: previous.etag } : { onlyIfNew: true });
    if (!write.modified) return reject(409, "Session modifiée simultanément.");
    return safeReply({ connected: true, expires_at: session.expires_at });
  }
  const snapshot = await store.getWithMetadata(roomKey(room),
    { type: "json", consistency: "strong" });
  const session = snapshot?.data;
  if (!session || session.expires_at <= now())
    return reject(410, "Session absente ou expirée.");

  if (path === "/api/scolia-backup/session" && request.method === "GET") {
    if (!authRoom(session, request, "client")) return reject(401, "Client non autorisé.");
    return safeReply({ epoch: session.epoch, expires_at: session.expires_at });
  }
  if (path !== "/api/scolia-backup/mail") return reject(404, "Route inconnue.");
  const role = onlyRole(url.searchParams.get("role"));
  if (!role || !authRoom(session, request, role))
    return reject(401, "Participant non autorisé.");

  if (request.method === "POST") {
    const body = await readBody(request);
    if (!ID.test(body?.id || "") || !validFrame(body?.ciphertext))
      return reject(400, "Trame chiffrée non conforme.");
    const recipient = role === "host" ? "client" : "host";
    const prefix = messagePrefix(room, recipient);
    const list = await store.list({ prefix });
    if ((list.blobs || []).length >= MAX_MAIL)
      return reject(429, "File de messages complète.");
    const key = prefix + body.id.toLowerCase();
    const message = {
      id: body.id.toLowerCase(), ciphertext: body.ciphertext,
      expires_at: Math.min(now() + MESSAGE_TTL, session.expires_at),
      acknowledged: false
    };
    const receipt = await store.setJSON(key, message, { onlyIfNew: true });
    if (!receipt.modified) return reject(409, "Message déjà envoyé.");
    return safeReply({ accepted: true, id: message.id, expires_at: message.expires_at }, 201);
  }
  if (request.method === "GET") {
    const list = await store.list({ prefix: messagePrefix(room, role) });
    const result = [];
    for (const entry of (list.blobs || []).slice(0, MAX_MAIL)) {
      if (!ID.test(entry.key.split("/").at(-1) || "")) continue;
      const data = await store.get(entry.key, { type: "json", consistency: "strong" });
      if (data && !data.acknowledged && data.expires_at > now())
        result.push({ id: data.id, ciphertext: data.ciphertext, expires_at: data.expires_at });
      if (result.length === 12) break;
    }
    return safeReply({ messages: result, poll_after_ms: 2500 });
  }
  if (request.method === "DELETE") {
    const id = url.searchParams.get("id");
    if (!ID.test(id || "")) return reject(400, "Identifiant invalide.");
    const key = messagePrefix(room, role) + id.toLowerCase();
    const item = await store.getWithMetadata(key, { type: "json", consistency: "strong" });
    if (!item?.data) return reject(404, "Message absent.");
    // Preserve a tombstone until TTL: replay with the same id cannot recreate it.
    if (!item.data.acknowledged) {
      const changed = await store.setJSON(key, { ...item.data, ciphertext: "",
        acknowledged: true }, { onlyIfMatch: item.etag });
      if (!changed.modified) return reject(409, "Accusé concurrent : relire l'état.");
    }
    return safeReply({ acknowledged: true });
  }
  return reject(405, "Méthode non autorisée.");
}
