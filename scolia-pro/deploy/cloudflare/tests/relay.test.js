import test from 'node:test';
import assert from 'node:assert/strict';
import { connectRoom, routeFrame, disconnectRoom, expireRoom, CLIENT_FIRST_FRAME_MS, CLIENT_LIFETIME_MS, HOST_LIFETIME_MS } from '../src/core.js';
import { allowRequest, resetLimits, PERMIT_LIMIT, WINDOW_MS } from '../src/limits.js';
import { sha256Hex } from '../src/protocol.js';

class MockSocket {
  readyState = 1;
  sent = [];
  buffer = 0;
  meta = null;
  get bufferedAmount() { return this.buffer; }
  serializeAttachment(value) { this.meta = structuredClone(value); }
  deserializeAttachment() { return structuredClone(this.meta); }
  send(value) { this.sent.push(new Uint8Array(value).slice()); }
  close(code) { this.readyState = 3; this.closedWith = code; }
}
class MockResponse {
  constructor(body, options) {
    this.body = body; this.status = options.status; this.webSocket = options.webSocket;
  }
}
class MockStorage {
  alarm = null;
  async getAlarm() { return this.alarm; }
  async setAlarm(at) { this.alarm = at; }
}
class MockCtx {
  peers = [];
  storage = new MockStorage();
  acceptWebSocket(socket, tags) { socket.tags = tags; this.peers.push(socket); }
  getWebSockets(tag) { return this.peers.filter(s => s.tags?.includes(tag)); }
}
globalThis.WebSocketPair = class {
  constructor() { return { 0: new MockSocket(), 1: new MockSocket() }; }
};
globalThis.Response = MockResponse;

async function setup(now = 1_000) {
  const ctx = new MockCtx();
  const pairHash = await sha256Hex('B'.repeat(64));
  const epoch = Uint8Array.from({ length: 32 }, (_, i) => i + 1);
  const headers = new Headers({ 'X-Relay-Pair': pairHash, 'X-Relay-Epoch': Buffer.from(epoch).toString('base64') });
  assert.equal((await connectRoom(ctx, 'host', headers, now)).status, 101);
  const host = ctx.getWebSockets('host')[0];
  assert.deepEqual(host.sent[0], epoch);
  return { ctx, host, epoch, pairHash, headers };
}

test('clients need the correct capability hash', async () => {
  const { ctx, pairHash, epoch } = await setup();
  assert.equal((await connectRoom(ctx, 'client', new Headers({ 'X-Relay-Client-Hash': '0'.repeat(64) }))).status, 403);
  assert.equal((await connectRoom(ctx, 'client', new Headers({ 'X-Relay-Client-Hash': pairHash }))).status, 101);
  assert.deepEqual(ctx.getWebSockets('client')[0].sent[0], epoch);
});

test('encrypted frames pass unchanged both ways and client closing notifies host', async () => {
  const { ctx, host, pairHash } = await setup();
  await connectRoom(ctx, 'client', new Headers({ 'X-Relay-Client-Hash': pairHash }));
  const client = ctx.getWebSockets('client')[0];
  const frame = Uint8Array.from({ length: 65 }, (_, i) => (i + 3) % 255);
  routeFrame(ctx, client, frame.buffer);
  assert.deepEqual(host.sent.at(-1), frame);
  routeFrame(ctx, host, frame.buffer);
  assert.deepEqual(client.sent.at(-1), frame);
  disconnectRoom(ctx, client);
  assert.deepEqual(host.sent.at(-1), frame.slice(0, 16));
});

test('client cannot send repeated requests or text frames', async () => {
  const { ctx, pairHash } = await setup();
  const headers = new Headers({ 'X-Relay-Client-Hash': pairHash });
  await connectRoom(ctx, 'client', headers);
  const first = ctx.getWebSockets('client')[0];
  routeFrame(ctx, first, new Uint8Array(40).buffer);
  routeFrame(ctx, first, new Uint8Array(40).buffer);
  assert.equal(first.closedWith, 1008);
  await connectRoom(ctx, 'client', headers);
  const second = ctx.getWebSockets('client').at(-1);
  routeFrame(ctx, second, 'injection');
  assert.equal(second.closedWith, 1008);
});

