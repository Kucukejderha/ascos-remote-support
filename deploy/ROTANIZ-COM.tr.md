# rotaniz.com dağıtımı

## 1.2.0-native.2 — .NET gerektirmeyen tek EXE önizlemesi

Müşteri bilgisayarına .NET Framework veya Visual C++ Redistributable kurdurmamak için
istemci statik CRT kullanan x64 Win32/C++20 uygulamasına taşındı. Aynı `RotaLink.exe`;
görünür kullanıcı arayüzü, geçici SYSTEM servisi ve aktif oturum helper rollerini içerir.
GitHub Actions üzerindeki gerçek Windows derlemesi CLR yokluğu, statik bağımlılık kümesi
ve 10 MB kesin üst sınır kontrollerinden geçmiştir.

- Dosya: `RotaLink-v1.2.0-native.2.exe`
- Boyut: `477.184` bayt
- SHA-256: `86aa742d58ccb5c686c966da93788f514f6bbeea8fee5fb601849bb5fbda1ce9`
- CI commit: `98b6bbf15a5930ebf468a1477cbd7cde42e5f91b`
- Durum: İmzasız teknik önizleme; hedef Windows VM matrisi tamamlanmadan sitenin
  kararlı `RotaLink.exe` bağlantısının üzerine yazılmaz.

## 1.1.0-alpha.25 Explorer kabuğu için UI Automation

Alpha.23 gerçek cihaz kaydı uzak koordinatların doğru pencerelere ulaştığını kesin
olarak gösterdi: görev çubuğu `MSTaskListWClass`, Explorer `CabinetWClass`, masaüstü
`SysListView32`. İlk komutlar çalıştıktan sonra kabuk eylemleri tekrar etkisiz kaldı;
istemci ve helper günlüklerinde bağlantı, IPC veya Win32 hatası bulunmadı.

Alpha.25 normal uygulamalar için atomik `SendInput` yolunu korur. Görev çubuğu
düğmeleri Windows UI Automation `InvokePattern`, masaüstü simgeleri ise
`SelectionItemPattern` ile işlenir. Masaüstü simgesinin otomasyon yoluyla çalıştırılması
desteklenmiyorsa ikinci tıklamada gerçek çift tıklama dizisi gönderilir. Masaüstü
boşluğu, sağ tık ve diğer hedefler Windows'un doğal seçim ve menü davranışını korumak
için atomik `SendInput` yolunda kalır.

- Beklenen günlükler: `Desktop item selected through UI Automation` ve
  `Taskbar control invoked through UI Automation`
- Test bağlantısı: `https://rotaniz.com/downloads/RotaLink-v1.1.0-alpha.25-UNSIGNED-DEVELOPMENT.exe`
- Dosya boyutu: `209.920` bayt
- SHA-256: `109648e008e7e7acbc7918b1ea046a5c935406ba0f0b0e788bc99766f416506f`
- Test paketi imzasızdır ve yalnız kontrollü doğrulama içindir.

## 1.1.0-alpha.23 Explorer kabuğu tıklamaları

Alpha.22 günlükleri tüm tıklamaların atomik `Event=click` olarak helper'a ulaştığını
gösterdi. Normal pencere hedeflerinde foreground hazırlığı başarılı olurken masaüstü,
görev çubuğu ve simge durumuna küçültülmüş pencere düğmelerinde
`AttachThreadInput(target/foreground) failed · Win32Error=5` oluştu. Yapay foreground
işlemi fiziksel farenin doğal aktivasyonundan farklı davranarak Explorer kabuğunu
engelliyordu.

Alpha.23 tıklama yolundan `AttachThreadInput`, `SetForegroundWindow`, `SetActiveWindow`
ve `SetFocus` çağrılarını kaldırır. Atomik `SendInput(move + down + up)` doğrudan sistem
input akışına eklenir; Windows masaüstü, görev çubuğu ve normal pencerelerde kendi
hit-test/aktivasyon sürecini yürütür. Hedef pencere sınıfı yalnız tanılama için kaydedilir.

