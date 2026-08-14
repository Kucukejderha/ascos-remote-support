# RotaLink güvenlik modeli

## Oturum güvenliği

- RotaLink yalnız kullanıcı tarafından görünür biçimde başlatılan destek oturumlarını kabul eder.
- İstemci penceresinin kapatılması ekran paylaşımını, kontrol bağlantısını ve geçici SYSTEM çalışma zamanını durdurur.
- Dokuz haneli destek kodu yalnız etkin host oturumu boyunca kullanılabilir; her operatör bağlantısında erişim anahtarı yenilenir.
- Host ve operatör WebSocket bağlantıları ayrı ayrı doğrulanır ve yalnız tek bir destek oturumuna yetkilendirilir.
- Kontrol ve görüntü kanalları birbirinden ayrıdır; video kuyruğu input trafiğini bloke etmez.

## Windows yetki sınırı

- Uzaktan input, LocalSystem servisinin seçtiği aktif WTS kullanıcı oturumundaki `RotaLink.SessionHelper` tarafından uygulanır.
- SessionHelper, input öncesinde etkin masaüstü bağlamını denetler ve sonucu günlüğe yazar.
- IPC yalnız beklenen RotaLink sürecinden gelen, boyutu ve biçimi doğrulanmış paketleri kabul eder.
- Başarısız `SendInput` çağrısı başarı olarak raporlanmaz; `Accepted=False`, aşama ve Win32 hata kodu operatöre iletilir.
- UAC güvenli masaüstü desteği, imzalı üretim çalışma zamanı tamamlanmadan ürün özelliği olarak taahhüt edilmez.

## Dağıtım güvenliği

- Üretim ortamında yalnız HTTPS/WSS kullanılmalıdır.
- Geniş müşteri dağıtımından önce istemci, servis ve SessionHelper güvenilir Authenticode sertifikasıyla imzalanmalıdır.
- `UNSIGNED-DEVELOPMENT` paketleri yalnız kontrollü test içindir; müşterilere dağıtılmamalıdır.
- İmzalı pakette gömülü çalışma zamanı dosyaları servis kaydından önce `WinVerifyTrust` ile doğrulanır.

## Kapsam dışı özellikler

Mevcut sürümde gözetimsiz erişim, gizli kalıcılık, kimlik bilgisi yakalama, pano aktarımı ve dosya aktarımı yoktur. Bu özellikler ayrı güvenlik tasarımı ve kullanıcı onayı olmadan eklenmemelidir.

Ayrıntılı uygulama notları için [docs/IMZALI-KONTROL-MOTORU.tr.md](docs/IMZALI-KONTROL-MOTORU.tr.md) belgesine bakın.
