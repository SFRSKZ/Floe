// Floe - Loom: GLSL ES 3.00 sources.
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

vec3 braidTint(float t) {
  return t < 0.5 ? mix(uCore, uSecond, t * 2.0) : mix(uSecond, uAccent, (t - 0.5) * 2.0);
}

void main() {
  if (uFlat > 0.5) { o = vec4(0.06, 0.22, 0.42, 0.55); return; }

  vec2 uv = vUV;
  float aspect = uRes.x / uRes.y;
  float px = 1.0 / uRes.y;
  float x = uv.x * aspect;
  float y = uv.y;

  float wl = uDock.z + 0.006;
  float energy = 0.70 + 0.30 * uLevel;
  float m = uMotion;

  vec3 lit = vec3(0.0);
  float mask = 0.0;

  float capW = uDock.y - uDock.x;
  float capH = uDock.w - uDock.z;
  bool capsule = capW > 0.05 && capH > 0.04;

  float t01 = 2.0 * uv.x - 1.0;
  float sag = 0.012 * (1.0 - t01 * t01);

  // ---- five braids of light, hanging from the rail ----------------------------------------
  for (int i = 0; i < 5; i++) {
    float fi = float(i);
    float bi = uBands[int(min(31.0, 3.0 + fi * 6.0))];
    float yc = wl + 0.135 + fi * 0.125 - sag * (0.6 + 0.1 * fi);
    float amp = (0.012 + 0.018 * uMid + 0.010 * bi) * mix(0.55, 1.0, m);
    float thick = 0.0105 + 0.014 * bi;
    vec3 braidCol = braidTint(fi / 4.0);

    // the spine: the braid seen as one soft rope
    float spine = bell(y - yc, thick * 2.4) * (0.10 + 0.16 * bi) * energy;
    lit += braidCol * spine;
    mask = max(mask, spine);

    for (int k = 0; k < 3; k++) {
      float fk = float(k);
      float phase = fk * 2.0944;
      float yy = yc
        + amp * sin(x * 2.05 - uTime * (0.55 + 0.09 * fi) + phase + fi * 1.35)
        + thick * sin(x * 5.4 + uTime * (1.05 + 0.12 * fi) + phase * 1.9);
      float d = y - yy;
      float core = bell(d, 0.85 * px);
      float halo = bell(d, 4.2 * px) * 0.32;
      vec3 strandCol = mix(braidCol, uAccent, 0.10 * fk);
      float a = (core + halo) * (0.40 + 0.50 * bi) * energy;
      lit += strandCol * a;
      mask = max(mask, a);
    }

  }


  // ---- light spilling from the rail: the cloth's warm base --------------------------------
  {
    float spill = ramp(y, wl + 0.16, wl) * 0.16 * energy;
    lit += mix(uCore, uAccent, 0.25) * spill;
    mask = max(mask, spill);
  }

  // ---- the shuttle: a hot knot sliding along a braid on every kick ------------------------
  {
    float shuttle = 0.0;
    for (int b = 0; b < 4; b++) {
      float age = uBeatAge[b];
      if (age < 0.0 || age > 2.4) continue;
      float strength = uBeatStr[b] * exp(-age * 1.2);
      if (strength < 0.02) continue;
      float dir = (b == 0 || b == 2) ? 1.0 : -1.0;
      float sx = 0.5 + dir * age * 0.55;
      float row = wl + 0.135 + float(b % 5) * 0.125;
      shuttle += bell(uv.x - sx, 0.030 + 0.02 * age) * bell(y - row, 0.020) * strength;
    }
    lit += mix(uAccent, vec3(1.0), 0.55) * shuttle * 1.2;
    mask = max(mask, shuttle * 0.95);

    // its glow drags the braid around it
    float drag = shuttle * 0.35;
    lit += braidTint(0.6) * drag;
    mask = max(mask, drag);
  }

  // ---- the song: a gold thread woven through the braids -----------------------------------
  {
    float wa = texture(uWaveA, vec2(uv.x, 0.5)).r;
    float wb = texture(uWaveB, vec2(uv.x, 0.5)).r;
    float w = mix(wa, wb, uWaveMix);
    float wy = wl + 0.70 + w * 0.06;
    float d = y - wy;
    float thread = bell(d, 0.85 * px) + 0.22 * bell(d, 3.6 * px);
    float a = thread * (0.55 + 0.45 * uLevel);
    lit += mix(uAccent, vec3(1.0), 0.35) * a;
    mask = max(mask, a * 0.95);
  }

  // ---- the rail: the selvedge the braids hang from -----------------------------------------
  {
    float d = y - wl;
    float rail = bell(d, 0.95 * px) + 0.22 * bell(d, 3.6 * px);
    lit += mix(uCore, vec3(1.0), 0.45) * rail * (0.70 + 0.30 * uLevel);
    mask = max(mask, rail * 0.95);

    float stitch = bell(y - (wl + 0.007), 0.50 * px) * 0.30;
    lit += uCore * stitch;
    mask = max(mask, stitch);
  }

  // ---- a slow sheen crossing the braids -----------------------------------------------------
  {
    float sx = 0.50 + 0.32 * sin(uTime * 0.16);
    float sheen = bell(uv.x - sx, 0.15) * 0.07 * (0.35 + 0.65 * uLevel);
    lit += mix(uSecond, vec3(1.0), 0.25) * sheen;
    mask = max(mask, sheen);
  }

  // ---- the capsule's rim ---------------------------------------------------------------------
  if (capsule) {
    vec2 b = vec2(capW * aspect * 0.5, capH * 0.5);
    vec2 c = vec2((uDock.x + uDock.y) * 0.5 * aspect, (uDock.z + uDock.w) * 0.5);
    float r = min(b.y * 0.85, 0.30 * aspect);
    float sd = sdRoundBox(vec2(x, y) - c, b, r);
    float rim = exp(-max(sd, 0.0) / (3.5 * px)) * 0.35 + exp(-max(-sd, 0.0) / (2.5 * px)) * 0.18;
    float aRim = rim * (0.10 + 0.18 * uLevel + 0.10 * uBass) * (0.7 + 0.3 * uSnare);
    lit += mix(uCore, uAccent, 0.35) * aRim;
    mask = max(mask, aRim);

    float inside = ramp(sd, 1.5 * px, -1.5 * px);
    lit *= 1.0 - 0.08 * inside;
    mask *= 1.0 - 0.04 * inside;
  }

  // ---- finishing ------------------------------------------------------------------------------
  float edge = ramp(uv.x, 0.0, 0.05) * ramp(uv.x, 1.0, 0.95);
  float topFade = ramp(uv.y, 1.0, 0.90);
  float fade = edge * topFade * uFade * (0.85 + 0.15 * uIntensity);

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













