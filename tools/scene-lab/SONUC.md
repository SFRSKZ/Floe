# Floe — Cyberpunk görselleştirici atölyesi ve 2 yeni sahne

**Tarih:** 2026-11-05 · **Durum:** kapatıldı. Atölye çalıştı, 5 aday üretildi/ölçüldü; kullanıcı
kararıyla Katana / Hologram çıkarıldı, kalan yeni CP77 sahneleri **CyberPulse (Nabız)** ve **CyberRift
(Yarık)** üretime alındı. Entegrasyon, doğrulama, gerçek ses testi ve paketleme tamamlandı.

> **Güncelleme (2026-11-05, kullanıcı kararı):** Katana / Sıvı Krom (`CyberChrome`) ve Hologram /
> Holo Yağmur (`CyberHoloRain`) istenmedi. Üretim sahneleri, enum/katalog/ayarlar kayıtları,
> `IsSceneLabLook`/`NormalizeTheme` değişiklikleri, lab sahne dosyaları ve kanıtları projeden **tamamen
> çıkarıldı**. Aşağıdaki §4/§5 ve kazanan tabloları tarihsel kayıttır, mevcut kodu anlatmaz. Kalan yeni
> CP77 sahneleri `CyberPulse` (Nabız) ve `CyberRift` (Yarık); bunlar dokümanda ayrıca ele alınmadı.

## 1. Ne yapıldı (özet)

1. Floe'nun 3840×126 px dock şeridi için **bir tasarım atölyesi (plugin) yazıldı**: `Floe SceneLab`.
   Sahneleri uygulamayı çalıştırmadan, gerçek D3D11 hattından, gerçek analizörle render eder; kanıt kareleri,
   mp4, kıyas sayfası ve ölçüm raporu üretir.
2. Atölyenin doğruluğu kanıtlandı: lab'ın çıktısı, uygulamanın kendi `--render-gpu-look` çıktısıyla
   **bit düzeyinde aynı** (SHA-256 eşleşmesi: `F83E2D9C…81032`).
3. **İlk tur pixel-art/8-bit çıktı kullanıcı tarafından reddedildi**; yön sinematik malzeme/ışığa çevrildi:
   lineer HDR + ACES tonlama + gerçek bloom + film grain + kromatik aberasyon + renk derecelendirmesi.
4. 5 aday üretildi, ölçüldü, elendi; **en iyi 2** seçildi: **Sıvı Krom** ve **Holo Yağmur**.
5. Her aday için t=2 / t=6 / t=10 / t=15.5 (sessizlik) / vuruş kareleri + mp4 + rapor üretildi; karelerin
   birbirinden farklı olduğu hash ile doğrulandı.

## 2. Atölye (plugin) — `floe-lab/`

