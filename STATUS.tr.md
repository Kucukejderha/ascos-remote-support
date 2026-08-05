# Rotaniz Remote Support — Sürüm Durumu

Sürüm tarihi: 2026-08-05

## Canlı hizmet

- Operatör arayüzü: https://45.87.173.201.nip.io/operator
- Sağlık kontrolü: https://45.87.173.201.nip.io/health
- Host sunucu adresi: `https://45.87.173.201.nip.io`
- Sunucu dizini: `/opt/ascos-remote-support`
- Konteynerler: `ascos_remote_support`, `ascos_remote_support_proxy`
- TLS: Caddy üzerinden otomatik Let’s Encrypt sertifikası
- Denetim kaydı: `/opt/ascos-remote-support/deploy/data/audit.jsonl`

## Doğrulanan davranışlar

- Release derlemesi: sıfır hata ve sıfır uyarı
- Kurulum gerektirmeyen, self-contained tek EXE
- HTTPS cihaz kaydı ve P-256 imzalı doğrulama
- Host açıkken yeniden kullanılabilen 9 haneli kod ve her kullanımda yenilenen operatör anahtarı
- Ayrı host/operatör WSS kimlik doğrulaması
- 960×540, en fazla 10 FPS, kayıpsız sıkıştırılmış anahtar/delta kare aktarımı
- Değişmeyen karelerin atlanması
- Güvenli masaüstü veya masaüstü geçişindeki geçici ekran yakalama hatalarında oturumu düşürmeden otomatik devam etme
- DPI ölçeklemesinden bağımsız tam sanal masaüstü yakalama
- Fare, tıklama, kaydırma ve klavye için `SendInput` tabanlı birleşik kontrol hattı
- Görüntü ve input işlemleri için aktif Windows masaüstüne bağlı kalıcı iş parçacıkları
- UAC geçişinden etkilenmeyen, etkileşimli `Default` masaüstüne sabit yakalama ve kontrol
- Taşınabilir istemcide UAC yükseltmesi olmadan doğrudan kullanıcı oturumunda çalışma
- Host kapandığında operatör bağlantısını sunucu tarafından otomatik sonlandırma
- Global input enjeksiyonu engellenen oturumlarda pencere mesajı tabanlı kontrol yedeği
- Odak değişmeden sürekli kare üretmek için tam boy GDI ara belleği üzerinden iki aşamalı ekran yakalama
- Operatörde görüntü ve gerçek kontrol kabulünü ayrı durumlar olarak gösterme
- HMAC doğrulamalı Named Pipe paketleme ve tekrar saldırısı engelleme
- Windows DPAPI cihaz kimliği saklama
- Yerel onaydan önce input reddi
- Kalıcı güvenlik denetim kayıtları

## Ürün sınırı

Bu sürüm, kullanıcı onaylı anlık destek MVP’sidir. Gözetimsiz erişim, gizli kalıcılık, UAC güvenli masaüstü kontrolü, pano ve dosya aktarımı kapsam dışıdır. Yüksek hareketli kullanım için DXGI/H.264/WebRTC sonraki performans aşamasıdır.
