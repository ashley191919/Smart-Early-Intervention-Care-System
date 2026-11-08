import { readFileSync } from 'node:fs';
import { createInterface } from 'node:readline';
import { startFrontend } from '../../tools/LocalHostProbe/server.mjs';

// Reuse the tested fixed-route, credential-free health proxy; no generic API proxy.
const control = startFrontend(new Map([
  ['/', ['text/html; charset=utf-8', readFileSync(new URL('./index.html', import.meta.url))]],
  ['/app.js', ['text/javascript; charset=utf-8', readFileSync(new URL('./app.js', import.meta.url))]],
  ['/probe.js', ['text/javascript; charset=utf-8', readFileSync(new URL('../../tools/LocalHostProbe/probe.js', import.meta.url))]],
]));
if (control) {
  const input = createInterface({ input: process.stdin, terminal: false });
  input.on('line', line => {
    if (line === 'stop') { input.close(); process.stdin.pause(); control.stop(); }
  });
}
