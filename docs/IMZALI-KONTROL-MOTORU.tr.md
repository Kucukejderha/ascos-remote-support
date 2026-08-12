# RotaLink imzalı kontrol motoru

## Neden gerekli?

`RotaLink v1.1.0-alpha.14` tanılamaları ağ, koordinat, oturum, IPC, `WinSta0` ve aktif desktop geçişlerinin doğru olduğunu; `SendInput` çağrısının Windows tarafından `ERROR_ACCESS_DENIED (5)` ile engellendiğini kanıtladı.

Windows, `uiAccess=true` isteyen uygulamalarda güvenilir Authenticode imzası ve güvenli kurulum konumu ister. Alpha.16 bu bağımlılığı kontrol motorundan kaldırdı: SessionHelper aktif kullanıcı oturumunda LocalSystem kimliğiyle çalışır ve `uiAccess` istemez. Üretim dağıtımında bütünlük ve yayıncı doğrulaması için Authenticode imzası yine zorunlu tutulur.

## alpha.15 mimarisi

- Müşteri tek `RotaLink.exe` dosyasını açar.
- Ana uygulama imzalı Service ve SessionHelper dosyalarını `%ProgramFiles%\RotaLink\Runtime\1.1.0-alpha.16` altına çıkarır.
- Her iki dosyanın Authenticode güven zinciri `WinVerifyTrust` ile doğrulanır.
- İmzasız veya güvenilmeyen dosyalar çalıştırılmaz; görüntü paylaşımı devam edebilir ancak kontrol motoru hazır gösterilmez.
- SYSTEM servisi, aktif kullanıcı oturumunda LocalSystem tokenıyla imzalı SessionHelper'ı başlatır.
- SessionHelper IPC bağlantısını yalnızca kendisini başlatan RotaLink işlem kimliğinden kabul eder.
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

`build-light-client.ps1` yalnızca `RotaLink-v1.1.0-alpha.17-UNSIGNED-DEVELOPMENT.exe` üretir. Kontrollü test için LocalSystem kontrol motorunu çalıştırır; imzasız olduğu için müşteri dağıtımına uygun değildir. Alpha.17, RDP/Windows Server sistemlerinde fiziksel konsol yerine RotaLink işleminin gerçek oturumunu hedefler ve input desktop'ı tam masaüstü erişimiyle açar. Web sitesi ve genel GitHub sürümü yalnızca `build-signed-client.ps1` başarıyla tamamlandıktan sonra güncellenmelidir.