- Beklenen günlük: `Natural click target observed`
- Artık görülmemesi gereken günlük: `Foreground input preparation failed`
- Test bağlantısı: `https://rotaniz.com/downloads/RotaLink-v1.1.0-alpha.23-UNSIGNED-DEVELOPMENT.exe`
- Dosya boyutu: `205.824` bayt
- SHA-256: `20ae90c536149c94230afa4224ca7ac4ec6a54277f2cb36b7a508717c9520dea`
- Test paketi imzasızdır ve yalnız kontrollü doğrulama içindir.

## 1.1.0-alpha.22 atomik ilk tıklama

Alpha.21 gerçek cihaz günlükleri helper'ın doğru kullanıcı kimliği, yükseltilmiş token,
aktif WTS oturumu ve `WinSta0` üzerinde çalıştığını doğruladı. `SendInput` başarılı
dönmesine rağmen ilk tıklama, Alpemix üzerinden bir kez tıklanana kadar hedefte etki
oluşturmuyordu. Kalan sorun yetki değil, tıklama aktivasyonu ve düğme basma/bırakma
olaylarının iki ayrı taşıma/IPC işlemi arasında bölünmesiydi.

Alpha.22'de normal tıklama tek `click` protokol komutu olarak taşınır ve helper içinde
tek bir `SendInput` çağrısında `move + button-down + button-up` dizisiyle uygulanır.
Hedef ve foreground giriş kuyrukları `AttachThreadInput` ile gerçek enjeksiyon
tamamlanana kadar bağlı tutulur. Dört pikselden fazla hareket edilen işlemler gerçek
sürükleme kabul edilir ve ayrı `down/move/up` akışını kullanır.

- Beklenen operatör durumu/günlüğü: `Event=click`
- Sürükleme günlükleri: `Event=button-down` ve `Event=button-up`
- Test bağlantısı: `https://rotaniz.com/downloads/RotaLink-v1.1.0-alpha.22-UNSIGNED-DEVELOPMENT.exe`
- Dosya boyutu: `207.360` bayt
- SHA-256: `d6b94d5a7b1064a83413018b803fe57bcc1cdb0f0faba0a7a77a9c2cfa672cb0`
- Test paketi imzasızdır ve yalnız kontrollü doğrulama içindir.

## 1.1.0-alpha.21 etkileşimli kullanıcı tokenı

Alpha.20 birleşik günlüğü input paketinin sunucu, istemci IPC ve SessionHelper
zincirini geçtiğini; helper'ın `Session=1`, `WTSState=Active`, `WinSta0` ve doğru
foreground/focus hedefinde çalıştığını gösterdi. `SendInput` her olay için bir
girdi kabul etmesine rağmen hedef Windows Server 2019/RDP oturumunda görünür bir
etki oluşmadı. Kök fark, helper'ın gerçek etkileşimli oturum tokenı yerine yalnız
`TokenSessionId` alanı değiştirilmiş bir LocalSystem tokenıyla çalışmasıdır.

Alpha.21'de servis, yükseltilmiş RotaLink istemcisinin gerçek süreç tokenını
`DuplicateTokenEx` ile primary token olarak çoğaltır ve helper'ı bu tokenla
`winsta0\\default` üzerinde başlatır. Böylece helper, RDP input kuyruğunun sahibi
olan kullanıcı logon bağlamına geri döner. Servis güvenilir başlatıcı olarak kalır;
named pipe yalnız servis başlangıcında bildirilen aynı istemci PID'sini kabul eder.

