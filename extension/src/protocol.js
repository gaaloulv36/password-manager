// Crypto for the extension ⇄ desktop app link. Must stay byte-for-byte compatible with
// src/Vault.Core/Browser/BrowserProtocol.cs (checked by test/protocol.test.mjs against a shared vector).

export const HOST_NAME = 'com.passwordmanager.host';
export const TO_APP = 'ext-to-app';
export const TO_EXTENSION = 'app-to-ext';

const encoder = new TextEncoder();
const decoder = new TextDecoder();
const ECDH = { name: 'ECDH', namedCurve: 'P-256' };

export const base64 = {
  encode(bytes) {
    let binary = '';
    for (const b of bytes) binary += String.fromCharCode(b);
    return btoa(binary);
  },
  decode(text) {
    return Uint8Array.from(atob(text), (c) => c.charCodeAt(0));
  },
};

function concat(...parts) {
  const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let offset = 0;
  for (const p of parts) {
    out.set(p, offset);
    offset += p.length;
  }
  return out;
}

/** Ephemeral key pair for one pairing attempt. */
export async function createPairingKeys() {
  const keyPair = await crypto.subtle.generateKey(ECDH, true, ['deriveBits']);
  const publicKey = new Uint8Array(await crypto.subtle.exportKey('raw', keyPair.publicKey));
  return { privateKey: keyPair.privateKey, publicKey };
}

/**
 * Derives the message key and the 6-digit code the user compares with the app.
 * @returns {Promise<{messageKey: Uint8Array, code: string}>}
 */
export async function completePairing(privateKey, extensionPublicKey, appPublicKey) {
  const peer = await crypto.subtle.importKey('raw', appPublicKey, ECDH, false, []);
  const shared = await crypto.subtle.deriveBits({ name: 'ECDH', public: peer }, privateKey, 256);
  const ikm = await crypto.subtle.importKey('raw', shared, 'HKDF', false, ['deriveBits']);
  const okm = new Uint8Array(await crypto.subtle.deriveBits(
    { name: 'HKDF', hash: 'SHA-256', salt: concat(appPublicKey, extensionPublicKey), info: encoder.encode('pm-browser-pairing-v1') },
    ikm, 512));

  const sasKey = await crypto.subtle.importKey('raw', okm.slice(32), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const mac = new Uint8Array(await crypto.subtle.sign('HMAC', sasKey, encoder.encode('pm-pairing-code')));
  const value = new DataView(mac.buffer).getUint32(0, false) % 1_000_000;
  const digits = String(value).padStart(6, '0');

  return { messageKey: okm.slice(0, 32), code: `${digits.slice(0, 3)} ${digits.slice(3)}` };
}

function aad(pairingId, direction) {
  return encoder.encode(`${pairingId}|${direction}`);
}

async function aesKey(messageKey, usage) {
  return crypto.subtle.importKey('raw', messageKey, 'AES-GCM', false, [usage]);
}

/** Encrypts a JSON message: base64(nonce(12) | ciphertext | tag(16)), like VaultCrypto.Encrypt. */
export async function seal(messageKey, pairingId, direction, message) {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const ciphertext = await crypto.subtle.encrypt(
    { name: 'AES-GCM', iv, additionalData: aad(pairingId, direction) },
    await aesKey(messageKey, 'encrypt'),
    encoder.encode(JSON.stringify(message)));
  return base64.encode(concat(iv, new Uint8Array(ciphertext)));
}

/** Decrypts and parses a sealed message; throws if it was tampered with or sent the other way. */
export async function open(messageKey, pairingId, direction, sealed) {
  const bytes = base64.decode(sealed);
  const plaintext = await crypto.subtle.decrypt(
    { name: 'AES-GCM', iv: bytes.slice(0, 12), additionalData: aad(pairingId, direction) },
    await aesKey(messageKey, 'decrypt'),
    bytes.slice(12));
  return JSON.parse(decoder.decode(plaintext));
}
