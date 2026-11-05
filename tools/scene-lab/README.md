# Floe SceneLab — görsel tasarım atölyesi (cyberpunk şerit visualizer'ları)

Bu klasör, Floe'nun 3840×126 px dock şeridinde çalan **sese gerçekten tepki veren** görsel efektleri
tasarlamak, ölçmek ve kanıtlamak için yazılmış bir atölyedir. Uygulamanın kendi kodunu değiştirmeden
(proje klasörü bu oturumda yazmaya kapalı) sahneleri burada yazıyoruz; kazananlar sonra uygulamaya taşınacak.

Kritik nokta: burada üretilen kare, uygulamanın kendi `--render-gpu-look` çıktısıyla **bit düzeyinde aynı**
(SHA-256 karşılaştırması yapıldı). Yani burada gördüğün şey dock'ta görünecek şeydir; "önizleme farklıdır"
mazereti yok.

## Klasör yapısı

```
floe-lab/
  src/                      uygulamanın kaynak anlık görüntüsü (salt okunur referans, dokunma)
  SceneLab/                 atölye aracı
    Program.cs              CLI: sahneyi render eder, kanıt dosyalarını yazar
    Audio/MockTrack.cs      16 s'lik tasarım parçası (120 BPM, intro/build/drop/breakdown + sessizlik)
    Audio/WavTrack.cs       gerçek müzik: dock'un kendi FFT/band/onset analizi
    Render/                 D3D11 offscreen renderer, PNG/contact sheet/mp4, dock-gap önizlemesi
    Scenes/                 SAHNELER — her tasarımcı yalnızca kendi dosyasına yazar
  artifacts/<isim>/         her tasarımcının özel derleme çıktısı (çakışmayı önler)
  out/                      render çıktıları
    ref-signal/             referans: mevcut en iyi sahne CyberSignal (sheet.png, shots/, mp4, report.txt)
    ref-blackwall/          referans: CyberBlackwall
```

## Sahne nedir

`Floe.App.Effects.Gpu.IGpuScene` uygulayan bir sınıf. Hazır altyapı için `FeedbackGpuScene`'den türetilir:
MilkDrop döngüsü (önceki kareyi `Last` dokusundan okuyup bükerek yeni kareyi çizme) hazır gelir; sen iki
HLSL pixel shader'ı (feedback + show) ve sabitleri (constants) yazarsın.

```csharp
[SceneDoc("tek cümlelik konsept")]
public sealed class BenimSahnem : FeedbackGpuScene
{
    private const int ConstantFloats = 64;   // 4'ün katı olmak ZORUNDA (16 byte hizalama)

    public BenimSahnem() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs) { }

    // 1) CPU tarafı: sahnenin kendi saatini/tepki durumunu ilerlet (dt, Beat.Hit, Beat.Pulse, ...)
    protected override void Advance(GpuFrame frame) { ... }

    // 2) her karede sabitleri doldur (Data[] ve Put/PutBands yardımcıları)
    protected override void Fill(GpuFrame frame) { ... }

    // 3) HLSL: ShaderHelpers (Hash/Noise/Band/MilkStep) + cbuffer + main
    private const string Shared = """ cbuffer ... """ + ShaderHelpers;
    private const string FeedbackPs = """ ... """;
    private const string ShowPs = """ ... """;
}
```

`Advanced`/`Fill` içinde kullanılabilen hazır alanlar:

| alan | anlamı |
|---|---|
| `frame.Audio.Bands[24]` | 30 Hz–16 kHz log bantlar, 0..1, kendi son yükseğine göre normalize |
| `frame.Audio.Bass/Mid/Treble/Level` | grup enerjileri ve genel ses düzeyi, 0..1 |
| `frame.Audio.Kick/Snare/Hat` | onset: sadece vuruş karesinde 0'dan büyük (0..1 gücü) |
| `frame.Audio.Wave[512]` | osiloskop, -1..1, sessizlikte 0 |
| `frame.Audio.Balance` | stereo denge -1..1 |
| `frame.Dt`, `frame.Time`, `frame.Fade` | saniye, sahne başından beri süre, müzikle 0..1 görünürlük |
| `frame.Width/Height/PixelsPerDip` | şerit piksel ölçüsü, 1 dip = 1.5 px |
| `frame.Palette[16]` | kullanıcının renkleri: ortadan (0) uçlara (15) |
| `frame.DockLeft/DockRight` | dock'un şerit üzerindeki yeri, 0..1 (varsayılan 0.27..0.73) |
| `frame.Performance` | performans seviyesi (Lite/Balanced/Full) |
| `Beat.Hit/Snare/Pulse/Swell/Sizzle` | `FeedbackGpuScene`'in vuruş çözümleyicisi (önerilir) |

