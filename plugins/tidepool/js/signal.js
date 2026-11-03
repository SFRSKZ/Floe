// Floe · Tidepool — audio conditioning.
//
// The host sends normalised spectrum frames at ~60 Hz. Drawing happens at the display's own rate,
// so this module turns the frame stream into continuous motion: snapshots are interpolated on the
// host clock, each band has its own attack/release, and onsets become envelopes the shader can use.

const BAND_COUNT = 32;
const BEAT_SLOTS = 4;

export class Signal {
  constructor() {
    this.prev = null;
    this.cur = null;
    this.offset = null;
    this.latency = 0;

    this.bands = new Float32Array(BAND_COUNT);
    this.bass = 0;
    this.mid = 0;
    this.treble = 0;
    this.level = 0;
    this.kick = 0;
    this.snare = 0;
    this.hat = 0;
    this.balance = 0;

    this.beats = [];
    this.beatAge = new Float32Array(BEAT_SLOTS).fill(-1);
    this.beatStr = new Float32Array(BEAT_SLOTS);

    this.wave = new Float32Array(512);
    this.waveVersion = 0;
    this.lastFrameAt = 0;
    this.stale = 0;
  }

  /** A frame straight from the host bridge: { t, bands, bass, mid, treble, level, kick, snare, hat, balance, wave }. */
  push(frame, localNow) {
    if (!frame || typeof frame.t !== 'number') return;
    const normalised = {
      t: frame.t,
      bands: frame.bands || [],
      bass: clamp01(frame.bass),
      mid: clamp01(frame.mid),
      treble: clamp01(frame.treble),
      level: clamp01(frame.level),
      kick: clamp01(frame.kick),
      snare: clamp01(frame.snare),
      hat: clamp01(frame.hat),
      balance: Math.max(-1, Math.min(1, frame.balance || 0)),
      wave: frame.wave || null,
    };

    this.prev = this.cur;
    this.cur = normalised;
    this.lastFrameAt = localNow;

    // Host clock → local clock. Follow the drift slowly so a hiccup never yanks the motion.
    const estimate = normalised.t - localNow;
    if (this.offset === null) {
      this.offset = estimate;
    } else {
      this.offset += (estimate - this.offset) * 0.06;
    }

    if (normalised.kick > 0.06) {
      this.beats.push({ age: 0, str: normalised.kick });
      if (this.beats.length > BEAT_SLOTS) this.beats.shift();
    }

    if (normalised.wave && normalised.wave.length > 1) {
      const n = Math.min(512, normalised.wave.length);
      for (let i = 0; i < n; i++) this.wave[i] = normalised.wave[i];
      this.waveVersion++;
    }
  }

  /** Advances envelopes by dt and returns everything a render frame needs. */
  advance(dt, localNow) {
    dt = Math.max(0.0005, Math.min(0.05, dt));
    const hostNow = localNow + (this.offset ?? 0);

    let raw = this.sampleRaw(hostNow);
    if (localNow - this.lastFrameAt > 0.35) {
      // the host went quiet (stopped, minimised, or lost the audio device): fall towards silence
      raw = this.silence(raw, dt);
    }

    for (let i = 0; i < BAND_COUNT; i++) {
      const target = Math.pow(raw.bands[i], 1.12);
      const tau = target > this.bands[i] ? 0.016 : 0.085;
      this.bands[i] += (target - this.bands[i]) * (1 - Math.exp(-dt / tau));
    }

    this.bass = follow(this.bass, raw.bass, dt, 0.035, 0.32);
    this.mid = follow(this.mid, raw.mid, dt, 0.030, 0.28);
    this.treble = follow(this.treble, raw.treble, dt, 0.022, 0.18);
    this.level = follow(this.level, raw.level, dt, 0.045, 0.35);
    this.balance = follow(this.balance, raw.balance, dt, 0.06, 0.5);

    // onsets as short envelopes so a single hit reads as a punch, not a step
    this.kick = Math.max(this.kick * Math.exp(-dt / 0.22), raw.kick);
    this.snare = Math.max(this.snare * Math.exp(-dt / 0.16), raw.snare);
    this.hat = Math.max(this.hat * Math.exp(-dt / 0.09), raw.hat);

    for (const beat of this.beats) beat.age += dt;
    this.beats = this.beats.filter((b) => b.age < 2.2);

    const n = Math.min(BEAT_SLOTS, this.beats.length);
    for (let i = 0; i < BEAT_SLOTS; i++) {
      if (i < n) {
        this.beatAge[i] = this.beats[i].age;
        this.beatStr[i] = this.beats[i].str * (i === n - 1 ? 1 : 0.6);
      } else {
        this.beatAge[i] = -1;
        this.beatStr[i] = 0;
      }
    }

    return {
      bands: this.bands,
      bass: this.bass,
      mid: this.mid,
      treble: this.treble,
      level: this.level,
      kick: this.kick,
      snare: this.snare,
      hat: this.hat,
      balance: this.balance,
      beatAge: this.beatAge,
      beatStr: this.beatStr,
      waveVersion: this.waveVersion,
      wave: this.wave,
    };
  }

  sampleRaw(hostNow) {
    const a = this.prev;
    const b = this.cur;
    if (!b) {
      return { bands: new Float32Array(BAND_COUNT), bass: 0, mid: 0, treble: 0, level: 0, kick: 0, snare: 0, hat: 0, balance: 0 };
    }

    const span = a ? Math.max(0.001, b.t - a.t) : 0.016;
    let k = a ? (hostNow - a.t) / span : 1;
    k = Math.max(0, Math.min(1, k));

    const bands = new Float32Array(BAND_COUNT);
    const count = Math.min(BAND_COUNT, b.bands.length || 0);
    for (let i = 0; i < count; i++) {
      const av = a && a.bands.length ? a.bands[i] : b.bands[i];
      bands[i] = (av || 0) * (1 - k) + (b.bands[i] || 0) * k;
    }
    for (let i = count; i < BAND_COUNT; i++) bands[i] = 0;

    return {
      bands,
      bass: mix(a && a.bass, b.bass, k),
      mid: mix(a && a.mid, b.mid, k),
      treble: mix(a && a.treble, b.treble, k),
      level: mix(a && a.level, b.level, k),
      kick: b.kick,
      snare: b.snare,
      hat: b.hat,
      balance: mix(a && a.balance, b.balance, k),
    };
  }

  silence(raw, dt) {
    const fall = Math.exp(-dt / 0.4);
    raw.bass *= fall;
    raw.mid *= fall;
    raw.treble *= fall;
    raw.level *= fall;
    raw.kick = 0;
    raw.snare = 0;
    raw.hat = 0;
    raw.balance *= fall;
    for (let i = 0; i < raw.bands.length; i++) raw.bands[i] *= fall;
    return raw;
  }
}

function follow(current, target, dt, attack, release) {
  const tau = target > current ? attack : release;
  return current + (target - current) * (1 - Math.exp(-dt / tau));
}

function mix(a, b, k) {
  return (a || 0) * (1 - k) + (b || 0) * k;
}

function clamp01(v) {
  return typeof v === 'number' && v > 0 ? Math.min(1, v) : 0;
}
