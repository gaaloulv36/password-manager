// Run: node --test extension/test/protocol.test.mjs
// Checks that the extension's crypto agrees with the C# side (same vector as BrowserTests.cs).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { completePairing, seal, open, TO_APP, TO_EXTENSION } from '../src/protocol.js';

const ECDH = { name: 'ECDH', namedCurve: 'P-256' };
const fromB64Url = (s) => Uint8Array.from(Buffer.from(s, 'base64url'));
const point = (x, y) => new Uint8Array([4, ...fromB64Url(x), ...fromB64Url(y)]);

const app = { x: 'rnYem5NxD-NbjRjmVbQtVWig40flkPZIYSQB2kVcRm0', y: 'aF9HaXA8pnY1pIi0NK2VdttYq3egmGC0VxuzGt7fL_k' };
const ext = {
  d: 'l5W71t3dudKtufwrwxC8Yz4Eaa9ecE0HgEezt-juX-c',
  x: '3tH7l6zxkaiu1n0cj1zK1RACpBBBZe-6JBTuog9zEJ8',
  y: 'jVE5vLBogLbBTDgOs5Y65TnstIto2fMht47vtllU68k',
};

test('pairing matches the shared C# test vector', async () => {
  const privateKey = await crypto.subtle.importKey('jwk',
    { kty: 'EC', crv: 'P-256', d: ext.d, x: ext.x, y: ext.y, ext: true }, ECDH, false, ['deriveBits']);

  const { messageKey, code } = await completePairing(privateKey, point(ext.x, ext.y), point(app.x, app.y));

  assert.equal(Buffer.from(messageKey).toString('hex'), '6ecd97cf96f84ccde7f3d1d36b596b5a7e85853a78e70b5be499dddc442d55d8');
  assert.equal(code, '816 954');
});

test('sealed messages round-trip and are bound to direction and pairing', async () => {
  const key = crypto.getRandomValues(new Uint8Array(32));
  const sealed = await seal(key, 'p1', TO_APP, { type: 'status' });

  assert.deepEqual(await open(key, 'p1', TO_APP, sealed), { type: 'status' });
  await assert.rejects(open(key, 'p1', TO_EXTENSION, sealed));
  await assert.rejects(open(key, 'p2', TO_APP, sealed));
});
