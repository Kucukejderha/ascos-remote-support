# RotaLink v1.1 Yeniden Yapılandırma

## Amaç

Bu sürüm, ekran aktarımı ile uzaktan input işleme işini aynı işlem ve aynı desktop bağlamına bağlayan eski yaklaşımı kaldırır.

```text
RotaLink.exe (kullanıcı arayüzü ve ağ)
  ├─ Control WebSocket ── güvenilir input paketleri
  ├─ Video WebSocket ──── maksimum bir bekleyen kare
  └─ Named Pipes
       └─ RotaLink.SessionHelper.exe (aktif oturumda SYSTEM)
            ├─ OpenInputDesktop + SendInput
            └─ RotaLink.NativeCapture.exe
                 └─ DXGI → NV12 → H.264 → Shared Memory

RotaLink.Service.exe (Session 0 / LocalSystem)
  └─ WTS oturum bildirimleri → aktif oturum helper yaşam döngüsü
```

## Faz 1 — Service ve Interactive Session Helper

- Service Control Manager ile doğrudan konuşan gerçek Windows Service giriş noktası eklendi.
- `WTSRegisterSessionNotification` için Session 0 içinde mesaj penceresi oluşturulur.
- Console connect/disconnect, logon/logoff, lock/unlock ve remote connect/disconnect olaylarında aktif oturum yeniden değerlendirilir.
- Servisin LocalSystem primary token’ı `DuplicateTokenEx` ile çoğaltılır.
- `TokenSessionId`, `WTSGetActiveConsoleSessionId` sonucuna ayarlanır.
- `SeAssignPrimaryTokenPrivilege` ve `SeIncreaseQuotaPrivilege` etkinleştirildikten sonra helper, `CreateProcessAsUser` ile `winsta0\default` üzerinde başlatılır.
- Helper her input komutundan hemen önce `OpenInputDesktop` ve `SetThreadDesktop` çalıştırır. Bu işlem ayrı, penceresiz input thread’i üzerinde yapılır.
- Portable istemci, kurulu helper bulunmadığında aynı dinamik desktop motorunu kendi işlemi içinde kullanır. Bu geri dönüş normal kullanıcı desktop’ı içindir; UAC secure desktop kontrolü SYSTEM helper gerektirir.
- `v1.1.0-alpha.2` canlı tanısı, `Default` desktop seçildikten sonra `SendInput` çağrısının `ERROR_ACCESS_DENIED (5)` ile reddedildiğini doğruladı. Ağ, video, kontrol kanalı ve koordinat zinciri bu noktaya kadar çalışmaktadır.
- `v1.1.0-alpha.3` portable EXE, Windows UAC üzerinden `requireAdministrator` ister. Onaylanan süreç yüksek bütünlük düzeyinde çalışır; kalıcı servis veya gözetimsiz erişim kurulmaz.
- İstemci başlığında “Yönetici yetkili oturum” veya “Sınırlı kullanıcı oturumu” gösterilir. Böylece yanlış token ile yapılan test görsel olarak anlaşılır.
- `v1.1.0-alpha.7`, gerçek sistem girdisi ürettiği doğrulanamayan `SetCursorPos`, `mouse_event`, `keybd_event` ve pencere mesajı yedeklerini kaldırır. `SendInput` başarısızsa operatöre `Accepted=False` ve gerçek Win32 hata aşaması bildirilir; böylece imleç hareketinin yanlışlıkla tam kontrol başarısı sayılması engellenir.
- Aynı sürüm, küçük .NET Framework 4.8 Service ve SessionHelper bileşenlerini tek taşınabilir EXE içine gömer. Uygulama açılırken görünür UAC onayından sonra geçici LocalSystem servisini başlatır; servis aktif kullanıcı oturumunda SYSTEM SessionHelper oluşturur. Pencere kapandığında servis durdurulur ve SCM kaydı silinir.

## Faz 2 — DXGI ve Shared Memory

