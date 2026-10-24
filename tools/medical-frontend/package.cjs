'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { root, assets } = require('./assets.cjs');
function config({ apiBaseUrl = '', caseSource = 'api' } = {}) {
  // Relative same-origin prefix only; origin cannot be known until deployed.
  if (typeof apiBaseUrl !== 'string' || (apiBaseUrl !== '' && !/^\/(?:[A-Za-z0-9_-]+\/)*[A-Za-z0-9_-]+\/?$/.test(apiBaseUrl)) ||
      !['api', 'demo'].includes(caseSource)) throw new Error('INVALID_DEPLOYMENT_CONFIGURATION');
  return { apiBaseUrl: apiBaseUrl.replace(/\/$/, ''), timeoutMs: 15000, caseSource };
}
function readAsset(base, asset) {
  const file = path.join(base, asset), real = fs.realpathSync(file), realRoot = fs.realpathSync(base);
  if (!real.startsWith(realRoot + path.sep) || fs.lstatSync(file).isSymbolicLink() || !fs.statSync(file).isFile())
    throw new Error('UNSAFE_SOURCE_ASSET: ' + asset);
  return fs.readFileSync(file);
}
function buildPackage(output, options = {}) {
  const settings = config(options), target = path.resolve(output);
  if (fs.existsSync(target)) throw new Error('OUTPUT_ALREADY_EXISTS: choose a new directory');
  // Preflight every allowlisted asset before creating any output; never glob/copy the repository.
  const files = assets.map(asset => ({ path: asset, bytes: asset === 'js/medical-config.js' ?
    Buffer.from('// Deployment configuration only. No credentials or cross-origin API.\nwindow.MedicalApiConfig = Object.freeze(' + JSON.stringify(settings) + ');\n') : readAsset(root, asset) }));
  fs.mkdirSync(target, { recursive: true });
  const manifest = { format: 'medical-static-v5.3-current', config: settings, assets: [] };
  for (const file of files) {
    const destination = path.join(target, file.path); fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.writeFileSync(destination, file.bytes, { flag: 'wx' });
    manifest.assets.push({ path: file.path, bytes: file.bytes.length, sha256: crypto.createHash('sha256').update(file.bytes).digest('hex') });
  }
  fs.writeFileSync(path.join(target, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n', { flag: 'wx' });
  return manifest;
}
module.exports = { config, readAsset, buildPackage };
if (require.main === module) {
  const args = process.argv.slice(2), allowed = ['--out', '--api-base', '--case-source'];
  for (let i = 0; i < args.length; i += 2) if (!allowed.includes(args[i]) || !args[i + 1] || args[i + 1].startsWith('--')) throw new Error('INVALID_ARGUMENTS');
  if (new Set(args.filter((_, i) => i % 2 === 0)).size !== args.length / 2) throw new Error('DUPLICATE_ARGUMENTS');
  const get = key => args.includes(key) ? args[args.indexOf(key) + 1] : undefined;
  if (!get('--out')) throw new Error('Usage: node tools/medical-frontend/package.cjs --out NEW_DIRECTORY [--api-base /gateway] [--case-source api|demo]');
  const result = buildPackage(get('--out'), { apiBaseUrl: get('--api-base'), caseSource: get('--case-source') });
  console.log(`Packaged ${result.assets.length} assets to ${path.resolve(get('--out'))}; source=${result.config.caseSource}. No backend or test fixtures included.`);
}
