# RotaLink — Rotanız Uzaktan Destek

RotaLink; Windows 10, Windows 11 ve Windows Server 2019 üzerinde kullanıcı tarafından başlatılan, görünür uzaktan destek oturumları için geliştirilen modüler bir uygulamadır.

Güncel geliştirme sürümü `1.1.0-alpha.20`dir. Sürüm ve canlı ortam ayrıntıları için [STATUS.md](STATUS.md), mimari için [docs/YENIDEN-YAPILANDIRMA-V1.1.tr.md](docs/YENIDEN-YAPILANDIRMA-V1.1.tr.md) belgesine bakın.

## Bileşenler

- `server/RemoteSupport.Signaling`: Cihaz kaydı, 9 haneli destek kodu, kimlik doğrulama ve ayrı kontrol/görüntü WebSocket aktarımı.
- `client/RemoteSupport.SessionAgent`: RotaLink kullanıcı arayüzü, oturum yaşam döngüsü ve görüntü gönderimi.
- `client/RemoteSupport.Service`: LocalSystem yetkili Windows servis katmanı ve aktif WTS oturumu yönetimi.
- `client/RotaLink.SessionHelper`: Aktif kullanıcı oturumunda çalışan, masaüstü bağlamını izleyen ve `SendInput` uygulayan yardımcı süreç.
- `client/RemoteSupport.Protocol`: Sürümlü IPC ve ikili taşıma protokolleri.
- `native`: DXGI Desktop Duplication, H.264 ve paylaşımlı bellek tabanlı yeni görüntü hattı kaynakları.

## Kullanım akışı

1. Destek isteyen kişi RotaLink istemcisini açar.
2. Uygulama ekran paylaşımını otomatik başlatır ve 9 haneli destek kodunu gösterir.
3. Destek veren kişi [operatör ekranına](https://45.87.173.201.nip.io/operator) kodu girer.
4. Görüntü ve kontrol kanalları ayrı bağlantılar üzerinden çalışır.
5. İstemci penceresi kapatıldığında destek oturumu ve geçici kontrol çalışma zamanı durdurulur.

## Derleme ve doğrulama

```powershell
dotnet build AscosRemoteSupport.sln
dotnet run --project tests/RemoteSupport.Smoke/RemoteSupport.Smoke.csproj -- http://127.0.0.1:5188
powershell -ExecutionPolicy Bypass -File scripts/build-light-client.ps1
```

İmzalı üretim paketi için:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-signed-client.ps1
```

`build-light-client.ps1` yalnız kontrollü testlerde kullanılabilecek, açıkça `UNSIGNED-DEVELOPMENT` olarak adlandırılan imzasız paket üretir. Müşteri dağıtımı için RotaLink istemcisi, servis ve SessionHelper güvenilir Authenticode sertifikasıyla imzalanmalıdır.

## Canlı adresler

- Operatör: https://45.87.173.201.nip.io/operator
- Sağlık kontrolü: https://45.87.173.201.nip.io/health
- Test istemcisi: https://rotaniz.com/downloads/RotaLink-v1.1.0-alpha.20-UNSIGNED-DEVELOPMENT.exe

## Ürün sınırı

Mevcut sürüm kullanıcı tarafından başlatılan anlık destek içindir. Gözetimsiz erişim, gizli çalışma, kimlik bilgisi toplama, pano aktarımı ve dosya aktarımı etkin değildir. İmzasız geliştirme paketi üretim veya geniş müşteri dağıtımı için uygun değildir.

Güvenlik ayrıntıları için [SECURITY.md](SECURITY.md), dağıtım geçmişi için [deploy/ROTANIZ-COM.tr.md](deploy/ROTANIZ-COM.tr.md) dosyasına bakın.
