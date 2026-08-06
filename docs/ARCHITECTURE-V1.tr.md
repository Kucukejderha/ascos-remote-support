# RotaLink Native v1 mimari kararı

## Neden mevcut hat değiştiriliyor?

Mevcut istemci GDI ile CPU belleğine ekran kopyalıyor, her kareyi JPEG olarak kodluyor ve güvenilir/sıralı tek WebSocket üzerinden gönderiyor. Bu model küçük bir EXE üretir; fakat CPU kopyası/kodlama gecikmesi, eski video karelerinin kuyruk oluşturması ve Windows input sınırlamaları nedeniyle gerçek zamanlı kontrol hedefini karşılamıyor.

Bu nedenle `v0.x` bakım hattı korunacak, performans geliştirmesi ayrı bir native `v1` hattında yapılacaktır.

## Yeni veri yolu

```text
Interactive Session Helper
  DXGI Desktop Duplication
       │ GPU texture + dirty/move rects
       ▼
  D3D11 Video Processor → NV12
       ▼
  Media Foundation H.264 MFT (hardware, low latency)
       ▼
  Video channel (latest-frame priority, bounded queue)
       ▼
  Native Operator Decoder + D3D11 renderer

Input channel ───────────────► Session Helper / Service broker
Signaling/auth ──────────────► Existing ASP.NET session boundary
```

## Bileşenler

1. `RotaLink.NativeHost`: C++20, Win32, D3D11, DXGI 1.2 ve Media Foundation; ek .NET kurulumu gerektirmez.
2. `RotaLink.NativeOperator`: D3D11 yüzeyine doğrudan video çözer; input video yoğunluğundan bağımsızdır.
3. `RotaLink.ControlService`: LocalSystem servisi ile kullanıcı oturumu yardımcısı arasında kimlik doğrulamalı named pipe sağlar.
4. `RotaLink.Gateway`: Mevcut cihaz kaydı, 9 haneli kod ve oturum yetkilendirmesini korur; video kuyruğunda yalnızca en yeni kareyi tutar.

## Performans hedefleri

- İlk görüntü: 500 ms altında
- Fare/tıklama geri bildirimi: aynı bölge içinde 150 ms altında
- Yakalama: 30 FPS hedef, 15 FPS alt sınır
- Video kuyruğu: en fazla 1 kare
- 1080p masaüstü: hareket durumuna göre 1–4 Mbit/sn
- Input kanalı: video yoğunluğundan bağımsız

## Geçiş sırası

1. DXGI yakalama motoru ve ölçüm aracı.
2. D3D11 BGRA→NV12 dönüşümü ve Media Foundation düşük gecikmeli H.264.
3. Native operatör decoder/renderer.
4. Ayrı input kanalı ve koordinat testleri.
5. Mevcut kimlik doğrulama/gateway entegrasyonu.
6. Servis + session helper ayrımı, kod imzalama ve unattended access.

`v1` gecikme hedeflerini iki gerçek Windows 10/11 cihazında sağlamadan web sitesindeki kararlı istemcinin yerini almayacaktır.
