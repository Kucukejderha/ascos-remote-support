# rotaniz.com dağıtımı

Müşterinin kurulum gerektirmeden çalıştırdığı Windows istemcisi **yalnızca GitHub Releases** üzerinden dağıtılır:

`https://github.com/Kucukejderha/ascos-rotalink/releases/latest/download/RotaLink.exe`

Sürüm bildirimi (self-update):

`https://github.com/Kucukejderha/ascos-rotalink/releases/latest/download/version.json`

`rotaniz.com` istemci dosyası barındırmaz; `/downloads/*` uç noktaları kaldırılmıştır. Sinyal sunucusu yalnızca `/operator`, `/health` ve `/v1/*` hizmetlerini sunar.

## Güncelleme kontrol listesi

1. Taşınabilir istemciyi `RotaLink.exe` adıyla üretin: `scripts/build.ps1 -Configuration Release`.
2. GitHub rolling release'e yayınlayın: `scripts/build.ps1 -Configuration Release -Deploy` → `rotalink-latest` etiketli release'e `RotaLink.exe` + `version.json` yüklenir (`gh` CLI gerekir).
3. `https://github.com/Kucukejderha/ascos-rotalink/releases/latest/download/RotaLink.exe` adresinin yeni dosyayı verdiğini doğrulayın (302 yönlendirme normaldir; istemci `HttpClient` yönlendirmeyi izler).
4. Yayınlanan dosyanın boyutu ve SHA-256 özeti `artifacts\version.json` içinde görünür; sürüm notuna ekleyin.

## 1.1.0-alpha.24 (güncel derleme)

- Rolling release: `https://github.com/Kucukejderha/ascos-rotalink/releases/latest`
- Sabit bağlantı: `https://github.com/Kucukejderha/ascos-rotalink/releases/latest/download/RotaLink.exe`
- Sürüm bildirimi: `https://github.com/Kucukejderha/ascos-rotalink/releases/latest/download/version.json`
- Otomatik güncelleme: istemci açılışta GitHub'daki `version.json`'u denetler; yeni sürüm/hash varsa indirir, SHA-256 doğrular, çalışan exe'yi değiştirip yeniden başlatır. `build.ps1 -Deploy` sürüm bildirimini otomatik yükler.

## 1.1.0-alpha.14–alpha.15 (eski notlar — arşiv)

Sunucu barındırmalı dağıtım (`ascos.rotaniz.com/downloads/...`) 2026-09-02 itibarıyla kaldırılmıştır; bu sürümlere ait sabit bağlantılar artık geçersizdir.

İstemci bağlantı tanılama günlüğünü `%LocalAppData%\RotaLink\rotalink.log` dosyasına; SYSTEM servis ve helper günlüklerini `%ProgramData%\RotaLink\Logs` dizinine yazar.
