// Builds browser-specific copies of the extension into dist/.
//   node extension/build.mjs          → dist/chromium (Chrome, Edge, Opera) and dist/firefox
// The extension folder itself can also be loaded unpacked in Chromium browsers during development.
import { cpSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
const base = JSON.parse(readFileSync(join(root, 'manifest.json'), 'utf8'));

export const FIREFOX_ID = 'vaultforge@gaaloulv36.github.io';

function firefoxManifest() {
  const { key, minimum_chrome_version, background, ...rest } = base;
  return {
    ...rest,
    background: { scripts: [background.service_worker], type: 'module' },
    // Firefox treats these as user-grantable; the popup asks for them if they aren't granted.
    host_permissions: ['http://*/*', 'https://*/*'],
    browser_specific_settings: { gecko: { id: FIREFOX_ID, strict_min_version: '128.0' } },
  };
}

function build(target, manifest) {
  const out = join(root, 'dist', target);
  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });
  cpSync(join(root, 'src'), join(out, 'src'), { recursive: true });
  cpSync(join(root, 'icons'), join(out, 'icons'), { recursive: true });
  cpSync(join(root, 'fonts'), join(out, 'fonts'), { recursive: true });
  writeFileSync(join(out, 'manifest.json'), JSON.stringify(manifest, null, 2));
  console.log(`built ${out}`);
}

build('chromium', base);
build('firefox', firefoxManifest());
