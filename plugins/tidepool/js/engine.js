// Floe - Tidepool: the WebGL2 render graph.
//
//   scene (emissive, supersampled) --> bright pass --> blur H --> blur V --> composite --> canvas
//                     |
//                     +-- instanced dust / spray / embers
//
// Everything is drawn for a wide, short strip: the scene shader works in "height units" so waves,
// ribbons and ribbons keep the same proportions at 8:1 and at 30:1.

import * as S from './shaders.js';

const TIERS = {
  high: { ss: 1.55, particles: 3400, bloomDiv: 2, maxPixels: 3.0e6 },
  balanced: { ss: 1.25, particles: 2000, bloomDiv: 2, maxPixels: 2.1e6 },
  low: { ss: 1.0, particles: 1100, bloomDiv: 3, maxPixels: 1.1e6 },
};

const BAND_COUNT = 32;

function compile(gl, type, source, label) {
  const shader = gl.createShader(type);
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
    const log = gl.getShaderInfoLog(shader) || 'unknown';
    gl.deleteShader(shader);
    throw new Error(`[tidepool] ${label} shader: ${log}`);
  }
  return shader;
}

function program(gl, vertSource, fragSource, label) {
  const vert = compile(gl, gl.VERTEX_SHADER, vertSource, `${label}.vert`);
  const frag = compile(gl, gl.FRAGMENT_SHADER, fragSource, `${label}.frag`);
  const prog = gl.createProgram();
  gl.attachShader(prog, vert);
  gl.attachShader(prog, frag);
  gl.linkProgram(prog);
  gl.deleteShader(vert);
  gl.deleteShader(frag);
  if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
    const log = gl.getProgramInfoLog(prog) || 'unknown';
    gl.deleteProgram(prog);
    throw new Error(`[tidepool] ${label} link: ${log}`);
  }
  const uniforms = new Map();
  const count = gl.getProgramParameter(prog, gl.ACTIVE_UNIFORMS);
  for (let i = 0; i < count; i++) {
    const info = gl.getActiveUniform(prog, i);
    uniforms.set(info.name.replace(/\[0\]$/, ''), gl.getUniformLocation(prog, info.name));
  }
  return { prog, uniforms };
}

function target(gl, w, h, internal, format, type) {
  const tex = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, tex);
  gl.texImage2D(gl.TEXTURE_2D, 0, internal, w, h, 0, format, type, null);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  const fbo = gl.createFramebuffer();
  gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
  gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
  const status = gl.checkFramebufferStatus(gl.FRAMEBUFFER);
  if (status !== gl.FRAMEBUFFER_COMPLETE) {
    throw new Error(`[tidepool] framebuffer incomplete: 0x${status.toString(16)}`);
  }
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  return { tex, fbo, w, h };
}

function disposeTarget(gl, t) {
  if (!t) return;
  gl.deleteFramebuffer(t.fbo);
  gl.deleteTexture(t.tex);
}

export class Engine {
  constructor(canvas, options = {}) {
    this.canvas = canvas;
    this.options = {
      intensity: 0.65,
      motion: 1,
      particles: true,
      palette: null, // { core, second, accent, deep } as [r,g,b] 0..1
      bloom: 0.55,
      flat: false,
      quality: 'auto',
    };
    Object.assign(this.options, options);

    const gl = canvas.getContext('webgl2', {
      alpha: true,
      premultipliedAlpha: true,
      antialias: false,
      depth: false,
      stencil: false,
      desynchronized: true,
      powerPreference: 'high-performance',
      preserveDrawingBuffer: false,
    });
    if (!gl) throw new Error('WebGL2 is unavailable in this WebView2 runtime');
    this.gl = gl;
    this.hasFloat = !!gl.getExtension('EXT_color_buffer_float');
    this.internal = this.hasFloat ? gl.RGBA16F : gl.RGBA8;

    this.progs = {
      scene: program(gl, S.fullscreenVert, S.sceneFrag, 'scene'),
      bright: program(gl, S.fullscreenVert, S.brightFrag, 'bright'),
      blur: program(gl, S.fullscreenVert, S.blurFrag, 'blur'),
      composite: program(gl, S.fullscreenVert, S.compositeFrag, 'composite'),
      particles: program(gl, S.particleVert, S.particleFrag, 'particles'),
    };

    this.emptyVao = gl.createVertexArray();
    this.particleVao = gl.createVertexArray();
    this.particleCount = 0;
    this.particleBuffers = null;
    this.makeParticles(TIERS.balanced.particles);

    this.waveTex = [this.makeWaveTexture(), this.makeWaveTexture()];
    this.waveSlot = 0;
    this.waveMix = 1;

    this.bands = new Float32Array(BAND_COUNT);
    this.palette = {
      core: [0.44, 0.89, 0.96],
      second: [0.69, 0.55, 1.0],
      accent: [1.0, 0.48, 0.72],
      deep: [0.05, 0.13, 0.22],
    };

    this.width = 1;
    this.height = 1;
    this.renderWidth = 1;
    this.renderHeight = 1;
    this.tier = 'balanced';
    this.autoTier = true;
    this.frameCost = 0;
    this.goodFrames = 0;
    this.badFrames = 0;

    this.scene = null;
    this.bloomA = null;
    this.bloomB = null;
  }

