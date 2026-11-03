// Floe - Loom: browser preview harness.
//
//   ?h=110         strip height in CSS px (48..240)
//   ?dock=0        hide the stand-in capsule
//   ?freeze=1      draw a single settled frame (used for stills and screenshots)
//   ?t=12.5        seconds into the synthetic set
//   ?quality=high  force a tier: high | balanced | low
//   ?palette=aurora  force a palette: auto | aurora | tide | ember | mono
//   ?intensity=0.8
//   ?motion=calm

import { Loom } from '../js/app.js';
import { Synth } from './synth.js';

const params = new URLSearchParams(location.search);
const stripHeight = Math.max(48, Math.min(240, Number(params.get('h') || 110)));
const showDock = params.get('dock') !== '0';
const freeze = params.get('freeze') === '1';
const startAt = Number(params.get('t') || 10.6);
const seed = Number(params.get('seed') || 1);
const quality = params.get('quality') || 'auto';
const palette = params.get('palette') || 'auto';
const intensity = Number(params.get('intensity') || 0.65);
const motion = params.get('motion') || 'full';

document.documentElement.style.setProperty('--strip-height', `${stripHeight}px`);
const dockEl = document.getElementById('dock');
if (!showDock) dockEl.style.display = 'none';

const canvas = document.getElementById('loom');
const session = new Loom(canvas);
session.onPerf = (message) => {
  if (message.type === 'perf') console.log('[loom]', message);
};
session.onError = (error) => console.error('[loom]', error);

function layout() {
  const rect = canvas.getBoundingClientRect();
  let dock = [0.26, 0.74, 0.10, 0.96];
  if (showDock) {
    const d = dockEl.getBoundingClientRect();
    dock = [
      (d.left - rect.left) / rect.width,
      (d.right - rect.left) / rect.width,
      (rect.bottom - d.bottom) / rect.height,
      (rect.bottom - d.top) / rect.height,
    ];
  }

  session.setState({
    w: rect.width,
    h: rect.height,
    dpr: Math.min(2, window.devicePixelRatio || 1),
    dock,
    palette: null,
    running: true,
    options: { quality, palette, intensity, motion, particles: params.get('particles') === '1', bloom: Number(params.get('bloom') ?? 0.55), flat: params.get('flat') === '1' },
  });
}

layout();
addEventListener('resize', layout);

const synth = new Synth(seed);

if (freeze) {
  session.source = (now) => synth.sample(startAt + now);
  // settle the envelopes exactly the way a live show would
  const dt = 1 / 60;
  for (let i = 1; i <= 170; i++) session.step(dt, i * dt);
  document.title = 'READY';
} else {
  session.source = (now) => synth.sample(startAt + now);
  session.start();
  // eslint-disable-next-line no-console
  console.log(`[loom] preview - -- ${stripHeight}px -- ${quality} -- ${palette}`);
  window.loom = session;
}



