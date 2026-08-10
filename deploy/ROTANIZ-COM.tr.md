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

Yetkili input düzeltmesini, alternatif Win32 input motorunu, tam sürüm etiketini ve tek örnek kilidini içeren `1.1.0-alpha.6`, cPanel oturumu yenilenene kadar
kontrol sunucusundaki `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
adresinden yayınlanır. Bu dosyanın boyutu `135.168` bayt, SHA-256 özeti
`82c7c638f0fdd6d4ed0064a7c2499005029fa5cb0dec5f28b322fbdf1d0f8faa` değeridir.
Önbellek veya eski İndirilenler dosyası karışıklığını önlemek için aynı derleme ayrıca
`https://45.87.173.201.nip.io/downloads/RotaLink-v1.1.0-alpha.6.exe`
adıyla ve `no-store` yanıt başlıklarıyla sunulur.

İstemci bağlantı tanılama günlüğünü `%LocalAppData%\RotaLink\rotalink.log` dosyasına yazar.
