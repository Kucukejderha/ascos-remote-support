# Rotaniz Remote Support — Faz 1

[English documentation](README.md)

Windows 10/11 ve Rotaniz sunucusu için kullanıcı onayını zorunlu tutan modüler uzaktan destek uygulamasıdır.

## Bileşenler

- `server/RemoteSupport.Signaling`: Cihaz kaydı, imzalı doğrulama, destek kodları, hız sınırlama ve host/operatör WebSocket aktarımı.
- `client/RemoteSupport.Protocol`: Sürüm kontrollü, HMAC doğrulamalı ve tekrar saldırısına karşı korumalı IPC paketleri.
- `client/RemoteSupport.SessionAgent`: Kullanıcı oturumunda ekran yakalama ve onaylanmış input uygulama süreci.
- `client/RemoteSupport.Service`: Windows servis sınırı ve cihaz kimliği yaşam döngüsü. Session 0 içinden ekran yakalamaz.

Faz 1; gözetimsiz erişim, gizli çalışma, UAC güvenli masaüstü kontrolü, pano veya dosya aktarımı içermez. Her oturum görünürdür ve yerel kullanıcı tarafından onaylanır.

## Görüntü aktarımı

- 960×540 çözünürlük ve en fazla 10 FPS
- Değişmeyen kareleri göndermeme
- Kayıpsız XOR fark kareleri
- İki saniyede bir tam anahtar kare
- Gzip sıkıştırma
- Tarayıcıda sıralı ve asenkron kare çözme

Bu yapı metin okunabilirliğini artırırken masaüstü kullanımındaki bant genişliğini önemli ölçüde azaltır. Yüksek hareketli video için sonraki aşama DXGI ve donanım H.264/WebRTC’dir.

## Kullanım

1. Destek alan bilgisayarda `RotaLink.exe` çalıştırılır.
2. Kullanıcı gösterilen 9 haneli kodu paylaşır ve bağlantıyı onaylar.
3. Operatör `/operator` sayfasına kodu girer.
4. Yerel kullanıcı uygulamayı kapatana kadar aynı kod yeniden kullanılabilir.
5. Yerel kullanıcı Enter tuşuyla oturumu istediği anda sonlandırabilir.

## Derleme

```powershell
dotnet build AscosRemoteSupport.sln
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
```

Portatif paket `artifacts/RotaLink.exe` olarak üretilir; kurulum veya önceden yüklenmiş .NET gerektirmez.

Canlı operatör: https://45.87.173.201.nip.io/operator

Güvenlik ayrıntıları için [SECURITY.tr.md](SECURITY.tr.md), sürüm durumu için [STATUS.tr.md](STATUS.tr.md) dosyasına bakın.
