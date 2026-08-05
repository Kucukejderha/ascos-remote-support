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

- Sürüm: `0.6.2`
- Dosya boyutu: `116.224` bayt
- SHA-256: `2f26a8b1e4de2faedf89758b891da689166cf734e0b2c870c970c6fa58b14605`

İstemci bağlantı tanılama günlüğünü `%LocalAppData%\RotaLink\rotalink.log` dosyasına yazar.