- Native capture hedefi `RotaLink.NativeCapture.exe` olarak ürünleştirildi.
- `IDXGIOutputDuplication::AcquireNextFrame` ile GPU yüzeyi alınır.
- BGRA yüzey GPU video processor ile NV12’e çevrilir ve Media Foundation düşük gecikmeli H.264 encoder’a verilir.
- `DXGI_ERROR_ACCESS_LOST` ve session disconnect durumlarında aktif input desktop yeniden açılır ve duplication nesnesi yeniden kurulur.
- H.264 kareleri `Global\RotaLink.FrameMap.{SessionId}` file mapping alanına yazılır.
- `Global\RotaLink.FrameReady.{SessionId}` auto-reset event’i okuyucuyu uyandırır.
- Mapping seqlock kullanır: tek sıra numarası yazım sürüyor, çift sıra numarası kararlı kare anlamına gelir.
- Paylaşımlı alan tek kareliktir; yeni kare eskisini doğrudan ezer ve kuyruk büyümez.

## Faz 3 — Ayrı Taşıma Kanalları

- Control ve video farklı WebSocket bağlantıları kullanır.
- Input akışının video gönderim kilidi veya `SemaphoreSlim` nesnesiyle hiçbir ortak kilidi yoktur.
- Sunucu video kanalında kapasitesi bir olan `Channel<byte[]>` kullanır ve `DropOldest` uygular.
- Guest yavaşsa WebSocket send doğal backpressure uygular; Shared Memory güncellenmeye devam ettiği için sonraki okuma en güncel kareyi alır.
- Input IPC paketi 40 bayt sabit binary şemaya sahiptir: magic, sürüm, tür, flags, sequence, normalize X/Y, data ve virtual-key.
- Video IPC başlığı 40 bayttır: codec, key-frame, sequence, 100 ns timestamp, genişlik, yükseklik ve payload uzunluğu.
- Operatör tarayıcısına H.264 aktarımı WebCodecs ile çözülür. Portable GDI/JPEG protokolü uyumluluk geri dönüşü olarak korunur.

## Faz 4 — DPI, Letterbox ve Çoklu Monitör Koordinatları

- Tarayıcı, canvas kutusu ile gerçek görüntü aspect ratio değerini karşılaştırır.
- Letterbox bantları koordinattan çıkarılır; bant içindeki tıklamalar gönderilmez.
- İçerik noktası `0..1` normalize koordinat olarak control kanalına gönderilir.
- İstemci `SM_XVIRTUALSCREEN`, `SM_YVIRTUALSCREEN`, `SM_CXVIRTUALSCREEN` ve `SM_CYVIRTUALSCREEN` değerlerini her input sırasında yeniden okur.
- Negatif konumdaki sol/üst monitörler pixel koordinatına dahil edilir.
- `SendInput`, `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_MOVE` ile çağrılır.
- Sağ ve alt son pixel 65535’e, sol ve üst başlangıç 0’a eşlenir.

## Doğrulama Durumu

- `RotaLink.exe`, `RotaLink.Service`, `RotaLink.SessionHelper` ve signaling projeleri sıfır C# derleme hatasıyla oluşturuldu.
- Binary input/video round-trip, sanal masaüstü sınırları ve video `DropOldest` davranışı smoke testinden geçti.
- Canlı signaling container’ı yeniden oluşturuldu ve health endpoint’i doğrulandı.
- Portable `v1.1.0-alpha.4` sıfır C# derleme hatasıyla üretildi; gömülü manifestte `requireAdministrator` ve Windows 10/11 uyumluluk kimliği doğrulandı.
- Kontrol sunucusundaki indirme dosyası `133.632` bayt ve `dcc40ae66ea22300110bcd62e4954ee220475d8979f7130c261032d89044f2f7` SHA-256 değeriyle uzaktan doğrulandı.
- Bu geliştirme makinesinde Windows SDK/C++ workload kurulu olmadığı için native EXE burada oluşturulamadı. Native kaynakların Windows SDK içeren CI runner’da derlenmesi ve gerçek iki monitör/UAC test matrisinden geçirilmesi, SYSTEM paketini müşteriye açmadan önce zorunludur.

## Güvenlik Kuralları

- Helper yalnızca servis tarafından oluşturulan aktif oturumda çalışır.
- Input pipe ACL’i LocalSystem ve o oturumun kullanıcı SID’i ile sınırlandırılır.
- Input sequence geriye gidemez; tekrar paketler reddedilir.
- Helper kapanırken servis önce named stop event gönderir, sonra yalnızca kendi tuttuğu process handle’ını sonlandırır.
- Portable geri dönüş hiçbir zaman SYSTEM veya secure desktop yetkisi varmış gibi raporlanmaz.
