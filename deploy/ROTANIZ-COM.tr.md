# rotaniz.com dağıtımı

Müşterinin kurulum gerektirmeden çalıştırdığı Windows istemcisi aşağıdaki sabit adreste yayınlanır:

`https://rotaniz.com/downloads/RotaLink.exe`

Canlı WordPress temasındaki indirme düğmesi `website/rotaniz-download-button.php` örneğini kullanır. Yayın dosyası `artifacts/RotaLink.exe` ile aynı derlemedir.

## Güncelleme kontrol listesi

1. Taşınabilir istemciyi `RotaLink.exe` adıyla üretin.
2. Dosyayı `public_html/downloads/RotaLink.exe` üzerine yazın.
3. Ana sayfadaki düğmenin `/downloads/RotaLink.exe` adresini gösterdiğini doğrulayın.
4. Yayınlanan dosyanın boyutunu ve SHA-256 özetini sürüm notuna ekleyin.

Son doğrulanan yayın:

- rotaniz.com sürümü: `1.1.0-alpha.2`
- Dosya boyutu: `132.096` bayt
- SHA-256: `08ce7a64b57e7cbebb645e45b38142da6ddf1a01bb7c56a0ed128f57a6bcc583`

Yetkili input düzeltmesini ve alternatif Win32 input motorunu içeren `1.1.0-alpha.4`, cPanel oturumu yenilenene kadar
kontrol sunucusundaki `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
adresinden yayınlanır. Bu dosyanın boyutu `133.632` bayt, SHA-256 özeti
`dcc40ae66ea22300110bcd62e4954ee220475d8979f7130c261032d89044f2f7` değeridir.

İstemci bağlantı tanılama günlüğünü `%LocalAppData%\RotaLink\rotalink.log` dosyasına yazar.
