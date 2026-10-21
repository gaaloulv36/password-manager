// Toolbar popup: connect (pair), unlock prompt, and the logins for the current tab.
const api = globalThis.browser ?? globalThis.chrome;
const content = document.getElementById('content');

async function call(cmd, extra = {}) {
  const reply = await api.runtime.sendMessage({ cmd, ...extra });
  if (!reply?.ok) throw new Error(reply?.error ?? 'internal');
  return reply.result;
}

function render(...nodes) {
  content.replaceChildren(...nodes);
}

function el(tag, props = {}, ...children) {
  const node = Object.assign(document.createElement(tag), props);
  node.append(...children);
  return node;
}

function showApp() {
  call('show-app').catch(() => {});
  window.close();
}

async function connect() {
  render(el('p', { className: 'muted' }, 'Starting secure connection…'));
  try {
    const { code } = await call('pair-start');
    render(
      el('p', {}, 'Password Manager is asking you to approve this browser. Check that it shows the same code:'),
      el('div', { className: 'code', role: 'status', ariaLabel: `Pairing code ${code}` }, code),
      el('p', { className: 'muted' }, 'Click “Approve” in the Password Manager window.'));
    await call('pair-finish');
    await refresh();
  } catch (error) {
    render(
      el('p', {}, error.message === 'pairing-rejected' ? 'The connection was not approved.' : 'Couldn’t connect. Is Password Manager running?'),
      el('div', { className: 'row' }, el('button', { className: 'primary', onclick: connect }, 'Try again')));
  }
}

async function showLogins() {
  const { logins, url } = await call('get-logins');
  const site = url ? new URL(url).hostname : '';
  if (logins.length === 0) {
    render(el('p', { className: 'muted' }, site ? `No saved logins for ${site}.` : 'No saved logins for this page.'),
      el('div', { className: 'row' }, el('button', { onclick: showApp }, 'Open Password Manager')));
    return;
  }

  const list = el('ul', { ariaLabel: `Logins for ${site}` });
  for (const login of logins) {
    const button = el('button', {
      onclick: async () => {
        try {
          await call('fill-from-popup', { itemId: login.itemId });
          window.close();
        } catch {
          render(el('p', {}, 'Couldn’t fill this page. Reload it and try again.'));
        }
      },
    }, el('div', { className: 'title' }, login.title), el('div', { className: 'sub' }, login.username ?? '(no username)'));
    list.append(el('li', {}, button));
  }
  render(el('p', { className: 'muted' }, `Logins for ${site}`), list);
  list.querySelector('button')?.focus();
}

const WEBSITE_ACCESS = { origins: ['http://*/*', 'https://*/*'] };

/** Firefox lets users withhold site access; without it the extension can't see login forms. */
async function hasWebsiteAccess() {
  return !api.permissions || api.permissions.contains(WEBSITE_ACCESS);
}

async function refresh() {
  if (!(await hasWebsiteAccess())) {
    render(el('p', {}, 'To fill logins, Password Manager needs access to the websites you visit. It only reads login forms, and only sends a password to a page when you pick a login.'),
      el('div', { className: 'row' }, el('button', {
        className: 'primary',
        onclick: async () => { if (await api.permissions.request(WEBSITE_ACCESS)) refresh(); },
      }, 'Allow access')));
    return;
  }

  try {
    const { state, vaultName } = await call('status');
    if (vaultName) document.getElementById('vault-name').textContent = vaultName;

    if (state === 'no-app') {
      render(el('p', {}, 'Password Manager isn’t running.'),
        el('div', { className: 'row' }, el('button', { className: 'primary', onclick: showApp }, 'Start Password Manager')));
    } else if (state === 'not-paired') {
      render(el('p', {}, 'Connect this browser to the Password Manager app on this PC to fill and save logins.'),
        el('div', { className: 'row' }, el('button', { className: 'primary', onclick: connect }, 'Connect')));
    } else if (state === 'locked') {
      render(el('p', {}, 'Your vault is locked.'),
        el('div', { className: 'row' }, el('button', { className: 'primary', onclick: showApp }, 'Unlock in Password Manager')));
    } else {
      await showLogins();
    }
  } catch {
    render(el('p', {}, 'Something went wrong.'), el('div', { className: 'row' }, el('button', { onclick: refresh }, 'Retry')));
  }
}

refresh();
