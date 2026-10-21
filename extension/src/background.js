// Background service worker: owns the native-messaging connection and all crypto.
// Content scripts and the popup talk to it with chrome.runtime.sendMessage({ cmd, ... }).
// Page URLs always come from the browser (sender / tabs API), never from what a page claims.
import { HOST_NAME, TO_APP, TO_EXTENSION, base64, createPairingKeys, completePairing, seal, open } from './protocol.js';

const api = globalThis.browser ?? globalThis.chrome;
const REQUEST_TIMEOUT_MS = 15_000;
const PAIRING_TIMEOUT_MS = 3 * 60_000;

class VaultError extends Error {}

// ---- Native connection -------------------------------------------------------------------------

let port = null;
const pending = new Map();

function connect() {
  port = api.runtime.connectNative(HOST_NAME);
  port.onMessage.addListener((message) => {
    const waiter = pending.get(message.id);
    if (!waiter) return;
    pending.delete(message.id);
    if (message.type === 'app-not-running') waiter.reject(new VaultError('no-app'));
    else waiter.resolve(message);
  });
  port.onDisconnect.addListener(() => {
    // Host missing (app not installed) or app closed the pipe.
    for (const waiter of pending.values()) waiter.reject(new VaultError('no-app'));
    pending.clear();
    port = null;
  });
}

function send(message, timeoutMs = REQUEST_TIMEOUT_MS) {
  if (!port) connect();
  const id = crypto.randomUUID();
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new VaultError('timeout'));
    }, timeoutMs);
    pending.set(id, {
      resolve: (v) => { clearTimeout(timer); resolve(v); },
      reject: (e) => { clearTimeout(timer); reject(e); },
    });
    port.postMessage({ ...message, id });
  });
}

// ---- Pairing -------------------------------------------------------------------------------------

async function getPairing() {
  const { pairing } = await api.storage.local.get('pairing');
  return pairing ? { id: pairing.id, key: base64.decode(pairing.key) } : null;
}

let pairingInProgress = null;

