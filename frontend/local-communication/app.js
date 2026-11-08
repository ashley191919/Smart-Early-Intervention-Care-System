import '/probe.js';

// Read-only layout evidence for the local page; no telemetry or external request.
document.documentElement.dataset.viewport = `${innerWidth}x${innerHeight}`;
document.documentElement.dataset.horizontalOverflow = String(document.documentElement.scrollWidth > innerWidth);
// Load the actual API result once on entry; the same button supports manual retry.
document.querySelector('#check').click();
