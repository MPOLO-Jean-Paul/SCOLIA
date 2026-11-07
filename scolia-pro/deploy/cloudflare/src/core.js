import { MAX_CLIENTS, frameBytes, frameKey, keyBytes, equalHash } from './protocol.js';

// Each room is exactly one Durable Object. Only websocket attachments survive
// hibernation; no school database, traffic history, log or queue is stored.
function connected(socket) { return socket?.readyState === 1; }
function sockets(ctx, tag) { return ctx.getWebSockets(tag).filter(connected); }
function attachment(socket) { return socket.deserializeAttachment() || {}; }
function safelyClose(socket, code = 1008) {
  try { if (connected(socket)) socket.close(code, 'SCOLIA relay closed'); } catch { /* already closed */ }
}
function safelySend(socket, bytes) {
  if (!connected(socket)) return false;
  try {
    // Never queue arbitrarily large records for a slow or disconnected peer.
    if (socket.bufferedAmount > 2 * 1024 * 1024) { safelyClose(socket, 1013); return false; }
    socket.send(bytes);
    return true;
  } catch { safelyClose(socket, 1011); return false; }
}

export function connectRoom(ctx, role, headers) {
  if (role === 'host') {
    if (sockets(ctx, 'host').length) return new Response('Host already connected', { status: 409 });
    const pairHash = headers.get('X-Relay-Pair');
    const epochBase64 = headers.get('X-Relay-Epoch');
    if (!pairHash || !epochBase64) return new Response('Invalid host', { status: 403 });
    const binary = atob(epochBase64);
    if (binary.length !== 32 || !/^[A-F0-9]{64}$/.test(pairHash))
      return new Response('Invalid host', { status: 403 });
    const [client, server] = Object.values(new WebSocketPair());
    ctx.acceptWebSocket(server, ['host']);
    server.serializeAttachment({ kind: 'host', pairHash, epoch: epochBase64 });
    if (!safelySend(server, Uint8Array.from(binary, c => c.charCodeAt(0))))
      return new Response('Host unavailable', { status: 503 });
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
  server.serializeAttachment({ kind: 'client', requestId: null });
  if (!safelySend(server, Uint8Array.from(binary, c => c.charCodeAt(0))))
    return new Response('Client unavailable', { status: 503 });
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
    socket.serializeAttachment({ kind: 'client', requestId: id });
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
