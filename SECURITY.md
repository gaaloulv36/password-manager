# Security

## Threat model

**Protected against**
- Someone who gets the **vault file**: a stolen laptop, a OneDrive breach, or a copy on another PC. Without the master password, the file reveals nothing useful.
- Someone who can **modify the synced file or drop files next to it**. Tampering is detected and refused; it can't inject, roll back, delete or swap entries.
- **Web pages**, including phishing look-alikes and compromised sites. They only get the login saved for their own site, and only after you click it.
- **Other Windows users** on the same PC. They can't reach the app's browser connection, and they can't read pairing or Windows Hello data.
- **Network observers.** The breach check sends only 5 hex characters of a SHA-1 hash.

**Not protected against (out of scope)**
- **Malware running as your Windows user.** It can log keystrokes, read the app's memory while the vault is unlocked, read the extension's pairing key from the browser profile, or show a fake Windows Hello prompt. No password manager can fully defend against this.
- **A forgotten master password.** There is deliberately no recovery.

## How it's protected

| Area | Mechanism |
|---|---|
| Master password → key | Argon2id (256 MiB, 3 passes, 4 lanes) → HKDF-SHA256 |
| Vault key | Random 256-bit key, wrapped with AES-256-GCM. Changing the master password only re-wraps it. |
| Entries | AES-256-GCM per entry. The entry's id, revision, timestamp and deleted flag are bound in as associated data, so none of them can be swapped, rolled back or faked. |
| File header | HMAC-SHA256 over the KDF settings, key generation and wrapped key |
| Untrusted file input | Structure, size (64 MiB) and KDF ranges checked before any work. Every entry is authenticated at unlock and before merging. |
| Sync | Only authenticated changes are merged. Rejected files and conflict copies are set aside, never deleted. |
| Windows Hello | A TPM-held key signs a random challenge; HKDF of the signature wraps the vault key. Stored only in `%LOCALAPPDATA%`. |
| Browser link | Named pipe limited to the current user, and only our own `Vault.NativeHost.exe` may connect. ECDH P-256 pairing confirmed with a 6-digit code. Every message is AES-256-GCM with its direction bound in; replays and stale messages are rejected. Pairing keys are protected with DPAPI. |
| Phishing | The **app** checks every request against the saved site: same host or a subdomain, punycode compared, never HTTPS→HTTP, never a bare TLD. |
| Filling | Only after a real click (`isTrusted`), only into visible fields, and only if the page is still on the site the login was released for. |
| Clipboard | Excluded from Win+V history and cloud clipboard; cleared after 30 s. |

## Review log

### 2026-10-22: full code review (Claude)

| # | Severity | Finding | Status |
|---|---|---|---|
| F1 | Medium | A file with one tampered entry unlocked, then failed while loading the list: a half-unlocked state where the browser bridge was serving. | **Fixed:** all entries are authenticated at unlock |
| F2 | Medium | Toolbar fill could deliver a password to a different site if the tab navigated between fetching and filling | **Fixed:** the page checks its own origin before filling |
| F3 | Low–Med | Fill fallback could target an invisible password field (a harvesting trick) | **Fixed:** visible fields only |
| F4 | Low–Med | KDF settings from the file weren't range-checked: a crafted file could crash the app on every unlock and hide the restore option | **Fixed:** checked on load, along with size, duplicate ids and missing payloads |
| F5 | Low | One tampered conflict copy blocked all syncing | **Fixed:** set aside to `Backups\Merged conflict copies\Rejected` |
| F6 | Low | Unlimited pairing prompts could pile up | **Fixed:** one prompt at a time |
| F7 | Low | Extension UI didn't require trusted (real user) clicks | **Fixed** |
| F8 | Info | Subdomain matching offers `google.com` logins on `sites.google.com`, which hosts user content (the same as other managers) | Accepted; a per-entry "exact host only" option is planned |
| F9 | Info | Same-user malware (see threat model) | Out of scope |
| F10 | Info | A CSV export opened in Excel could evaluate formulas, and deleted export files aren't securely wiped | Accepted; the export already warns that the file is plaintext and should be deleted |

Also found during the fixes: with F1 in place, a tampered vault file would have been reported as "Windows Hello stopped working" and Hello turned off. The two cases are now distinguished.

Regression tests: `tests/Vault.Core.Tests/HardeningTests.cs`.

**Limitations of this review:** a single reviewer, code reading plus tests; no fuzzing yet and no independent audit. Before trusting the vault with anything critical, get a second pair of eyes on `Vault.Core/Crypto`, `UnlockedVault.cs` and `Vault.Desktop/Browser`.

## Reporting a problem

Please open a private security advisory on GitHub (**Security → Report a vulnerability**) rather than a public issue.
