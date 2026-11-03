# Floe görselleştirici eklentileri

Bir Floe görselleştiricisi, içinde `manifest.json` ve bir WebGL2 sayfası olan bir klasördür. Floe bu
klasörü bulur, Ayarlar'da bir seçenek olarak gösterir ve şeridin arkasında kendi WebView2 sürecinde
çalıştırır. Eklenti Floe'nin içine derlenmez; güncellemek için uygulamayı yeniden kurmak gerekmez.

Bu depodaki referans eklentiler: [`tidepool/`](tidepool/) (sıvı cam gelgitleri) ve [`loom/`](loom/)
(ışıktan dokunmuş ipek). Tasarım gerekçeleri: [`docs/visualizer-plugins.md`](../docs/visualizer-plugins.md).

![Tidepool](../docs/tidepool-dock.png)

## Hızlı başlangıç

1. `plugins/tidepool` klasörünü kopyalayıp adını değiştirin.
2. `manifest.json` içinde `id`, `name` ve `version` alanlarını kendinize göre düzenleyin.
3. `index.html` + `js/` içeriğini kendi görselleştiricinizle değiştirin.
4. Klasörü şuraya koyun:
   - kullanıcı: `%LOCALAPPDATA%\Floe\plugins\<klasör>`
   - paketlenmiş: `Floe.exe` yanındaki `plugins\<klasör>`
5. Floe'yi açın → saatine sağ tıklayın → **Ayarlar → Cam → WebGL (eklenti)** → eklentinizi seçin.
6. Müzik çalmaya başlayın.

Eklenti listesi Ayarlar açıldığında yeniden taranır; klasörü ekledikten sonra Ayarlar'ı kapatıp açmanız
yeterlidir.

## Klasör yapısı

```
plugins/<klasör>/
  manifest.json          zorunlu
  index.html             zorunlu (entry)
  js/…                   serbest; ES modülleri desteklenir
  preview/…              isteğe bağlı; tarayıcıda önizleme (Floe yüklemez)
```

Kurallar:

- `entry` ve `preview` yolları klasörün **içinde** olmalı; `..`, sürücü harfi ve mutlak yollar reddedilir.
- Sayfa `https://floe.plugin/` sanal adresinden yüklenir; dış ağa çıkamaz (`connect-src 'none'`).
- `id` biçimi: `yazar.eklenti` — küçük harf, rakam, tire; nokta ayırıcı.
- Manifest 512 KB'ı geçemez.

## `manifest.json`

| Alan | Tip | Not |
| --- | --- | --- |
| `schemaVersion` | 1 | Şimdilik yalnızca `1` |
| `apiVersion` | `"1.0"` | Ana sürüm 1 olmalı |
| `id` | string | Örn. `floe.tidepool` |
| `version` | string | Eklentinin kendi sürümü |
| `name` | `{en, tr}` | Ayarlar'da görünen ad |
| `description` | `{en, tr}` | İsteğe bağlı |
| `entry` | string | `index.html` |
| `preview` | string? | Tarayıcı önizlemesi |
| `capabilities` | string[] | `audio.bands`, `audio.wave`, `audio.onsets`, `audio.stereo`, `host.palette`, `host.dockGeometry`, `host.clock`, `host.options` |
| `audio.bands` | 4–64 | Varsayılan 32; host bu sayıda bant yollar |
| `audio.wave` | 512 | API 1.x'te sabit: analizörün 512 örneklik osiloskop tamponu |
| `audio.waveRateHz` | 15–120 | Dalga güncelleme hedefi |
| `render.frameBudgetMs` | 4–40 | Kendi bütçeniz |
| `render.minHeight` | 24–400 | Altında çizilmeyeceğiniz yükseklik |
| `render.maxPixels` | 100k–16M | Kendi piksel tavanınız |
| `options` | nesne[] | `{id, type, min?, max?, step?, default?, values?, label?, labels?}`; `type`: `number`, `bool`, `enum` |

`options` değerleri `%APPDATA%\Floe\plugins.json` içinde eklenti başına saklanır ve `state.options`
olarak gelir. Bilinmeyen seçenek anahtarları yok sayılır; varsayılanlar manifestten gelir.

## Köprü

Host, `state` ve `frame` iletilerini `window.chrome.webview` üzerinden yollar. Floe dışında bu nesne
yoktur; eklenti kendi önizleme kaynağına düşmelidir (`tidepool/js/app.js` bunu yapar).

### `state`

```json
{
  "type": "state",
  "w": 1600, "h": 120, "dpr": 1.5,
  "dock": [0.21, 0.79, 0.08, 0.92],
  "palette": { "core": "#70E2F5", "second": "#AF8CFF", "accent": "#FF7AB8", "deep": "#081826" },
  "options": { "intensity": 0.65 },
  "fpsCap": 60,
  "running": true
}
```

