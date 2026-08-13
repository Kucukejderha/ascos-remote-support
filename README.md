# RotaLink — Rotanız Uzaktan Destek

RotaLink; Windows 10, Windows 11 ve Windows Server 2012–2025 Desktop Experience üzerinde kullanıcı tarafından başlatılan, görünür uzaktan destek oturumları için geliştirilen modüler bir uygulamadır. Platformların güncel doğrulama durumu ve önkoşulları [Windows uyumluluk yol haritasında](docs/WINDOWS-UYUMLULUK-YOL-HARITASI.tr.md) tanımlanır; bu ifade tüm matrisin henüz doğrulandığı anlamına gelmez.

Mevcut `1.1.0-alpha.26` yalnızca eski `net48` laboratuvar hattıdır. Müşteri dağıtımı için
ek çalışma zamanı gerektirmeyen, statik CRT kullanan `1.2.0-native` istemci geliştirilmektedir.
Sürüm ve canlı ortam ayrıntıları için [STATUS.md](STATUS.md), mimari için
[docs/YENIDEN-YAPILANDIRMA-V1.1.tr.md](docs/YENIDEN-YAPILANDIRMA-V1.1.tr.md) belgesine bakın.

Geliştirme sırası: [Windows 10/11 ve Server 2012+ uyumluluk yol haritası](docs/WINDOWS-UYUMLULUK-YOL-HARITASI.tr.md).
Temiz VM testinin uygulanışı: [Windows test laboratuvarı](docs/WINDOWS-TEST-LABORATUVARI.tr.md).
Native tek-EXE tasarımı: [Native müşteri istemcisi mimarisi](docs/NATIVE-ISTEMCI-MIMARISI.tr.md).

## Bileşenler

- `server/RemoteSupport.Signaling`: Cihaz kaydı, 9 haneli destek kodu, kimlik doğrulama ve ayrı kontrol/görüntü WebSocket aktarımı.
- `client/RemoteSupport.SessionAgent`: Geçiş dönemi `net48` kullanıcı arayüzü ve davranış referansı; müşteri paketi değildir.
- `client/RemoteSupport.Service`: LocalSystem yetkili Windows servis katmanı ve aktif WTS oturumu yönetimi.
- `client/RotaLink.SessionHelper`: Aktif kullanıcı oturumunda çalışan, masaüstü bağlamını izleyen ve `SendInput` uygulayan yardımcı süreç.
- `client/RemoteSupport.Protocol`: Sürümlü IPC ve ikili taşıma protokolleri.
- `native/RotaLink.Client`: .NET/VC++ Runtime gerektirmeyen statik Win32 müşteri istemcisi.
- `native/RotaLink.NativeHost`: DXGI Desktop Duplication, H.264 ve paylaşımlı bellek görüntü motoru.

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
cmake -S native -B native/out -A x64
cmake --build native/out --config Release --target RotaLink.Client
powershell -ExecutionPolicy Bypass -File scripts/Test-NativeClientArtifact.ps1 `
  -Path native/out/Release/RotaLink.exe
```

Kolay derleme komutu `scripts/build-native-client.ps1` dosyasıdır. Visual Studio C++
Desktop workload, Windows SDK ve CMake yalnız derleme/CI makinesinde gerekir; müşteriye
kurulmaz. PE kapısı EXE'nin x64 olduğunu, CLR başlığı taşımadığını ve 10 MB sınırını denetler.

İmzalı üretim paketi için:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-signed-client.ps1
```

`build-light-client.ps1` yalnız kontrollü testlerde kullanılabilecek, açıkça `UNSIGNED-DEVELOPMENT` olarak adlandırılan imzasız paket üretir. Müşteri dağıtımı için RotaLink istemcisi, servis ve SessionHelper güvenilir Authenticode sertifikasıyla imzalanmalıdır.

## Canlı adresler

- Operatör: https://45.87.173.201.nip.io/operator
- Sağlık kontrolü: https://45.87.173.201.nip.io/health
- Güncel native önizleme GitHub Actions tarafından üretilir; siteye yükleme yalnız gerçek Windows uyumluluk testi tamamlandıktan sonra yapılır.

## Ürün sınırı

Mevcut sürüm kullanıcı tarafından başlatılan anlık destek içindir. Gözetimsiz erişim, gizli çalışma, kimlik bilgisi toplama, pano aktarımı ve dosya aktarımı etkin değildir. İmzasız geliştirme paketi üretim veya geniş müşteri dağıtımı için uygun değildir.

Güvenlik ayrıntıları için [SECURITY.md](SECURITY.md), dağıtım geçmişi için [deploy/ROTANIZ-COM.tr.md](deploy/ROTANIZ-COM.tr.md) dosyasına bakın.
