// Manual integration test: plays the browser against the real native host + running desktop app.
// usage: node extension/test/e2e.mjs <path-to-Vault.NativeHost.exe>
// Prints the pairing code and waits for you to approve it in the app. Expects a "GitHub" login
// for https://github.com in the open vault.
import { spawn } from 'node:child_process';
import assert from 'node:assert/strict';
import { base64, createPairingKeys, completePairing, seal, open, TO_APP, TO_EXTENSION } from '../src/protocol.js';

const host = spawn(process.argv[2], ['chrome-extension://test/'], { stdio: ['pipe', 'pipe', 'inherit'] });
const waiting = new Map();
let buffer = Buffer.alloc(0);

host.stdout.on('data', (chunk) => {
  buffer = Buffer.concat([buffer, chunk]);
  while (buffer.length >= 4 && buffer.length >= 4 + buffer.readUInt32LE(0)) {
    const length = buffer.readUInt32LE(0);
    const message = JSON.parse(buffer.subarray(4, 4 + length).toString('utf8'));
    buffer = buffer.subarray(4 + length);
    waiting.get(message.id)?.(message);
  }
});

function send(message) {
  const id = crypto.randomUUID();
  const body = Buffer.from(JSON.stringify({ ...message, id }));
  const header = Buffer.alloc(4);
  header.writeUInt32LE(body.length);
  host.stdin.write(Buffer.concat([header, body]));
  return new Promise((resolve) => waiting.set(id, resolve));
}

let pairing;
async function request(type, payload = {}, reuse) {
  const inner = reuse ?? { type, id: crypto.randomUUID(), ts: Date.now(), ...payload };
  const data = await seal(pairing.key, pairing.id, TO_APP, inner);
  const reply = await send({ type: 'encrypted', pairingId: pairing.id, data });
  assert.equal(reply.type, 'encrypted', JSON.stringify(reply));
  return { response: await open(pairing.key, pairing.id, TO_EXTENSION, reply.data), inner };
}

// Pairing
const keys = await createPairingKeys();
const challenge = await send({ type: 'pair-request', extPublicKey: base64.encode(keys.publicKey), browser: 'E2E test' });
assert.equal(challenge.type, 'pair-challenge');
const { messageKey, code } = await completePairing(keys.privateKey, keys.publicKey, base64.decode(challenge.appPublicKey));
console.log(`CODE ${code}`);
const approved = await send({ type: 'pair-wait' });
assert.equal(approved.type, 'pair-approved');
pairing = { id: approved.pairingId, key: messageKey };
console.log('paired');

const check = (name, condition, detail = '') => console.log(`${condition ? 'PASS' : 'FAIL'} ${name} ${detail}`);

let { response } = await request('status');
check('status: unlocked', response.ok && response.locked === false, response.vaultName);

({ response } = await request('get-logins', { url: 'https://github.com/login' }));
const github = response.logins.find((l) => l.title === 'GitHub');
check('get-logins on github.com finds GitHub', !!github, `(${response.logins.length} match)`);
check('get-logins returns no passwords', !JSON.stringify(response).includes('s3cret'));

({ response } = await request('get-logins', { url: 'https://g1thub.com/login' }));
check('look-alike g1thub.com gets nothing', response.logins.length === 0);

({ response } = await request('get-credential', { url: 'https://g1thub.com/login', itemId: github.itemId }));
check('credential refused for look-alike site', response.ok === false && response.error === 'no-match');

({ response } = await request('get-credential', { url: 'https://github.com/login', itemId: github.itemId }));
check('credential released for github.com', response.ok && response.password === 's3cret-P@ssw0rd!', `totp=${response.totp}`);

({ response } = await request('check-login', { url: 'https://shop.example.org/signin', username: 'e2e@example.org', password: 'first-pass' }));
check('check-login: new site → new', response.status === 'new');

({ response } = await request('save-login', { url: 'https://shop.example.org/signin', username: 'e2e@example.org', password: 'first-pass' }));
check('save-login adds entry', response.result === 'added');

({ response } = await request('save-login', { url: 'https://shop.example.org/signin', username: 'e2e@example.org', password: 'second-pass' }));
check('save-login with new password updates', response.result === 'updated');

const first = await request('status');
const replay = await request(null, {}, first.inner);
check('replayed request is rejected', replay.response.ok === false && replay.response.error === 'replayed');

const stale = await request('status', {}, { type: 'status', id: crypto.randomUUID(), ts: Date.now() - 10 * 60_000 });
check('stale request is rejected', stale.response.ok === false);

const forged = await send({ type: 'encrypted', pairingId: pairing.id, data: base64.encode(crypto.getRandomValues(new Uint8Array(64))) });
check('forged ciphertext is rejected', forged.type === 'not-paired');

host.stdin.end();
host.kill();
