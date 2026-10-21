# Password Manager browser extension

Fills and saves logins from the Password Manager desktop app. Works in **Chrome, Edge, Opera, Opera GX and Brave** (same code), and **Firefox**.

The extension never stores passwords. It asks the desktop app for one login at a time, when you pick it. Every message is end-to-end encrypted with a key agreed when you connect the browser.

## Install (development / unpacked)

First run the desktop app once. On startup it registers its browser connector (`Vault.NativeHost.exe`) for all supported browsers. This needs no admin rights.

| Browser | Steps |
|---|---|
| **Chrome** | `chrome://extensions` → turn on **Developer mode** → **Load unpacked** → pick the `extension` folder |
| **Edge** | `edge://extensions` → turn on **Developer mode** → **Load unpacked** → pick the `extension` folder |
| **Opera / Opera GX** | `opera://extensions` → turn on **Developer mode** → **Load unpacked** → pick the `extension` folder |
| **Brave** | `brave://extensions` → turn on **Developer mode** → **Load unpacked** → pick the `extension` folder |
| **Firefox** | Run `node extension/build.mjs`, then `about:debugging#/runtime/this-firefox` → **Load Temporary Add-on…** → pick `extension/dist/firefox/manifest.json`. Temporary add-ons are removed when Firefox restarts. For permanent use, the add-on must be signed by Mozilla. |

All Chromium-based browsers get the same extension ID (`mdkfdlddmhpcgcdlbnbjmedhbpfnodgp`, fixed by the `key` in `manifest.json`). The desktop app only accepts that ID.

## Connect

1. Click the Password Manager icon in the toolbar → **Connect**.
2. The browser and the desktop app both show a 6-digit code. If they match, click **Approve** in the app.
3. Done. You only need to do this once per browser. Connected browsers are listed in the app under **Settings → Browser extension**.

## Use

- Click into a login field. A small padlock icon appears; click it to see the logins saved for this site and pick one to fill.
- Or click the toolbar icon, or press **Ctrl+Shift+L**: if there's a single match it's filled straight away.
- After you sign in somewhere new, or change a password, the extension asks whether to **Save** or **Update** it in your vault.

## Security model

- Logins are only offered on the site they were saved for, or its subdomains. Look-alike domains (`g1thub.com`, `github.com.evil.example`, Unicode look-alikes) get nothing. The **desktop app** enforces this check, not just the extension.
- A login saved for HTTPS is never filled on HTTP.
- Nothing is filled automatically; filling always takes a click.
- Each message is AES-256-GCM encrypted. Replayed or out-of-date messages are rejected.
- The connector only accepts connections from your own Windows account. The app only accepts its own `Vault.NativeHost.exe`.

## Tests

```
node --test extension/test/protocol.test.mjs   # crypto matches the C# side (shared test vector)
node extension/test/e2e.mjs <path>\Vault.NativeHost.exe   # full run against the running app (needs approval in the app)
node extension/test/login-page.mjs              # local sign-in page at http://localhost:8765/login for trying it by hand
```
