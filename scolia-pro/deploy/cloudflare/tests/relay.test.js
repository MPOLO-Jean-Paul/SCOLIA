import test from 'node:test';
import assert from 'node:assert/strict';
import { connectRoom, routeFrame, disconnectRoom } from '../src/core.js';
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
class MockCtx {
  peers = [];
  acceptWebSocket(socket, tags) { socket.tags = tags; this.peers.push(socket); }
  getWebSockets(tag) { return this.peers.filter(s => s.tags?.includes(tag)); }
}
globalThis.WebSocketPair = class {
  constructor() { return { 0: new MockSocket(), 1: new MockSocket() }; }
};
globalThis.Response = MockResponse;

async function setup() {
  const ctx = new MockCtx();
  const pairHash = await sha256Hex('B'.repeat(64));
  const epoch = Uint8Array.from({ length: 32 }, (_, i) => i + 1);
  const headers = new Headers({ 'X-Relay-Pair': pairHash, 'X-Relay-Epoch': Buffer.from(epoch).toString('base64') });
  assert.equal(connectRoom(ctx, 'host', headers).status, 101);
  const host = ctx.getWebSockets('host')[0];
  assert.deepEqual(host.sent[0], epoch);
  return { ctx, host, epoch, pairHash, headers };
}

test('only one host and clients need the correct capability hash', async () => {
  const { ctx, pairHash, epoch, headers } = await setup();
  assert.equal(connectRoom(ctx, 'host', headers).status, 409);
  assert.equal(connectRoom(ctx, 'client', new Headers({ 'X-Relay-Client-Hash': '0'.repeat(64) })).status, 403);
  assert.equal(connectRoom(ctx, 'client', new Headers({ 'X-Relay-Client-Hash': pairHash })).status, 101);
  assert.deepEqual(ctx.getWebSockets('client')[0].sent[0], epoch);
});

test('encrypted frames pass unchanged both ways and client closing notifies host', async () => {
  const { ctx, host, pairHash } = await setup();
  connectRoom(ctx, 'client', new Headers({ 'X-Relay-Client-Hash': pairHash }));
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
  connectRoom(ctx, 'client', headers);
  const first = ctx.getWebSockets('client')[0];
  routeFrame(ctx, first, new Uint8Array(40).buffer);
  routeFrame(ctx, first, new Uint8Array(40).buffer);
  assert.equal(first.closedWith, 1008);
  connectRoom(ctx, 'client', headers);
  const second = ctx.getWebSockets('client').at(-1);
  routeFrame(ctx, second, 'injection');
  assert.equal(second.closedWith, 1008);
});

test('routing survives a Durable Object hibernation through socket attachments', async () => {
  const { ctx, host, pairHash } = await setup();
  connectRoom(ctx, 'client', new Headers({ 'X-Relay-Client-Hash': pairHash }));
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
  for (let i = 0; i < 16; i++) assert.equal(connectRoom(ctx, 'client', headers).status, 101);
  assert.equal(connectRoom(ctx, 'client', headers).status, 429);
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
