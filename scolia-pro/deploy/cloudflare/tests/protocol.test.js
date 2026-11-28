import test from 'node:test';
import assert from 'node:assert/strict';
import { parseRoute, sha256Hex, equalHash, decodeEpoch, frameBytes, frameKey, keyBytes, MAX_FRAME } from '../src/protocol.js';

const room = 'F'.repeat(64);
test('SCOLIA v1 URL format is strict', () => {
  assert.deepEqual(parseRoute('/v1/host/' + room), { role: 'host', room });
  assert.deepEqual(parseRoute('/v1/client/' + room), { role: 'client', room });
  assert.equal(parseRoute('/v1/host/' + room.toLowerCase()), null);
  assert.equal(parseRoute('/v1/host/' + room + '/evil'), null);
  assert.equal(parseRoute('/v1/admin/' + room), null);
});
test('host capability SHA256 agrees with existing SCOLIA .NET relay', async () => {
  assert.equal(await sha256Hex('A'.repeat(64)), 'D53EDA7A637C99CC7FB566D96E9FA109BF15C478410A3F5EB4D4C4E26CD081F6');
  assert.equal(equalHash(room, room), true);
  assert.equal(equalHash(room, 'E' + room.slice(1)), false);
});
test('host epoch is exactly 32 bytes encoded with standard Base64', () => {
  const epoch = Uint8Array.from({ length: 32 }, (_, i) => i);
  assert.deepEqual(decodeEpoch(Buffer.from(epoch).toString('base64')), epoch);
  assert.equal(decodeEpoch('bad'), null);
  assert.equal(decodeEpoch(Buffer.alloc(24).toString('base64')), null);
});
test('encrypted frames preserve opaque GUID prefix and enforce length limits', () => {
  const bytes = Uint8Array.from({ length: 40 }, (_, i) => i);
  assert.equal(frameKey(frameBytes(bytes.buffer)), '000102030405060708090a0b0c0d0e0f');
  assert.deepEqual(keyBytes(frameKey(bytes)), bytes.slice(0, 16));
  assert.equal(frameBytes('raw text'), null);
  assert.equal(frameBytes(new ArrayBuffer(39)), null);
  assert.equal(frameBytes(new ArrayBuffer(MAX_FRAME + 1)), null);
});