- `w`, `h`: **CSS pikseli**; canvas'ın görünür boyutu.
- `dpr`: fiziksel piksel / CSS piksel. Canvas arka tamponunu `w * dpr` (gerekirse üstüne süper örnekleme)
  yapın.
- `dock`: dock kapsülünün şerit içindeki yeri, 0..1, `y` aşağıdan yukarı. Kapsül yoksa yükseklik 0 gelir.
- `palette`: Floe'nin kendi görselleştirici paletinden türetilir; "otomatik" palet budur.
- `running`: şarkı çalıyor mu. `false` geldiğinde kendi sönme animasyonunuzu çalıştırın (Floe 450 ms
  sonra pencereyi gizler).

### `frame`

```json
{
  "type": "frame",
  "t": 12.345,
  "bands": [0.0, 0.12, …],          // 32 değer, 0..1, logaritmik bantlar
  "bass": 0.7, "mid": 0.5, "treble": 0.3, "level": 0.6,
  "kick": 0.9, "snare": 0.0, "hat": 0.4,
  "balance": -0.1,
  "wave": [-0.2, 0.1, …]            // 512 örnek, -1..1
}
```

`bands` logaritmik ve kendi son yükseğine göre normalize edilir; `wave` mono, yükselen sıfır geçişinden
hizalıdır. `kick/snare/hat` o karede bir vuruş varsa gücüdür (0..1), yoksa 0'dır — sürekli bir zarf
değildir, kendi zarflarınızı üretin.

### Eklentiden host'a

```js
window.chrome.webview.postMessage({ type: 'ready', id: 'floe.tidepool', version: '1.0.0' });
window.chrome.webview.postMessage({ type: 'perf', fps: 60, ms: 1.8, tier: 'high' });
window.chrome.webview.postMessage({ type: 'error', message: 'shader link failed: …' });
```

`error`, host tarafından ölümcül sayılır (eklenti kapatılır, Floe yerleşik stile düşer). Gölgelendirici
derleme hatalarını yakalayıp `error` göndermek, sessiz siyah ekrandan iyidir.

## Çizim kuralları

- **Şeffaflık.** Sayfa zemini ve canvas `transparent` olmalı. Floe çıktıyı premultiplied alfa olarak
  birleştirir: `rgb <= a` tutun (`col = min(col, a)`), aksi hâlde tarayıcı kırpar.
- **Ölçek.** Şerit geniş ve kısa olabilir (8:1 – 30:1). Çizimi "yükseklik birimi" üzerinden kurun; sabit
  piksel kalınlıkları için `1.0 / height` kullanın.
- **Kenarlar.** Şeridin sol/sağ uçlarında alfa 0'a inin; ekran kenarında kesik çizgi görünmesin.
- **Kare bütçesi.** Varsayılan hedef 60 fps; `quality` seçeneğinde `auto` bırakıp kendi kademelerinizi
  uygulayın (bkz. `js/engine.js`). Ağır işi yarım çözünürlükte yapın (bloom gibi).
- **Süreklilik.** `requestAnimationFrame` kullanın; 120 Hz ekranda da 60 Hz'de de aynı hızda aksın.
  Zamanı `performance.now()` tabanlı tutun, kare sayısına bağlamayın.
- **Sönme.** `running=false` geldiğinde ~0.3–0.4 s içinde alfayı 0'a indirin; host sonra gizler.

## Önizleme

Eklentinin Floe'ye ihtiyacı yoktur. Depo kökünde bir statik sunucu açın (ES modülleri `file://`'dan
yüklenmez):

```powershell
# Windows
python -m http.server 8765 --bind 127.0.0.1
# ya da
npx serve .
```

Sonra açın:

```
http://127.0.0.1:8765/plugins/tidepool/preview/preview.html?dock=1&t=31
```

Parametreler: `h` şerit yüksekliği, `dock=0|1` kapsül taklidi, `freeze=1` tek kare, `t` sentetik sette
saniye, `quality`, `palette`, `intensity`, `motion`, `seed`.

`plugins/preview.ps1` bunu tek komuta indirir:

```powershell
.\plugins\preview.ps1 -Plugin tidepool -Query "dock=1&t=31"
```

## Paylaşma

Eklentiyi klasör olarak sıkıştırıp paylaşabilirsiniz; Floe içe aktarma sihirbazı henüz yok, klasörü
`%LOCALAPPDATA%\Floe\plugins` altına açmak yeterli. Sürüm yükseltirken `version` alanını artırın; Floe
aynı `id` için klasörü olduğu gibi yeniden yükler.

## API sürümleme

`apiVersion` ana sürümü host ile eşleşmeli. Uyumsuz eklenti yüklenmez ve Ayarlar'da listelenmez; bu,
sessiz bozulmadan iyidir. 1.x içinde yeni isteğe bağlı alanlar eklenebilir; mevcut alanların anlamı
değişmez.

