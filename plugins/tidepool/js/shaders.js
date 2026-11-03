// Floe - Tidepool: GLSL ES 3.00 sources.
//
// The scene is accumulated as emissive light plus coverage, then premultiplied at the end, so the
// strip can sit over any wallpaper without painting a dark band behind it.

export const fullscreenVert = /* glsl */ `#version 300 es
precision highp float;
out vec2 vUV;
void main() {
  // one triangle that covers the viewport
  vec2 p = vec2(gl_VertexID == 1 ? 3.0 : -1.0, gl_VertexID == 2 ? 3.0 : -1.0);
  vUV = p * 0.5 + 0.5;
  gl_Position = vec4(p, 0.0, 1.0);
}`;

export const sceneFrag = /* glsl */ `#version 300 es
precision highp float;

in vec2 vUV;
out vec4 o;

uniform vec2  uRes;
uniform float uTime;
uniform float uFade;
uniform float uIntensity;
uniform float uMotion;
uniform vec4  uDock;        // left, right, bottom, top - normalised, y from the bottom
uniform vec3  uCore;
uniform vec3  uSecond;
uniform vec3  uAccent;
uniform vec3  uDeep;
uniform float uBass, uMid, uTreble, uLevel;
uniform float uSnare, uHat;
uniform float uBands[32];
uniform float uBeatAge[4];
uniform float uBeatStr[4];
uniform sampler2D uWaveA;
uniform sampler2D uWaveB;
uniform float uWaveMix;
uniform float uFlat;

float hash12(vec2 p) {
  vec3 p3 = fract(vec3(p.xyx) * 0.1031);
  p3 += dot(p3, p3.yzx + 33.33);
  return fract((p3.x + p3.y) * p3.z);
}

float vnoise(vec2 p) {
  vec2 i = floor(p);
  vec2 f = fract(p);
  f = f * f * (3.0 - 2.0 * f);
  float a = hash12(i);
  float b = hash12(i + vec2(1.0, 0.0));
  float c = hash12(i + vec2(0.0, 1.0));
  float d = hash12(i + vec2(1.0, 1.0));
  return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
  return 0.55 * vnoise(p) + 0.30 * vnoise(p * 2.03 + 5.0) + 0.15 * vnoise(p * 4.11 + 9.0);
}

// A smooth 0..1 ramp that also works when b < a (smoothstep would be undefined there).
float ramp(float x, float a, float b) {
  float t = clamp((x - a) / (b - a), 0.0, 1.0);
  return t * t * (3.0 - 2.0 * t);
}

float bell(float d, float width) {
  float k = d / width;
  return exp(-k * k);
}

float sdRoundBox(vec2 p, vec2 b, float r) {
  vec2 d = abs(p) - b + r;
  return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - r;
}

// The strip's colour runs from the centre out to the edges, the way Floe paints its own palettes.
vec3 stripTint(float u) {
  float t = clamp(abs(u * 2.0 - 1.0), 0.0, 1.0);
  return t < 0.5 ? mix(uCore, uSecond, t * 2.0) : mix(uSecond, uAccent, (t - 0.5) * 2.0);
}

// The waterline: long swells from the bass, ripples from the mids, capillary shimmer from the
// treble, then a travelling wave packet from every kick that is still young.
float surface(float x, float wl) {
  float t = uTime;
  float amp = smoothstep(0.0, 0.30, vUV.x) * smoothstep(1.0, 0.70, vUV.x);
  float h = (0.020 + 0.055 * uBass) * sin(x * 1.9 - t * 0.85 + 0.7);
  h += (0.010 + 0.026 * uMid) * sin(x * 3.4 + t * 1.35 + 2.1);
  h += (0.004 + 0.013 * uTreble) * sin(x * 6.3 - t * 2.4 + 4.4);
  h += 0.006 * uLevel * sin(x * 9.2 + t * 3.1);

  float packet = 0.0;
  for (int i = 0; i < 4; i++) {
    float age = uBeatAge[i];
    if (age < 0.0 || age > 2.2) continue;
    float s = uBeatStr[i] * exp(-age * 2.0);
    if (s < 0.02) continue;
    float dir = (i == 0 || i == 2) ? 1.0 : -1.0;
    float c = 0.5 * (uRes.x / uRes.y) + dir * age * 2.0;
    float d = x - c;
    packet += s * bell(d, 0.55) * sin(d * 6.0 - age * 8.0);
  }

  return (h + 0.022 * packet) * amp * mix(0.6, 1.0, uMotion);
}

void main() {
  if (uFlat > 0.5) { o = vec4(0.06, 0.22, 0.42, 0.55); return; }

  vec2 uv = vUV;
  float aspect = uRes.x / uRes.y;
  float px = 1.0 / uRes.y;
  float x = uv.x * aspect;
  float y = uv.y;
  vec3 tint = stripTint(uv.x);

  float wl = uDock.z + 0.006;
  float sY = wl + surface(x, wl);

  vec3 lit = vec3(0.0);
  float mask = 0.0;

  float capW = uDock.y - uDock.x;
  float capH = uDock.w - uDock.z;
  bool capsule = capW > 0.05 && capH > 0.04;

  // ---- the basin: a pool of light right under the capsule -----------------------------------
  {
    float gx = (x - 0.5 * (uDock.x + uDock.y) * aspect) / (0.55 * aspect + 0.0001);
    float gy = (y - wl) / 0.34;
    float g = bell(sqrt(gx * gx + gy * gy * 0.5), 1.10);
    float ga = (0.10 + 0.22 * uLevel + 0.10 * uBass) * g * 0.9;
    lit += mix(tint, uAccent, 0.25) * ga;
    mask = max(mask, ga);
  }

  // ---- the liquid: dark body, thin caustics, one bright line at the surface ---------------
  if (y < sY) {
    float d = sY - y;
    float depth = clamp(d / 0.15, 0.0, 1.0);
    vec3 deep = uDeep * 0.9;
    vec3 body = mix(mix(tint, uSecond, 0.25), deep, pow(depth, 0.40));

    vec2 q1 = vec2(x * 1.5 + uTime * 0.10, d * 5.4 - uTime * 0.32);
    vec2 q2 = vec2(x * 2.4 - uTime * 0.16, d * 9.5 + uTime * 0.50);
    float n1 = fbm(q1);
    float n2 = fbm(q2);
    float web = ramp(n1 * n2 * 1.7, 0.30, 0.64);
    float veins = ramp(abs(n1 - n2), 0.17, 0.006);
    vec3 caus = mix(tint, vec3(1.0), 0.22);
    body += caus * (web * 0.16 + veins * 0.60) * (0.35 + 0.85 * uTreble) * (1.0 - 0.55 * depth);
    body += mix(tint, vec3(1.0), 0.30) * bell(d, 2.0 * px) * 0.18;

    float aBody = mix(0.32, 0.56, pow(depth, 0.75)) * mix(0.85, 1.0, uIntensity);
    lit += body * aBody;
    mask = max(mask, aBody);
  }

  // ---- the surface line: the brightest thing in the strip ----------------------------------
  {
    float d = y - sY;
    float slope = (surface(x + 4.0 * px, wl) - surface(x - 4.0 * px, wl)) / (8.0 * px);
    float spec = clamp(0.55 + slope * 0.22, 0.0, 1.0);
    float core = bell(d, 0.90 * px) * (0.85 + 0.15 * spec);
    float swell = bell(d, 4.5 * px) * 0.065;
    vec3 lineCol = mix(mix(tint, vec3(1.0), 0.55), vec3(1.0), spec * 0.3);
    lit += lineCol * (core + swell);
    mask = max(mask, core * 0.97 + swell * 0.45);

    float ripple = sin(x * 34.0 + sin(x * 2.6 + uTime * 0.9) * 2.2 + uTime * 2.8) * 0.5 + 0.5;
    float rippleA = bell(d, 4.5 * px) * ripple * 0.05 * (0.35 + 0.65 * uTreble);
    lit += tint * rippleA;
    mask = max(mask, rippleA);
  }

  // ---- the crest's reflection, blurred more the further it rises ---------------------------
  {
    float d = (2.0 * wl - y) - sY;
    float spread = (4.0 + 10.0 * clamp((y - wl) / 0.22, 0.0, 1.0)) * px;
    float refl = bell(d, spread) * 0.12;
    lit += mix(tint, uSecond, 0.5) * refl;
    mask = max(mask, refl);
  }

  // ---- caustics lifted off the water: the web of light a rippling surface throws upward ----
  {
    float band = ramp(y, wl + 0.55, wl + 0.02) * ramp(y, 0.0, 0.02);
    vec2 p = vec2(x * 0.95, (y - wl) * 2.3);
    float drift = uTime * 0.55;
    float a = p.x * 2.0 + fbm(p * 0.9 + vec2(drift * 0.35, -drift * 0.2)) * 3.4;
    float b = p.y * 3.2 + fbm(p * 1.2 + vec2(-drift * 0.25, drift * 0.3)) * 3.4;
    float ridges = abs(sin(a) + sin(b));
    float caus = ramp(ridges, 0.62, 0.0);
    float aC = caus * band * (0.22 + 0.20 * uLevel + 0.10 * uBass);
    lit += mix(tint, vec3(1.0), 0.42) * aC;
    mask = max(mask, aC * 0.9);
  }
  // ---- silk threads: three strands, each with a fainter echo two pixels off ----------------
  if (uMotion > 0.05) {
    for (int i = 0; i < 3; i++) {
      float fi = float(i);
      float yc = wl + 0.10 + fi * 0.155;
      float amp = (0.017 + 0.030 * uMid + 0.008 * uBass) * (1.0 + 0.15 * fi);
      float k = 1.55 + fi * 0.55;
      float ride = yc + amp * sin(x * k - uTime * (0.42 + 0.13 * fi) + fi * 2.2);
      ride += amp * 0.45 * sin(x * (k * 1.9) + uTime * (0.85 + 0.15 * fi) + fi * 1.3);

      float th = (0.95 + 1.15 * uTreble + 0.40 * uMid) * px * 1.40;
      float d = abs(y - ride);
      float core = ramp(d, th, 0.0);
      float halo = bell(d, th * 2.6) * 0.20;
      float weave = 0.72 + 0.28 * sin(x * 5.5 - uTime * 0.65 + fi * 1.7);

      vec3 rc = i == 0 ? mix(tint, uAccent, 0.10) : (i == 1 ? mix(tint, uSecond, 0.55) : mix(tint, uAccent, 0.65));
      float aR = (core * 0.90 + halo * 0.5) * (0.70 + 0.20 * uMid) * weave;

      // the echo sits just under the thread: a second strand, like a silk fibre
      float echo = ramp(abs(y - ride - 2.0 * px), th * 0.55, 0.0) * 0.22;
      lit += rc * (aR + echo);
      mask = max(mask, aR + echo * 0.8);
    }
  }

  // ---- the spectrum: a comb of hairlines, mirrored about the centre --------------------------
  {
    float margin = 0.05;
    float span = 1.0 - margin * 2.0;
    for (int i = 0; i < 32; i++) {
      float bi = uBands[i < 16 ? i : 31 - i];
      float t = (float(i) + 0.5) / 32.0;
      float xi = (margin + t * span) * aspect;
      float hgt = (0.025 + 0.320 * sqrt(bi)) * (0.55 + 0.55 * uLevel);
      float top = wl + hgt;
      float dy = clamp(y, wl, top);
      float dx = x - xi;
      float d = sqrt(dx * dx + (y - dy) * (y - dy));
      float line = ramp(d, 1.3 * px, 0.0) * 1.05;
      float cap = bell(sqrt(dx * dx + (y - top) * (y - top)), 2.2 * px) * 0.85 * bi;
      vec3 bc = mix(mix(tint, uAccent, 0.30), vec3(1.0), 0.20 + 0.30 * bi);
      float aB = (line + cap) * (0.30 + 0.30 * uLevel) * (0.55 + 0.45 * bi);
      lit += bc * aB;
      mask = max(mask, aB);
    }
  }

  // ---- the song itself: the waveform thread -------------------------------------------------
  {
    float wa = texture(uWaveA, vec2(uv.x, 0.5)).r;
    float wb = texture(uWaveB, vec2(uv.x, 0.5)).r;
    float w = mix(wa, wb, uWaveMix);
    float wy = wl + 0.075 + w * 0.048;
    float d = y - wy;
    float core = bell(d, 0.95 * px);
    float halo = bell(d, 4.0 * px) * 0.24;
    float aW = (core * 0.95 + halo) * (0.45 + 0.55 * uLevel);
    lit += mix(tint, vec3(1.0), 0.55) * aW;
    mask = max(mask, aW * 0.95);
  }

  // ---- dust glints --------------------------------------------------------------------------
  {
    vec2 q = vec2(x * 8.0, y * 12.0);
    float g = hash12(floor(q));
    float tw = smoothstep(0.975, 1.0, g) * (0.20 + 0.80 * uTreble) * (0.35 + 0.65 * uLevel);
    vec2 f = fract(q) - 0.5;
    float dotA = tw * ramp(sqrt(f.x * f.x + f.y * f.y), 0.45, 0.08) * 0.55;
    lit += mix(tint, vec3(1.0), 0.3) * dotA;
    mask = max(mask, dotA);
  }

  // ---- the capsule's own rim: a thin line of light along the glass edge ---------------------
  if (capsule) {
    vec2 b = vec2(capW * aspect * 0.5, capH * 0.5);
    vec2 c = vec2((uDock.x + uDock.y) * 0.5 * aspect, (uDock.z + uDock.w) * 0.5);
    float r = min(b.y * 0.85, 0.30 * aspect);
    float sd = sdRoundBox(vec2(x, y) - c, b, r);
    float rim = exp(-max(sd, 0.0) / (3.5 * px)) * 0.40 + exp(-max(-sd, 0.0) / (2.5 * px)) * 0.20;
    float aRim = rim * (0.10 + 0.20 * uLevel + 0.12 * uBass) * (0.7 + 0.3 * uSnare);
    lit += mix(tint, uAccent, 0.35) * aRim;
    mask = max(mask, aRim);

    float inside = ramp(sd, 1.5 * px, -1.5 * px);
    lit *= 1.0 - 0.08 * inside;
    mask *= 1.0 - 0.04 * inside;
  }

  // ---- finishing -----------------------------------------------------------------------------
  float edge = ramp(uv.x, 0.0, 0.05) * ramp(uv.x, 1.0, 0.95);
  float topFade = ramp(uv.y, 1.0, 0.90);
  float fade = edge * topFade * uFade;

  mask *= fade;
  lit *= fade;

  float lumaTop = max(lit.r, max(lit.g, lit.b));
  float a = clamp(max(mask, lumaTop * 0.95), 0.0, 1.0);
  vec3 col = clamp(lit, 0.0, 1.0);
  col = min(col, vec3(a));
  o = vec4(col, a);
}`;
export const brightFrag = /* glsl */ `#version 300 es
precision highp float;
in vec2 vUV;
out vec4 o;
uniform sampler2D uScene;
uniform float uThreshold;
void main() {
  vec4 s = texture(uScene, vUV);
  float luma = max(max(s.r, s.g), s.b);
  float k = smoothstep(uThreshold, 1.0, luma);
  o = vec4(s.rgb * k, s.a * k);
}`;

