import { MAX_CLIENTS, frameBytes, frameKey, keyBytes, equalHash } from './protocol.js';

// Each room is exactly one Durable Object. Only websocket attachments survive
// hibernation; no school database, traffic history, log or queue is stored.
// The only storage call is the room alarm, which enforces the same time limits
// as the .NET relay (Scolia.Relay): 15 s for a client to send its request,
// 15 min per client connection, and one hour per host session.
export const CLIENT_FIRST_FRAME_MS = 15_000;
export const CLIENT_LIFETIME_MS = 15 * 60_000;
// The SCOLIA host renews its session after 55 minutes; 65 leaves a margin.
export const HOST_LIFETIME_MS = 65 * 60_000;
export const MAX_BUFFERED = 2 * 1024 * 1024;

function connected(socket) { return socket?.readyState === 1; }
function sockets(ctx, tag) { return ctx.getWebSockets(tag).filter(connected); }
function attachment(socket) { return socket.deserializeAttachment() || {}; }
function safelyClose(socket, code = 1008) {
  try { if (connected(socket)) socket.close(code, 'SCOLIA relay closed'); } catch { /* already closed */ }
}
function safelySend(socket, bytes) {
  if (!connected(socket)) return false;
  try {
    // Never queue arbitrarily large records for a slow peer. The Workers runtime
    // may not expose bufferedAmount: frames are then still bounded by MAX_FRAME
    // and MAX_CLIENTS, and the room alarm closes connections that last too long.
    if (typeof socket.bufferedAmount === 'number' && socket.bufferedAmount > MAX_BUFFERED) {
      safelyClose(socket, 1013); return false;
    }
    socket.send(bytes);
    return true;
  } catch { safelyClose(socket, 1011); return false; }
}

/** Time after which the relay closes this socket. */
export function deadline(meta) {
  const opened = Number(meta.openedAt) || 0;
  if (meta.kind === 'host') return opened + HOST_LIFETIME_MS;
  if (meta.kind === 'client') return opened + (meta.requestId ? CLIENT_LIFETIME_MS : CLIENT_FIRST_FRAME_MS);
  return opened;
}

async function scheduleAlarm(ctx, at) {
  const storage = ctx.storage;
  if (!storage?.setAlarm) return;
  const current = await storage.getAlarm();
  if (current === null || current === undefined || current > at) await storage.setAlarm(at);
}

/** Old host, replaced by a new session: its later close event must not touch the new clients. */
function retire(socket) {
  try { socket.serializeAttachment({ kind: 'retired' }); } catch { /* already closed */ }
  safelyClose(socket, 1012);
}

export async function connectRoom(ctx, role, headers, now = Date.now()) {
  if (role === 'host') {
    const pairHash = headers.get('X-Relay-Pair');
    const epochBase64 = headers.get('X-Relay-Epoch');
    if (!pairHash || !epochBase64) return new Response('Invalid host', { status: 403 });
    const binary = atob(epochBase64);
    if (binary.length !== 32 || !/^[A-F0-9]{64}$/.test(pairHash))
      return new Response('Invalid host', { status: 403 });
    // The Worker has already checked that this host owns the room token. A new
    // session replaces the previous one instead of being refused: after a network
    // cut or a session renewal, the old socket can look open for a while.
    for (const previous of sockets(ctx, 'host')) retire(previous);
    for (const peer of sockets(ctx, 'client')) safelyClose(peer, 1012);
    const [client, server] = Object.values(new WebSocketPair());
    ctx.acceptWebSocket(server, ['host']);
    server.serializeAttachment({ kind: 'host', pairHash, epoch: epochBase64, openedAt: now });
    if (!safelySend(server, Uint8Array.from(binary, c => c.charCodeAt(0))))
      return new Response('Host unavailable', { status: 503 });
    await scheduleAlarm(ctx, now + HOST_LIFETIME_MS);
    return new Response(null, { status: 101, webSocket: client });
  }
  const host = sockets(ctx, 'host')[0];
  if (!host) return new Response('Server offline', { status: 503 });
  const provided = headers.get('X-Relay-Client-Hash');
  if (!equalHash(provided, attachment(host).pairHash)) return new Response('Unauthorized', { status: 403 });
  if (sockets(ctx, 'client').length >= MAX_CLIENTS) return new Response('Busy', { status: 429 });
  const binary = atob(attachment(host).epoch || '');
  if (binary.length !== 32) return new Response('Host epoch invalid', { status: 503 });
  const [client, server] = Object.values(new WebSocketPair());
  ctx.acceptWebSocket(server, ['client']);
  server.serializeAttachment({ kind: 'client', requestId: null, openedAt: now });
  if (!safelySend(server, Uint8Array.from(binary, c => c.charCodeAt(0))))
    return new Response('Client unavailable', { status: 503 });
  await scheduleAlarm(ctx, now + CLIENT_FIRST_FRAME_MS);
  return new Response(null, { status: 101, webSocket: client });
}

export function routeFrame(ctx, socket, message) {
  const meta = attachment(socket);
  const bytes = frameBytes(message);
  if (!bytes) { safelyClose(socket); return; }
  const id = frameKey(bytes);
  if (!id) { safelyClose(socket); return; }

  if (meta.kind === 'client') {
    // .NET uses exactly one encrypted request on each client socket.
    if (meta.requestId) { safelyClose(socket); return; }
    const host = sockets(ctx, 'host')[0];
    if (!host) { safelyClose(socket, 1011); return; }
    if (sockets(ctx, 'client').some(x => x !== socket && attachment(x).requestId === id)) {
      safelyClose(socket); return;
    }
    socket.serializeAttachment({ ...meta, kind: 'client', requestId: id });
    if (!safelySend(host, bytes)) safelyClose(socket, 1011);
    return;
  }
  if (meta.kind !== 'host') { safelyClose(socket); return; }
  const target = sockets(ctx, 'client').find(x => attachment(x).requestId === id);
  if (target && !safelySend(target, bytes)) safelyClose(target, 1011);
}

export function disconnectRoom(ctx, socket) {
  const meta = attachment(socket);
  if (meta.kind === 'host') {
    for (const peer of sockets(ctx, 'client')) safelyClose(peer, 1011);
  } else if (meta.kind === 'client' && meta.requestId) {
    const host = sockets(ctx, 'host')[0];
    const cancel = keyBytes(meta.requestId);
    if (host && cancel) safelySend(host, cancel);
  }
}

/** Room alarm: closes expired sockets, then plans the next check while sockets remain. */
export async function expireRoom(ctx, now = Date.now()) {
  let next = Infinity;
  for (const socket of [...sockets(ctx, 'host'), ...sockets(ctx, 'client')]) {
    const meta = attachment(socket);
    const limit = deadline(meta);
    if (limit <= now) {
      disconnectRoom(ctx, socket);
      if (meta.kind === 'host') retire(socket); else safelyClose(socket, 1001);
    } else next = Math.min(next, limit);
  }
  if (next !== Infinity && ctx.storage?.setAlarm) await ctx.storage.setAlarm(next);
}
