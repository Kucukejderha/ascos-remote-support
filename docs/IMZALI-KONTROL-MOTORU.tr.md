# RotaLink imzalı kontrol motoru

## Neden gerekli?

`RotaLink v1.1.0-alpha.14` tanılamaları ağ, koordinat, oturum, IPC, `WinSta0` ve aktif desktop geçişlerinin doğru olduğunu; `SendInput` çağrısının Windows tarafından `ERROR_ACCESS_DENIED (5)` ile engellendiğini kanıtladı.

UIAccess yalnızca token üzerindeki bir bayrak değildir. Windows, UIAccess uygulamasının güvenilir bir Authenticode imzasına sahip olmasını ve `%ProgramFiles%` ya da `%SystemRoot%` altındaki güvenli bir konumdan çalışmasını bekler. Bu nedenle geçici `%ProgramData%` çalışma zamanı kaldırılmıştır.

## alpha.15 mimarisi

- Müşteri tek `RotaLink.exe` dosyasını açar.
- Ana uygulama imzalı Service ve SessionHelper dosyalarını `%ProgramFiles%\RotaLink\Runtime\1.1.0-alpha.15` altına çıkarır.
- Her iki dosyanın Authenticode güven zinciri `WinVerifyTrust` ile doğrulanır.
- İmzasız veya güvenilmeyen dosyalar çalıştırılmaz; görüntü paylaşımı devam edebilir ancak kontrol motoru hazır gösterilmez.
- SYSTEM servisi, aktif kullanıcı oturumunda imzalı ve `uiAccess="true"` manifestli SessionHelper'ı başlatır.
- Uygulama kapanınca servis durur; imzalı dosyalar ve servis kaydı sonraki açılış için korunur.

## İmzalı derleme

PFX kullanan otomatik derleme örneği:

```powershell
$env:ROTALINK_SIGNING_PASSWORD = '<PFX parolası>'
.\scripts\build-signed-client.ps1 -PfxPath 'C:\secure\rotalink-code-signing.pfx'
```

Windows sertifika deposundaki sertifikayla derleme:

```powershell
.\scripts\build-signed-client.ps1 -CertificateThumbprint '<SHA1 thumbprint>'
```

Betik sırasıyla SessionHelper, Service ve bunları içeren ana RotaLink dosyasını imzalar; zaman damgası ekler ve her dosyayı `signtool verify /pa /all` ile doğrular. Parola komut satırı argümanı olarak kabul edilmez.

## Dağıtım kapısı

`build-light-client.ps1` yalnızca `RotaLink-UNSIGNED-DEVELOPMENT.exe` üretir. Bu dosya müşteri dağıtımına uygun değildir ve güvenilir kontrol motorunu bilinçli olarak başlatamaz. Web sitesi ve GitHub sürümü yalnızca `build-signed-client.ps1` başarıyla tamamlandıktan sonra güncellenmelidir.
