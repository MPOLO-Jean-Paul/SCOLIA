import { DurableObject } from 'cloudflare:workers';
import { parseRoute, sha256Hex, TOKEN64, HEX64, decodeEpoch } from './protocol.js';
import { connectRoom, routeFrame, disconnectRoom } from './core.js';

interface Env {
  SCOLIA_ROOMS: DurableObjectNamespace<ScoliaRoom>;
}
const noStore = { 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' };
const textResponse = (message: string, status: number) =>
  new Response(message, { status, headers: noStore });

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    if (request.method === 'GET' && url.pathname === '/health')
      return new Response(JSON.stringify({ service: 'SCOLIA relais', protocol: 1, provider: 'cloudflare' }),
        { headers: { ...noStore, 'Content-Type': 'application/json' } });

    if (request.method !== 'GET') return textResponse('Method not allowed', 405);
    const route = parseRoute(url.pathname);
    if (!route) return textResponse('Not found', 404);
    if (url.search || request.headers.get('Upgrade')?.toLowerCase() !== 'websocket')
      return textResponse('WebSocket required', 426);

    // The public Worker checks capabilities before it creates/bills a DO request.
    // Only verified hashes (never the raw tokens) are forwarded to the room.
    // Upgrade must be forwarded to allow Cloudflare's 101 WebSocket response.
    const headers = new Headers({ 'Upgrade': 'websocket', 'X-Relay-Role': route.role });
    if (route.role === 'host') {
      const token = request.headers.get('X-Scolia-Host');
      const pair = request.headers.get('X-Scolia-Pair');
      const epoch = request.headers.get('X-Scolia-Epoch');
      if (!token || !TOKEN64.test(token) || !pair || !HEX64.test(pair) || !decodeEpoch(epoch))
        return textResponse('Forbidden', 403);
      if (await sha256Hex(token) !== route.room) return textResponse('Forbidden', 403);
      headers.set('X-Relay-Pair', pair);
      headers.set('X-Relay-Epoch', epoch!);
    } else {
      const token = request.headers.get('X-Scolia-Client');
      if (!token || !TOKEN64.test(token)) return textResponse('Forbidden', 403);
      headers.set('X-Relay-Client-Hash', await sha256Hex(token));
    }

    const object = env.SCOLIA_ROOMS.getByName(route.room);
    return object.fetch(new Request(request.url, { method: 'GET', headers }));
  }
};

export class ScoliaRoom extends DurableObject<Env> {
  async fetch(request: Request): Promise<Response> {
    const role = request.headers.get('X-Relay-Role');
    if (role !== 'host' && role !== 'client') return textResponse('Forbidden', 403);
    return connectRoom(this.ctx, role, request.headers);
  }
  webSocketMessage(socket: WebSocket, message: string | ArrayBuffer): void {
    routeFrame(this.ctx, socket, message);
  }
  webSocketClose(socket: WebSocket, _code: number, _reason: string, _clean: boolean): void {
    disconnectRoom(this.ctx, socket);
  }
  webSocketError(socket: WebSocket, _error: unknown): void {
    disconnectRoom(this.ctx, socket);
  }
}
