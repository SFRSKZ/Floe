// Floe · Tidepool — palettes.
//
// "auto" takes the colours Floe already resolved for its own visualizers (tint, gradient palettes,
// custom colours), so the plugin sits in the same light as the dock. The presets are alternatives
// for people who want a different mood.

export const PRESETS = {
  aurora: { core: '#7AE7FF', second: '#B18CFF', accent: '#FF8EC8', deep: '#08182B' },
  tide: { core: '#59D6C2', second: '#3E8ED0', accent: '#FFE38A', deep: '#062028' },
  ember: { core: '#FFB35C', second: '#FF6B6B', accent: '#FFE7A8', deep: '#2A0E0A' },
  mono: { core: '#EDF3FF', second: '#9FB4D0', accent: '#FFFFFF', deep: '#0B0F16' },
};

const FALLBACK = { core: '#70E2F5', second: '#AF8CFF', accent: '#FF7AB8', deep: '#081826' };

/** Resolves the effective palette for a host palette message. */
export function resolvePalette(option, host) {
  const base = {
    core: (host && host.core) || FALLBACK.core,
    second: (host && host.second) || FALLBACK.second,
    accent: (host && host.accent) || FALLBACK.accent,
    deep: (host && host.deep) || FALLBACK.deep,
  };

  if (!option || option === 'auto') return base;
  const preset = PRESETS[option];
  return preset ? { ...preset } : base;
}

export function toRgb(hex) {
  const h = String(hex || '').replace('#', '');
  const full = h.length === 3 ? h.split('').map((c) => c + c).join('') : h;
  const n = parseInt(full, 16);
  if (!Number.isFinite(n)) return [1, 1, 1];
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
}
