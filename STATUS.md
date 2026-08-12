# RotaLink — Sürüm Durumu

Güncelleme tarihi: 12 Ağustos 2026

## Güncel sürüm

- Sürüm: `1.1.0-alpha.23`
- Kanal: İmzasız kontrollü geliştirme testi
- Paket: `RotaLink-v1.1.0-alpha.23-UNSIGNED-DEVELOPMENT.exe`
- Boyut: `205.824` bayt
- SHA-256: `20ae90c536149c94230afa4224ca7ac4ec6a54277f2cb36b7a508717c9520dea`
- İndirme: https://rotaniz.com/downloads/RotaLink-v1.1.0-alpha.23-UNSIGNED-DEVELOPMENT.exe

## Sürüm doğrulama özeti

- `alpha.18`: Windows Server 2019 üzerinde SYSTEM SessionHelper yoluyla fare ve klavye kontrolü gerçek cihazda doğrulandı.
- `alpha.19`: Kaynak ekranın en-boy oranını koruyan dinamik yakalama ve operatör tuvali koordinat düzeltmesi eklendi. Bazı RDP oturumlarında input API çağrısı başarılı dönmesine rağmen giriş etkin olmayan oturuma yönlenebildi.
- `alpha.20`: SessionHelper WTS oturum durumunu günlüğe ekler. Fare basımından hemen önce tıklanan kök pencereyi hedef input kuyruğuna bağlar ve foreground/active/focus hazırlığı yapar.
- `alpha.21`: SessionHelper, yalnız oturum numarası değiştirilmiş SYSTEM tokenı yerine yükseltilmiş RotaLink istemcisinin gerçek etkileşimli tokenıyla başlatılır.
- `alpha.22`: Normal tıklamalar tek protokol komutu ve tek atomik `SendInput(move + down + up)` çağrısı olarak uygulanır. `AttachThreadInput` bağlantısı enjeksiyon tamamlanana kadar korunur; sürükleme ayrı `down/move/up` akışında devam eder.
- `alpha.23`: Alpha.22 gerçek cihaz günlüğünde Explorer masaüstü ve görev çubuğu hedeflerinde görülen `AttachThreadInput · Win32 5` hatası giderildi. Helper artık fiziksel fare gibi doğrudan sistem input akışına tıklama ekler; hedef pencereyi zorla foreground/active/focus yapmaz.

`alpha.22` gerçek cihaz kaydı atomik `Event=click` paketlerinin helper'a ulaştığını doğruladı. İlk iki normal pencere hedefi hazırlanırken Explorer kabuğuna geçildiğinde `AttachThreadInput(target/foreground) failed · Win32Error=5` tekrarlandı; masaüstü simgeleri ve görev çubuğu çalışmadı. `alpha.23` bu yapay foreground katmanını kaldırır. Beklenen günlük `Natural click target observed` satırıdır; `Foreground input preparation failed` artık görülmemelidir.

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
