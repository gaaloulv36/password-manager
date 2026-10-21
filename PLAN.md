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
