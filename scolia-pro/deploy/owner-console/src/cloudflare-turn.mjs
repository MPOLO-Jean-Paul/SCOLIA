// Répertoire Cloudflare Realtime TURN : métadonnées uniquement.
// L'inventaire propriétaire ne fournit jamais de credentials TURN à un navigateur.
// Clé Cloudflare avec permission Calls Read, limitée au compte, gardée en secret Worker.
// Un TURN (turns://) n'est PAS un relais WebSocket (wss://).
import { cleanLabel } from "./policy.mjs";

const TURN_HOSTS = Object.freeze([
  "stun:stun.cloudflare.com:3478",
  "turn:turn.cloudflare.com:3478?transport=udp",
  "turn:turn.cloudflare.com:443?transport=udp",
  "turn:turn.cloudflare.com:3478?transport=tcp",
  "turn:turn.cloudflare.com:80?transport=tcp",
  "turns:turn.cloudflare.com:5349?transport=tcp",
  "turns:turn.cloudflare.com:443?transport=tcp"
]);
const ACCOUNT_RE = /^[a-f0-9]{32}$/i;
const UID_RE = /^[a-f0-9]{32}$/i;

export async function discoverCloudflareTurn(env, fetcher = fetch) {
  const account = String(env.CLOUDFLARE_TURN_ACCOUNT_ID || env.CLOUDFLARE_ACCOUNT_ID || "").trim();
  const token = env.CLOUDFLARE_TURN_DISCOVERY_TOKEN;
  const base = {
    source: "cloudflare_realtime_turn",
    provider: "Cloudflare Realtime TURN",
    type: "turn",
    transport: "webrtc_ice",
    configured: false,
    relays: [],
    stun_url: "stun:stun.cloudflare.com:3478",
    notice: "TURN WebRTC et relais WSS sont deux protocoles distincts ; aucun fournisseur n'est activé automatiquement."
  };
  if (!ACCOUNT_RE.test(account) || typeof token !== "string" || token.length < 20)
    return { ...base, status: "configuration_required",
      explanation: "Ajoutez au Worker CLOUDFLARE_TURN_ACCOUNT_ID et CLOUDFLARE_TURN_DISCOVERY_TOKEN (Calls Read) pour répertorier vos relais TURN Cloudflare." };
  let response;
  try {
    response = await fetcher(
      `https://api.cloudflare.com/client/v4/accounts/${account}/calls/turn_keys`,
      {
        method: "GET",
        headers: { "Authorization": `Bearer ${token}`, "Accept": "application/json" },
        signal: AbortSignal.timeout(8000),
        redirect: "manual"
      }
    );
  } catch {
    return { ...base, configured: true, status: "network_unavailable",
      explanation: "Impossible de consulter Cloudflare pour le moment. Les relais manuels restent disponibles." };
  }
  // Cloudflare Workers ne prend pas en charge redirect: "error".
  // redirect: "manual" interdit de suivre un 3xx avec le Bearer token.
  if (response.status >= 300 && response.status < 400)
    return { ...base, configured: true, status: "unexpected_redirect",
      explanation: "L'API Cloudflare a redirigé la requête. Aucun jeton n'a été transmis à cette destination." };
  if (!response.ok)
    return { ...base, configured: true, status: response.status === 403 || response.status === 401
      ? "read_permission_required" : "cloudflare_unavailable",
      explanation: "Le catalogue Cloudflare est inaccessible. Vérifier uniquement la permission Calls Read du jeton de découverte." };
  let parsed;
  try { parsed = await response.json(); } catch {
    return { ...base, configured: true, status: "invalid_response",
      explanation: "Cloudflare a retourné un format de catalogue inattendu." };
  }
  if (parsed?.success !== true || !Array.isArray(parsed.result))
    return { ...base, configured: true, status: "invalid_response",
      explanation: "Réponse du catalogue Cloudflare non exploitable." };
  return {
    ...base, configured: true, status: "available",
    relays: parsed.result.slice(0, 100).filter(x => x && UID_RE.test(x.uid))
      .map(x => ({
        uid: x.uid,
        label: cleanLabel(x.name, 90) || "Relais Cloudflare sans nom",
        provider: "Cloudflare Realtime TURN",
        transport: "webrtc_ice",
        available_urls: TURN_HOSTS,
        credentials: "credentials_short_lived_required",
        configured_for_school: false,
        source: "cloudflare_api",
        // Ne jamais exposer les secrets, API tokens ni identités des clients.
      })),
    explanation: "Noms détectés automatiquement. Une clé TURN Cloudflare ne suffit pas : des identifiants temporaires seront nécessaires pour WebRTC, et chaque établissement doit autoriser la connexion."
  };
}
