# Code signing policy

Windows releases of Password Manager are signed so you can check they really come from this project and haven't been altered.

> Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).
> *(Status: application in progress. Until it's approved, releases are unsigned.)*

## What is signed
- `Vault.Desktop.exe`, `Vault.NativeHost.exe` and the project's own `Vault.*.dll` files
- The installer, `PasswordManager-win-Setup.exe`

Only files built from this repository are signed. Third-party files (the .NET runtime, NuGet packages, Velopack's updater) are shipped as their authors publish them.

## How a release is made
1. A version tag (`v1.2.3`) is pushed to this repository.
2. **GitHub Actions** builds everything from that tag on GitHub's servers, runs all tests (including the fuzz tests), and only then submits the files to SignPath for signing.
3. SignPath checks that the files come from this repository's workflow (origin verification), and a release is signed only after a project approver approves the request.
4. The signed files are published as a GitHub Release. Installed copies update from there.

Nothing is ever signed from a developer's own PC.

## Roles
| Role | Who |
|---|---|
| Committers and reviewers | [gaaloulv36](https://github.com/gaaloulv36) |
| Approvers (approve each signing request) | [gaaloulv36](https://github.com/gaaloulv36) |

All team members use multi-factor authentication for GitHub and SignPath.

## Privacy policy
This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. Specifically:

| Connection | When | What is sent |
|---|---|---|
| GitHub (`api.github.com`, `github.com`) | Update checks: at start and every 6 hours. Can be turned off in **Settings → App & updates**. | A request for the latest release. No vault data, no identifiers. |
| Have I Been Pwned (`api.pwnedpasswords.com`) | Only when you click **Check for breaches** | The first 5 hex characters of each password's SHA-1 hash, never the password or full hash ([k-anonymity](https://haveibeenpwned.com/API/v3#SearchingPwnedPasswordsByRange)) |
| Websites you open | Only when you click a website link in the app | Nothing beyond opening the link in your browser |

There is no telemetry, no analytics and no account. Your vault file is synced only by your own sync service (for example OneDrive), never by this app.

## Changes to your system
The app installs and runs for your Windows account only, with no admin rights. It changes these settings, and **uninstalling removes all of them**:

| Change | Why | When |
|---|---|---|
| Installs to `%LOCALAPPDATA%\PasswordManager` with a Start menu shortcut | the app itself | on install |
| Registry: `HKCU\Software\{Google\Chrome, Microsoft\Edge, Chromium, BraveSoftware\Brave-Browser, Mozilla}\NativeMessagingHosts\com.passwordmanager.host` | lets the browser extension talk to the app ([details](extension/README.md)) | on every start (kept up to date) |
| Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PasswordManager` | "Start with Windows" | only if you turn it on in Settings |

Your vault file, settings (`%APPDATA%\PasswordManager`) and backups (`%LOCALAPPDATA%\PasswordManager\Backups`) are kept on uninstall so you don't lose data. The [User Guide](docs/USER-GUIDE.md#13-updates-and-uninstalling) explains how to remove them too.
