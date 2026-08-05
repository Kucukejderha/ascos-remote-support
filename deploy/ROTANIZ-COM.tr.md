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

- Sürüm: `0.4.4`
- Dosya boyutu: `111.104` bayt
- SHA-256: `79c59414bce0d40d4bef742df0bdb19384b76fd2cb12dc8242d86af8e6fadb05`

İstemci bağlantı tanılama günlüğünü `%LocalAppData%\RotaLink\rotalink.log` dosyasına yazar.
