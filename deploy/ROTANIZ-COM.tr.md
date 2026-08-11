# rotaniz.com dağıtımı

Müşterinin kurulum gerektirmeden çalıştırdığı Windows istemcisi aşağıdaki sabit adreste yayınlanır:

`https://rotaniz.com/downloads/RotaLink.exe`

Canlı WordPress temasındaki indirme düğmesi `website/rotaniz-download-button.php` örneğini kullanır. Yayın dosyası `artifacts/RotaLink.exe` ile aynı derlemedir.

## Güncelleme kontrol listesi

1. Taşınabilir istemciyi `RotaLink.exe` adıyla üretin.
2. Dosyayı `public_html/downloads/RotaLink.exe` üzerine yazın.
3. Ana sayfadaki düğmenin `/downloads/RotaLink.exe` adresini gösterdiğini doğrulayın.
4. Yayınlanan dosyanın boyutunu ve SHA-256 özetini sürüm notuna ekleyin.

## 1.1.0-alpha.13

`alpha.13`, SessionHelper'ı SYSTEM servis tokenından değil `WTSQueryUserToken` ile aktif kullanıcının gerçek etkileşimli logon tokenından üretir. SYSTEM servis bu tokena doğrulanmış `TokenUIAccess=1` ekleyerek helper'ı `WinSta0` üzerinde başlatır.

- Kontrol sunucusu sürümlü bağlantısı: `https://45.87.173.201.nip.io/downloads/RotaLink-v1.1.0-alpha.13.exe`
- Sabit bağlantı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Beklenen helper başlangıcı: `Identity=<etkileşimli kullanıcı>, UIAccess=True`
- Beklenen başarılı kontrol durumu: `system-helper-ok`

## 1.1.0-alpha.12

`alpha.12`, SYSTEM servisinin etkileşimli SessionHelper tokenına `TokenUIAccess=1` atamasını ve başlatılan helper içinde bu bayrağın tekrar doğrulanmasını ekler. Amaç, `alpha.11` birleşik günlüğünde SYSTEM/Session 1/WinSta0/aktif desktop zinciri doğru olduğu hâlde `SendInput` çağrısının UIPI tarafından `ERROR_ACCESS_DENIED (5)` ile reddedilmesini gidermektir.

- Kontrol sunucusu sürümlü bağlantısı: `https://45.87.173.201.nip.io/downloads/RotaLink-v1.1.0-alpha.12.exe`
- Sabit bağlantı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Beklenen helper başlangıcı: `Identity=NT AUTHORITY\\SYSTEM, UIAccess=True`
- Beklenen başarılı kontrol durumu: `system-helper-ok`

## 1.1.0-alpha.11

`alpha.11`, SYSTEM SessionHelper sürecini input iş parçacığı başlamadan önce etkileşimli `WinSta0` pencere istasyonuna bağlar. Named-pipe tekrar bağlantılarında sıra numarası yeniden başlayabildiği için geçerli girdileri reddeden süreç-geneli replay kontrolü bağlantı kapsamına alınmıştır. Helper cevap protokolü gerçek hata aşamasını ve Win32 hata kodunu operatöre taşır.

- Kontrol sunucusu sürümlü bağlantısı: `https://45.87.173.201.nip.io/downloads/RotaLink-v1.1.0-alpha.11.exe`
- Sabit bağlantı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Beklenen başarılı kontrol durumu: `system-helper-ok`

## 1.1.0-alpha.10

Windows Service Control Manager kayıt adı ile gömülü servis dispatch adı arasındaki uyuşmazlık giderildi. Servis artık gerçekten `SERVICE_RUNNING` durumuna ulaşmadan başarılı sayılmıyor. Bu düzeltme SYSTEM oturum helper'ının başlamasını ve kontrol paketlerinin UIPI tarafından engellenen ana süreç yerine yetkili helper üzerinden uygulanmasını sağlar.

`alpha.9`, yüksek DPI/ekran ölçeklemesinde kesilen sürüm etiketini esnek alt bilgi yerleşimiyle düzeltir. İstemci penceresi SYSTEM kontrol motorunun başlangıç sonucunu gösterir. “Tanılama günlüğü (tümü)” bağlantısı kullanıcı uygulaması, SYSTEM servisi ve oturum helper günlüklerini tek dosyada birleştirir. SYSTEM servisi çalışırken helper IPC kurulamıyorsa uygulama artık yerel UIPI yoluna düşmez ve `system-helper-ipc-unavailable` sonucunu verir.

`alpha.10`, named pipe üzerinde desteklenmeyen `ReadTimeout`/`WriteTimeout` özelliklerinin ilk input paketinde uzak oturumu kapatmasını düzeltir. Acknowledgement okuması asynchronous pipe üzerinde iki saniyelik deadline kullanır ve IPC hatası oturum çökmesi yerine açık kontrol sonucu olarak raporlanır.

- Kontrol sunucusu sabit bağlantısı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Kontrol sunucusu sürümlü bağlantısı: `https://45.87.173.201.nip.io/downloads/RotaLink-v1.1.0-alpha.10.exe`
- Dosya boyutu: `187.904` bayt
- SHA-256: `37ca4ff6270afdead8f8e25da8fcf308d8d8359ece4364b7b2ed978e0574817a`

`rotaniz.com` üzerindeki sabit dosya, cPanel/hosting aktarımı tamamlandıktan sonra aynı SHA-256 değerini vermelidir.

İstemci bağlantı tanılama günlüğünü `%LocalAppData%\RotaLink\rotalink.log` dosyasına; SYSTEM servis ve helper günlüklerini `%ProgramData%\RotaLink\Logs` dizinine yazar.
