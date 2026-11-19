// Floe - Tidepool: session wiring.
//
// Host mode: Floe posts `state` and `frame` messages through WebView2's bridge, the plugin answers
// with `ready`/`perf`/`error`. Preview mode: a synthetic performance feeds the same pipeline so the
// visualizer can be tuned in a plain browser without Floe.

import { Engine } from './engine.js';
import { Signal } from './signal.js';
import { resolvePalette } from './palette.js';

const DEFAULT_DOCK = [0.26, 0.74, 0.10, 0.96];

export class Tidepool {
  constructor(canvas) {
    this.canvas = canvas;
    this.engine = new Engine(canvas, { quality: 'auto' });
    this.signal = new Signal();

    this.width = 1280;
    this.height = 96;
    this.dpr = 1;
    this.dock = DEFAULT_DOCK.slice();
    this.paletteOption = 'auto';
    this.hostPalette = null;
    this.options = { intensity: 0.65, motion: 'full', particles: true, quality: 'auto', bloom: 0.55 };

    this.running = true;
    this.fade = 0;
    this.targetFade = 0;
    this.time = 0;
    this.last = 0;
    this.lastWave = -1;
    this.raf = 0;
    this.source = null;
    this.lastSourceAt = 0;
    this.reportAt = 0;
    this.reportFrames = 0;
    this.reportCost = 0;
    this.onPerf = null;
    this.onError = null;

    this.frame = {
      time: 0,
      fade: 0,
      dock: this.dock,
      bass: 0, mid: 0, treble: 0, level: 0,
      kick: 0, snare: 0, hat: 0,
      beatAge: new Float32Array([-1, -1, -1, -1]),
      beatStr: new Float32Array(4),
    };

    this.tick = this.tick.bind(this);
  }

  // ---- host state ----------------------------------------------------------------------

  setState(state) {
    if (!state) return;
    if (typeof state.w === 'number' && typeof state.h === 'number' && state.w > 0 && state.h > 0) {
      this.width = state.w;
      this.height = state.h;
    }
    if (typeof state.dpr === 'number' && state.dpr > 0) this.dpr = state.dpr;
    if (Array.isArray(state.dock) && state.dock.length === 4) this.dock = state.dock.map((v) => Number(v) || 0);
    if (state.palette) this.hostPalette = state.palette;
    if (typeof state.paletteOption === 'string') this.paletteOption = state.paletteOption;
    if (state.options) this.setOptions(state.options);
    this.engine.setPalette(resolvePalette(this.paletteOption, this.hostPalette));
    this.engine.resize(this.width, this.height, this.dpr);

    if (typeof state.running === 'boolean') {
      this.setRunning(state.running);
    }
  }

  setOptions(options) {
    Object.assign(this.options, options);
    if (options.palette) this.paletteOption = options.palette;
    this.engine.setPalette(resolvePalette(this.paletteOption, this.hostPalette));
    this.engine.setOptions(options);
    this.engine.resize(this.width, this.height, this.dpr);
  }

  setRunning(running) {
    this.running = !!running;
    this.targetFade = this.running ? 1 : 0;
    if (this.running) this.start();
  }

  // ---- frames --------------------------------------------------------------------------

  pushFrame(frame, localNow) {
    this.signal.push(frame, localNow === undefined ? performance.now() / 1000 : localNow);
  }

  start() {
    if (this.raf) return;
    this.last = 0;
    this.raf = requestAnimationFrame(this.tick);
  }

  stop() {
    if (this.raf) cancelAnimationFrame(this.raf);
    this.raf = 0;
  }

  tick(nowMs) {
    this.raf = requestAnimationFrame(this.tick);
    const now = nowMs / 1000;
    const dt = this.last ? Math.min(0.05, Math.max(1 / 1000, now - this.last)) : 1 / 60;
    this.last = now;
    this.time += dt;

    const started = performance.now();
    this.step(dt, now);
    this.measure(performance.now() - started, dt);
  }

  step(dt, now) {
    if (this.source && (this.lastSourceAt === 0 || now - this.lastSourceAt >= 1 / 61)) {
      this.pushFrame(this.source(now), now);
      this.lastSourceAt = now;
    }

    const audio = this.signal.advance(dt, now);

    const tau = this.targetFade > this.fade ? 0.20 : 0.38;
    this.fade += (this.targetFade - this.fade) * (1 - Math.exp(-dt / tau));
    if (!this.running && this.fade < 0.004) {
      this.fade = 0;
      this.engine.render({ ...this.frame, fade: 0 }, dt);
      this.stop();
      return;
    }

    if (audio.waveVersion !== this.lastWave) {
      this.engine.pushWave(audio.wave);
      this.lastWave = audio.waveVersion;
    }

    this.engine.setBands(audio.bands);
    this.frame.time = this.time;
    this.frame.fade = this.fade;
    this.frame.dock = this.dock;
    this.frame.bass = audio.bass;
    this.frame.mid = audio.mid;
    this.frame.treble = audio.treble;
    this.frame.level = audio.level;
    this.frame.kick = audio.kick;
    this.frame.snare = audio.snare;
    this.frame.hat = audio.hat;
    this.frame.beatAge = audio.beatAge;
    this.frame.beatStr = audio.beatStr;

    this.engine.render(this.frame, dt);
  }

  measure(cost, dt) {
    this.reportFrames++;
    this.reportCost += cost;
    this.reportAt += dt;
    const tier = this.engine.noteFrame(cost);
    if (tier) this.emit({ type: 'perf', tier });

    if (this.reportAt >= 1.0) {
      const avg = this.reportCost / Math.max(1, this.reportFrames);
      this.emit({ type: 'perf', fps: Math.round(this.reportFrames / this.reportAt), ms: Number(avg.toFixed(2)), tier: this.engine.tier });
      this.reportAt = 0;
      this.reportFrames = 0;
      this.reportCost = 0;
    }
  }

  emit(message) {
    if (this.onPerf) this.onPerf(message);
  }

  fail(error) {
    this.emit({ type: 'error', message: String(error && error.message ? error.message : error) });
    if (this.onError) this.onError(error);
  }
}

/** Boots the plugin inside Floe (WebView2 bridge present). */
export function startHost() {
  const canvas = document.getElementById('tidepool');
  const view = window.chrome && window.chrome.webview;

  if (!view) {
    document.title = 'TIDEPOOL - NO HOST';
    console.warn('[tidepool] no WebView2 bridge; open preview/preview.html instead');
    return null;
  }

  let session;
  try {
    session = new Tidepool(canvas);
  } catch (error) {
    view.postMessage({ type: 'error', phase: 'init', message: String(error) });
    document.title = 'TIDEPOOL - INIT FAILED';
    throw error;
  }

  const handle = (data) => {
    if (!data) return;
    const message = typeof data === 'string' ? safeParse(data) : data;
    if (!message || !message.type) return;
    if (message.type === 'state') session.setState(message);
    else if (message.type === 'frame') session.pushFrame(message);
    else if (message.type === 'options') session.setOptions(message.options || {});
    else if (message.type === 'preview') session.setState({ running: true });
  };

  view.addEventListener('message', (event) => handle(event.data));
  session.onPerf = (perf) => view.postMessage(perf);
  session.onError = (error) => {
    view.postMessage({ type: 'error', phase: 'frame', message: String(error) });
    document.title = 'TIDEPOOL - FRAME ERROR';
  };

  session.setState({ running: false });
  session.start();
  view.postMessage({ type: 'ready', id: 'floe.tidepool', version: '1.0.0' });
  document.title = 'TIDEPOOL - READY';
  return session;
}

function safeParse(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}