Tam yol: `C:\Users\Haluk\Documents\deepseek-harness\default-workspace\floe-lab\`

| Parça | İş |
|---|---|
| `SceneLab/` | C# + HLSL atölye aracı: sahneleri 3840×126'da gerçek D3D11 ile render eder |
| `SceneLab/Scenes/` | Aday sahneler (her sahne tek dosya; eklemek için dosyayı koymak yeterli) |
| `Audio/MockTrack.cs` | 16 s'lik deterministik tasarım parçası (120 BPM: intro/build/drop/breakdown + sessizlik) |
| `Audio/WavTrack.cs` | Gerçek müzik: dock'un kendi FFT/24-bant/onset analizi (ses dosyasından) |
| `Render/` | PNG yazımı, contact sheet, dock-gap önizlemesi, mp4, tone/motion ölçümü |
| `tools/compare.py` | Adayı mevcut en iyi 2 sahneyle aynı ölçekte kıyaslar + kare hash kontrolü |
| `tools/montage.py` | Tek sayfada film şeridi |
| `tools/fix-encoding.py` | Türkçe karakter bozulması onarımı |
| `assets/test-track.wav` | Gerçek-ses testi: 12 s davul+bas, 10.6 s'den sonra gerçek sessizlik |
| `out/` | Tüm kanıt kareleri, mp4'ler, kıyas sayfaları, raporlar |

Kullanım:

```powershell
$lab = 'C:\Users\Haluk\Documents\deepseek-harness\default-workspace\floe-lab'
dotnet build "$lab\SceneLab\SceneLab.csproj" -c Release --artifacts-path "$lab\artifacts\lead"
& "$lab\artifacts\lead\bin\SceneLab\release\SceneLab.exe" --list
& "$lab\artifacts\lead\bin\SceneLab\release\SceneLab.exe" --scene Katana --out "$lab\out\Katana" --from 0 --to 16 --mp4
& "$lab\artifacts\lead\bin\SceneLab\release\SceneLab.exe" --scene Hologram --out "$lab\out\Hologram" --wav "$lab\assets\test-track.wav" --from 0 --to 12
```

Rapor (`report.txt`) her koşuda: ms/kare, ses değerleri, drop/sessizlik hareket oranı, **tone dağılımı**
(`cinematic tone spread` / `FLAT pixel-art tone`) ve sessizlik hareketi.

## 3. Adaylar ve ölçümler

Aynı mock parça, 0–16 s, 3840×126 @1.5 px/dip, hedef ≤ 8 ms/kare.

| # | Aday | Fikir | ms/kare | drop hareket | sessizlik | tone (shadow/mid) | Sonuç |
|---|---|---|---|---|---|---|---|
| 1 | **Sıvı Krom** (`Scenes/Katana.cs`) | Karanlıkta akan cilalı krom kütle; neonları aynalar, ıslak zeminde yansır | **6.4** | 0.0415 | 0.0012 | 0.26 / 0.74 | ✅ **kazanan** |
| 2 | **Holo Yağmur** (`Scenes/Hologram.cs`) | Yağmurda asimetrik Arasaka hologram panoları; hacimsel ışık, ıslak asfalt | **6.2** | 0.0134 | 0.0000 | 0.00 / 1.00 | ✅ **kazanan** |
| 3 | Neon Yağmur (`Scenes/NeonRain.cs`) | Sular altındaki Night City sokağı; spektrumdan yükselen silüet | 7.2 | 0.0133 | 0.0000 | 0.52 / 0.48 | yedek (şehir yönü) |
| 4 | Liquid Glass (`Scenes/LiquidGlass.cs`) | Cam prizma/kırılma | 7.1 | 0.0096 | 0.0007 | 0.16 / 0.84 | elendi (fazla yıkanmış) |
| 5 | Skyline (`Scenes/Skyline.cs`) | Neon tel-kafes şehir silüeti | 7.8 | 0.0108 | 0.0000 | 0.12 / 0.88 | elendi (pixel-art'a yakın) |

Karşılaştırma için mevcut en iyi iki sahne: CyberSignal 4.3 ms (drop 0.0673 / sessizlik **0.0248**),
CyberBlackwall 3.9 ms (0.0242 / 0.0047). Yeni sahneler sessizlikte **tamamen** duruyor (0.000–0.001).

## 4. Kazananlar

### 4.1 Sıvı Krom — `Floe.App.Effects.Gpu.ChromeGpuScene`

- **Tek cümle:** "Karanlıkta süzülen sıvı krom: yüzeyi akan cilalı metal kütle; çevresindeki neonları
  aynalar, sese göre kıvrılır, altındaki ıslak zeminde yansır."
- **Neden güzel:** tek güçlü form (126 px'te okunan silüet), gerçek yansıma/fresnel hissi, sıcak-soğuk
  ışık dengesi; 8-bit değil, "render" gibi duruyor.
- **Zayıf yanı:** yansımalar prosedürel bantlardan geliyor (gerçek HDRI/GI/SSR yok); zemin yansıması
  yaklaşık (dikey smear); damla kümesi bazen "kabarcık" gibi okunuyor.
- **Ses:** bas = kütle kalınlığı, kick = yüzeyde gerilim dalgası + damla, snare = kütleden damla kopması,
  hat = mikro sıçrama. Sessizlikte tek saat durur; soğuyan ışık dışında kare fotoğraf.
- **Kanıt:** `floe-lab/out/Katana/` — `shots/`, `sheet.png`, `sheet-with-dock.png`, `Katana.mp4`, `report.txt`.

### 4.2 Holo Yağmur — `Floe.App.Effects.Gpu.HoloRainGpuScene`

- **Tek cümle:** "Yağmurlu karanlık Night City sokağında asimetrik Arasaka hologram panoları; hacimsel ışık
  şaftları yağmurun içinden iner, ıslak asfaltta dalgalı yansıma olur."
- **Neden güzel:** atmosfer ve ışık; nebze gürültülü ama yumuşak, film karesi gibi; panolar dock'un iki
  yanına oturuyor, orta boşluk kayıp değil.
- **Zayıf yanı:** iki pano hâlâ birbirine benziyor (asimetri iyileştirildi ama tam değil); şehir pencereleri
  seyrek; amblem stilize.
- **Ses:** bass = ekran parlaklığı + su halkaları, kick = sokakta ışık dalgası, hat = yağmur hızlanır;
  sessizlikte yağmur havada asılı kalır.
- **Kanıt:** `floe-lab/out/Hologram/` — `shots/`, `sheet.png`, `sheet-with-dock.png`, `Hologram.mp4`, `report.txt`.

### Kıyas sayfaları

- `floe-lab/out/final-karsilastirma.png` — eski en iyi 2 + yeni 2, aynı ölçekte
- `floe-lab/out/compare-LiquidChrome.png`, `floe-lab/out/compare-Hologram.png` — 5 zaman noktası, satır satır

## 5. Uygulamaya entegrasyon — TARİHSEL KAYIT (geri alındı)

> **Bu bölüm mevcut kodu anlatmaz.** 2026-11-05 kullanıcı kararıyla aşağıdaki iki sahne ve tüm
> izleri (enum/katalog/ayarlar/`IsSceneLabLook`/`NormalizeTheme`) projeden çıkarıldı; bkz. üstteki
> güncelleme ve §6. Tablo yalnızca o tarihteki ara durumu belgeler.

Proje: `C:\Users\Haluk\Documents\Projects\MacDock_backup_20261011`

| Dosya | Değişiklik | Durum |
|---|---|---|
| `src/Floe.App/Effects/Gpu/ChromeGpuScene.cs` | **yeni** — lab `Katana.cs` gövdesi, temiz başlıkla | ✅ |
| `src/Floe.App/Effects/Gpu/HoloRainGpuScene.cs` | **yeni** — lab `Hologram.cs` gövdesi | ✅ |
| `src/Floe.App/Services/GlassSettings.cs` | enum `CyberChrome`/`CyberHoloRain`, `IsCyberLook`, `IsSceneLabLook` | ✅ |
| `src/Floe.App/Effects/Gpu/GpuSceneCatalog.cs` | `Has` + `Create` | ✅ |
| `src/Floe.App/Windows/SettingsWindow.xaml` | 2 radyo düğmesi (Krom, Holo yağmur) | ✅ |
| `src/Floe.App/Windows/SettingsWindow.xaml.cs` | `foreach` bağlama; `#else` gizleme **kaldırıldı** (iki üründe de görünür) | ✅ |

