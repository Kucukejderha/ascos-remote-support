# RotaLink imzalı kontrol motoru

## Neden gerekli?

`RotaLink v1.1.0-alpha.14` tanılamaları ağ, koordinat, oturum, IPC, `WinSta0` ve aktif desktop geçişlerinin doğru olduğunu; `SendInput` çağrısının Windows tarafından `ERROR_ACCESS_DENIED (5)` ile engellendiğini kanıtladı.

Windows, `uiAccess=true` isteyen uygulamalarda güvenilir Authenticode imzası ve güvenli kurulum konumu ister. Alpha.16 bu bağımlılığı kontrol motorundan kaldırdı: SessionHelper aktif kullanıcı oturumunda LocalSystem kimliğiyle çalışır ve `uiAccess` istemez. Üretim dağıtımında bütünlük ve yayıncı doğrulaması için Authenticode imzası yine zorunlu tutulur.

## Güncel mimari

- Müşteri tek `RotaLink.exe` dosyasını açar.
- Ana uygulama imzalı Service ve SessionHelper dosyalarını `%ProgramFiles%\RotaLink\Runtime\<sürüm>` altına çıkarır.
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

`build-light-client.ps1` sürüm numarası taşıyan `UNSIGNED-DEVELOPMENT` paketi üretir. Kontrollü test için servis tarafından başlatılan, gerçek yükseltilmiş kullanıcı tokenına sahip etkileşimli kontrol motorunu çalıştırır; imzasız olduğu için müşteri dağıtımına uygun değildir. Güncel `alpha.24`, normal uygulamalarda atomik `SendInput(move + down + up)` dizisini kullanır. Fiziksel fare davranışını bozan `AttachThreadInput`, `SetForegroundWindow`, `SetActiveWindow` ve `SetFocus` uygulanmaz. Klasik Explorer görev çubuğu ve masaüstü denetimlerinde, Windows Server/RDP altında sistem input akışının görünür etki oluşturmaması durumuna karşı süre sınırlı senkron kabuk tıklaması uygulanır; başarısızlıkta normal `SendInput` yoluna dönülür. Web sitesindeki imzasız dosya yalnız sınırlı test için tutulmalı; kararlı GitHub sürümü ve genel müşteri dağıtımı yalnız `build-signed-client.ps1` başarıyla tamamlandıktan sonra güncellenmelidir.