function browserName() {
  const ua = navigator.userAgent;
  if (/OPR\//.test(ua)) return 'Opera';
  if (/Edg\//.test(ua)) return 'Edge';
  if (/Firefox\//.test(ua)) return 'Firefox';
  return 'Chrome';
}

/** Step 1: key exchange. Returns the code to show; the app shows the same code for approval. */
async function startPairing() {
  const keys = await createPairingKeys();
  const challenge = await send({ type: 'pair-request', extPublicKey: base64.encode(keys.publicKey), browser: browserName() });
  if (challenge.type !== 'pair-challenge') throw new VaultError('pairing-failed');

  const { messageKey, code } = await completePairing(keys.privateKey, keys.publicKey, base64.decode(challenge.appPublicKey));
  pairingInProgress = messageKey;
  return { code };
}

/** Step 2: wait for the user to approve in the app. */
async function finishPairing() {
  if (!pairingInProgress) throw new VaultError('pairing-failed');
  const messageKey = pairingInProgress;
  pairingInProgress = null;

  const result = await send({ type: 'pair-wait' }, PAIRING_TIMEOUT_MS);
  if (result.type !== 'pair-approved') throw new VaultError('pairing-rejected');
  await api.storage.local.set({ pairing: { id: result.pairingId, key: base64.encode(messageKey) } });
  return { paired: true };
}

// ---- Encrypted requests --------------------------------------------------------------------------

async function request(type, payload = {}) {
  const pairing = await getPairing();
  if (!pairing) throw new VaultError('not-paired');

  const inner = { type, id: crypto.randomUUID(), ts: Date.now(), ...payload };
  const reply = await send({ type: 'encrypted', pairingId: pairing.id, data: await seal(pairing.key, pairing.id, TO_APP, inner) });

  if (reply.type === 'not-paired') {
    await api.storage.local.remove('pairing'); // the app forgot us (or was reinstalled)
    throw new VaultError('not-paired');
  }
  if (reply.type !== 'encrypted') throw new VaultError(reply.error ?? 'bad-reply');

  const response = await open(pairing.key, pairing.id, TO_EXTENSION, reply.data);
  if (response.id !== inner.id) throw new VaultError('bad-reply');
  if (!response.ok) throw new VaultError(response.error);
  return response;
}

async function status() {
  try {
    const result = await request('status');
    return { state: result.locked ? 'locked' : 'unlocked', vaultName: result.vaultName };
  } catch (error) {
    if (error instanceof VaultError && (error.message === 'not-paired' || error.message === 'no-app')) return { state: error.message };
    throw error;
  }
}

// ---- Save-login prompts --------------------------------------------------------------------------

// Logins captured on submit, waiting for the user's answer; kept per tab so the prompt survives navigation.
const pendingSaves = new Map();

async function loginCaptured(tabId, url, username, password) {
  const { neverSave = [] } = await api.storage.local.get('neverSave');
  const site = new URL(url).hostname;
  if (neverSave.includes(site)) return null;

  const check = await request('check-login', { url, username, password });
  if (check.status !== 'new' && check.status !== 'changed') return null;

  const prompt = { url, username, password, site: check.site, kind: check.status, expires: Date.now() + 60_000 };
  pendingSaves.set(tabId, prompt);
  // Always shown by the top-level page, even when the login form was in an iframe.
  api.tabs.sendMessage(tabId, { cmd: 'show-save-prompt', prompt: { site: prompt.site, username, kind: prompt.kind } }, { frameId: 0 })
    .catch(() => {}); // page is navigating; it asks for the pending prompt once loaded
  return null;
}

function pendingSave(tabId) {
  const prompt = pendingSaves.get(tabId);
  if (!prompt || prompt.expires < Date.now()) {
    pendingSaves.delete(tabId);
    return null;
  }
  return { site: prompt.site, username: prompt.username, kind: prompt.kind };
}

async function answerSave(tabId, answer) {
  const prompt = pendingSaves.get(tabId);
  pendingSaves.delete(tabId);
  if (!prompt) return null;
  if (answer === 'never') {
    const { neverSave = [] } = await api.storage.local.get('neverSave');
    await api.storage.local.set({ neverSave: [...new Set([...neverSave, new URL(prompt.url).hostname])] });
    return null;
  }
  if (answer === 'save') return request('save-login', { url: prompt.url, username: prompt.username, password: prompt.password });
  return null;
}

// ---- Message routing -----------------------------------------------------------------------------

async function activeTab() {
  const [tab] = await api.tabs.query({ active: true, currentWindow: true });
  return tab;
}

async function handle(message, sender) {
  // Content scripts: the URL is the frame's real URL as reported by the browser.
  const pageUrl = sender.tab ? (sender.url ?? sender.tab.url) : null;

  switch (message.cmd) {
    case 'status': return status();
    case 'pair-start': return startPairing();
    case 'pair-finish': return finishPairing();
    case 'show-app':
      try {
        return await request('show-app');
      } catch {
        return send({ type: 'launch-app' }, 20_000); // app not running: ask the host to start it
      }

    case 'get-logins': {
      const url = pageUrl ?? (await activeTab())?.url;
      return { logins: (await request('get-logins', { url })).logins, url };
    }

    case 'fill-from-page': // content script asked for one login (user clicked it in the inline menu)
      return request('get-credential', { url: pageUrl, itemId: message.itemId });

    case 'fill-from-popup': { // popup: fetch for the active tab's top frame and hand to its content script
      const tab = await activeTab();
      const credential = await request('get-credential', { url: tab.url, itemId: message.itemId });
      await api.tabs.sendMessage(tab.id, { cmd: 'fill', credential }, { frameId: 0 });
      return { filled: true };
    }

    case 'login-captured': return loginCaptured(sender.tab.id, pageUrl, message.username, message.password);
    case 'pending-save': return pendingSave(sender.tab.id);
    case 'answer-save': return answerSave(sender.tab.id, message.answer);
    default: throw new VaultError('unknown-command');
  }
}

api.runtime.onMessage.addListener((message, sender, sendResponse) => {
  handle(message, sender).then(
    (result) => sendResponse({ ok: true, result }),
    (error) => sendResponse({ ok: false, error: error instanceof VaultError ? error.message : 'internal' }));
  return true; // async response
});

// Ctrl+Shift+L: fill when there's exactly one match, otherwise show the inline menu.
api.commands?.onCommand.addListener(async (command) => {
  if (command !== 'fill-login') return;
  const tab = await activeTab();
  if (tab?.id) api.tabs.sendMessage(tab.id, { cmd: 'shortcut-fill' }, { frameId: 0 }).catch(() => {});
});

api.tabs.onRemoved.addListener((tabId) => pendingSaves.delete(tabId));