export const blurFrag = /* glsl */ `#version 300 es
precision highp float;
in vec2 vUV;
out vec4 o;
uniform sampler2D uTex;
uniform vec2 uDir;        // texel step
uniform float uRadius;
void main() {
  const float w0 = 0.2270270;
  const float w1 = 0.1945946;
  const float w2 = 0.1216216;
  const float w3 = 0.0540540;
  const float w4 = 0.0162162;
  vec2 p = vUV;
  vec4 sum = texture(uTex, p) * w0;
  for (int i = 1; i <= 4; i++) {
    float r = float(i) * uRadius;
    vec2 d = uDir * r;
    float w = i == 1 ? w1 : (i == 2 ? w2 : (i == 3 ? w3 : w4));
    sum += (texture(uTex, p + d) + texture(uTex, p - d)) * w;
  }
  o = sum;
}`;

export const compositeFrag = /* glsl */ `#version 300 es
precision highp float;
in vec2 vUV;
out vec4 o;
uniform sampler2D uScene;
uniform sampler2D uBloom;
uniform vec2 uRes;
uniform float uBloomStrength;
uniform float uFade;
uniform float uTime;

float hash12(vec2 p) {
  vec3 p3 = fract(vec3(p.xyx) * 0.1031);
  p3 += dot(p3, p3.yzx + 33.33);
  return fract((p3.x + p3.y) * p3.z);
}

void main() {
  vec2 uv = vUV;
  vec4 s = texture(uScene, uv);
  vec4 b = texture(uBloom, uv);
  float luma = max(max(b.r, b.g), b.b);

  vec3 col = s.rgb + b.rgb * uBloomStrength;
  float a = max(s.a, min(0.85, luma * uBloomStrength * 0.5));
  a = clamp(a * uFade, 0.0, 1.0);
  col *= uFade;

  // A whisper of grain keeps wide gradients from banding on 8-bit panels.
  float g = hash12(uv * uRes + uTime * 60.0) - 0.5;
  float scale = 1.0 / 255.0;
  col += g * scale * 0.55;
  a += g * scale * 0.20;

  col = clamp(col, 0.0, 1.0);
  col = min(col, vec3(max(a, 0.0)));
  o = vec4(col, clamp(a, 0.0, 1.0));
}`;

