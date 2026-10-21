# Password Manager â€” Plan

**Scope:** Windows desktop app + browser extension (Chrome/Edge/Firefox). Sync = the encrypted vault file lives in a folder I already sync (OneDrive / Google Drive / Syncthing). No server.

**Stack:** .NET 9 (move to .NET 10 LTS â€” just change `TargetFramework`), WPF + CommunityToolkit.Mvvm, xUnit. Extension in TypeScript (Manifest V3).

## Architecture

```
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”  native messaging  â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”  named pipe   â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚ Browser extensionâ”‚ â—„â”€â”€â”€â”€â”€(stdio)â”€â”€â”€â”€â”€â–ºâ”‚ Vault.NativeHost â”‚ â—„â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â–ºâ”‚ Vault.Desktop (WPF)  â”‚
â”‚ (TypeScript, MV3)â”‚                    â”‚ (tiny console)   â”‚  encrypted    â”‚ holds unlocked vault â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜                    â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜               â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
                                                                                      â”‚ Vault.Core
                                                                           â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â–¼â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
                                                                           â”‚ vault.pmvault (JSON) â”‚
                                                                           â”‚ in OneDrive folder   â”‚
                                                                           â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
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
master password â”€Argon2id(salt, 256 MiB, t=3, p=4)â”€â–º master key â”€HKDF("pm:wrap:v1")â”€â–º wrapping key
random 256-bit vault key â”€â”€ AES-256-GCM(wrapping key) â”€â”€â–º WrappedVaultKey   (in file header)
each item (JSON) â”€â”€ AES-256-GCM(vault key, AAD = item id) â”€â”€â–º EncryptedItem.Payload
```

- Changing the master password only re-wraps the vault key.
- AAD binds each ciphertext to its item id â†’ payloads can't be swapped between entries.
- KDF params are stored in the file â†’ can be raised later.
- Only `VaultCrypto` touches crypto APIs. Only built-in .NET crypto + Konscious Argon2id (verified against RFC 9106 vector).

**Known .NET limitation:** decrypted `string`s can't be wiped from memory. Mitigations: keep vault locked by default, decrypt on demand, short auto-lock, zero all `byte[]` keys.

## Roadmap

### Phase 1 â€” Core âœ… (started)
- [x] Argon2id + HKDF + AES-GCM (`VaultCrypto`)
- [x] Vault file: create / unlock / save (atomic write + `.bak`) / change password
- [x] Items with revisions + delete tombstones
- [x] Password generator
- [ ] Passphrase generator (EFF wordlist)
- [ ] TOTP (RFC 6238) + tests against RFC vectors
- [ ] Merge two copies of the vault (per item: highest revision wins, then `UpdatedAt`) â€” needed for sync

### Phase 2 â€” Desktop MVP (first daily-usable version)
- [ ] Create vault / unlock screen
- [ ] Item list + instant search, detail/edit view
- [ ] Copy username/password; clear clipboard after 30s; exclude from Win+V history (`ExcludeClipboardContentFromMonitorProcessing`)
- [ ] Auto-lock on idle, on Windows lock (`SessionSwitch`), on minimize (configurable)
- [ ] Generator dialog
- [ ] Tray icon + global hotkey to open search
- [ ] Import: Chrome/Edge CSV, Bitwarden JSON, KeePass CSV. Export: encrypted backup

### Phase 3 â€” File sync
- [ ] Choose vault location (e.g. OneDrive folder)
- [ ] `FileSystemWatcher` â†’ reload + merge when the file changes on disk
- [ ] Before saving: re-read file, merge, then write (never blindly overwrite)
- [ ] Detect OneDrive "conflict copies" (`vault-PCNAME.pmvault`) and merge them in
- [ ] Automatic dated backups (keep last N)

### Phase 4 â€” Browser extension
- [ ] `Vault.NativeHost` + registry entries for Chrome/Edge/Firefox native messaging
- [ ] Named-pipe server in desktop app (restricted to current user via pipe ACL)
- [ ] Pairing: first connection shows a code in the desktop app, confirm â†’ shared key; all messages encrypted with it
- [ ] Extension: detect login forms, match domain (public suffix list, exact host by default), fill on click (no silent autofill)
- [ ] "Save this login?" prompt on form submit
- [ ] Phishing guard: never fill on a domain that doesn't match the saved URL

### Phase 5 â€” Hardening & extras
- [ ] Windows Hello quick unlock (vault key protected with `KeyCredentialManager`/TPM)
- [ ] Have I Been Pwned check (k-anonymity API) + weak/reused password report
- [ ] Vault parser fuzzing; external review of `Vault.Core`
- [ ] Installer (MSIX or Velopack) + code signing

## Rules for myself
1. Keep my old password manager in parallel for the first months.
2. Encrypted backup outside the sync folder, weekly.
3. Never log secrets. Never write plaintext to disk. Never change crypto without a format version bump.
