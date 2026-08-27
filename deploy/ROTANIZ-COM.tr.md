# rotaniz.com dağıtımı

Müşterinin kurulum gerektirmeden çalıştırdığı Windows istemcisi kontrol sunucusunda yayınlanır:

`https://45.87.173.201.nip.io/downloads/RotaLink.exe`

`rotaniz.com` üzerinde ayrı bir dosya barındırılmaz; ana sayfadaki indirme düğmesi doğrudan yukarıdaki adresi gösterir. Canlı WordPress temasındaki indirme düğmesi `website/rotaniz-download-button.php` örneğini kullanır. Yayın dosyası `server/RemoteSupport.Signaling/downloads/RotaLink.exe` ile `artifacts/RotaLink.exe` aynı derlemedir.

## Güncelleme kontrol listesi

1. Taşınabilir istemciyi `RotaLink.exe` adıyla üretin (`scripts/build.ps1`).
2. Dosyayı `server/RemoteSupport.Signaling/downloads/RotaLink.exe` üzerine yazın.
3. Sunucuda `/opt/ascos-remote-support` altındaki sinyal imajını yeniden derleyip konteyneri yeniden başlatın (`docker compose up -d --build`).
4. `https://45.87.173.201.nip.io/downloads/RotaLink.exe` adresinin yeni dosyayı verdiğini doğrulayın.
5. Yayınlanan dosyanın boyutunu ve SHA-256 özetini sürüm notuna ekleyin.

Sürümlü indirme bağlantıları (`RotaLink-vX.Y.Z-*.exe`) kaldırılmıştır; yalnızca sabit `RotaLink.exe` yayınlanır. Sürüm notlarındaki eski sürümlü bağlantı satırları artık geçersizdir.

## 1.1.0-alpha.15 (güncel derleme)

`alpha.15`, güvenlik ve operasyonel iyileştirmeleri içerir: input pipe istemci kimliği doğrulaması, tek kullanımlık destek kodu, süresi dolan sunucu kayıtlarının periyodik temizliği, video kanalının host yeniden bağlanmasında kurtarılması, kalıcı DPAPI cihaz kimliğinin istemcide aktifleştirilmesi, HTTP retry, DXGI/H.264 native yakalama motorunun gömülmesi ve `build.ps1` ile birleştirilmiş derleme akışı.

- Sabit bağlantı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Dosya boyutu: `501.760` bayt
- SHA-256: `c82a73cca65e0c1fb447d522dccc0e538bb4aebdee6d2804590a31043e804048`
- Canlı sunucuya dağıtım: 2026-08-27
- Düzeltme: elevated istemciye input pipe kimlik doğrulaması (bütünlük seviyesi engeli) — process açılamıyorsa bağlantı ACL + oturum eşleşmesiyle kabul edilir

## 1.1.0-alpha.14 (önceki derleme)

`alpha.14`, kullanıcı-tokenlı SessionHelper'ın SYSTEM sahipli günlük dosyasına erişemediği ve pipe ACL'sinde LocalSystem'e özel `WTSQueryUserToken` kullandığı için sürekli kapanmasını düzeltir. Helper günlüğü `%LOCALAPPDATA%` altındadır ve birleşik tanılamaya eklenir.

- Sabit bağlantı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Beklenen helper başlangıcı: `Identity=<etkileşimli kullanıcı>, UIAccess=True`
- Beklenen IPC kaydı: `Privileged SessionHelper input IPC connected`

## 1.1.0-alpha.13

`alpha.13`, SessionHelper'ı SYSTEM servis tokenından değil `WTSQueryUserToken` ile aktif kullanıcının gerçek etkileşimli logon tokenından üretir. SYSTEM servis bu tokena doğrulanmış `TokenUIAccess=1` ekleyerek helper'ı `WinSta0` üzerinde başlatır.

- Sabit bağlantı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Beklenen helper başlangıcı: `Identity=<etkileşimli kullanıcı>, UIAccess=True`
- Beklenen başarılı kontrol durumu: `system-helper-ok`

## 1.1.0-alpha.12

`alpha.12`, SYSTEM servisinin etkileşimli SessionHelper tokenına `TokenUIAccess=1` atamasını ve başlatılan helper içinde bu bayrağın tekrar doğrulanmasını ekler. Amaç, `alpha.11` birleşik günlüğünde SYSTEM/Session 1/WinSta0/aktif desktop zinciri doğru olduğu hâlde `SendInput` çağrısının UIPI tarafından `ERROR_ACCESS_DENIED (5)` ile reddedilmesini gidermektir.

- Beklenen helper başlangıcı: `Identity=NT AUTHORITY\\SYSTEM, UIAccess=True`
- Beklenen başarılı kontrol durumu: `system-helper-ok`

## 1.1.0-alpha.11

`alpha.11`, SYSTEM SessionHelper sürecini input iş parçacığı başlamadan önce etkileşimli `WinSta0` pencere istasyonuna bağlar. Named-pipe tekrar bağlantılarında sıra numarası yeniden başlayabildiği için geçerli girdileri reddeden süreç-geneli replay kontrolü bağlantı kapsamına alınmıştır. Helper cevap protokolü gerçek hata aşamasını ve Win32 hata kodunu operatöre taşır.

- Beklenen başarılı kontrol durumu: `system-helper-ok`

## 1.1.0-alpha.10

Windows Service Control Manager kayıt adı ile gömülü servis dispatch adı arasındaki uyuşmazlık giderildi. Servis artık gerçekten `SERVICE_RUNNING` durumuna ulaşmadan başarılı sayılmıyor. Bu düzeltme SYSTEM oturum helper'ının başlamasını ve kontrol paketlerinin UIPI tarafından engellenen ana süreç yerine yetkili helper üzerinden uygulanmasını sağlar.

`alpha.9`, yüksek DPI/ekran ölçeklemesinde kesilen sürüm etiketini esnek alt bilgi yerleşimiyle düzeltir. İstemci penceresi SYSTEM kontrol motorunun başlangıç sonucunu gösterir. “Tanılama günlüğü (tümü)” bağlantısı kullanıcı uygulaması, SYSTEM servisi ve oturum helper günlüklerini tek dosyada birleştirir. SYSTEM servisi çalışırken helper IPC kurulamıyorsa uygulama artık yerel UIPI yoluna düşmez ve `system-helper-ipc-unavailable` sonucunu verir.

`alpha.10`, named pipe üzerinde desteklenmeyen `ReadTimeout`/`WriteTimeout` özelliklerinin ilk input paketinde uzak oturumu kapatmasını düzeltir. Acknowledgement okuması asynchronous pipe üzerinde iki saniyelik deadline kullanır ve IPC hatası oturum çökmesi yerine açık kontrol sonucu olarak raporlanır.

- Sabit bağlantı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Dosya boyutu: `187.904` bayt
- SHA-256: `37ca4ff6270afdead8f8e25da8fcf308d8d8359ece4364b7b2ed978e0574817a`

`rotaniz.com` kendi dosya kopyasını barındırmaz; WordPress düğmesi kontrol sunucusu adresine yönlendirir.

İstemci bağlantı tanılama günlüğünü `%LocalAppData%\RotaLink\rotalink.log` dosyasına; SYSTEM servis ve helper günlüklerini `%ProgramData%\RotaLink\Logs` dizinine yazar.
