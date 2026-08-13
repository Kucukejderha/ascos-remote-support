# RotaLink — Sürüm Durumu

Güncelleme tarihi: 13 Ağustos 2026

## Güncel sürüm

- Sürüm: `1.1.0-alpha.26`
- Kanal: İmzasız kontrollü geliştirme testi
- Paket: `RotaLink-v1.1.0-alpha.26-UNSIGNED-DEVELOPMENT.exe`
- Boyut: `215.040` bayt
- SHA-256: `231ad5e60f1723b059c38afdd9fae5767aa0ac8ad7e1f10a18aeeb6707206a24`
- İndirme: Henüz yayınlanmadı; Alpha.25 web testi korunuyor.
- Uyumluluk test kiti: `RotaLink-Alpha26-Uyumluluk-Test-Kiti.zip`, `144.278` bayt,
  SHA-256 `762048ca72ac3180e5c743b4847767e131c19c9e5e548df6dd19623cca5abfce`

## Sürüm doğrulama özeti

- `alpha.18`: Windows Server 2019 üzerinde SYSTEM SessionHelper yoluyla fare ve klavye kontrolü gerçek cihazda doğrulandı.
- `alpha.19`: Kaynak ekranın en-boy oranını koruyan dinamik yakalama ve operatör tuvali koordinat düzeltmesi eklendi. Bazı RDP oturumlarında input API çağrısı başarılı dönmesine rağmen giriş etkin olmayan oturuma yönlenebildi.
- `alpha.20`: SessionHelper WTS oturum durumunu günlüğe ekler. Fare basımından hemen önce tıklanan kök pencereyi hedef input kuyruğuna bağlar ve foreground/active/focus hazırlığı yapar.
- `alpha.21`: SessionHelper, yalnız oturum numarası değiştirilmiş SYSTEM tokenı yerine yükseltilmiş RotaLink istemcisinin gerçek etkileşimli tokenıyla başlatılır.
- `alpha.22`: Normal tıklamalar tek protokol komutu ve tek atomik `SendInput(move + down + up)` çağrısı olarak uygulanır. `AttachThreadInput` bağlantısı enjeksiyon tamamlanana kadar korunur; sürükleme ayrı `down/move/up` akışında devam eder.
- `alpha.23`: Alpha.22 gerçek cihaz günlüğünde Explorer masaüstü ve görev çubuğu hedeflerinde görülen `AttachThreadInput · Win32 5` hatası giderildi. Helper artık fiziksel fare gibi doğrudan sistem input akışına tıklama ekler; hedef pencereyi zorla foreground/active/focus yapmaz.
- `alpha.24`: Alpha.23 günlüğünde doğru hit-test edilen ancak bir süre sonra işlemeyen Explorer kabuk denetimleri için hedefe özgü senkron pencere mesajı yolu eklendi. Gerçek cihaz testi, mesaj tesliminin gerçek seçim/odak ve görev çubuğu etkinleştirme davranışını üretmediğini gösterdi.
- `alpha.25`: Explorer'a doğrudan `WM_LBUTTONDOWN/UP` gönderme kaldırıldı. Görev çubuğu için UI Automation `InvokePattern`, masaüstü simgesi için `SelectionItemPattern` ve çift tıklamada `InvokePattern` kullanılır. Boş masaüstü ve sağ tık gerçek, atomik `SendInput` olarak kalır.
- `alpha.26` Faz 0 uyumluluk başlangıcı: Uygulama gerçek Windows sürümünü `RtlGetVersion` ile, istemci/sunucu türünü, Server Core/Desktop Experience bilgisini, mimariyi ve .NET 4.8 release değerini günlüğe yazar. Destek dışı Server Core ve x86 sistemleri servis kurulmadan önce açık hata ile durdurur. Windows uyumluluk matrisi ve ürünleştirme sırası `docs/WINDOWS-UYUMLULUK-YOL-HARITASI.tr.md` belgesindedir.
- Faz 1 laboratuvar başlangıcı: PowerShell 3 uyumlu, WMI zorunluluğu olmayan makine probu; sekiz hedefli sürüm matrisi; eksik/P0 hatalı raporda yayını kapatan sonuç birleştirici ve Windows Server 2022 CI tabanı eklendi. Alpha.26 gerçek VM matrisi tamamlanmadan webde yayınlanmaz.

`alpha.24` gerçek cihaz kaydı, senkron pencere mesajlarının API seviyesinde başarılı dönmesine rağmen masaüstü seçimini, bağlam menüsünün kapanmasını ve bazı görev çubuğu düğmelerinin etkinleşmesini sağlamadığını doğruladı. `alpha.25` bu sahte başarı yolunu kaldırır. Beklenen günlükler `Desktop item selected through UI Automation` ve `Taskbar control invoked through UI Automation` satırlarıdır; boş alanda bu satırlar oluşmaz ve gerçek `SendInput` çalışır.

## Canlı hizmet

- Operatör arayüzü: https://45.87.173.201.nip.io/operator
- Sağlık kontrolü: https://45.87.173.201.nip.io/health
- Host sunucu adresi: `https://45.87.173.201.nip.io`
- Sunucu dizini: `/opt/ascos-remote-support`
- Konteynerler: `ascos_remote_support`, `ascos_remote_support_proxy`
- Denetim kaydı: `/opt/ascos-remote-support/deploy/data/audit.jsonl`

## Teknik durum

- Kontrol ve görüntü için ayrı WebSocket kanalları kullanılmaktadır.
- GDI uyumluluk yakalaması kaynak en-boy oranını korur ve en fazla `1440×900` sınırına orantılı küçültür.
- Operatör tuvali gelen karenin gerçek en-boy oranına göre boyutlandırılır.
- Input, aktif kullanıcı oturumunda gerçek yükseltilmiş kullanıcı tokenıyla çalışan SessionHelper üzerinden uygulanır.
- Başarısız input çağrıları yedek API ile sahte başarıya çevrilmez.
- DXGI/H.264/paylaşımlı bellek hattı kaynak kodda bulunur; üretim ana akışının tüm cihaz matrisindeki doğrulaması henüz tamamlanmamıştır.

## Üretim engeli

Mevcut indirilebilir dosya imzasız geliştirme paketidir. Güvenilir Authenticode sertifikasıyla imzalama ve Windows 10/11/Server gerçek cihaz test matrisi tamamlanmadan kararlı müşteri sürümü olarak yayımlanmamalıdır.
