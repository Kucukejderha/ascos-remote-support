# rotaniz.com dağıtımı

Müşterinin kurulum gerektirmeden çalıştırdığı Windows istemcisi aşağıdaki sabit adreste yayınlanır:

`https://rotaniz.com/downloads/RotaLink.exe`

Canlı WordPress temasındaki indirme düğmesi `website/rotaniz-download-button.php` örneğini kullanır. Yayın dosyası `artifacts/RotaLink.exe` ile aynı derlemedir.

## Güncelleme kontrol listesi

1. Taşınabilir istemciyi `RotaLink.exe` adıyla üretin.
2. Dosyayı `public_html/downloads/RotaLink.exe` üzerine yazın.
3. Ana sayfadaki düğmenin `/downloads/RotaLink.exe` adresini gösterdiğini doğrulayın.
4. Yayınlanan dosyanın boyutunu ve SHA-256 özetini sürüm notuna ekleyin.

## 1.1.0-alpha.8

Windows Service Control Manager kayıt adı ile gömülü servis dispatch adı arasındaki uyuşmazlık giderildi. Servis artık gerçekten `SERVICE_RUNNING` durumuna ulaşmadan başarılı sayılmıyor. Bu düzeltme SYSTEM oturum helper'ının başlamasını ve kontrol paketlerinin UIPI tarafından engellenen ana süreç yerine yetkili helper üzerinden uygulanmasını sağlar.

- Kontrol sunucusu sabit bağlantısı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Kontrol sunucusu sürümlü bağlantısı: `https://45.87.173.201.nip.io/downloads/RotaLink-v1.1.0-alpha.8.exe`
- Dosya boyutu: `184.320` bayt
- SHA-256: `a2f1bbad5e8566748cdd95ae24734e6aaeb3cebbe058fc82ba38cc482543820a`

`rotaniz.com` üzerindeki sabit dosya, cPanel/hosting aktarımı tamamlandıktan sonra aynı SHA-256 değerini vermelidir.

İstemci bağlantı tanılama günlüğünü `%LocalAppData%\RotaLink\rotalink.log` dosyasına; SYSTEM servis ve helper günlüklerini `%ProgramData%\RotaLink\Logs` dizinine yazar.
