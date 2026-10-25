# Password Manager: User Guide

A step-by-step guide to everyday use. For an overview of the features see the [README](../README.md); for how your data is protected see [SECURITY.md](../SECURITY.md).

**Contents**
1. [Install](#1-install)
2. [Create your vault](#2-create-your-vault)
3. [Unlock and lock](#3-unlock-and-lock)
4. [Add and manage logins](#4-add-and-manage-logins)
5. [Generate strong passwords](#5-generate-strong-passwords)
6. [Two-factor (2FA) codes](#6-two-factor-2fa-codes)
7. [Move your passwords in (import)](#7-move-your-passwords-in-import)
8. [The browser extension](#8-the-browser-extension)
9. [Use the vault on several PCs](#9-use-the-vault-on-several-pcs)
10. [Check your password health](#10-check-your-password-health)
11. [Backups, restore and export](#11-backups-restore-and-export)
12. [Settings reference](#12-settings-reference)
13. [Updates and uninstalling](#13-updates-and-uninstalling)
14. [Keyboard shortcuts](#14-keyboard-shortcuts)
15. [Troubleshooting](#15-troubleshooting)
16. [FAQ](#16-faq)

---

## 1. Install

1. Download **`PasswordManager-win-Setup.exe`** from the [latest release](https://github.com/gaaloulv36/password-manager/releases/latest).
2. Run it. If Windows SmartScreen says *"Windows protected your PC"*, click **More info → Run anyway**. The installer isn't code-signed yet; nothing is wrong with it.
3. The app installs for your Windows account. There's no admin prompt, and nothing else needs to be installed first. It adds a Start menu shortcut and opens.

> The app lives in the **notification area** (system tray, next to the clock). Closing the window doesn't quit it; right-click the tray icon → **Exit** to quit.

---

## 2. Create your vault

Your vault is **one encrypted file** that holds all your logins.

1. On the welcome screen, click **Create a new vault**.
2. **Vault file:** the app suggests your OneDrive folder (`OneDrive\Passwords.pmvault`). Keep that if you want the vault on several PCs; otherwise click **Browse…** and pick any folder.
3. **Master password:** type it twice. Tips:
   - Use **5 or more random words**. **Suggest a passphrase…** makes one for you; type it in yourself so you remember it.
   - Don't use it anywhere else.
   - The meter must not say *Very weak*. Aim for **Strong** or **Very strong**.
4. Tick **"I understand that if I forget my master password, my vault cannot be recovered by anyone."**
5. Click **Create vault**. It takes about a second, then your empty vault opens.

> ⚠️ **Write your master password down and keep it somewhere safe** (not on the PC). There is no "forgot password". Without it, nobody can open the vault, including you.

---

## 3. Unlock and lock

### Unlock
- Type your master password and press **Enter**.
- **With Windows Hello** (face, fingerprint or PIN), once it's turned on in **Settings → Windows Hello → Turn on Windows Hello**: click **Unlock with Windows Hello**, or press **Space** because it's focused. The master password field stays underneath as a fallback.

### Lock
The vault locks by itself:

| When | Setting |
|---|---|
| No keyboard/mouse use on the PC for 5 minutes | **Settings → Locking** (1 minute to 1 hour) |
| You lock Windows (**Win+L**) or the PC sleeps | always |
| You minimise the window | optional, **Settings → Locking** |

To lock right away: **Lock** button, **Ctrl+L**, or tray icon → **Lock**.

### Open the app quickly
Press **Ctrl+Alt+P** from anywhere in Windows. The app comes to the front with the cursor in the search box, or on the unlock button if it's locked.

---

## 4. Add and manage logins

### Add a login
1. Click **＋ New entry** (or **Ctrl+N**).
2. Fill in:
   - **Title** *(required)*, e.g. *GitHub*.
   - **Username or email.**
   - **Password**: type it, or click **Generate…**. Click 👁 to check what you typed.
   - **Websites**: one per line, e.g. `github.com`. The browser extension uses these to know where to offer the login.
   - **Only on these exact websites…**: tick it to stop the login being offered on subdomains (e.g. a `google.com` login won't appear on `sites.google.com`).
   - **2FA secret**: see [section 6](#6-two-factor-2fa-codes).
   - **Notes**: anything else, such as security questions or recovery codes.
3. Click **Save**.

### Find, view and copy
- **Search**: type in the box at the top left (**Ctrl+F**). It searches titles, usernames, websites and notes.
- Click an entry to see its details. Next to each field:
  - **Copy** puts the value on the clipboard. Passwords are **cleared from the clipboard after 30 seconds** and never appear in Windows clipboard history (Win+V).
  - **👁** shows or hides the password.
  - Click a **website** link to open it in your browser.

### Edit or delete
- **Edit** changes any field, then **Save**.
- **Delete** removes the entry after you confirm. It also disappears on your other PCs.

### Previous passwords
Each time you change an entry's password, the old one is kept. Open **Previous passwords (N)** in the entry's details to see when each was replaced, and **Copy** one if you need it back. The last 10 are kept.

---

## 5. Generate strong passwords

Open the generator with **Generator** in the toolbar, or **Generate…** while editing an entry.

- **Password** mode: choose the length (8–64) and the character types (a–z, A–Z, 0–9, symbols). Tick **Avoid look-alike characters** for passwords you'll type by hand.
- **Passphrase** mode: random words (3–12), e.g. `canyon-velvet-orbit-mango-tulip`. You can choose the separator, capitalise words, or add a number. Passphrases are easier to type and remember.
- The strength in **bits** is shown under the result. 70+ bits is strong, and 100+ is excellent.
- **↻** makes a new one; **Copy** copies it; **Use** puts it into the entry you're editing.

---

## 6. Two-factor (2FA) codes

The app can show the rotating 6-digit codes that an authenticator app normally shows.

1. On the website, turn on 2FA with an **"authenticator app"**.
2. When it shows a QR code, look for **"Can't scan it?"** or **"Enter a setup key"**. Copy that key (e.g. `JBSW Y3DP EHPK 3PXP`).
3. Edit the entry in Password Manager and paste the key into **2FA secret**. An `otpauth://…` link works too.
4. **Save**. The entry now shows a live **2FA code** with a countdown. When a site asks for the code, click **Copy** next to it and paste it in. The browser extension fills the username and password, not the code.

> Keeping the password and the 2FA secret in the same vault is convenient, but it means the vault alone can get into the account. For your most important accounts (email, bank) you may prefer a separate authenticator app on your phone.

---

## 7. Move your passwords in (import)

### Step 1: export from your old manager
| From | How to export |
|---|---|
| **Chrome** | Settings → Autofill and passwords → Google Password Manager → Settings → **Export passwords** |
| **Edge** | Settings → Profiles → Passwords → ⋯ → **Export passwords** |
| **Opera / Opera GX / Brave** | Settings → search "passwords" → ⋯ → **Export passwords** |
| **Firefox** | Menu → Passwords → ⋯ → **Export passwords** |
| **Bitwarden** | Tools → Export vault → **.json** (not "encrypted") or **.csv** |
| **KeePass / KeePassXC** | File → Export → **CSV** |

### Step 2: import
1. Click **Import…** in the toolbar and choose the exported file.
2. Check the preview: **New** entries are added, and entries **Already in vault** (same site, username and password) are skipped, unless you tick *Also import entries that are already in my vault*.
3. Keep **Delete … after importing** ticked. The export file contains every password in plain text.
4. Click **Import N entries**.

> If you exported from several places, also empty the browser's own saved passwords afterwards, so you're not keeping two copies.

---

## 8. The browser extension

The extension fills and saves logins in **Opera GX, Brave, Chrome, Edge, Opera** and **Firefox**. The desktop app must be running (it usually is, in the tray).

### Install (until it's in the browser stores)
1. From the [latest release](https://github.com/gaaloulv36/password-manager/releases/latest), download `password-manager-extension-chromium-….zip` (or `…-firefox-….zip` for Firefox).
2. Extract it to a folder you'll **keep**, e.g. `Documents\PasswordManagerExtension`. The browser loads the extension from that folder every time.
3. In the browser:

| Browser | Steps |
|---|---|
| Opera GX / Opera | `opera://extensions` → **Developer mode** on → **Load unpacked** → pick the folder |
| Brave | `brave://extensions` → **Developer mode** on → **Load unpacked** → pick the folder |
| Chrome | `chrome://extensions` → **Developer mode** on → **Load unpacked** → pick the folder |
| Edge | `edge://extensions` → **Developer mode** on → **Load unpacked** → pick the folder |
| Firefox | `about:debugging#/runtime/this-firefox` → **Load Temporary Add-on…** → pick `manifest.json` in the folder. Firefox removes it when it restarts. |

4. Pin it: click the puzzle-piece icon in the toolbar → pin **Password Manager**.

> Browsers show a warning about developer-mode extensions. That's expected until the extension is published in the stores.

### Connect the browser (once per browser)
1. Click the **Password Manager** icon in the browser toolbar → **Connect**.
2. The browser shows a **6-digit code**, and a **"Connect a browser"** window opens in the app with a code too.
3. **If the codes are the same**, click **Approve** in the app. If they differ, or you didn't just click Connect, click **Reject**.

Connected browsers are listed in **Settings → Browser extension**, where you can **Disconnect** them.

### Fill a login
- Click into the username or password field on a login page. A **blue padlock** appears in the field; click it and pick the login.
- Or click the **toolbar icon** and pick a login.
- Or press **Ctrl+Shift+L**. With a single matching login it fills straight away; otherwise the list opens.

Nothing is ever filled without your click. Logins are only offered on the site they were saved for, so a fake look-alike site (e.g. `g1thub.com`) gets nothing.

### Save or update a login
After you sign in somewhere, the extension asks **"Save login for site?"**, or **"Update password for site?"** if the password changed.
- **Save / Update**: stored in your vault. The old password goes into *Previous passwords*.
- **Not now**: asks again next time.
- **Never for this site**: stops asking for that site.

### What the messages mean
| Message | What to do |
|---|---|
| *Your vault is locked* | Click **Unlock in Password Manager**, then unlock. |
| *Password Manager isn't running* | Click **Start Password Manager**. |
| *Not connected yet* | Do the **Connect** steps above. |
| *No saved logins for this site* | Add the site's address to the entry's **Websites**. |

---

## 9. Use the vault on several PCs

The vault file syncs through **OneDrive** (or Dropbox, Google Drive, Syncthing). There's no server and no account.

1. On the first PC, keep the vault in your OneDrive folder (the default).
2. On the other PC: install the app → **Open an existing vault…** → pick `OneDrive\Passwords.pmvault` → unlock with the same master password.

From then on:
- Changes made on one PC **appear on the other within seconds** of OneDrive syncing the file.
- If both PCs change things at the same time, **both changes are kept**. For the same entry, the latest edit wins.
- If OneDrive creates a *conflict copy* (e.g. `Passwords-LAPTOP.pmvault`), the app merges it back in and moves it to the backups folder.
- Windows Hello and connected browsers are **per PC**: set them up on each PC.
- If you change the master password on one PC, the others ask for the new one next time they unlock.

---

## 10. Check your password health

Click **🛡 Health** in the toolbar.

- The **score** (out of 100) is the share of your logins with no problems.
- Problems found:
  - **Found in data breaches**: change it **first**.
  - **Also used for other logins**: give each site its own password.
  - **Weak / Very weak**: easy to guess.
  - **Not changed for over a year.**
- Click **Check for breaches** to check against *Have I Been Pwned*. Only the first 5 characters of each password's SHA-1 fingerprint leave your PC, never the password.
- Each login has **Open website** (to change the password there) and **Edit** (to save the new one here).

---

## 11. Backups, restore and export

### Automatic backups
Once a day, when you unlock, the app saves an encrypted copy of the vault on this PC, **outside OneDrive**, and keeps the last 14. See them with **Settings → Backup & export → Open backups folder** (`%LOCALAPPDATA%\PasswordManager\Backups`).

### Make your own backup
**Settings → Backup & export → Save a backup…** saves an encrypted copy wherever you like, e.g. a USB stick. Do this now and then: if OneDrive or this PC ever fails, it saves you. A backup opens with the master password you had **when the backup was made**.

### Restore a backup
If the vault file is damaged, the unlock screen says so and shows **Restore from a backup…**:
1. Click it and pick a backup (the backups folder opens).
2. Confirm. The damaged file is kept in `Backups\Damaged`; it's moved aside, not deleted.
3. Unlock with the master password that was in use when that backup was made.

If the file in OneDrive is changed by something else while the vault is open, the app refuses it. It then asks whether to **replace it with your open vault**; the rejected file is kept in `Backups\Damaged`.

### Export (to leave or to switch)
**Settings → Backup & export → Export to CSV**: enter your master password, accept the warning, and choose where to save. The CSV holds **every password in plain text**. Import it into the new manager, then delete it.

---

## 12. Settings reference

Open with **⚙ Settings** in the toolbar. Changes apply and save immediately.

| Section | Setting | Default |
|---|---|---|
| **Locking** | Lock after no keyboard/mouse use | 5 minutes |
| | Lock when the window is minimised | off |
| | Keep running in the notification area when closed | on |
| | Hide Password Manager from screenshots and screen sharing | **on** |
| | Clear copied passwords after | 30 seconds |
| **Windows Hello** | Turn on / off unlock with face, fingerprint or PIN (this PC only) | off |
| **Change master password** | Current, new and confirm | |
| **Browser extension** | Connected browsers, each with **Disconnect** | |
| **Backup & export** | Save a backup… · Open backups folder · Export to CSV | |
| **App & updates** | Version · Start with Windows · Download updates automatically · Install updates while away · Check for updates / Restart to update · User guide | on, except Start with Windows |

**Screen-capture protection** hides the app from screenshots, recordings and Teams/Zoom/Discord screen sharing. Viewers see what's behind the window. Turn it off temporarily if you want to take a screenshot of the app yourself.

---

## 13. Updates and uninstalling

### Updates
- The app checks for a new version **every 6 hours** and downloads it in the background.
- When it's ready, you'll see a **notification** (click it), a **bar at the top of the window**, and **Restart to update** in the tray menu.
- If you do nothing, it installs when you exit the app, or **by itself while the vault is locked and you've been away for 10 minutes** (you can turn that off).
- To check now: **Settings → App & updates → Check for updates**.

### Uninstall
**Windows Settings → Apps → Installed apps → Password Manager → Uninstall.**
This **keeps** your vault file, settings and backups, so reinstalling picks up where you left off. To remove everything, also delete:
- the vault file (e.g. `OneDrive\Passwords.pmvault`) **only if you're sure** you don't need your passwords anymore,
- `%APPDATA%\PasswordManager` (settings, connected browsers),
- `%LOCALAPPDATA%\PasswordManager` (backups, Windows Hello data).

---

## 14. Keyboard shortcuts

| Shortcut | Where | Action |
|---|---|---|
| **Ctrl+Alt+P** | anywhere in Windows | Open Password Manager |
| **Ctrl+F** | vault | Search |
| **Ctrl+N** | vault | New entry |
| **Ctrl+L** | vault | Lock |
| **Enter** | unlock screen | Unlock |
| **Esc** | editor / dialogs | Cancel / close |
| **Ctrl+Shift+L** | browser | Fill the login for this page |

---

## 15. Troubleshooting

**I forgot my master password.**
It can't be recovered or reset. That's what keeps the vault safe. Try passwords you might have used (remember capitals and keyboard layout). If you exported a CSV earlier or still have your old password manager, you can rebuild from that.

**Windows Hello stopped working.**
This happens after resetting your Windows PIN or changing fingerprints. The app turns Hello off and asks for the master password. Unlock, then **Settings → Windows Hello → Turn on** again.

**The browser says "Password Manager isn't running" although it is.**
Restart the app (tray → **Exit**, then start it from the Start menu); it re-registers its browser connector on every start. Then reload the extension on the browser's extensions page.

**The browser says "Not connected yet" again.**
The connection was removed (e.g. **Disconnect** in Settings, or the app's data was reset). Click the toolbar icon → **Connect** and approve again.

**The padlock doesn't appear on a login page.**
Click the toolbar icon instead, or use **Ctrl+Shift+L**. Check that the entry's **Websites** contains this site's address. For sites that sign in on a different domain (e.g. `login.microsoftonline.com`), add that address too.

**Screenshots still show the app.**
Check **Settings → Locking → Hide Password Manager from screenshots…** is ticked and **Settings → App & updates** shows version **1.0.2 or newer**. Message boxes and the browser extension aren't covered (they never show passwords).

**"The vault file on disk was modified…"**
Something other than the app changed the file in OneDrive. If you didn't expect that, choose **Yes** to replace it with your open vault; the rejected file is kept in `Backups\Damaged`.

**"The vault file is damaged" when unlocking.**
Click **Restore from a backup…** ([section 11](#11-backups-restore-and-export)).

**SmartScreen blocks the installer.**
Click **More info → Run anyway**. The installer isn't code-signed yet.

**An entry I created has no password.**
Versions 1.0.0–1.0.2 had a bug where a password *typed* into a *new* entry wasn't saved. Update to 1.0.3 or newer, then **Edit** the entry and type the password again.

---

## 16. FAQ

**Where are my passwords stored?**
In the one vault file you chose (e.g. `OneDrive\Passwords.pmvault`), encrypted. Without your master password it only reveals how many entries you have and when they were last changed. Not even the site names are visible.

**Can Microsoft (OneDrive) or the developer see my passwords?**
No. The file is encrypted on your PC before OneDrive ever sees it, and there's no server or account.

**Is it safe to keep the vault in OneDrive?**
Yes; that's what it's designed for. The encryption (Argon2id + AES-256) assumes the file may be copied by others. Your protection is a strong master password.

**What if I lose my PC?**
Install the app on the new PC and open the vault from OneDrive with your master password. Without the password, whoever has the old PC can't read your vault. If you use Windows Hello, it only works on that PC with your face, fingerprint or PIN.

**Does it work offline?**
Yes, completely. Only syncing (OneDrive), updates and the optional breach check need the internet.

**Can I use it on my phone?**
Not yet; it's Windows only for now.
