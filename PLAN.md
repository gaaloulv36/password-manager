# Password Manager — Plan

**Scope:** Windows desktop app + browser extension (Chrome/Edge/Firefox). Sync = the encrypted vault file lives in a folder I already sync (OneDrive / Google Drive / Syncthing). No server.

**Stack:** .NET 9 (move to .NET 10 LTS — just change `TargetFramework`), WPF + CommunityToolkit.Mvvm, xUnit. Extension in TypeScript (Manifest V3).

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
each item (JSON) ── AES-256-GCM(vault key, AAD = item id) ──► EncryptedItem.Payload
```

- Changing the master password only re-wraps the vault key.
- AAD binds each ciphertext to its item id → payloads can't be swapped between entries.
- KDF params are stored in the file → can be raised later.
- Only `VaultCrypto` touches crypto APIs. Only built-in .NET crypto + Konscious Argon2id (verified against RFC 9106 vector).

**Known .NET limitation:** decrypted `string`s can't be wiped from memory. Mitigations: keep vault locked by default, decrypt on demand, short auto-lock, zero all `byte[]` keys.

## Roadmap

### Phase 1 — Core ✅ (started)
- [x] Argon2id + HKDF + AES-GCM (`VaultCrypto`)
- [x] Vault file: create / unlock / save (atomic write + `.bak`) / change password
- [x] Items with revisions + delete tombstones
- [x] Password generator
- [ ] Passphrase generator (EFF wordlist)
- [ ] TOTP (RFC 6238) + tests against RFC vectors
- [ ] Merge two copies of the vault (per item: highest revision wins, then `UpdatedAt`) — needed for sync

### Phase 2 — Desktop MVP (first daily-usable version)
- [ ] Create vault / unlock screen
- [ ] Item list + instant search, detail/edit view
- [ ] Copy username/password; clear clipboard after 30s; exclude from Win+V history (`ExcludeClipboardContentFromMonitorProcessing`)
- [ ] Auto-lock on idle, on Windows lock (`SessionSwitch`), on minimize (configurable)
- [ ] Generator dialog
- [ ] Tray icon + global hotkey to open search
- [ ] Import: Chrome/Edge CSV, Bitwarden JSON, KeePass CSV. Export: encrypted backup

### Phase 3 — File sync
- [ ] Choose vault location (e.g. OneDrive folder)
- [ ] `FileSystemWatcher` → reload + merge when the file changes on disk
- [ ] Before saving: re-read file, merge, then write (never blindly overwrite)
- [ ] Detect OneDrive "conflict copies" (`vault-PCNAME.pmvault`) and merge them in
- [ ] Automatic dated backups (keep last N)

### Phase 4 — Browser extension
- [ ] `Vault.NativeHost` + registry entries for Chrome/Edge/Firefox native messaging
- [ ] Named-pipe server in desktop app (restricted to current user via pipe ACL)
- [ ] Pairing: first connection shows a code in the desktop app, confirm → shared key; all messages encrypted with it
- [ ] Extension: detect login forms, match domain (public suffix list, exact host by default), fill on click (no silent autofill)
- [ ] "Save this login?" prompt on form submit
- [ ] Phishing guard: never fill on a domain that doesn't match the saved URL

### Phase 5 — Hardening & extras
- [ ] Windows Hello quick unlock (vault key protected with `KeyCredentialManager`/TPM)
- [ ] Have I Been Pwned check (k-anonymity API) + weak/reused password report
- [ ] Vault parser fuzzing; external review of `Vault.Core`
- [ ] Installer (MSIX or Velopack) + code signing

## Rules for myself
1. Keep my old password manager in parallel for the first months.
2. Encrypted backup outside the sync folder, weekly.
3. Never log secrets. Never write plaintext to disk. Never change crypto without a format version bump.
