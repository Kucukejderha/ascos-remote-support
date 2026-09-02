# Güvenlik modeli

- Her oturumda yerel Windows onay penceresi gösterilir.
- Gözetimsiz erişim, gizli mod, güvenli masaüstü atlatma, kimlik bilgisi yakalama, pano ve dosya aktarımı uygulanmamıştır.
- Yerel kullanıcı kontrolü istediği anda sonlandırabilir; onay en fazla 15 dakika geçerlidir.
- Cihaz doğrulaması ECDSA P-256 imzalı challenge kullanır.
- Dokuz haneli destek kodu rastgeledir, hız sınırlamasına tabidir, 10 dakikada sona erer ve ilk başarılı kullanımda atomik olarak tüketilir. Ağ kopmalarında kod yeniden kullanılmaz; oturuma özel operatör anahtarıyla yeniden bağlanılır. Host oturumu kapandığında bu anahtar da geçersizleşir.
- Host ve operatör WebSocket bağlantıları ayrı kimlik doğrular ve yalnızca tek oturum için yetkilidir.
- Input mesajları izin listesi, boyut/koordinat sınırları ve saniyede 240 olay sınırıyla korunur; açık onaydan önce uygulanmaz.
- Interaktif input pipe'ı ACL ile yalnızca etkileşimli kullanıcıya açıktır; bağlanan process'in kimliği ve görüntü adı doğrulanır; bağlantı başına sıra numaraları tekrar saldırısını reddeder.
- Üretim ortamında yalnızca HTTPS/WSS kullanılmalıdır.

## Bilinen sınırlar

- Görüntü aktarımı 960×540, en fazla 10 FPS, kayıpsız delta ve gzip kullanır. Yüksek hareketli içerik için DXGI ve donanım H.264/WebRTC planlanmaktadır.
- Sunucu oturum durumu bellektedir; sunucunun yeniden başlaması aktif oturumları kapatır.
- Windows UAC güvenli masaüstü ve kilit ekranı bilerek kontrol edilmez.
- Geniş dağıtımdan önce ikili dosyalar Authenticode ile imzalanmalıdır.