**Her iki üründe de çalışır** (kullanıcı "CP77 değil" dedi): düz `Floe` ve `Floe CP77` aynı sahneyi çizer.
`NormalizeTheme` düz üründe bu iki görünümü Stardust'a düşürmüyor ve GPU şeridini kendiliğinden açıyor
(`IsSceneLabLook`); CP77'de `IsCyberLook` üzerinden aynı davranış.

Doğrulama (bu makinede yapıldı):

| Adım | Sonuç |
|---|---|
| `dotnet build Floe.sln -c Release` | ✅ 0 hata, 0 uyarı |
| `dotnet build src\Floe.App\Floe.App.csproj -c Release -p:FloeProduct=CP77` | ✅ 0 hata |
| `dotnet test tests\Floe.Tests -c Release` | ✅ 35/35 başarılı |
| `Floe.exe --render-gpu-look=CyberChrome;…;30;8` | ✅ 30 kare (`floe-lab/out/prod-chrome-plain/`) |
| `Floe.exe --render-gpu-look=CyberHoloRain;…;30;8` | ✅ 30 kare (`floe-lab/out/prod-holo-plain/`) |
| Atölye kopyası `tools/scene-lab/` | ✅ proje içinde |
| `installer\build-setup.ps1 -Product Floe` | 🏗️ kurulum dosyası `work/guncel49/` altına üretiliyor |