- Beklenen servis kaydı: `Elevated interactive client helper token prepared`
- Beklenen helper kaydı: `Elevated=True`, `UIAccess=False`, `WTSState=Active`
- Eski `Identity=NT AUTHORITY\\SYSTEM` kaydı Alpha.21 testinde görülmemelidir.
- Test paketi imzasızdır ve yalnız kontrollü doğrulama içindir.
- Test bağlantısı: `https://rotaniz.com/downloads/RotaLink-v1.1.0-alpha.21-UNSIGNED-DEVELOPMENT.exe`
- Dosya boyutu: `205.312` bayt
- SHA-256: `aed2f408088915f61db9fafe2e5771b92741f0e567f27e8dc195a0e3c9eee40c`

## 1.1.0-alpha.20 oturum ve foreground aktivasyonu

Alpha.19 tanılamasında başarısız ilk deneme `Session=2`, Alpemix bağlantısından
sonra çalışan deneme ise `Session=5` içinde gerçekleşti. `SendInput` her iki
durumda da başarılı olsa da Windows bağlantısı kesilmiş/etkin olmayan RDP
oturumunda olayı işleme koymayabilir. Alpha.20, helper başlangıcında gerçek WTS
oturum durumunu günlükler. İlk fare basımından önce tıklanan kök pencereyi hedef
input kuyruğuna `AttachThreadInput` ile bağlayıp foreground/active/focus
hazırlığını yapar; fazladan tıklama üretmez.

- Test bağlantısı: `https://rotaniz.com/downloads/RotaLink-v1.1.0-alpha.20-UNSIGNED-DEVELOPMENT.exe`
- Dosya boyutu: `204.288` bayt
- SHA-256: `94cf9b65f5d5b8f5a9da692bb3da08e24f5bc6e87e4e6f07d610513f0e01e3f8`
- Beklenen helper kaydı: `WTSState=Active`
- İlk hedefte beklenen kayıt: `Prepared foreground input target`

## Alpha.19 operatör koordinat düzeltmesi

4:3 gibi 16:9 dışındaki masaüstleri için operatör tuvali artık sabit 16:9
kutuda tutulmaz. Görünen tuval, gelen karenin gerçek en-boy oranıyla çalışma
alanına sığdırılır ve koordinatlar doğrudan bu görünür dikdörtgenden normalize
edilir. Bu sunucu taraflı düzeltme Alpha.19 EXE bağlantısını değiştirmez.

## 1.1.0-alpha.19 görüntü iyileştirmesi

`alpha.19`, GDI görüntüyü sabit `960×540` boyutuna germek yerine kaynak
masaüstünün en-boy oranını korur. 1440×900 sınırının altındaki ekranlar doğal
çözünürlüklerinde aktarılır; daha büyük ekranlar kırpılmadan orantılı küçültülür.
RDP yeniden bağlantısı, ekran çözünürlüğü değişimi veya monitör değişikliği her
karede algılanarak yakalama yüzeyleri yeniden oluşturulur. DPI manifesti
Per-Monitor V2 olarak tanımlanmış, masaüstü metinleri için JPEG kalitesi
yükseltilmiştir.

- Test bağlantısı: `https://rotaniz.com/downloads/RotaLink-v1.1.0-alpha.19-UNSIGNED-DEVELOPMENT.exe`
- Dosya boyutu: `201.216` bayt
- SHA-256: `30e92a4e2887403e44356ad67f012d7065df6b32694e68c67890e666e82b3f14`
- İmzalı kararlı müşteri dosyası bu test sürümüyle değiştirilmez.

## 1.1.0-alpha.18 test sürümü

`alpha.17` tanılama kayıtları SYSTEM helper ve `SendInput` zincirinin artık
başarıyla çalıştığını (`system-helper-ok`) doğruladı. Kalan gecikmenin nedeni,
operatör tarayıcısının fare hareketlerini IPC yanıt hızından daha hızlı üretmesi
ve tıklama paketlerinin biriken hareket paketleri arkasında kalmasıydı.

