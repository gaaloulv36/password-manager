# Password Manager

A personal, offline-first password manager for Windows with a companion browser extension. It stores all your logins in a single encrypted vault file that only you can open with your master password, and fills them into websites for you.

> **Status:** in active development. See [PLAN.md](PLAN.md) for what is built so far and what is next.

---

## Overview

- **One master password** unlocks everything. It is never stored anywhere and cannot be recovered.
- **One encrypted vault file** holds all your entries. You choose where it lives.
- **No account, no server, no subscription.** To use the vault on several PCs, put the file in a folder you already sync (OneDrive, Google Drive, Dropbox, Syncthing…).
- **Desktop app** to manage your vault, and a **browser extension** to fill logins on websites.

---

## Getting started

### Creating a vault
1. On first launch, choose **Create new vault**.
2. Pick where to save the vault file (for example, your OneDrive folder if you want it on multiple PCs).
3. Choose a master password and type it twice. A strength meter shows how strong it is.
4. The vault opens, empty and ready to use.

> ⚠️ If you forget your master password, your vault **cannot** be opened by anyone, including you. Write it down and keep it somewhere safe.

### Opening an existing vault
1. Choose **Open vault** and select the vault file. This is how you open the same vault on a second PC.
2. Enter your master password.
3. If the password is wrong, the app says so and the vault stays locked.

The app remembers the last vault you opened, so next time you only need to type your master password.

---

## Managing entries

Each entry can hold:

| Field | Description |
|---|---|
| **Title** | Name shown in the list, e.g. "GitHub" |
| **Username / email** | The login name |
| **Password** | The secret, hidden by default |
| **Website(s)** | One or more addresses, used by the browser extension to find the right login |
| **Notes** | Free text, e.g. security questions or recovery codes |
| **2FA secret (TOTP)** | Optional. The app then shows the current 6-digit two-factor code |

You can:
- **Add** a new entry, or create one from the password generator.
- **Edit** any field. The entry's "last modified" date updates automatically.
- **Delete** an entry. Deletions also reach your other PCs when the vault syncs.
- **Search** instantly by title, username, website or notes.
- **Reveal** a password temporarily with the eye icon.

### Copying to the clipboard
- One click copies the username, password or 2FA code.
- Copied passwords are **cleared from the clipboard after 30 seconds**.
- Copied secrets do not appear in Windows clipboard history (Win+V) or cloud clipboard.

---

## Password generator

Create strong random passwords from the entry editor or the toolbar.

- **Length:** 4 to 128 characters (default 20).
- **Character types:** lowercase, uppercase, digits, symbols. Turn each on or off.
- **Avoid look-alike characters** (`I l 1 O 0 o`) for passwords you have to type by hand.
- Every enabled character type is guaranteed to appear at least once.
- **Passphrase mode:** generates easy-to-type phrases of random words (e.g. `canyon-velvet-orbit-mango-tulip`).
- Click **Regenerate** for a new one, or **Use** to put it into the entry.

---

## Locking

The vault locks automatically so nobody can use it while you're away. When it locks, every decrypted entry is cleared from the app's memory and the master password is needed again.

The vault locks:
- after a period of inactivity (configurable, default 5 minutes),
- when you lock Windows (Win+L) or the PC goes to sleep,
- when you click **Lock**,
- optionally, when the window is minimized.

Optionally, **Windows Hello** (PIN, fingerprint or face) can unlock the vault on a PC where you've already entered the master password once.

---

## Using the vault on several PCs (sync)

There is no server. Sync works through a file-sync service you already use:

1. Save or move the vault file into a synced folder, e.g. OneDrive.
2. On your other PC, choose **Open vault** and pick the same file.

What happens next:
- **Changes from another PC show up automatically.** The app notices when the file changes and reloads it.
- **Edits on two PCs at the same time are merged, not lost.** If you changed different entries, both changes are kept. If you changed the same entry, the most recent edit wins.
- **Deleted entries stay deleted** on every PC.
- If your sync service creates a "conflicted copy" of the vault, the app merges it back in.
- The file in the sync folder is always encrypted, so the storage provider can't read your passwords.

---

## Browser extension

The extension works in **Chrome, Edge and Firefox**, together with the desktop app.

### First-time pairing
1. Install the extension and click its icon.
2. The desktop app shows a **pairing code**. Check that it matches the one in the browser, then confirm.
3. The browser is now paired. Pairing is needed only once per browser.

### Filling logins
- On a login page, the extension shows an icon in the username/password fields.
- Click it to see the matching entries for **this website** and choose one to fill.
- If the vault is locked, the extension asks you to unlock it in the desktop app first.
- Logins are filled only when you click, never silently in the background.

### Phishing protection
- A login is offered only on the website it was saved for. A look-alike address such as `g1thub.com` gets no suggestions for your GitHub login.
- By default it matches the exact site. You can allow matching on all subdomains for each entry.

### Saving new logins
- When you sign in or register on a site that isn't in your vault, the extension asks **"Save this login?"**
- If you change a password on a site, it offers to **update** the existing entry.

---

## Import and export

### Import
Bring your existing passwords in from:
- Chrome / Edge (CSV export)
- Firefox (CSV export)
- Bitwarden (JSON export)
- KeePass / KeePassXC (CSV export)

The app shows a preview before importing and flags duplicates.

> After importing, delete the exported CSV/JSON file. It contains your passwords in plain text.

### Export and backup
- **Encrypted backup:** a copy of the vault that only opens with your master password. Safe to keep on a USB drive or in cloud storage.
- **Automatic backups:** the app keeps the last few versions of the vault next to it, in case something goes wrong.
- **Plain CSV export:** available for moving to another password manager. It needs your master password and shows a warning, because the file is unencrypted.

---

## Security checks

- **Weak passwords:** lists entries with short or easy-to-guess passwords.
- **Reused passwords:** lists entries that share the same password.
- **Breached passwords:** checks whether a password appears in known data breaches (via *Have I Been Pwned*). Only a short, anonymous fragment of the password's fingerprint is sent, never the password itself.

---

## Changing the master password

**Settings → Change master password.** Enter the current password, then the new one twice. Other PCs using the same vault file will ask for the new password the next time they unlock.

---

## Good habits

- Use a long master password you don't use anywhere else. A passphrase of 5 or more random words works well.
- Keep an **encrypted backup** somewhere other than your sync folder.
- Turn on two-factor authentication on important accounts. You can store the 2FA secret in the entry.
