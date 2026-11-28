// Per-IP fixed window, in the Worker isolate's memory only: no storage, no log.
// Each Cloudflare isolate counts separately, so this is a best-effort bound
// (like the .NET relay's 120 requests per minute), not a global quota.
export const WINDOW_MS = 60_000;
export const PERMIT_LIMIT = 120;
const MAX_TRACKED = 10_000;
const windows = new Map();

export function allowRequest(ip, now = Date.now()) {
  const key = typeof ip === 'string' && ip.length <= 64 ? ip : 'unknown';
  let entry = windows.get(key);
  if (!entry || now - entry.start >= WINDOW_MS) {
    if (!entry && windows.size >= MAX_TRACKED) {
      for (const [k, v] of windows) if (now - v.start >= WINDOW_MS) windows.delete(k);
      if (windows.size >= MAX_TRACKED) windows.clear();
    }
    entry = { start: now, count: 0 };
    windows.set(key, entry);
  }
  entry.count++;
  return entry.count <= PERMIT_LIMIT;
}

export function resetLimits() { windows.clear(); }