Yardımcılar: `Put(at, Color)` (4 float: rgb+1), `PutBands(at, frame)` (24 float), `Lead(Color)` /
`Second(Color)` (tema rengi varsa onu döndürür), `Data[]` sabit dizi (shader'daki cbuffer sırasıyla birebir).

HLSL tarafında hazır: `Hash`, `Noise`, `Band(float u)` (0..1 → 24 bandın interpolasyonu), `MilkFlow(p)`,
`MilkStep(at, size, dice)`, `Last` (t0, önceki kare), `Linear` (s0), `Motion` (b1: MilkZoom/Spin/Warp/Time).

## Derleme ve çalıştırma (her tasarımcı kendi izole derlemesini yapar)

```powershell
$lab = 'C:\Users\Haluk\Documents\deepseek-harness\default-workspace\floe-lab'

# yalnızca kendi sahnenle, kendi artifacts klasöründe derle (BAŞKASININ derlemesini kilitlemez)
dotnet build "$lab\SceneLab\SceneLab.csproj" -c Release `
  --artifacts-path "$lab\artifacts\<SENIN-ADIN>" `
  -p:SceneFile=Scenes\<Sahnen>.cs

# çalıştır (exe yolu)
$exe = "$lab\artifacts\<SENIN-ADIN>\bin\SceneLab\release\SceneLab.exe"
& $exe --list
& $exe --scene <Sahnen> --out "$lab\out\<Sahnen>" --from 0 --to 16 --mp4 --keep-frames
```

`--from/--to` istediğin pencere; hızlı iterasyon için `--from 7.5 --to 10 --mp4` gibi kısa aralık kullan.
Çıktılar: `out\<Sahnen>\shots\shot-*.png` (t=2, 6, 10, 15.5 + vuruş), `sheet.png` (hepsi alt alta),
`sheet-with-dock.png` (dock ortayı kapatmış hâli), `report.txt` (ses değerleri + hareket ölçümü),
isteğe bağlı `<Sahnen>.mp4`.

Gerçek müzikle denemek için: `--wav "C:\...\parca.wav"` (PCM/float WAV; dock'un analizi birebir uygulanır).

## Tasarım sözleşmesi (bunlar pazarlık konusu değil)

1. **Tek cümle, tek fikir.** "Çıplak spektrum çubuğu", "dalga çizgisi", "genel equalizer" yasak.
2. **126 px'te okunur.** 3840 px genişliğe sığan ama 126 px yükseklikte kaybolan detay ölü detaydır.
   Tasarlarken `sheet.png`'e normal boyutta, `shot-*.png`'e %100'de bak.
3. **Keskin piksel.** Bloom, blur, yumuşak parlama yok. Parlak çekirdek + koyu 1 px kenar (kontrast için).
   Ön plan/arka plan ayrımı sert eşiklerle yapılır (ör. `step`, `smoothstep` küçük aralık).
4. **Dock boşluğu.** Şeridin ortası %46 (medya açıkken %58) dock'un altında kalır. Kompozisyonun ana
   okuması kenarlarda olmalı; ortadan geçen süreklilik (ör. uzun bir çizgi) dock'la kesilince bozulmuyorsa iyi.
   `sheet-with-dock.png`'e bakmadan teslim etme.
5. **Sessizlikte hareket durur, iskelet kalır.** Ses sıfırken sahne "ölü" değil, "nefesini tutmuş" olmalı:
   animasyon durur, geride tanınır bir yapı/kalıntı kalır. `report.txt`'te silence hareketi ~0 olmalı.
6. **Ölçülü tepki.** Her karede değişen ama müzikle *anlamlı* değişen bir görüntü: bas = gövde/kütle,
   kick = olay (doğum/kırılma/sıçrama), snare/hat = doku/kıvılcım. Report'taki `motion ratio` drop/silence
   olarak belirgin olmalı (hedef ≥ 5×, sessizlik ≈ 0).
7. **Prodüksiyon paleti.** Cyberpunk sarı `#FCEE0A`, kırmızı `#FF003C`, cyan `#02D7F2`, magenta `#FF2E88`,
   zemin `#0B0E14`. Renkler `frame.Palette` veya tema renginden de türetilebilir.
8. **Performans.** 3840×126'da kare başına ≤ 8 ms (donanım D3D11, WARP değil). Report'taki `ms/frame`'e bak.
9. **Y könü.** HLSL'de `SV_Position` y'si yukarıdan aşağıdır (y=0 üst satır). Uygulamanın diğer sahneleri de
   böyle. Tasarımda "üst/alt" varsayımı yaptığın her yeri konsept notuna yaz (port sırasında ters çevrilecek
   yerler listesi).
10. **Port notu.** Dosyanın başına yorum olarak: konsept cümlesi, dört reddedilen hatadan (statik illüstrasyon /
    dock boşluğunu yok sayma / ölçüsüz tepki / gereksiz süs) hangilerine düşmediği, porta çevrilecek yerler.

## HLSL tuzakları

- Constant buffer boyutu 4'ün katı float olmalı; `float4`/`float2` hizalaması cbuffer sırasını bozar —
  `Data[]` indeksleri ile cbuffer alan sırası birebir aynı olmalı. Renk/band bloğunu **en sona** koy.
- HLSL derlemesi çalışma anında olur: derleme hatası `SharpGenException` olarak stderr'e düşer, kare gelmez.
- `MilkStep` yalnızca tam piksel adımları verir (blur yok). `Last.Load` kullan, `Sample` kullanma.
- Alpha premultiplied: `return float4(color * alpha, alpha)`; saydam bölgede `float4(0,0,0,0)` döndür.
- Şeridin tepesinde kesik çizgi olmasın: çoğu sahne üst kenarı yumuşakça söndürür (`smoothstep(0,.35,y/Size.y)`).

## Referans ve kıyas

`out/ref-signal/sheet.png` ve `out/ref-blackwall/sheet.png` mevcut en iyi iki sahne; `mp4`'leri de var.
Kendi `sheet.png`'ini bunlarla **aynı ölçekte** yan yana koymadan "iyi oldu" deme. Kıyas kriteri:
aynı ses altında hangisi daha okunur, hangisi daha "bu Cyberpunk 2077" hissi veriyor, hangisi 126 px'te
daha net bir silüet bırakıyor.

Kıyas ve kanıt araçları:

```powershell
python tools\compare.py --candidate out\Skyline --name Skyline   # baseline'larla aynı ölçekte kıyas + kare hash kontrolü
python tools\montage.py out\checkpoint.png Skyline NeonRain      # tek sayfada aday film şeridi
python assets\make-test-track.py                                 # gerçek ses testi: 12 s davul+bas, 10.6 s'den sonra sessizlik
SceneLab --scene <Ad> --wav assets\test-track.wav --from 0 --to 12   # dock'un gerçek FFT analiziyle çalıştır
```

## PROFESYONEL (SİNEMATİK) GÖRÜNÜM ŞARTNAMESİ — 2026-11 geri bildirimi

Kullanıcı geri bildirimi: "8-bit oyun gibi" görünen iş kabul edilmiyor. Aşağıdakiler artık zorunlu:

1. **Lineer HDR + ACES filmic tonlama.** Emissive değerler 1'i aşsın; `x*(2.51x+0.03)/(x*(2.43x+0.59)+0.14)`
   ile tonlayıp `pow(1/2.2)` uygula. Saf siyah/parlak kütleler yok; ara tonlar baskın.
2. **Tone ölçütü.** `report.txt` içindeki `tone` satırı `cinematic tone spread` demeli (mid >= 0,35).
   `FLAT pixel-art tone` çıkarsa teslim edilmez.
3. **Büyük form, az öğe.** 126 px yükseklikte küçük detay kaybolur: 3-6 güçlü öğe, net silüet, yumuşak
   gradyan. Nokta-matrix, 1 px HUD ızgarası, düz renk bloğu, konfeti gibi dağılmış pikseller yasak.
4. **Işık fiziksel.** Gerçek bloom (feedback geçişiyle parlak pikselleri yayma), fresnel kenar, yansıma
   (ıslak zemin/cam), hacimsel sis/huzme. Bloom "her şeyi parlat" değil, sadece eşiği aşan ışık taşar.
5. **Lens katmanı.** Film grain (%1,5-2), hafif kromatik aberasyon, vignette, lift/gamma/gain derecelendirmesi
   (gölgeler teal, vurgular sıcak).
6. **Analitik AA.** Kenarlar `smoothstep`/`exp` ile yumuşatılır; testere dişi yok.
7. **Sessizlikte tam donma.** Tüm zaman tabanlı fazlar (grain dahil) sesle ilerleyen bir saatten geçer:
   `_clock += dt * Level * k`. Ses sıfırken kare fotoğraf gibi durur; `silence` hareketi ~0 olmalı.
8. **Performans.** `ms/frame <= 8` (paralel çalışırken ölçüm şişer; yalnız koşuda doğrula).
9. **Türkçe karakter tuzağı.** Dosyaları **yalnızca `write`/`edit` araçlarıyla** yaz. PowerShell
   `Get-Content`/`Set-Content` turu UTF-8'i ANSI'ye çevirip "ç→ç" bozar; bozulursa
   `python tools\fix-encoding.py <dosya>` ile onar.

## Sahiplik

- Tasarımcı **yalnızca** `SceneLab/Scenes/<KendiSahnesi>.cs`, `artifacts/<kendi-adı>/`, `out/<KendiSahnesi>/`
  altına yazar. `Program.cs`, `Options.cs`, `Render/`, `Audio/`, diğer sahneler ve `src/` **paylaşılan**;
  değiştirmek gerekirse Lead'e söyle.
- Aynı anda birden çok tasarımcı çalıştığı için **çıplak `dotnet build` çalıştırma**; yukarıdaki
  `--artifacts-path` + `-p:SceneFile=` komutunu kullan.

## Uygulamaya taşıma (Lead yapar)

Kazanan sahne `src/Floe.App/Effects/Gpu/` altına taşınır: namespace `Floe.App.Effects.Gpu` olur,
`GpuSceneCatalog.Has/Create`'e ve `VisualizerStyle` enum'una birer satır eklenir, ayarlar ekranındaki
görünüm listesine etiketi yazılır. Lab dosyası olduğu gibi kopyalanabilir; `SceneDoc` niteliği ve
`SceneLab.Scenes` namespace'i taşınmaz.
