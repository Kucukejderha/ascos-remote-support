# RotaLink — Sürüm Durumu

Güncelleme tarihi: 12 Ağustos 2026

## Güncel sürüm

- Sürüm: `1.1.0-alpha.24`
- Kanal: İmzasız kontrollü geliştirme testi
- Paket: `RotaLink-v1.1.0-alpha.24-UNSIGNED-DEVELOPMENT.exe`
- Boyut: `207.872` bayt
- SHA-256: `bf79658088613a570d0f377b9d89bdc0e63c42dc25e8d46d5f5af50613602af3`
- İndirme: https://rotaniz.com/downloads/RotaLink-v1.1.0-alpha.24-UNSIGNED-DEVELOPMENT.exe

## Sürüm doğrulama özeti

- `alpha.18`: Windows Server 2019 üzerinde SYSTEM SessionHelper yoluyla fare ve klavye kontrolü gerçek cihazda doğrulandı.
- `alpha.19`: Kaynak ekranın en-boy oranını koruyan dinamik yakalama ve operatör tuvali koordinat düzeltmesi eklendi. Bazı RDP oturumlarında input API çağrısı başarılı dönmesine rağmen giriş etkin olmayan oturuma yönlenebildi.
- `alpha.20`: SessionHelper WTS oturum durumunu günlüğe ekler. Fare basımından hemen önce tıklanan kök pencereyi hedef input kuyruğuna bağlar ve foreground/active/focus hazırlığı yapar.
- `alpha.21`: SessionHelper, yalnız oturum numarası değiştirilmiş SYSTEM tokenı yerine yükseltilmiş RotaLink istemcisinin gerçek etkileşimli tokenıyla başlatılır.
- `alpha.22`: Normal tıklamalar tek protokol komutu ve tek atomik `SendInput(move + down + up)` çağrısı olarak uygulanır. `AttachThreadInput` bağlantısı enjeksiyon tamamlanana kadar korunur; sürükleme ayrı `down/move/up` akışında devam eder.
- `alpha.23`: Alpha.22 gerçek cihaz günlüğünde Explorer masaüstü ve görev çubuğu hedeflerinde görülen `AttachThreadInput · Win32 5` hatası giderildi. Helper artık fiziksel fare gibi doğrudan sistem input akışına tıklama ekler; hedef pencereyi zorla foreground/active/focus yapmaz.
- `alpha.24`: Alpha.23 günlüğünde doğru hit-test edilen ancak bir süre sonra işlemeyen Explorer kabuk denetimleri için hedefe özgü senkron tıklama yolu eklendi. `MSTaskListWClass`, `SysListView32` ve `Shell_TrayWnd` mesajları 250 ms zaman aşımıyla işlenir; başarısız olursa atomik `SendInput` kullanılır.

`alpha.23` gerçek cihaz kaydı doğru hit-test hedeflerini doğruladı: görev çubuğu `MSTaskListWClass`, Explorer pencereleri `CabinetWClass` ve masaüstü `SysListView32`. İlk komutlar çalıştıktan sonra görev çubuğu ve masaüstü kabuk eylemleri tekrar etkisiz kaldı; paketler ve `SendInput` sonuçları başarılıydı. `alpha.24`, yalnız bu klasik Explorer kabuk sınıflarına senkron pencere mesajı gönderir. Beklenen günlük `Explorer shell click delivered synchronously` satırıdır.

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