  // ---- configuration -------------------------------------------------------------------

  setPalette(palette) {
    if (!palette) return;
    const read = (c) => (Array.isArray(c) ? c : hexToRgb(c));
    this.palette = {
      core: read(palette.core),
      second: read(palette.second),
      accent: read(palette.accent),
      deep: read(palette.deep),
    };
  }

  setOptions(options) {
    if (!options) return;
    for (const key of ['bloom', 'flat']) {
      if (typeof options[key] === 'number' || typeof options[key] === 'boolean') {
        if (this.options[key] !== options[key]) this.options[key] = options[key];
      }
    }
    if (typeof options.intensity === 'number') this.options.intensity = Math.min(1, Math.max(0, options.intensity));
    if (typeof options.motion === 'string') this.options.motion = options.motion === 'calm' ? 0.25 : 1;
    if (typeof options.particles === 'boolean' && options.particles !== this.options.particles) {
      this.options.particles = options.particles;
      this.makeParticles(this.tierConfig().particles);
    }
    if (options.quality) {
      if (options.quality === 'auto') {
        this.autoTier = true;
      } else if (TIERS[options.quality] && this.tier !== options.quality) {
        this.autoTier = false;
        this.setTier(options.quality);
      }
    }
  }

  tierConfig() {
    return TIERS[this.tier] || TIERS.balanced;
  }

  setTier(name) {
    if (this.tier === name || !TIERS[name]) return false;
    this.tier = name;
    this.makeParticles(this.tierConfig().particles);
    this.resize(this.width, this.height, this.dpr, true);
    return true;
  }

  // ---- sizing --------------------------------------------------------------------------

  resize(cssWidth, cssHeight, dpr, force = false) {
    const scale = this.tierConfig().ss;
    const pixelRatio = Math.max(1, Math.min(3, dpr || 1));
    let rw = Math.max(2, Math.round(cssWidth * pixelRatio * scale));
    let rh = Math.max(2, Math.round(cssHeight * pixelRatio * scale));

    // keep the fragment budget bounded on very wide monitors
    const budget = this.tierConfig().maxPixels;
    const pixels = rw * rh;
    if (pixels > budget) {
      const k = Math.sqrt(budget / pixels);
      rw = Math.max(2, Math.round(rw * k));
      rh = Math.max(2, Math.round(rh * k));
    }

    if (!force && rw === this.renderWidth && rh === this.renderHeight) {
      this.width = cssWidth;
      this.height = cssHeight;
      this.dpr = pixelRatio;
      return false;
    }

    this.width = cssWidth;
    this.height = cssHeight;
    this.dpr = pixelRatio;
    this.renderWidth = rw;
    this.renderHeight = rh;

    const gl = this.gl;
    disposeTarget(gl, this.scene);
    disposeTarget(gl, this.bloomA);
    disposeTarget(gl, this.bloomB);
    this.scene = target(gl, rw, rh, this.internal, gl.RGBA, this.hasFloat ? gl.HALF_FLOAT : gl.UNSIGNED_BYTE);
    const bw = Math.max(2, Math.floor(rw / this.tierConfig().bloomDiv));
    const bh = Math.max(2, Math.floor(rh / this.tierConfig().bloomDiv));
    this.bloomA = target(gl, bw, bh, this.internal, gl.RGBA, this.hasFloat ? gl.HALF_FLOAT : gl.UNSIGNED_BYTE);
    this.bloomB = target(gl, bw, bh, this.internal, gl.RGBA, this.hasFloat ? gl.HALF_FLOAT : gl.UNSIGNED_BYTE);

    this.canvas.width = rw;
    this.canvas.height = rh;
    this.canvas.style.width = `${cssWidth}px`;
    this.canvas.style.height = `${cssHeight}px`;
    return true;
  }

