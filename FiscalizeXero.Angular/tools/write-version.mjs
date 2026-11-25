// Writes the web app's version (package.json), git commit and build time to public/version.json, served as /version.json
// outside the service worker cache. The app reads it at run time (VersionService) and anyone can check a deployed build with
// a plain request. Runs before every serve, build and test (package.json "pre*" scripts). Generated, not committed; when it
// is missing the app just says "development".
import fs from 'node:fs';
import { execSync } from 'node:child_process';

const pkg = JSON.parse(fs.readFileSync(new URL('../package.json', import.meta.url), 'utf8'));
let commit = null;
try { commit = execSync('git rev-parse --short=12 HEAD', { stdio: ['ignore', 'pipe', 'ignore'] }).toString().trim() || null; } catch { /* not a git checkout */ }
const info = { version: pkg.version, commit, builtAt: new Date().toISOString() };

fs.writeFileSync(new URL('../public/version.json', import.meta.url), JSON.stringify(info, null, 2) + '\n');
console.log(`web version ${info.version}${commit ? ` (${commit})` : ''}`);
