// SCOLIA relay v1: transparent binary routing. Never parse or store school records.
export const MAX_FRAME = 1024 * 1024;
export const MAX_CLIENTS = 16;
export const HEX64 = /^[A-F0-9]{64}$/;
export const TOKEN64 = /^[a-fA-F0-9]{64}$/;

export function parseRoute(pathname) {
  const match = /^\/v1\/(host|client)\/([A-F0-9]{64})$/.exec(pathname);
  return match ? { role: match[1], room: match[2] } : null;
}

export async function sha256Hex(value) {
  const bytes = new TextEncoder().encode(value);
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  return Array.from(digest, b => b.toString(16).padStart(2, '0')).join('').toUpperCase();
}

export function equalHash(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string' || a.length !== 64 || b.length !== 64) return false;
  let diff = 0;
  for (let i = 0; i < 64; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

export function decodeEpoch(input) {
  if (typeof input !== 'string' || !/^[A-Za-z0-9+/]{43}=$/.test(input)) return null;
  try {
    const binary = atob(input);
    if (binary.length !== 32) return null;
    return Uint8Array.from(binary, c => c.charCodeAt(0));
  } catch { return null; }
}

export function frameBytes(message) {
  if (!(message instanceof ArrayBuffer) || message.byteLength < 40 || message.byteLength > MAX_FRAME) return null;
  return new Uint8Array(message);
}

export function frameKey(bytes) {
  if (!(bytes instanceof Uint8Array) || bytes.length < 16) return null;
  return Array.from(bytes.subarray(0, 16), b => b.toString(16).padStart(2, '0')).join('');
}

export function keyBytes(key) {
  if (typeof key !== 'string' || !/^[a-f0-9]{32}$/.test(key)) return null;
  const bytes = new Uint8Array(16);
  for (let i = 0; i < 16; i++) bytes[i] = Number.parseInt(key.slice(i * 2, i * 2 + 2), 16);
  return bytes;
}
