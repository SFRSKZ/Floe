// Floe · Tidepool — a synthetic performance for the browser preview.
//
// It is not a recording: just enough structure (intro, verse, build, drop, break, second drop) to
// exercise every part of the visualizer with musical timing, so the preview looks like a show.

const BPM = 122;
const BEAT = 60 / BPM;
const BAR = BEAT * 4;
const LOOP_BARS = 48;

export class Synth {
  constructor(seed = 1) {
    this.seed = seed * 0.37;
  }

  sectionAt(t) {
    const bars = (t / BAR) % LOOP_BARS;
    const i = Math.floor(bars);
    let name = 'intro';
    let drums = 0;
    let pad = 0.35;
    let hats = 0;
    let lead = 0.2;
    let rise = 0;

    if (i < 4) {
      name = 'intro';
    } else if (i < 12) {
      name = 'verse';
      drums = 0.55; pad = 0.55; hats = 0.22; lead = 0.3;
    } else if (i < 16) {
      name = 'build';
      drums = 0.7; pad = 0.7; hats = 0.38; lead = 0.5;
      rise = (i - 12) / 4;
    } else if (i < 28) {
      name = 'drop';
      drums = 1; pad = 0.8; hats = 0.6; lead = 0.9;
    } else if (i < 32) {
      name = 'break';
      pad = 0.9; lead = 0.45;
    } else if (i < 44) {
      name = 'drop2';
      drums = 1; pad = 0.85; hats = 0.7; lead = 1;
    } else {
      name = 'outro';
      drums = 0.25; pad = 0.5; hats = 0.1; lead = 0.25;
    }

    return { bars, i, name, drums, pad, hats, lead, rise };
  }

  /** A synthetic spectrum frame at time t (seconds). Deterministic. */
  sample(t) {
    const s = this.sectionAt(t);
    const kickAge = t % BEAT;
    const snareOn = Math.floor(t / BEAT) % 2 === 1;
    const hatAge = t % (BEAT / 2);

    const kickEnv = s.drums * Math.exp(-kickAge / 0.16) * (0.75 + 0.25 * Math.sin(t * 0.6));
    const snareEnv = s.drums * (snareOn ? Math.exp(-kickAge / 0.11) : 0) * 0.8;
    const hatEnv = s.hats * Math.exp(-hatAge / 0.045) * 0.7 + s.hats * 0.18;

    // a slow chord movement: three partials that glide every four bars
    const phrase = Math.floor(s.bars / 4);
    const root = 110 * (1 + 0.06 * Math.sin(phrase * 1.7 + this.seed));
    const partials = [1, 1.51, 2.42];

    const bands = new Float32Array(32);
    for (let i = 0; i < 32; i++) {
      const f = 30 * Math.pow(16000 / 30, (i + 0.5) / 32);
      const bassShape = Math.exp(-f / 130);
      const midShape = Math.exp(-Math.pow((Math.log2(f) - Math.log2(620)) / 1.5, 2));
      const highShape = 1 / (1 + Math.pow(2400 / f, 2.2));

      let v = 0;
      v += kickEnv * bassShape * 1.25;
      v += s.pad * 0.55 * (bassShape * 0.35 + midShape * 0.9 + highShape * 0.15);
      v += snareEnv * (midShape * 0.55 + highShape * 0.45) * 0.65;
      v += hatEnv * highShape * 0.9;
      v += s.lead * (midShape * 0.35 + bassShape * 0.15) * (0.5 + 0.5 * Math.sin(t * 2.1 + i * 0.5));
      v += s.rise * highShape * 0.35;

      // resonance around the chord partials, so the spectrum has peaks instead of a smooth hill
      for (const p of partials) {
        const target = root * p;
        const d = Math.log2(f / target);
        v += (kickEnv * 0.25 + s.pad * 0.4) * Math.exp(-(d * d) / 0.06) * 0.9;
      }

      const wobble = 0.75 + 0.25 * n1(t * 0.9 + i * 0.37, this.seed + i);
      v *= wobble * (0.55 + 0.45 * s.lead);
      bands[i] = softclip(v * 0.9);
    }

    const bass = average(bands, 0, 7);
    const mid = average(bands, 7, 19);
    const treble = average(bands, 19, 31);
    const level = softclip((bass * 1.1 + mid * 0.8 + treble * 0.6) * 0.55);

    const wave = new Float32Array(512);
    const fs = 6000;
    const phase = 2 * Math.PI * 110 * t;
    for (let n = 0; n < 512; n++) {
      const tt = t + n / fs;
      const k = Math.exp(-(tt % BEAT) / 0.13) * s.drums;
      let v =
        0.55 * Math.sin(2 * Math.PI * 110 * tt + 0.4) +
        0.24 * Math.sin(2 * Math.PI * 220 * tt + 1.1) +
        0.12 * Math.sin(2 * Math.PI * 330 * tt + 2.2) +
        0.10 * Math.sin(2 * Math.PI * 660 * tt + 0.7) +
        0.28 * k * Math.sin(2 * Math.PI * 62 * tt);
      v += 0.05 * n1(tt * 11, this.seed + n * 0.01);
      wave[n] = Math.max(-1, Math.min(1, v * (0.75 + 0.25 * s.lead)));
    }

    return {
      t,
      bands,
      bass,
      mid,
      treble,
      level,
      kick: s.drums > 0.3 ? 0.85 * Math.exp(-kickAge / 0.05) : 0,
      snare: snareOn ? 0.7 * Math.exp(-kickAge / 0.05) : 0,
      hat: hatEnv > 0.3 ? 0.6 : 0.1,
      balance: Math.sin(t * 0.21) * 0.25,
      wave,
    };
  }
}

function n1(t, seed) {
  return Math.sin(t + seed) * 0.55 + Math.sin(t * 2.3 + seed * 2.1) * 0.3 + Math.sin(t * 4.7 + seed * 3.3) * 0.15;
}

function softclip(v) {
  return Math.min(1, Math.max(0, 1 - Math.exp(-Math.max(0, v) * 1.6)));
}

function average(bands, from, to) {
  let sum = 0;
  let count = 0;
  for (let i = from; i <= to && i < bands.length; i++) {
    sum += bands[i];
    count++;
  }
  return count ? sum / count : 0;
}
