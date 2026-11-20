// Stockage des sauvegardes chiffrées par les écoles.
//
// Deux possibilités, choisies automatiquement :
// 1. la liaison R2 du Worker (« BACKUPS »), créée par le workflow de déploiement ;
// 2. sinon, les identifiants R2 saisis par JP-Services dans la console (API compatible S3).
//    Le secret est gardé chiffré dans D1 (AES-256-GCM, clé dérivée du jeton propriétaire) ;
//    il n'est jamais renvoyé au navigateur.

const SETTINGS_KEY = "storage.r2";
const ACCOUNT = /^[a-f0-9]{32}$/i;
const BUCKET = /^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$/;
const ACCESS_KEY = /^[A-Za-z0-9]{16,128}$/;
const encoder = new TextEncoder();

const toHex = buffer => Array.from(new Uint8Array(buffer), n => n.toString(16).padStart(2, "0")).join("");
const sha256Hex = async data => toHex(await crypto.subtle.digest("SHA-256", typeof data === "string" ? encoder.encode(data) : data));
async function hmac(key, data) {
  const material = await crypto.subtle.importKey("raw", typeof key === "string" ? encoder.encode(key) : key,
    { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return crypto.subtle.sign("HMAC", material, encoder.encode(data));
}
const b64 = bytes => btoa(String.fromCharCode(...new Uint8Array(bytes)));
const unb64 = text => Uint8Array.from(atob(text), c => c.charCodeAt(0));

// ---------- Signature AWS SigV4 (R2 parle l'API S3) ----------

const encodePath = path => path.split("/").map(segment => encodeURIComponent(segment)
  .replace(/[!'()*]/g, c => "%" + c.charCodeAt(0).toString(16).toUpperCase())).join("/");

/** En-têtes signés d'une requête S3 (SigV4). Exposé pour les tests de conformité. */
export async function signS3({ method, host, path, headers = {}, payloadHash, accessKeyId, secretAccessKey, region = "auto", amzDate }) {
  const date = amzDate.slice(0, 8);
  const all = { host, "x-amz-content-sha256": payloadHash, "x-amz-date": amzDate };
  for (const [k, v] of Object.entries(headers)) all[k.toLowerCase()] = String(v).trim();
  const names = Object.keys(all).sort();
  const canonical = [method, encodePath(path), "", names.map(n => `${n}:${all[n]}`).join("\n") + "\n", names.join(";"), payloadHash].join("\n");
  const scope = `${date}/${region}/s3/aws4_request`;
  const toSign = ["AWS4-HMAC-SHA256", amzDate, scope, await sha256Hex(canonical)].join("\n");
  let key = await hmac("AWS4" + secretAccessKey, date);
  key = await hmac(key, region);
  key = await hmac(key, "s3");
  key = await hmac(key, "aws4_request");
  const signature = toHex(await hmac(key, toSign));
  const signed = { ...all };
  delete signed.host;
  signed.authorization = `AWS4-HMAC-SHA256 Credential=${accessKeyId}/${scope}, SignedHeaders=${names.join(";")}, Signature=${signature}`;
  return signed;
}

function s3Bucket(config, fetcher = fetch) {
  const host = `${config.account_id}.r2.cloudflarestorage.com`;
  async function call(method, key, body = null) {
    const path = `/${config.bucket}/${key}`;
    const bytes = body ? new Uint8Array(body) : null;
    const amzDate = new Date().toISOString().replace(/[:-]|\.\d{3}/g, "");
    const headers = await signS3({ method, host, path, amzDate,
      payloadHash: bytes ? await sha256Hex(bytes) : await sha256Hex(""),
      accessKeyId: config.access_key_id, secretAccessKey: config.secret_access_key,
      headers: bytes ? { "content-type": "application/octet-stream" } : {} });
    return fetcher(`https://${host}${encodePath(path)}`, { method, headers, body: bytes ?? undefined, redirect: "error" });
  }
  return {
    async put(key, value) {
      const response = await call("PUT", key, value);
      if (!response.ok) throw new Error(`R2 : envoi refusé (${response.status}).`);
    },
    async get(key) {
      const response = await call("GET", key);
      if (response.status === 404) return null;
      if (!response.ok) throw new Error(`R2 : lecture refusée (${response.status}).`);
      return { body: response.body };
    },
    async delete(key) {
      const response = await call("DELETE", key);
      if (!response.ok && response.status !== 404) throw new Error(`R2 : suppression refusée (${response.status}).`);
    },
  };
}

// ---------- Secret chiffré dans D1 ----------

async function settingsKey(env) {
  if (!env.OWNER_ADMIN_TOKEN) throw new Error("Jeton propriétaire absent.");
  const base = await crypto.subtle.importKey("raw", encoder.encode(env.OWNER_ADMIN_TOKEN), "HKDF", false, ["deriveKey"]);
  return crypto.subtle.deriveKey({ name: "HKDF", hash: "SHA-256", salt: encoder.encode("scolia-owner-settings"), info: encoder.encode("v1") },
    base, { name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
}

async function seal(env, value) {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const data = await crypto.subtle.encrypt({ name: "AES-GCM", iv }, await settingsKey(env), encoder.encode(JSON.stringify(value)));
  return b64(iv) + "." + b64(data);
}

async function open(env, text) {
  const [iv, data] = String(text).split(".");
  const clear = await crypto.subtle.decrypt({ name: "AES-GCM", iv: unb64(iv) }, await settingsKey(env), unb64(data));
  return JSON.parse(new TextDecoder().decode(clear));
}

async function savedConfig(env) {
  const row = await env.OWNER_DB.prepare("SELECT value,updated_at FROM owner_settings WHERE key=?").bind(SETTINGS_KEY).first();
  if (!row) return null;
  try { return { ...(await open(env, row.value)), updated_at: row.updated_at }; }
  catch { return { unreadable: true, updated_at: row.updated_at }; }
}

/** Stockage utilisable par les routes de sauvegarde, ou null s'il n'est pas configuré. */
export async function backupStore(env, fetcher = env.STORAGE_FETCH) {
  if (env.BACKUPS) return env.BACKUPS;
  const config = await savedConfig(env);
  if (!config || config.unreadable) return null;
  return s3Bucket(config, fetcher);
}

export function validateStorageInput(body) {
  const config = {
    account_id: String(body?.account_id ?? "").trim().toLowerCase(),
    bucket: String(body?.bucket ?? "").trim().toLowerCase(),
    access_key_id: String(body?.access_key_id ?? "").trim(),
    secret_access_key: String(body?.secret_access_key ?? "").trim(),
  };
  if (!ACCOUNT.test(config.account_id)) return { error: "Identifiant de compte Cloudflare : 32 caractères hexadécimaux." };
  if (!BUCKET.test(config.bucket)) return { error: "Nom du bucket R2 : minuscules, chiffres et tirets (3 à 63 caractères)." };
  if (!ACCESS_KEY.test(config.access_key_id)) return { error: "Identifiant de la clé d'accès R2 invalide." };
  if (config.secret_access_key.length < 32 || config.secret_access_key.length > 256)
    return { error: "Clé secrète R2 invalide (32 caractères au moins)." };
  return { config };
}

/** Écrit, relit et supprime un petit fichier témoin : la configuration n'est gardée que si tout fonctionne. */
export async function probe(store) {
  const key = `controle/${crypto.randomUUID()}.txt`;
  const content = encoder.encode("SCOLIA " + new Date().toISOString());
  await store.put(key, content);
  const read = await store.get(key);
  if (!read) throw new Error("Le fichier témoin n'a pas pu être relu.");
  const back = new Uint8Array(await new Response(read.body).arrayBuffer());
  await store.delete(key);
  if (back.length !== content.length || back.some((b, i) => b !== content[i])) throw new Error("Le fichier témoin relu est différent.");
}

const mask = value => value ? value.slice(0, 4) + "…" + value.slice(-4) : null;

/** Routes propriétaire : état, test et enregistrement des identifiants R2. */
export async function ownerStorageRequest(request, env, { jsonBody, fail, result, audit, nowMs, fetcher }) {
  const db = env.OWNER_DB;
  const now = nowMs();
  if (request.method === "GET") {
    const config = await savedConfig(env);
    let ready = false, error = null;
    try {
      const store = await backupStore(env, fetcher);
      if (store) { await probe(store); ready = true; }
    } catch (e) { error = e.message; }
    return result({
      mode: env.BACKUPS ? "binding" : config && !config.unreadable ? "credentials" : "none",
      ready, error,
      account_id: config?.account_id ?? null, bucket: config?.bucket ?? null,
      access_key_id: mask(config?.access_key_id), unreadable: !!config?.unreadable,
      updated_at: config?.updated_at ?? null,
      notice: env.BACKUPS
        ? "Stockage relié par le déploiement (liaison R2) : rien à saisir."
        : "Saisissez un jeton API R2 (Cloudflare › R2 › Gérer les jetons API › « Object Read & Write » sur le bucket).",
    });
  }
  if (request.method === "PUT") {
    const checked = validateStorageInput(await jsonBody(request));
    if (checked.error) return fail(checked.error, 400);
    try { await probe(s3Bucket(checked.config, fetcher ?? fetch)); }
    catch (e) { return fail("Test du stockage échoué : " + e.message + " Vérifiez le compte, le bucket et les droits de la clé.", 422); }
    await db.prepare(`INSERT INTO owner_settings(key,value,updated_at) VALUES(?,?,?)
      ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at`)
      .bind(SETTINGS_KEY, await seal(env, checked.config), now).run();
    await audit(db, "storage_configured", null, now);
    return result({ ready: true, mode: env.BACKUPS ? "binding" : "credentials", bucket: checked.config.bucket,
      access_key_id: mask(checked.config.access_key_id),
      notice: "Stockage vérifié (écriture, lecture, suppression) et enregistré chiffré." });
  }
  if (request.method === "DELETE") {
    await db.prepare("DELETE FROM owner_settings WHERE key=?").bind(SETTINGS_KEY).run();
    await audit(db, "storage_removed", null, now);
    return result({ removed: true, notice: "Identifiants supprimés. Les copies déjà stockées restent dans le bucket." });
  }
  return fail("Méthode non prise en charge.", 405);
}