`alpha.18`, aynı anda yalnızca bir fare hareketi gönderir; bekleyen hareketleri
biriktirmek yerine sadece en güncel konumu saklar. Her input paketine ACK
dönülür. Fare/klavye bırakma olayları hız sınırına takılmaz; böylece sürükleme
veya basılı tuş durumunda kalma engellenir.

- Test bağlantısı: `https://rotaniz.com/downloads/RotaLink-v1.1.0-alpha.18-UNSIGNED-DEVELOPMENT.exe`
- Beklenen kontrol durumu: `system-helper-ok`
- İmzalı kararlı müşteri dosyası bu test sürümüyle değiştirilmez.

Müşterinin kurulum gerektirmeden çalıştırdığı Windows istemcisi aşağıdaki sabit adreste yayınlanır:

`https://rotaniz.com/downloads/RotaLink.exe`

Canlı WordPress temasındaki indirme düğmesi `website/rotaniz-download-button.php` örneğini kullanır. Yayın dosyası `artifacts/RotaLink.exe` ile aynı derlemedir.

## Güncelleme kontrol listesi

1. Taşınabilir istemciyi `RotaLink.exe` adıyla üretin.
2. Dosyayı `public_html/downloads/RotaLink.exe` üzerine yazın.
3. Ana sayfadaki düğmenin `/downloads/RotaLink.exe` adresini gösterdiğini doğrulayın.
4. Yayınlanan dosyanın boyutunu ve SHA-256 özetini sürüm notuna ekleyin.

## 1.1.0-alpha.14

`alpha.14`, kullanıcı-tokenlı SessionHelper'ın SYSTEM sahipli günlük dosyasına erişemediği ve pipe ACL'sinde LocalSystem'e özel `WTSQueryUserToken` kullandığı için sürekli kapanmasını düzeltir. Helper günlüğü `%LOCALAPPDATA%` altındadır ve birleşik tanılamaya eklenir.

- Kontrol sunucusu sürümlü bağlantısı: `https://45.87.173.201.nip.io/downloads/RotaLink-v1.1.0-alpha.14.exe`

## 1.1.0-alpha.15 dağıtım kapısı

alpha.15 kaynak kodu UIAccess güven zincirine geçirilmiştir. Bu sürüm yalnızca Service, SessionHelper ve ana istemci güvenilir Authenticode sertifikasıyla imzalandıktan sonra web sitesine veya GitHub sürümlerine yüklenebilir. `RotaLink-UNSIGNED-DEVELOPMENT.exe` müşteri dosyası değildir ve dağıtılmamalıdır.

İmzalı üretim çıktısı `scripts\\build-signed-client.ps1` ile oluşturulur. Kod imzalama sertifikası bulunmadığı sürece sitedeki kararlı bağlantı alpha.14 üzerinde kalır; böylece imzasız bir dosya yanlışlıkla müşterilere sunulmaz.
- Sabit bağlantı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Beklenen helper başlangıcı: `Identity=<etkileşimli kullanıcı>, UIAccess=True`
- Beklenen IPC kaydı: `Privileged SessionHelper input IPC connected`

## 1.1.0-alpha.13

`alpha.13`, SessionHelper'ı SYSTEM servis tokenından değil `WTSQueryUserToken` ile aktif kullanıcının gerçek etkileşimli logon tokenından üretir. SYSTEM servis bu tokena doğrulanmış `TokenUIAccess=1` ekleyerek helper'ı `WinSta0` üzerinde başlatır.

- Kontrol sunucusu sürümlü bağlantısı: `https://45.87.173.201.nip.io/downloads/RotaLink-v1.1.0-alpha.13.exe`
- Sabit bağlantı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Beklenen helper başlangıcı: `Identity=<etkileşimli kullanıcı>, UIAccess=True`
- Beklenen başarılı kontrol durumu: `system-helper-ok`

## 1.1.0-alpha.12