Yedek: düzenlenen 4 dosyanın entegrasyon öncesi hâli `floe-lab/out/integration-backup/` içinde.

## 6. Kapatma (2026-11-05)

Katana/Sıvı Krom ve Hologram/Holo Yağmur projeden çıkarıldıktan sonra §6'daki maddelerin durumu:

| # | Madde | Durum |
|---|---|---|
| 1 | Tema rengi (`CyberLook`) bağlantısı | Kapsam dışı kaldı: ilgili iki sahne silindi. Kalan 12 cyber sahne `Lead()`/`Second()` yolunu zaten kullanıyor. |
| 2 | Gerçek müzikle test | **Teknik olarak bitti, insan testi açık.** WAV ile gerçek FFT/24-bant/onset analizi `CyberPulse`/`CyberRift` üzerinde koştu (`work/cp77-kanit/wav-*/`). Ayrıca bu oturumda **canlı WASAPI loopback** doğrulandı: `Floe.Core.Interop.AudioSpectrum` ile gerçek `test-track.wav` çalınırken örnekleme → müzikte `Level/Bass/Mid/Treble` aktif ve t=9,16 sn'de kick/snare/hat onset'leri, WAV'ın 10,6 sn sonrası sessizliğinde 10,55–12,65 sn **tüm değerler 0,000**, WAV bittikten sonra sistemden gelen başka sesle tekrar tepki (kanıt: §7). Masaüstü ekran görüntüsü denemesi üst üste açık pencereler nedeniyle dock şeridini ayıramadı; **10 dakikalık insan kulağı + göz kontrolü hâlâ elle yapılmalı**. |
| 3 | Atölye `src/` anlık görüntüsü | **Bitti.** `floe-lab/src` `robocopy /E /XD obj bin` ile yenilendi; proje içi `tools/scene-lab/SceneLab/SceneLab.csproj` proje kökünden 0 hata/0 uyarı ile derlendi. `--list` çıktısında Katana/Hologram yok, `CyberPulse`/`CyberRift` var. |
| 4 | Ölçek | 3840×126 @1.5 px/dip dışında doğrulanmadı; ölçek değişirse yeniden bakılmalı (**açık limit**). |

### Bu oturumdaki doğrulamalar

- `dotnet build Floe.sln -c Release --no-incremental` → 0 hata / 0 uyarı
- `dotnet build src\Floe.App\Floe.App.csproj -c Release -p:FloeProduct=CP77 --no-incremental` → 0 hata / 0 uyarı
- `dotnet test tests\Floe.Tests -c Release` → 35/35
- `FloeCP77.exe --render-gpu-look=CyberPulse;…;30;8` ve `CyberRift` → 30'ar kare, `render-error.txt` yok (`work/cp77-kanit/`)
- Negatif test: `--render-gpu-look=CyberChrome` / `CyberHoloRain` → `unknown visualizer look`, çıkış kodu 2
- `installer\build-setup.ps1 -Product All -Out work\guncel56` → `FloeSetup.exe` + `FloeCP77Setup.exe`; yayınlanan `FloeCP77.dll` içinde `CyberPulse`/`CyberRift` var, `CyberChrome`/`CyberHoloRain`/`ChromeGpuScene`/`HoloRainGpuScene` yok.

