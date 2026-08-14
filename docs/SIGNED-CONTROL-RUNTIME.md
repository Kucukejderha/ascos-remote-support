# RotaLink imzalı kontrol çalışma zamanı

Bu belge eski İngilizce bağlantıları bozmamak için aynı dosya adıyla korunmuş, içeriği Türkçeleştirilmiştir. Güncel ayrıntılı belge: [IMZALI-KONTROL-MOTORU.tr.md](IMZALI-KONTROL-MOTORU.tr.md).

## Mimari

RotaLink kontrol hattı üç yürütülebilir dosyadan oluşur:

1. `RotaLink.exe`: Kullanıcı arayüzü, ağ bağlantıları ve oturum yaşam döngüsü.
2. `RotaLink.Service.exe`: LocalSystem olarak çalışan ve etkin WTS oturumunu yöneten Windows servisi.
3. `RotaLink.SessionHelper.exe`: Seçilen etkileşimli kullanıcı oturumunda çalışan ve `SendInput` uygulayan yardımcı süreç.

Servis, SessionHelper'ı RotaLink istemcisinin gerçek oturumunda başlatır. IPC yalnız servis tarafından belirlenen istemci süreç kimliğinden gelen paketleri kabul eder. SessionHelper her input öncesinde etkin masaüstü ve WTS oturum durumunu denetler.

## İmzalı üretim derlemesi

`scripts/build-signed-client.ps1`, PFX dosyası veya Windows sertifika deposundaki thumbprint ile imzalı paket üretir. PFX parolası yalnız `ROTALINK_SIGNING_PASSWORD` ortam değişkeninden verilmelidir. İmzalama sırası şöyledir:

1. SessionHelper
2. Service
3. Bu iki imzalı dosyayı içinde taşıyan RotaLink istemcisi

İstemci, gömülü servis ve SessionHelper dosyalarını kaydetmeden veya başlatmadan önce `WinVerifyTrust` ile doğrular.

## İmzasız geliştirme derlemesi

`scripts/build-light-client.ps1`, yalnız kontrollü test için açıkça `UNSIGNED-DEVELOPMENT` adı taşıyan paket üretir. Bu paket müşterilere dağıtılmamalı ve kararlı sürüm olarak sunulmamalıdır.

Güncel test sürümü ve doğrulama durumu için [../STATUS.md](../STATUS.md) belgesine bakın.
