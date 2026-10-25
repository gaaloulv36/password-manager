# Password Manager — Plan

**Scope:** Windows desktop app + browser extension (Chrome/Edge/Firefox). Sync = the encrypted vault file lives in a folder I already sync (OneDrive / Google Drive / Syncthing). No server.

**Stack:** .NET 10 (LTS), WPF + CommunityToolkit.Mvvm, xUnit. Extension in TypeScript (Manifest V3).

## Architecture

```
┌──────────────────┐  native messaging  ┌──────────────────┐  named pipe   ┌──────────────────────┐
│ Browser extension│ ◄─────(stdio)─────►│ Vault.NativeHost │ ◄────────────►│ Vault.Desktop (WPF)  │
│ (TypeScript, MV3)│                    │ (tiny console)   │  encrypted    │ holds unlocked vault │
└──────────────────┘                    └──────────────────┘               └──────────┬───────────┘
                                                                                      │ Vault.Core
                                                                           ┌──────────▼───────────┐
                                                                           │ vault.pmvault (JSON) │
                                                                           │ in OneDrive folder   │
                                                                           └──────────────────────┘
```

| Project | Role |
|---|---|
| `src/Vault.Core` | Crypto, vault model, file format, generator, TOTP, merge. **No UI, no I/O beyond the file.** |
| `src/Vault.Desktop` | WPF app: unlock, list/search, edit, generator, settings, tray icon, auto-lock. |
| `src/Vault.NativeHost` | Launched by the browser; relays messages to the desktop app over a named pipe. |
| `extension/` | Detects login forms, asks the desktop app for matching credentials, fills them. |
| `tests/Vault.Core.Tests` | Unit tests, incl. crypto test vectors. |

## Crypto design

```
master password ─Argon2id(salt, 256 MiB, t=3, p=4)─► master key ─HKDF("pm:wrap:v1")─► wrapping key
random 256-bit vault key ── AES-256-GCM(wrapping key) ──► WrappedVaultKey   (in file header)
each item (JSON) ── AES-256-GCM(vault key, AAD = id|revision|updatedAt|deleted) ──► EncryptedItem.Payload
header fields ── HMAC-SHA256(HKDF(vault key, "pm:header-mac:v1")) ──► HeaderMac
```

- Changing the master password only re-wraps the vault key (and bumps `KeyGeneration`).
- AAD binds each ciphertext to its item id **and metadata** → payloads can't be swapped between entries, and timestamps/deletion flags can't be forged to win a merge. Tombstones carry an encrypted empty payload, so deletions are authenticated too.
- `HeaderMac` makes header tampering (KDF params, key generation, wrapped key) detectable on unlock and merge.
- KDF params are stored in the file → can be raised later.
- Only `VaultCrypto` touches crypto APIs. Only built-in .NET crypto + Konscious Argon2id (verified against RFC 9106 vector).

**Known .NET limitation:** decrypted `string`s can't be wiped from memory. Mitigations: keep vault locked by default, decrypt on demand, short auto-lock, zero all `byte[]` keys.

## Roadmap

### Phase 1 — Core ✅
- [x] Argon2id + HKDF + AES-GCM (`VaultCrypto`)
- [x] Vault file: create / unlock / save (atomic write + `.bak`) / change password
- [x] Items with revisions + delete tombstones
- [x] Password generator
- [x] Passphrase generator (EFF large wordlist, embedded)
- [x] TOTP (RFC 6238; SHA1/256/512, `otpauth://` URIs) + tests against all RFC vectors
- [x] Merge two copies of the vault (`UnlockedVault.Merge`) — needed for sync
  - per item: latest `UpdatedAt` wins; revision / deleted / payload bytes only break ties deterministically → both PCs converge
  - header: higher `KeyGeneration` wins (master password changed on another PC → adopted, reported in `MergeResult`)
  - everything taken from the other copy is authenticated first; different `VaultId` is refused
  - edit timestamps never go backwards, even if this PC's clock is behind