export const particleVert = /* glsl */ `#version 300 es
precision highp float;
in vec2 aCorner;    // -1..1 quad
in vec4 aSeed;      // x, y, size, phase
in vec4 aMotion;    // vx, vy, kind, strength
out vec2 vC;
out float vAlpha;
out vec3 vTint;
uniform vec2 uRes;
uniform float uTime;
uniform float uFade;
uniform float uLevel;
uniform float uTreble;
uniform float uKick;
uniform float uMotion;
uniform vec3 uCore;
uniform vec3 uAccent;

void main() {
  float t = uTime;
  float aspect = uRes.x / uRes.y;
  float kind = aMotion.z;
  float size = aSeed.z;
  vec2 pos = vec2(0.5);
  float alpha = 0.0;
  vec3 tint = mix(uCore, uAccent, aSeed.w);

  if (kind < 0.5) {
    // dust: rises slowly, loops its life, sways on its own phase
    float life = fract(t * (0.055 + 0.075 * aSeed.w) + aSeed.y);
    pos = vec2(
      aSeed.x + 0.05 * sin(t * (0.45 + 0.6 * aSeed.w) + aSeed.y * 6.28) + aMotion.x * life,
      life * (0.55 + 0.35 * aSeed.w) + 0.02
    );
    alpha = (0.10 + 0.20 * uTreble) * smoothstep(0.0, 0.18, life) * smoothstep(1.0, 0.55, life);
    size *= 0.75 + 0.5 * aSeed.w;
  } else if (kind < 1.5) {
    // spray: thrown up by the kick, falls back under gravity
    float age = fract(t * (0.30 + 0.22 * aSeed.w) + aSeed.y);
    float vy = (0.30 + 0.50 * aSeed.w) * (0.45 + 0.75 * uKick);
    pos = vec2(aSeed.x + aMotion.x * age, 0.10 + age * vy - 0.55 * age * age);
    alpha = 0.55 * (0.25 + 0.75 * uKick) * smoothstep(0.0, 0.06, age) * smoothstep(1.0, 0.70, age);
    size *= 0.7;
  } else {
    // embers: slow lights drifting up around the capsule
    float life = fract(t * 0.085 + aSeed.y);
    pos = vec2(aSeed.x + 0.015 * sin(t * 1.1 + aSeed.w * 9.0), 0.06 + life * (0.35 + 0.3 * aSeed.w));
    alpha = 0.14 * (0.35 + 0.65 * uLevel) * smoothstep(0.0, 0.25, life) * smoothstep(1.0, 0.6, life);
  }

  vec2 q = aCorner * vec2(size / aspect, size);
  gl_Position = vec4((pos + q) * 2.0 - 1.0, 0.0, 1.0);
  vC = aCorner;
  vAlpha = alpha * uFade * mix(0.55, 1.0, uMotion);
  vTint = tint;
}`;

export const particleFrag = /* glsl */ `#version 300 es
precision highp float;
in vec2 vC;
in float vAlpha;
in vec3 vTint;
out vec4 o;
void main() {
  float r = length(vC);
  float a = smoothstep(1.0, 0.05, r);
  a *= a;
  o = vec4(vTint * a * vAlpha, a * vAlpha);
}`;