  // ---- audio ---------------------------------------------------------------------------

  makeWaveTexture() {
    const gl = this.gl;
    const tex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.R16F, 512, 1, 0, gl.RED, gl.FLOAT, new Float32Array(512));
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return tex;
  }

  /** Uploads a fresh waveform into the spare texture and cross-fades from the previous one. */
  pushWave(wave) {
    if (!wave || wave.length < 2) return;
    const gl = this.gl;
    const slot = 1 - this.waveSlot;
    gl.bindTexture(gl.TEXTURE_2D, this.waveTex[slot]);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, Math.min(512, wave.length), 1, gl.RED, gl.FLOAT, wave);
    this.waveSlot = slot;
    this.waveMix = 0;
  }

  setBands(bands) {
    if (!bands) return;
    const n = Math.min(BAND_COUNT, bands.length);
    for (let i = 0; i < n; i++) this.bands[i] = Math.min(1, Math.max(0, bands[i]));
    for (let i = n; i < BAND_COUNT; i++) this.bands[i] = 0;
  }

  // ---- particles -----------------------------------------------------------------------

  makeParticles(count) {
    const gl = this.gl;
    count = Math.max(0, Math.round(count));
    this.particleCount = count;

    if (this.particleVao) gl.deleteVertexArray(this.particleVao);
    if (this.particleBuffers) {
      gl.deleteBuffer(this.particleBuffers.corner);
      gl.deleteBuffer(this.particleBuffers.seed);
      gl.deleteBuffer(this.particleBuffers.motion);
    }

    this.particleVao = gl.createVertexArray();
    gl.bindVertexArray(this.particleVao);

    const corners = new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]);
    const seed = new Float32Array(count * 4);
    const motion = new Float32Array(count * 4);
    const rand = mulberry(0x5EEDBEEF);

    for (let i = 0; i < count; i++) {
      const kindRoll = rand();
      let kind;
      let x;
      if (kindRoll < 0.52) {
        kind = 0; // dust
        x = 0.04 + rand() * 0.92;
      } else if (kindRoll < 0.82) {
        kind = 1; // spray
        x = 0.18 + rand() * 0.64;
      } else {
        kind = 2; // embers
        x = rand() < 0.5 ? 0.02 + rand() * 0.12 : 0.86 + rand() * 0.12;
      }

      seed[i * 4 + 0] = x;
      seed[i * 4 + 1] = rand();
      seed[i * 4 + 2] = (0.006 + rand() * 0.012) * (kind === 1 ? 0.8 : 1);
      seed[i * 4 + 3] = rand();
      motion[i * 4 + 0] = (rand() - 0.5) * 0.18;
      motion[i * 4 + 1] = rand();
      motion[i * 4 + 2] = kind;
      motion[i * 4 + 3] = rand();
    }

    const corner = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, corner);
    gl.bufferData(gl.ARRAY_BUFFER, corners, gl.STATIC_DRAW);
    const cornerLoc = gl.getAttribLocation(this.progs.particles.prog, 'aCorner');
    gl.enableVertexAttribArray(cornerLoc);
    gl.vertexAttribPointer(cornerLoc, 2, gl.FLOAT, false, 0, 0);

    const seedBuf = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, seedBuf);
    gl.bufferData(gl.ARRAY_BUFFER, seed, gl.STATIC_DRAW);
    const seedLoc = gl.getAttribLocation(this.progs.particles.prog, 'aSeed');
    gl.enableVertexAttribArray(seedLoc);
    gl.vertexAttribPointer(seedLoc, 4, gl.FLOAT, false, 0, 0);
    gl.vertexAttribDivisor(seedLoc, 1);

    const motionBuf = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, motionBuf);
    gl.bufferData(gl.ARRAY_BUFFER, motion, gl.STATIC_DRAW);
    const motionLoc = gl.getAttribLocation(this.progs.particles.prog, 'aMotion');
    gl.enableVertexAttribArray(motionLoc);
    gl.vertexAttribPointer(motionLoc, 4, gl.FLOAT, false, 0, 0);
    gl.vertexAttribDivisor(motionLoc, 1);

    gl.bindVertexArray(null);
    this.particleBuffers = { corner, seed: seedBuf, motion: motionBuf, rand };
  }

  // ---- drawing -------------------------------------------------------------------------

  render(frame, dt) {
    const gl = this.gl;
    if (!this.scene) return;

    this.waveMix = Math.min(1, this.waveMix + dt * 12);

    // scene
    gl.bindFramebuffer(gl.FRAMEBUFFER, this.scene.fbo);
    gl.viewport(0, 0, this.scene.w, this.scene.h);
    gl.disable(gl.BLEND);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT);
    this.drawScene(frame);

    if (this.options.particles && this.options.motion > 0.4 && this.particleCount > 0) {
      this.drawParticles(frame);
    }

    // bloom
    gl.disable(gl.BLEND);
    gl.bindFramebuffer(gl.FRAMEBUFFER, this.bloomA.fbo);
    gl.viewport(0, 0, this.bloomA.w, this.bloomA.h);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT);
    this.useFullscreen(this.progs.bright);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, this.scene.tex);
    gl.uniform1i(this.progs.bright.uniforms.get('uScene'), 0);
    gl.uniform1f(this.progs.bright.uniforms.get('uThreshold'), 0.66);
    this.drawFullscreen();

    this.blur(this.bloomA, this.bloomB, 1, 1.0);
    this.blur(this.bloomB, this.bloomA, 0, 1.0);

    // composite
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.viewport(0, 0, this.canvas.width, this.canvas.height);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT);
    this.useFullscreen(this.progs.composite);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, this.scene.tex);
    gl.uniform1i(this.progs.composite.uniforms.get('uScene'), 0);
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, this.bloomA.tex);
    gl.uniform1i(this.progs.composite.uniforms.get('uBloom'), 1);
    gl.uniform2f(this.progs.composite.uniforms.get('uRes'), this.canvas.width, this.canvas.height);
    gl.uniform1f(this.progs.composite.uniforms.get('uBloomStrength'), this.options.bloom * 0.62);
    gl.uniform1f(this.progs.composite.uniforms.get('uFade'), frame.fade);
    gl.uniform1f(this.progs.composite.uniforms.get('uTime'), frame.time);
    gl.disable(gl.BLEND);
    this.drawFullscreen();
    gl.activeTexture(gl.TEXTURE0);
  }

  drawScene(frame) {
    const gl = this.gl;
    const p = this.progs.scene;
    this.useFullscreen(p);
    const u = p.uniforms;
    gl.uniform2f(u.get('uRes'), this.scene.w, this.scene.h);
    gl.uniform1f(u.get('uTime'), frame.time);
    gl.uniform1f(u.get('uFade'), frame.fade);
    gl.uniform1f(u.get('uIntensity'), this.options.intensity);
    gl.uniform1f(u.get('uMotion'), this.options.motion);
    gl.uniform4f(u.get('uDock'), frame.dock[0], frame.dock[1], frame.dock[2], frame.dock[3]);
    gl.uniform3fv(u.get('uCore'), this.palette.core);
    gl.uniform3fv(u.get('uSecond'), this.palette.second);
    gl.uniform3fv(u.get('uAccent'), this.palette.accent);
    gl.uniform3fv(u.get('uDeep'), this.palette.deep);
    gl.uniform1f(u.get('uBass'), frame.bass);
    gl.uniform1f(u.get('uMid'), frame.mid);
    gl.uniform1f(u.get('uTreble'), frame.treble);
    gl.uniform1f(u.get('uLevel'), frame.level);
    gl.uniform1f(u.get('uSnare'), frame.snare);
    gl.uniform1f(u.get('uHat'), frame.hat);
    gl.uniform1fv(u.get('uBands'), this.bands);
    gl.uniform1fv(u.get('uBeatAge'), frame.beatAge);
    gl.uniform1fv(u.get('uBeatStr'), frame.beatStr);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, this.waveTex[1 - this.waveSlot]);
    gl.uniform1i(u.get('uWaveA'), 0);
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, this.waveTex[this.waveSlot]);
    gl.uniform1i(u.get('uWaveB'), 1);
    gl.uniform1f(u.get('uWaveMix'), this.waveMix);
    gl.uniform1f(u.get('uFlat'), this.options.flat ? 1 : 0);
    this.drawFullscreen();
    gl.activeTexture(gl.TEXTURE0);
  }

  drawParticles(frame) {
    const gl = this.gl;
    const p = this.progs.particles;
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
    gl.useProgram(p.prog);
    const u = p.uniforms;
    gl.uniform2f(u.get('uRes'), this.scene.w, this.scene.h);
    gl.uniform1f(u.get('uTime'), frame.time);
    gl.uniform1f(u.get('uFade'), frame.fade);
    gl.uniform1f(u.get('uLevel'), frame.level);
    gl.uniform1f(u.get('uTreble'), frame.treble);
    gl.uniform1f(u.get('uKick'), frame.kick);
    gl.uniform1f(u.get('uMotion'), this.options.motion);
    gl.uniform3fv(u.get('uCore'), this.palette.core);
    gl.uniform3fv(u.get('uAccent'), this.palette.accent);
    gl.bindVertexArray(this.particleVao);
    gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, this.particleCount);
    gl.bindVertexArray(null);
    gl.disable(gl.BLEND);
  }

  blur(from, to, horizontal, radius) {
    const gl = this.gl;
    gl.bindFramebuffer(gl.FRAMEBUFFER, to.fbo);
    gl.viewport(0, 0, to.w, to.h);
    this.useFullscreen(this.progs.blur);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, from.tex);
    gl.uniform1i(this.progs.blur.uniforms.get('uTex'), 0);
    gl.uniform2f(this.progs.blur.uniforms.get('uDir'), horizontal ? 1 / from.w : 0, horizontal ? 0 : 1 / from.h);
    gl.uniform1f(this.progs.blur.uniforms.get('uRadius'), radius);
    this.drawFullscreen();
  }

  useFullscreen(p) {
    this.gl.useProgram(p.prog);
    this.gl.bindVertexArray(this.emptyVao);
  }

  drawFullscreen() {
    this.gl.drawArrays(this.gl.TRIANGLES, 0, 3);
  }

  // ---- adaptivity ----------------------------------------------------------------------

  /** Feeds the measured frame cost; returns a new tier name when the engine switched. */
  noteFrame(milliseconds) {
    this.frameCost = this.frameCost * 0.9 + milliseconds * 0.1;
    if (!this.autoTier) return null;
    const order = ['low', 'balanced', 'high'];
    const at = order.indexOf(this.tier);
    if (this.frameCost > 21 && at > 0) {
      this.badFrames++;
      this.goodFrames = 0;
      if (this.badFrames >= 45) {
        this.badFrames = 0;
        this.setTier(order[at - 1]);
        return this.tier;
      }
    } else if (this.frameCost < 12 && at < order.length - 1) {
      this.goodFrames++;
      this.badFrames = 0;
      if (this.goodFrames >= 150) {
        this.goodFrames = 0;
        this.setTier(order[at + 1]);
        return this.tier;
      }
    } else {
      this.badFrames = 0;
      this.goodFrames = 0;
    }
    return null;
  }

  dispose() {
    const gl = this.gl;
    disposeTarget(gl, this.scene);
    disposeTarget(gl, this.bloomA);
    disposeTarget(gl, this.bloomB);
    gl.deleteTexture(this.waveTex[0]);
    gl.deleteTexture(this.waveTex[1]);
    for (const p of Object.values(this.progs)) gl.deleteProgram(p.prog);
    for (const t of Object.values(this.particleBuffers || {})) {
      if (t && t !== this.particleBuffers.rand) gl.deleteBuffer(t);
    }
    gl.deleteVertexArray(this.particleVao);
  }
}

function hexToRgb(hex) {
  if (typeof hex !== 'string') return [1, 1, 1];
  const h = hex.replace('#', '');
  const n = parseInt(h.length === 3 ? h.split('').map((c) => c + c).join('') : h, 16);
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
}

function mulberry(seed) {
  let a = seed >>> 0;
  return () => {
    a |= 0;
    a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}