### Phase 2 — Desktop MVP (first daily-usable version) ✅
- [x] Create vault (strength meter, no-recovery acknowledgement, OneDrive suggested) / unlock screen (remembers last vault)
- [x] Item list + instant search (title, username, websites, notes), detail/edit view, delete with confirmation
- [x] Live 2FA codes with countdown
- [x] Copy username/password/2FA code; clear clipboard after 30s; excluded from Win+V history and cloud clipboard
- [x] Save = merge with file on disk first (`VaultFileStore.SaveMerged`), so another PC's changes are never overwritten
- [x] Auto-lock on system idle (5 min), Windows lock, sleep
- [x] Settings window: auto-lock timeout, lock on minimize, clipboard delay, close-to-tray; change master password
- [x] Generator dialog (password + passphrase)
- [x] Tray icon, global hotkey Ctrl+Alt+P (focuses search/unlock), single instance
- [x] Import: Chrome/Edge, Firefox, Bitwarden CSV/JSON, KeePass, KeePassXC — preview, duplicate detection, deletes the export file
- [x] Backups: automatic daily copy (last 14, `%LOCALAPPDATA%\PasswordManager\Backups`), manual backup; CSV export behind master password + warning
- [ ] Later: mask password in edit mode; better strength meter (zxcvbn-style); Windows Hello unlock (Phase 5)

### Phase 3 — File sync ✅
- [x] Choose vault location (OneDrive suggested on create)
- [x] `FileSystemWatcher` (1 s debounce) → merge when the file changes on disk; own saves skipped by file hash
- [x] Only write back when the file is missing something (`VaultSync.SyncWithDisk`) → no save ping-pong between PCs
- [x] Before saving: re-read file, merge, then write (never blindly overwrite)
- [x] Conflict copies (OneDrive `Name-PC`, Dropbox "conflicted copy", Syncthing `.sync-conflict-`, `Name (1)`) merged, then moved to the backups folder; other vaults' files ignored (VaultId)
- [x] Half-written files retried (5×); tampered/foreign files refused with a warning, open vault untouched
- [x] Automatic dated backups (keep last 14)
- [ ] Later: offer "overwrite the damaged file with my open vault" when the file on disk is rejected

