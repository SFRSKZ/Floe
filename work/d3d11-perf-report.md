# D3D11 performans denemesi — rapor (2026-10-25, dal `gpu-dock-perf`)

Makine: 180 Hz monitör, Performance = Full (kare sınırı yok). CPU süreleri `Process.Threads` ile thread başına ölçüldü (ms CPU / saniye). Komutlar `Services/PerfBench.cs` içinde.

## Ne yapıldı
1. **Küpler GPU'da** — `CubeGpuScene` (strip küpleri) ve `TronGpuScene` (dock'taki Tron küpleri), ikisi de `IGpuScene` + `GpuLayerWindow`. Görünüş aynı:
   - Küpler: GPU vs WPF ortalama piksel farkı ~1.4/255 (fark sadece kenar yumuşatmada).
   - Tron: ortalama fark 0.4/255.
   - Yan yana PNG: `--render-gpu-cubes=<dir>` (gpu/, wpf/, compare/), `--render-gpu-tron=<dir>`.
   - İkisi de `GlassSettings.GpuVisualizer` açıkken devreye giriyor.
2. **Dock camı D3D'de prototipi yerine ölçüm** — camı tek tek kapatıp neyin ne tuttuğunu ölçtüm (aşağıda). Ayrıca DWM-alpha dock (layered değil) prototipi: `--dwm-dock` / `FLOE_DWM_DOCK=1`.
3. **Bench araçları** — `--bench-viz`, `--bench-dock` (hover süpürme), `--bench-window`, `--bench-cubes`, `--bench-shot`.

## Ölçümler
| Senaryo | CPU ms/s | fps / not |
|---|---|---|
| Strip, WPF küpler (30 fps) | 125–141 (ui ~40, render 70–91) | |
| Strip, GPU küpler (60 fps) | 19–41 | 2× fps, ~4× az CPU |
| Dock hover süpürme (1582×144) | ~700 (ui ~180, render ~440) | ~83–85 fps |
| … cam gizli | 516–564 | 88 fps |
| … cam+ikon+aura+saat gizli | ~320 | 165 fps |
| … shadow/vibrancy/metal/capture kapalı | fark yok | |
| Boş pencere, tam yeniden çizim, layered | 190–199 | ~94 fps |
| Boş pencere, aynı, DWM-alpha | 74–90 | ~179 fps |
| Dock layered vs DWM-alpha | ~700 vs 674–736 | 793–814 vs 893–925 kare/10 s (+%12) |
| Dock + strip, WPF küpler | 745–763 | 767 kare |
| Dock + strip, GPU küpler | 650–675 | 847–868 kare |
| Tron, WPF | 1040–1120 | ~25 fps, kare işi 5.2–5.7 ms, ~240 geç kare/10 s |
| Tron, GPU | 1068–1174 | ~80–84 fps, kare işi 3.3–4.2 ms, 1–8 geç kare |

Offline (kare başına): WPF küp UI 0.69 ms (+ render thread tesselasyonu), GPU küp CPU 0.21 ms, GPU bitiş 0.78 ms; Tron UI 1.88 ms (421 parça).
Tron'da kare başı CPU ~43 ms → ~13 ms. Blur kapatmanın etkisi yok: maliyet geometri tesselasyonuydu.

## Sonuç
- **GPU küpler ve GPU Tron: kesin kazanç, devam.** Strip ~4× daha ucuz, Tron 25 → 80+ fps.
- **Camı D3D'ye taşımak: düşük değer.** Cam en fazla ~1.4 ms/kare render tutuyor (~%20–25); pencere layered kaldıkça ~2 ms/kare layered maliyeti ve fps tavanı sürüyor. Büyük iş, küçük kazanç.
- **DWM-alpha dock: orta değer (+%12 kare).** Görüntü aynı (ekran görüntüsü farkı 0.005). Ama şeffaf kenarlarda tıklama geçirme (click-through) yazılmadı; bu yapılmadan açılamaz.
- Tron'da kalan CPU UI thread'de mesh kurulumunda; istenirse oradan da kısılabilir.

## Merge notu
Ana klasörde CubeVisualizer'a commit edilmemiş renk/rim ayarları var (Rim .6, Gloss .32, alt rim grade-2, tip rengi). Merge'de bunlar `CubeGpuScene`'e de aynen yansıtılmalı, yoksa GPU küpler eski tonla çıkar.