`alpha.12`, SYSTEM servisinin etkileşimli SessionHelper tokenına `TokenUIAccess=1` atamasını ve başlatılan helper içinde bu bayrağın tekrar doğrulanmasını ekler. Amaç, `alpha.11` birleşik günlüğünde SYSTEM/Session 1/WinSta0/aktif desktop zinciri doğru olduğu hâlde `SendInput` çağrısının UIPI tarafından `ERROR_ACCESS_DENIED (5)` ile reddedilmesini gidermektir.

- Kontrol sunucusu sürümlü bağlantısı: `https://45.87.173.201.nip.io/downloads/RotaLink-v1.1.0-alpha.12.exe`
- Sabit bağlantı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Beklenen helper başlangıcı: `Identity=NT AUTHORITY\\SYSTEM, UIAccess=True`
- Beklenen başarılı kontrol durumu: `system-helper-ok`

## 1.1.0-alpha.11

`alpha.11`, SYSTEM SessionHelper sürecini input iş parçacığı başlamadan önce etkileşimli `WinSta0` pencere istasyonuna bağlar. Named-pipe tekrar bağlantılarında sıra numarası yeniden başlayabildiği için geçerli girdileri reddeden süreç-geneli replay kontrolü bağlantı kapsamına alınmıştır. Helper cevap protokolü gerçek hata aşamasını ve Win32 hata kodunu operatöre taşır.

- Kontrol sunucusu sürümlü bağlantısı: `https://45.87.173.201.nip.io/downloads/RotaLink-v1.1.0-alpha.11.exe`
- Sabit bağlantı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Beklenen başarılı kontrol durumu: `system-helper-ok`

## 1.1.0-alpha.10

Windows Service Control Manager kayıt adı ile gömülü servis dispatch adı arasındaki uyuşmazlık giderildi. Servis artık gerçekten `SERVICE_RUNNING` durumuna ulaşmadan başarılı sayılmıyor. Bu düzeltme SYSTEM oturum helper'ının başlamasını ve kontrol paketlerinin UIPI tarafından engellenen ana süreç yerine yetkili helper üzerinden uygulanmasını sağlar.

`alpha.9`, yüksek DPI/ekran ölçeklemesinde kesilen sürüm etiketini esnek alt bilgi yerleşimiyle düzeltir. İstemci penceresi SYSTEM kontrol motorunun başlangıç sonucunu gösterir. “Tanılama günlüğü (tümü)” bağlantısı kullanıcı uygulaması, SYSTEM servisi ve oturum helper günlüklerini tek dosyada birleştirir. SYSTEM servisi çalışırken helper IPC kurulamıyorsa uygulama artık yerel UIPI yoluna düşmez ve `system-helper-ipc-unavailable` sonucunu verir.

`alpha.10`, named pipe üzerinde desteklenmeyen `ReadTimeout`/`WriteTimeout` özelliklerinin ilk input paketinde uzak oturumu kapatmasını düzeltir. Acknowledgement okuması asynchronous pipe üzerinde iki saniyelik deadline kullanır ve IPC hatası oturum çökmesi yerine açık kontrol sonucu olarak raporlanır.

- Kontrol sunucusu sabit bağlantısı: `https://45.87.173.201.nip.io/downloads/RotaLink.exe`
- Kontrol sunucusu sürümlü bağlantısı: `https://45.87.173.201.nip.io/downloads/RotaLink-v1.1.0-alpha.10.exe`
- Dosya boyutu: `187.904` bayt
- SHA-256: `37ca4ff6270afdead8f8e25da8fcf308d8d8359ece4364b7b2ed978e0574817a`

`rotaniz.com` üzerindeki sabit dosya, cPanel/hosting aktarımı tamamlandıktan sonra aynı SHA-256 değerini vermelidir.

İstemci bağlantı tanılama günlüğünü `%LocalAppData%\RotaLink\rotalink.log` dosyasına; SYSTEM servis ve helper günlüklerini `%ProgramData%\RotaLink\Logs` dizinine yazar.