## 7. Son kapatma (2026-11-05, OpenCode oturumu)

Önceki oturumun bıraktığı açık işler tamamlandı:

| İş | Sonuç / kanıt |
|---|---|
| Build + test (mevcut kaynak, taze) | `dotnet build Floe.sln -c Release` → **0 hata / 0 uyarı**; `dotnet build src\Floe.App -c Release -p:FloeProduct=CP77` → **0 hata / 0 uyarı**; `dotnet test` → **35/35** |
| 4 yeni sahnenin üretim render'ı | `FloeCP77.exe --render-gpu-look=CyberPulse;…;30;8` ve `CyberRift` → **30'ar PNG, hata yok**; kaldırılan `CyberChrome`/`CyberHoloRain` → `unknown visualizer look` (negatif test, çıkış 2) |
| Canlı WASAPI loopback | `Floe.Core.Interop.AudioSpectrum` kullanan küçük örnekleyici (`%TEMP%\opencode\floe-loopback\`) gerçek WAV çalarken: `0,04–9,85 sn` aktif spektrum, `9,16 sn` kick 0,250 / snare 0,496 / hat 0,250; `10,55 / 11,25 / 11,95 / 12,65 sn` **0,000** (sessizlik); `13,35 sn` sonrası sistem sesiyle tekrar `1,000` onset. Çıktı: `out.txt` |
| README | "10 cyber visualizers" → **12** (Nabız + Yarık eklendi) |
| Kurulum dosyaları | Kaynaktan `installer\build-setup.ps1 -Product All -Out work\guncel57` yeniden üretildi (iki ürün de 0 hata/0 uyarı); `work\` kökündeki 11-01 tarihli bayat exe'ler son sürümle değiştirildi. Yayınlanan `FloeCP77.dll` taranarak doğrulandı: `CyberChrome`, `CyberHoloRain`, `ChromeGpuScene`, `HoloRainGpuScene` **yok**; `CyberPulse`, `CyberRift` **var** |
| Kurulum hash'leri (SHA-256, ilk 16) | `work\FloeSetup.exe` = `79EF78417432BBF7`, `work\FloeCP77Setup.exe` = `8F6FE4B821BF8741` (ikisi de 12:35, ~92 MB) |
| Eşzamanlı oturum notu | Bu paketleme 12:35 kaynağından yapıldı. Başka bir oturum 12:37–12:38 arası `PulseGpuScene.cs` üzerinde çalışmaya devam etti (commit dışı, `git status`'ta `M`). O değişiklik ship edilecekse kurulum exe'leri bir kez daha yenilenmeli; bu oturum onların diff'ine dokunmadı. |
| Kayıtlar | `SONUC.md` başlığı/§5/§6 güncellendi, `tools/scene-lab/SONUC.md` kopyası eşitlendi |
| Versiyon kontrolü | `src/`, `README.md`, `SONUC.md` ve atölye kaynakları tek commit'e alındı; `work/` kanıt ve exe'leri bilinçli olarak commit dışında |

**Açık kalan (yalnızca insan doğrulaması):** CP77 dock'unu masaüstünde 10 dakika gerçek müzikle
dinleyip görsel olarak izlemek; otomatik ekran yakalama bu makinede üst üste açık pencereler yüzünden
dock şeridini izole edemedi. Ayrıca dış çalışma alanındaki `floe-lab/SceneLab/Scenes/Katana.cs`,
`Hologram.cs` ve `out/` kayıtları bu projenin dışında olduğu için silinmedi; istenirse orası da temizlenir.