### Phase 4 — Browser extension ✅ (confirmed working by hand in Opera GX and Brave)
- [x] `Vault.NativeHost` relays native-messaging frames ⇄ named pipe; can start the app on request
- [x] Registry entries (HKCU) for Chrome, Edge, Chromium (Opera reads Chrome's) and Firefox, rewritten on every start
- [x] Named-pipe server: `CurrentUserOnly` + client must be our own `Vault.NativeHost.exe`
- [x] Pairing: ECDH P-256 + HKDF; 6-digit code compared in browser and app; key stored with DPAPI; disconnect in Settings
- [x] Every message AES-256-GCM with direction bound in; replayed/stale/forged messages rejected
- [x] Extension (plain JS, no build step for Chromium): inline padlock menu, toolbar popup, Ctrl+Shift+L, fill on click
- [x] "Save / Update login?" prompt after sign-in (survives the page navigating), "Never for this site"
- [x] Phishing guard enforced in the app (`UrlMatcher`): same host or subdomain, punycode, no HTTPS→HTTP, no bare TLDs
- [x] Firefox build (`node extension/build.mjs` → `dist/firefox`), site-access request in popup
- [x] Tests: C#⇄JS shared crypto vector; e2e through the real native host and app (12 checks)
- [x] Tried in real browsers: Opera GX, Brave (2026-10-21)
- [ ] Try in Chrome / Edge / Firefox; iframes (all_frames) later
- [ ] Publish to Chrome Web Store / Edge Add-ons / Opera add-ons / AMO (store IDs must be added to the host manifest)

### Phase 5a — Everyday comfort ✅ (confirmed by hand, incl. Windows Hello, 2026-10-22)
- [x] Windows Hello unlock: TPM key (KeyCredentialManager) signs a challenge → HKDF → wraps the vault key; stored in %LOCALAPPDATA%, never synced; turned off automatically if it stops matching (e.g. PIN reset)
- [x] Password masked in the edit form (👁 to reveal)
- [x] Login forms inside iframes (save prompt always shown by the top-level page)
- [x] Damaged vault file: "Restore from a backup…" on the unlock page; "replace the file with my open vault" when a tampered/foreign file is detected while unlocked (old file kept in Backups\Damaged)

### Phase 5b — Security checks ✅ (confirmed by hand)
- [x] Strength meter knows the 10k most common passwords (SecLists, MIT), l33t + number/symbol variants, keyboard/alphabet runs (QWERTY, AZERTY, QWERTZ)
- [x] Breach check via Have I Been Pwned range API (k-anonymity, padded responses); on demand only
- [x] Password health report: score, breached / reused / weak / not changed for a year, with Edit and Open website
- [x] `PasswordChangedAt` tracked when the password itself changes (editor, browser "update password")

### Phase 6 — Installer & auto-update ✅
- [x] Velopack: per-user install in %LOCALAPPDATA%\PasswordManager, no admin, Start menu shortcut
- [x] Self-contained (.NET bundled); app + native host published into one folder (~88 MB installer, delta updates after)
- [x] Auto-update from GitHub Releases: background download, install on exit or "Restart to update"; can be turned off
- [x] Start with Windows (HKCU Run, `--minimized` → tray only)
- [x] Uninstall hook removes native-messaging registry entries + Run key; vault/settings/backups kept
- [x] Native host exits when the app isn't running (doesn't lock files during updates)
- [x] `build/pack.ps1` (installer + extension zips); GitHub Actions: CI on push, release on `v*` tag
- [x] v1.0.0 released 2026-10-22 (GitHub Actions), installed by hand
- [x] Auto-update confirmed on this PC: 0.9.0 → 1.0.1 (app + native host updated, connector path unchanged)

### Phase 9a — Polish (v1.0.2) ✅
- [x] Password history per entry (last 10, Copy), kept on editor and browser password changes
- [x] "Only on these exact websites" per entry (enforced by the app)
- [x] Screen-capture protection (on by default; window excluded from screenshots / screen sharing)
- [x] Dates in local time and Windows locale; details pane scrolls

### Updater improvements (v1.0.4) ✅
- [x] Checks every 6 h (not only at startup); retries after 1 h when offline
- [x] Update ready → clickable tray notification, "Restart to update" in tray menu, bar at the top of the window
- [x] Installs by itself when the vault is locked and the PC has been idle 10 min (restarts to the tray); setting to turn off
- [x] Clean restart: vault locked, clipboard cleared, tray icon removed before Velopack replaces the app
- [x] XAML smoke test: every window/page is built in CI

### Phase 5 — Hardening & extras (remaining)
- [ ] Windows Hello quick unlock (vault key protected with `KeyCredentialManager`/TPM)
- [ ] Have I Been Pwned check (k-anonymity API) + weak/reused password report
- [x] Security review 1 (2026-10-22): 7 findings fixed, see SECURITY.md
- [x] Mutation fuzz tests in CI (vault file, imports, browser protocol, handler leak check): 4 robustness bugs fixed
- [x] MIT license; code signing policy (CODE_SIGNING.md) + SignPath steps in release workflow (inactive until approved)
- [ ] SignPath Foundation application approved → first signed release
- [ ] Decide: libsodium Argon2 (audited, but single-lane: ~0.27 s slower unlock, Konscious still needed for old backups)
- [ ] External review of `Vault.Core`
- [ ] Installer (MSIX or Velopack) + code signing

## Rules for myself
1. Keep my old password manager in parallel for the first months.
2. Encrypted backup outside the sync folder, weekly.
3. Never log secrets. Never write plaintext to disk. Never change crypto without a format version bump.
