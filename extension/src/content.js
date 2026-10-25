// Content script: finds login fields, shows the inline icon/menu, fills on click, and offers to save
// logins after a form is submitted. Passwords only arrive here after the user picks a login.
(() => {
  if (window.__passwordManagerLoaded) return;
  window.__passwordManagerLoaded = true;

  const api = globalThis.browser ?? globalThis.chrome;
  const USERNAME_HINT = /user|e-?mail|login|account|identifier|phone/i;

  // ---- Talking to the background ---------------------------------------------------------------

  async function call(cmd, extra = {}) {
    const reply = await api.runtime.sendMessage({ cmd, ...extra });
    if (!reply?.ok) throw new Error(reply?.error ?? 'internal');
    return reply.result;
  }

  // ---- Finding fields --------------------------------------------------------------------------

  const isVisible = (el) => {
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0 && getComputedStyle(el).visibility !== 'hidden';
  };
  const isTextLike = (el) => el instanceof HTMLInputElement && ['text', 'email', 'tel', ''].includes(el.type) && !el.readOnly && !el.disabled;
  const isPassword = (el) => el instanceof HTMLInputElement && el.type === 'password' && !el.readOnly && !el.disabled;

  function looksLikeUsername(el) {
    if (!isTextLike(el)) return false;
    const autocomplete = (el.autocomplete || '').toLowerCase();
    if (autocomplete.includes('username') || autocomplete.includes('email')) return true;
    return el.type === 'email' || USERNAME_HINT.test(`${el.name} ${el.id} ${el.placeholder} ${el.getAttribute('aria-label') ?? ''}`);
  }

  /** The username/password pair around a field (either may be missing on multi-step logins). */
  function loginFieldsFor(field) {
    const scope = field.form ?? document;
    const inputs = [...scope.querySelectorAll('input')].filter(isVisible);
    const password = isPassword(field) ? field : inputs.find(isPassword) ?? null;
    let username = isTextLike(field) ? field : null;
    if (!username && password) {
      // Nearest text field before the password field.
      const before = inputs.slice(0, inputs.indexOf(password)).filter(isTextLike);
      username = before.reverse().find(looksLikeUsername) ?? before[0] ?? null;
    }
    return { username, password };
  }

  const isLoginField = (el) => isPassword(el) || (looksLikeUsername(el) && (loginFieldsFor(el).password !== null || el.autocomplete?.includes('username')));

  // ---- Filling ---------------------------------------------------------------------------------

  function setValue(input, value) {
    if (!input || value == null) return;
    input.focus();
    // Use the native setter so frameworks (React, Vue, …) notice the change.
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  }

  const firstVisiblePassword = () => [...document.querySelectorAll('input[type=password]')].find((p) => isVisible(p) && isPassword(p)) ?? null;

  function fill(credential, field) {
    const target = field && document.contains(field) ? field : firstVisiblePassword() ?? lastFocused;
    // Never fill invisible fields: some pages hide inputs to harvest whatever a password manager drops in.
    if (!target || !isVisible(target)) return;
    const { username, password } = loginFieldsFor(target);
    setValue(username, credential.username);
    setValue(password, credential.password);
    (password ?? username)?.focus();
  }

  // ---- Inline UI (closed shadow root so the page can't restyle or script it) -------------------

  /** Our buttons only react to real user input, never to clicks synthesized by page scripts. */
  const trusted = (handler) => (event) => {
    if (event.isTrusted) handler(event);
  };

  const host = document.createElement('vaultforge-ui');
  host.style.cssText = 'all: initial; position: absolute; top: 0; left: 0; z-index: 2147483647;';
  const root = host.attachShadow({ mode: 'closed' });
  root.innerHTML = `
    <style>
      /* Forge Studio brand (dark-only): Charcoal surfaces, Ash text, Ember primary, VaultForge blue accent. */
      :host { all: initial; }
      * { box-sizing: border-box; font: 13px/1.4 "Inter", "Segoe UI", system-ui, sans-serif; }
      .icon { position: absolute; width: 22px; height: 22px; border: 0; border-radius: 6px; padding: 2px; cursor: pointer;
              background: #22252D; display: none; box-shadow: 0 0 0 1px #353944; }
      .icon:hover { box-shadow: 0 0 0 1px #3D8BFF; }
      .icon svg { width: 18px; height: 18px; display: block; }
      .menu, .banner { position: absolute; display: none; min-width: 260px; max-width: 360px; background: #1C1F26; color: #F4F1EC;
              border: 1px solid #2A2E37; border-radius: 12px; box-shadow: 0 14px 40px -12px rgba(0,0,0,.6); overflow: hidden; }
      .menu button.item { display: block; width: 100%; text-align: left; border: 0; background: none; padding: 9px 14px; cursor: pointer; color: inherit; }
      .menu button.item:hover, .menu button.item:focus { background: #22252D; outline: none; box-shadow: inset 2px 0 0 #3D8BFF; }
      .title { font-weight: 600; } .sub { color: #A3A7B0; font-size: 12px; }
      .note { padding: 10px 14px; color: #C9CCD3; }
      .banner { position: fixed; top: 16px; right: 16px; padding: 14px 16px; }
      .banner .brand { font: 700 12px/1 "Space Grotesk", "Segoe UI", sans-serif; letter-spacing: -0.01em; color: #A3A7B0; margin-bottom: 8px; }
      .banner .actions { display: flex; gap: 8px; justify-content: flex-end; margin-top: 12px; }
      .banner button { border: 1px solid #353944; background: #22252D; border-radius: 8px; padding: 6px 12px; cursor: pointer; color: #F4F1EC; }
      .banner button:hover { border-color: #3D8BFF; }
      .banner button.primary { background: #F2752B; border-color: #F2752B; color: #15171C; font-weight: 600; }
      .banner button.primary:hover { background: #FF8A45; border-color: #FF8A45; }
      button:focus-visible { outline: 2px solid #3D8BFF; outline-offset: 2px; }
    </style>
    <button class="icon" type="button" aria-label="VaultForge: show logins for this site" title="VaultForge">
      <svg viewBox="0 0 24 24" fill="none" stroke="#3D8BFF" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
        <path d="M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6z"/><circle cx="12" cy="10.5" r="1.8"/><path d="M12 12.3V15.5"/>
      </svg>
    </button>
    <div class="menu" role="menu" aria-label="VaultForge logins"></div>
    <div class="banner" role="dialog" aria-label="Save login"></div>`;

  const icon = root.querySelector('.icon');
  const menu = root.querySelector('.menu');
  const banner = root.querySelector('.banner');
  let activeField = null;
  let lastFocused = null;

  const ensureHost = () => { if (!host.isConnected) document.documentElement.appendChild(host); };

  function positionNear(el, field, below) {
    const r = field.getBoundingClientRect();
    el.style.top = `${window.scrollY + (below ? r.bottom + 4 : r.top + (r.height - 22) / 2)}px`;
    el.style.left = `${window.scrollX + (below ? r.left : r.right - 26)}px`;
  }

  function showIcon(field) {
    ensureHost();
    activeField = field;
    positionNear(icon, field, false);
    icon.style.display = 'block';
  }

  function hideMenu() {
    menu.style.display = 'none';
    menu.replaceChildren();
  }

  function note(text, action) {
    const div = document.createElement('div');
    div.className = 'note';
    div.textContent = text;
    menu.append(div);
    if (action) {
      const button = document.createElement('button');
      button.className = 'item';
      button.textContent = action.label;
      button.addEventListener('click', trusted(action.run));
      menu.append(button);
    }
  }

  async function openMenu(field) {
    ensureHost();
    hideMenu();
    positionNear(menu, field, true);
    menu.style.display = 'block';
    note('Loading…');

    let logins;
    try {
      ({ logins } = await call('get-logins'));
    } catch (error) {
      menu.replaceChildren();
      if (error.message === 'locked') note('Your vault is locked.', { label: 'Unlock in VaultForge', run: () => { call('show-app'); hideMenu(); } });
      else if (error.message === 'not-paired') note('Not connected yet. Click the VaultForge toolbar icon to connect.');
      else if (error.message === 'no-app') note('VaultForge isn’t running.', { label: 'Start VaultForge', run: () => { call('show-app'); hideMenu(); } });
      else note('Something went wrong. Try again.');
      return;
    }

    menu.replaceChildren();
    if (logins.length === 0) {
      note(`No saved logins for ${location.hostname}.`);
      return;
    }
    for (const login of logins) {
      const button = document.createElement('button');
      button.className = 'item';
      button.setAttribute('role', 'menuitem');
      const title = Object.assign(document.createElement('div'), { className: 'title', textContent: login.title });
      const sub = Object.assign(document.createElement('div'), { className: 'sub', textContent: login.username ?? '(no username)' });
      button.append(title, sub);
      button.addEventListener('click', trusted(async () => {
        hideMenu();
        try {
          fill(await call('fill-from-page', { itemId: login.itemId }), field);
        } catch {
          /* vault locked meanwhile, or entry no longer matches */
        }
      }));
      menu.append(button);
    }
    menu.querySelector('button')?.focus();
  }

  icon.addEventListener('mousedown', (e) => e.preventDefault()); // keep focus in the field
  icon.addEventListener('click', trusted(() => activeField && openMenu(activeField)));

  document.addEventListener('focusin', (e) => {
    const target = e.composedPath()[0];
    if (target === host) return;
    if (target instanceof HTMLInputElement && isVisible(target) && isLoginField(target)) {
      lastFocused = target;
      showIcon(target);
    } else {
      icon.style.display = 'none';
    }
  }, true);

  document.addEventListener('mousedown', (e) => {
    if (!e.composedPath().includes(host)) hideMenu();
  }, true);
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape') hideMenu(); }, true);
  window.addEventListener('resize', () => { if (activeField && icon.style.display === 'block') positionNear(icon, activeField, false); });

  // ---- Save prompts ----------------------------------------------------------------------------

  let lastCapture = '';

  function capture(form) {
    const password = [...(form ?? document).querySelectorAll('input[type=password]')].find((p) => p.value);
    if (!password) return;
    const { username } = loginFieldsFor(password);
    const key = `${username?.value}\u0000${password.value}`;
    if (key === lastCapture) return;
    lastCapture = key;
    // The background shows the prompt in the top-level page (this may be an iframe).
    call('login-captured', { username: username?.value ?? '', password: password.value }).catch(() => {});
  }

  function showBanner(prompt) {
    ensureHost();
    banner.replaceChildren();
    const text = document.createElement('div');
    text.innerHTML = '<div class="brand">VaultForge</div><div class="title"></div><div class="sub"></div>';
    text.querySelector('.title').textContent = prompt.kind === 'changed' ? `Update password for ${prompt.site}?` : `Save login for ${prompt.site}?`;
    text.querySelector('.sub').textContent = prompt.username || '(no username)';
    const actions = document.createElement('div');
    actions.className = 'actions';
    const answer = (value) => () => {
      banner.style.display = 'none';
      call('answer-save', { answer: value }).catch(() => {});
    };
    for (const [label, value, primary] of [['Never for this site', 'never'], ['Not now', 'dismiss'], [prompt.kind === 'changed' ? 'Update' : 'Save', 'save', true]]) {
      const button = Object.assign(document.createElement('button'), { textContent: label, type: 'button' });
      if (primary) button.className = 'primary';
      button.addEventListener('click', trusted(answer(value)));
      actions.append(button);
    }
    banner.append(text, actions);
    banner.style.display = 'block';
  }

  document.addEventListener('submit', (e) => capture(e.target), true);
  // Many sites log in with a script instead of submitting the form, so also watch submit-like buttons.
  // (Not every button: a "show password" toggle would otherwise capture a half-typed password.)
  const SUBMIT_TEXT = /log\s?in|sign\s?in|sign\s?up|register|continue|submit|next|connexion|anmelden/i;
  const isSubmitLike = (button) =>
    (button.type === 'submit' && (button instanceof HTMLInputElement || button.form)) ||
    SUBMIT_TEXT.test(button.textContent || button.value || button.getAttribute('aria-label') || '');

  document.addEventListener('click', (e) => {
    const button = e.target instanceof Element ? e.target.closest('button, input[type=submit], [role=button]') : null;
    if (!button || button === host || !isSubmitLike(button)) return;
    const form = button.form ?? button.closest('form');
    if ((form ?? document).querySelector('input[type=password]')) capture(form);
  }, true);
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && isPassword(e.target)) capture(e.target.form);
  }, true);

  // A prompt captured just before the page navigated is shown on the next page (top-level page only).
  const isTopFrame = window === window.top;
  if (isTopFrame) call('pending-save').then((prompt) => prompt && showBanner(prompt)).catch(() => {});

  // ---- Messages from the background ------------------------------------------------------------

  api.runtime.onMessage.addListener((message) => {
    if (message.cmd === 'show-save-prompt' && isTopFrame) showBanner(message.prompt);
    if (message.cmd === 'fill' && message.url && new URL(message.url).origin === location.origin) fill(message.credential, lastFocused);
    if (message.cmd === 'shortcut-fill') {
      const field = lastFocused ?? firstVisiblePassword();
      if (!field) return;
      call('get-logins').then(({ logins }) => {
        if (logins.length === 1) call('fill-from-page', { itemId: logins[0].itemId }).then((c) => fill(c, field));
        else openMenu(field);
      }).catch(() => openMenu(field));
    }
  });
})();