test('routing survives a Durable Object hibernation through socket attachments', async () => {
  const { ctx, host, pairHash } = await setup();
  await connectRoom(ctx, 'client', new Headers({ 'X-Relay-Client-Hash': pairHash }));
  const client = ctx.getWebSockets('client')[0];
  const frame = Uint8Array.from({ length: 40 }, (_, i) => i);
  routeFrame(ctx, client, frame.buffer);
  const recreated = new MockCtx();
  recreated.peers = [host, client]; // No in-memory map survives; only attachments.
  routeFrame(recreated, host, frame.buffer);
  assert.deepEqual(client.sent.at(-1), frame);
  disconnectRoom(recreated, host);
  assert.equal(client.readyState, 3);
});

test('caps concurrent clients, prevents request collisions and backpressure', async () => {
  const { ctx, host, pairHash } = await setup();
  const headers = new Headers({ 'X-Relay-Client-Hash': pairHash });
  for (let i = 0; i < 16; i++) assert.equal((await connectRoom(ctx, 'client', headers)).status, 101);
  assert.equal((await connectRoom(ctx, 'client', headers)).status, 429);
  const [first, second] = ctx.getWebSockets('client');
  const frame = new Uint8Array(40);
  routeFrame(ctx, first, frame.buffer);
  routeFrame(ctx, second, frame.buffer);
  assert.equal(second.closedWith, 1008);
  host.buffer = 4 * 1024 * 1024;
  const last = ctx.getWebSockets('client').at(-1);
  frame[0] = 20;
  routeFrame(ctx, last, frame.buffer);
  assert.equal(host.closedWith, 1013);
});

test('a new host session replaces a stale one without closing the new clients later', async () => {
  const { ctx, host, headers, pairHash } = await setup();
  await connectRoom(ctx, 'client', new Headers({ 'X-Relay-Client-Hash': pairHash }));
  const oldClient = ctx.getWebSockets('client')[0];
  assert.equal((await connectRoom(ctx, 'host', headers)).status, 101);
  assert.equal(host.closedWith, 1012);
  assert.equal(oldClient.readyState, 3);
  const hosts = ctx.getWebSockets('host').filter(s => s.readyState === 1);
  assert.equal(hosts.length, 1);
  assert.notEqual(hosts[0], host);
  await connectRoom(ctx, 'client', new Headers({ 'X-Relay-Client-Hash': pairHash }));
  const newClient = ctx.getWebSockets('client').at(-1);
  disconnectRoom(ctx, host); // late close event of the replaced host
  assert.equal(newClient.readyState, 1);
});

test('room alarm enforces the .NET relay time limits', async () => {
  const start = 1_000;
  const { ctx, host, pairHash } = await setup(start);
  const headers = new Headers({ 'X-Relay-Client-Hash': pairHash });
  await connectRoom(ctx, 'client', headers, start);
  await connectRoom(ctx, 'client', headers, start);
  const [silent, active] = ctx.getWebSockets('client');
  assert.equal(ctx.storage.alarm, start + CLIENT_FIRST_FRAME_MS);
  const frame = new Uint8Array(40); frame[0] = 7;
  routeFrame(ctx, active, frame.buffer);
  await expireRoom(ctx, start + CLIENT_FIRST_FRAME_MS);
  assert.equal(silent.closedWith, 1001);
  assert.equal(active.readyState, 1);
  assert.equal(ctx.storage.alarm, start + CLIENT_LIFETIME_MS);
  await expireRoom(ctx, start + CLIENT_LIFETIME_MS);
  assert.equal(active.closedWith, 1001);
  assert.deepEqual(host.sent.at(-1), frame.slice(0, 16)); // host told to cancel the call
  assert.equal(ctx.storage.alarm, start + HOST_LIFETIME_MS);
  await expireRoom(ctx, start + HOST_LIFETIME_MS);
  assert.equal(host.closedWith, 1012);
});

test('backpressure check is skipped when the runtime has no bufferedAmount', async () => {
  const { ctx, host, pairHash } = await setup();
  Object.defineProperty(host, 'bufferedAmount', { get: () => undefined });
  await connectRoom(ctx, 'client', new Headers({ 'X-Relay-Client-Hash': pairHash }));
  const client = ctx.getWebSockets('client')[0];
  const frame = new Uint8Array(40); frame[0] = 9;
  routeFrame(ctx, client, frame.buffer);
  assert.deepEqual(host.sent.at(-1), frame);
});

test('per-IP limit allows 120 attempts per minute', () => {
  resetLimits();
  for (let i = 0; i < PERMIT_LIMIT; i++) assert.equal(allowRequest('198.51.100.7', 0), true);
  assert.equal(allowRequest('198.51.100.7', 10), false);
  assert.equal(allowRequest('198.51.100.8', 10), true);
  assert.equal(allowRequest('198.51.100.7', WINDOW_MS), true);
});
