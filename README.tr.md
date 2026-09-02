# Rotaniz Remote Support — Faz 1

> Service/SessionHelper/DXGI yeniden yapılandırmasının uygulama ve doğrulama durumu: [`docs/YENIDEN-YAPILANDIRMA-V1.1.tr.md`](docs/YENIDEN-YAPILANDIRMA-V1.1.tr.md)

> Yeni düşük gecikmeli native mimari çalışması için [`docs/ARCHITECTURE-V1.tr.md`](docs/ARCHITECTURE-V1.tr.md) belgesine bakın. Mevcut v0.x istemci, v1 gerçek cihaz testlerini geçene kadar kararlı sürüm olarak korunur.

[English documentation](README.md)

[Sürümleri indir](https://github.com/Kucukejderha/ascos-remote-support/releases) ·
[Kaynak kod ve lisans](LICENSE) · [Gizlilik](PRIVACY.md) ·
[Kod imzalama politikası](CODE_SIGNING_POLICY.md)

Windows 10/11 ve Rotaniz sunucusu için kullanıcı onayını zorunlu tutan modüler uzaktan destek uygulamasıdır.

## Bileşenler

- `server/RemoteSupport.Signaling`: Cihaz kaydı, imzalı doğrulama, destek kodları, hız sınırlama ve host/operatör WebSocket aktarımı.
- `client/RemoteSupport.Protocol`: net8 + net48 için ortak ikili codec'ler ve IPC ilkelleri; net8 doğrulama yüzeyi HMAC doğrulamalı ve tekrar saldırısına karşı korumalı paketler ekler.
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

1. Destek alan bilgisayarda `RotaLink.exe` çalıştırılır; siyah konsol yerine RotaLink Windows arayüzü açılır.
2. Uygulama ekran paylaşımını otomatik başlatır; kullanıcı gösterilen 9 haneli kodu paylaşır.
3. Operatör `/operator` sayfasına kodu girer.
4. Yerel kullanıcı RotaLink penceresini kapatarak paylaşımı ve kontrolü sonlandırabilir.
5. Operatör bağlantısı kapandığında istemci açık kalırsa otomatik olarak yeni bir destek kodu hazırlar ve paylaşımı yeniden başlatır.

## Derleme

```powershell
dotnet build AscosRemoteSupport.sln
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
```

Yalnız taşınabilir istemci için `build.ps1` yeterlidir; kurulum paketi dahil tam sürüm için `build.ps1 -Full` çalıştırın. Hafif istemci `artifacts/RotaLink.exe` olarak üretilir. Windows 10/11 ile birlikte gelen .NET Framework 4.8'i ve standart Windows iletişim pencerelerini kullanır; ayrıca .NET kurulumu, çalışma zamanı gömme veya üçüncü taraf EXE birleştiricisi gerektirmez.

Canlı operatör: https://ascos.rotaniz.com/operator

Güvenlik ayrıntıları için [SECURITY.tr.md](SECURITY.tr.md), sürüm durumu için [STATUS.tr.md](STATUS.tr.md) dosyasına bakın.

## Lisans ve kod imzalama

Telif hakkı (C) 2026 Ali Haydar Sultan Eroglu. RotaLink,
[GNU Affero Genel Kamu Lisansı sürüm 3.0-only](LICENSE) altında yayımlanır. Lisansın
kaynak kod ve bildirim yükümlülüklerine uyulması şartıyla ticari dağıtım ve ücretli
destek mümkündür. RotaLink ve Rotaniz adları ile logoları, değiştirilmiş veya üçüncü
taraf derlemelerin resmî ürün olduğu izlenimini verecek şekilde kullanılamaz.

Ücretsiz kod imzalama [SignPath.io](https://signpath.io/) tarafından, sertifika
[SignPath Foundation](https://signpath.org/) tarafından sağlanır. Ayrıntılar için
[kod imzalama politikası](CODE_SIGNING_POLICY.md) ve
[gizlilik politikası](PRIVACY.md) belgelerine bakın.
