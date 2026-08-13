# RotaLink Windows uyumluluk ve ürünleştirme yol haritası

Bu belge RotaLink'in hangi sistemlerde hangi koşullarla destekleneceğini ve geliştirme
fazlarının hangi kabul ölçütleriyle tamamlanacağını tanımlar. Bir platform yalnız EXE
açıldığı için desteklenmiş sayılmaz; görüntü, giriş, oturum, DPI, güvenlik ve yeniden
bağlantı testlerinin tamamını geçmelidir.

## 1. Destek sözleşmesi

### Üretim hedefi

| İşletim sistemi | Kurulum türü | Mimari | İlk dağıtım koşulu | Hedef durum |
|---|---|---:|---|---|
| Windows 11 | İstemci masaüstü | x64 | Güncel güvenlik yamaları | Birincil |
| Windows 10 22H2 / destekli LTSC veya ESU | İstemci masaüstü | x64 | Güncel güvenlik yamaları | Uyumlu |
| Windows Server 2025 / 2022 | Desktop Experience | x64 | Güncel güvenlik yamaları | Birincil |
| Windows Server 2019 | Desktop Experience | x64 | .NET Framework 4.8 | Uyumlu |
| Windows Server 2016 | Desktop Experience | x64 | .NET Framework 4.8 | Uyumlu |
| Windows Server 2012 / 2012 R2 | Desktop Experience | x64 | .NET Framework 4.8 ve ESU | Eski sistem / sınırlı |

Windows Server Core geleneksel etkileşimli masaüstü içermediği için bu ürünün uzaktan
masaüstü hedefi değildir. Çok oturumlu RDS sistemlerinde yalnız kullanıcının açıkça
başlattığı aktif oturum kontrol edilir; başka bir oturuma sessiz geçiş yapılmaz.

Microsoft'un dağıtım tablosuna göre Server 2012'de 4.5, 2012 R2'de 4.5.1,
Server 2016'da 4.6.2 ve Server 2019'da 4.7.2 hazır gelir. Bu sistemlerin tümü 4.8'e
yükseltilebilir. Mevcut Alpha hattı `net48` olduğundan eski sunucularda 4.8 önkoşuldur.
Güvenlik güncellemesi almayan 4.5'e geri hedefleme yapılmayacaktır.

## 2. Destek seviyeleri

- **Doğrulandı:** Gerçek veya temiz sanal makinede tüm P0/P1 testleri geçti.
- **Uyumluluk adayı:** API ve derleme düzeyinde destekleniyor, gerçek sistem matrisi
  henüz tamamlanmadı.
- **Eski sistem / sınırlı:** Yalnız güncel ESU ve 4.8 bulunan Desktop Experience
  kurulumunda test edilir; platform üreticisinin yaşam döngüsü ayrıca geçerlidir.
- **Destek dışı:** Server Core, Windows 8/8.1, Windows 7, ARM64 ve güvenlik güncellemesi
  almayan yapılandırmalar.

## 3. Zorunlu kabul matrisi

Her işletim sistemi için aşağıdaki testlerin sonucu sürüm kanıtı olarak saklanır:

1. Temiz sistemde başlatma, tek örnek kilidi, UAC ve kapatınca servis/helper temizliği.
2. Konsol oturumu ve RDP oturumunda ilk kare süresi, yeniden bağlanma ve oturum geçişi.
3. Fare hareketi, tek/çift/sağ tık, sürükleme, tekerlek ve klavye.
4. Masaüstü simgesi seçme/açma, bağlam menüsünü boşluğa tıklayarak kapatma, görev
   çubuğundan küçültme/geri açma ve Başlat menüsü.
5. Standart kullanıcı, yükseltilmiş uygulama, UAC secure desktop ve kilit/açma.
6. %100/%125/%150/%200 DPI; 1366x768, 1920x1080, 4K; negatif koordinatlı iki monitör;
   dikey ve yatay ekran.
7. DXGI birincil yakalama, desteklenmeyen GPU/RDP durumunda GDI geri dönüşü ve
   çözünürlük değişikliği.
8. 30 ve 60 dakikalık dayanıklılık; ağ kesintisi, 1/5/20 Mbps sınırı, gecikme ve paket
   kaybı altında input önceliği.
9. Authenticode, indirme özeti, servis/named-pipe ACL, günlükte sır veya token
   bulunmaması ve sürüm geri alma testi.

Bir P0 testi başarısızsa sürüm yayınlanmaz. API'nin `true` döndürmesi başarı kanıtı
değildir; ekrandaki beklenen durum değişikliği otomatik veya görüntülü olarak
doğrulanmalıdır.

## 4. Geliştirme fazları

### Faz 0 — Platform sözleşmesi ve tanılama

**Çıktı:** Kesin OS sürümü (`RtlGetVersion`), istemci/sunucu ayrımı, Desktop
Experience/Server Core, mimari, .NET 4.8 release ve oturum bilgilerinin günlüklenmesi;
destek dışı platformda açık hata.

**Bitiş ölçütü:** Desteklenen altı OS ailesinde rapor doğru; Server Core ve Windows
8.1 destek dışı olarak tanımlanıyor.

### Faz 1 — Tekrarlanabilir Windows test laboratuvarı

**Çıktı:** Windows 10, 11, Server 2012 R2, 2016, 2019, 2022/2025 temiz VM şablonları;
yerel konsol ve RDP senaryolarını çalıştıran imzalı test paketi; sonuç JSON'u ve ekran
kaydı.

**Bitiş ölçütü:** Her commit için derleme; sürüm adayı için tüm VM matrisinde P0/P1.
Server 2012/R2 yalnız ESU'lu imajda çalıştırılır.

### Faz 2 — Girdi motorunun kararlılaştırılması

**Çıktı:** Service + aktif kullanıcı tokenlı SessionHelper; dinamik
`OpenInputDesktop`; atomik `SendInput`; Explorer görev çubuğu/masaüstü için sürüme
duyarlı UI Automation; input ACK'nin görünür durum değişikliğiyle doğrulanması.

**Bitiş ölçütü:** Kabul matrisinin 3–5. maddeleri tüm hedef sistemlerde 100 ardışık
işlemde hatasız; Alpemix/AnyDesk gibi başka bir ürünle oturumun “hazırlanmasına” ihtiyaç
yok.

### Faz 3 — Görüntü ve ekran topolojisi

**Çıktı:** DXGI Desktop Duplication birincil motoru ürün akışına bağlanır; GDI yalnız
ölçümlü geri dönüş olur. Kirli bölgeler, donanım H.264, imleç şekli, ekran döndürme,
çoklu monitör ve `DXGI_ERROR_ACCESS_LOST` yeniden başlatma tamamlanır.

**Bitiş ölçütü:** 1080p masaüstünde hedef 30 FPS, input sırasında p95 uçtan uca
gecikme 150 ms altında; video kuyruğu en fazla bir kare.

### Faz 4 — Taşıma ve bağlantı kalitesi

**Çıktı:** Input ve video ayrı bağlantı/kuyruk; P2P WebRTC/ICE/STUN, gerektiğinde TURN
relay; adaptif bit hızı ve kalite profilleri; bağlantı telemetrisi.

**Bitiş ölçütü:** Video doygunluğunda input bloke olmuyor; ağ kesintisinde oturum ve
basılı tuş/fare durumu güvenli biçimde toparlanıyor.

### Faz 5 — Dağıtım, imza ve .NET bağımsız taşınabilir istemci

**Kısa vade:** Küçük yerel bootstrapper .NET 4.8, x64, Desktop Experience, TLS ve
sertifika zincirini uygulama açılmadan denetler; eksik önkoşulu Türkçe açıklar.

**Ürün hedefi:** UI, servis, helper ve yakalama motoru statik CRT kullanan Win32/C++
paketine taşınır. Böylece Server 2012–2025 ve Windows 10–11 için .NET kurulumu veya
self-contained .NET gömme zorunluluğu kalmaz.

**Bitiş ölçütü:** İmzalı tek EXE, 10 MB altı hedef boyut, SmartScreen itibarı, atomik
güncelleme ve geri alma; paket içeriği SBOM ve SHA-256 ile yayınlanır.

### Faz 6 — Pilot ve kararlı sürüm

**Çıktı:** Önce iç kullanım, ardından seçili müşteriler, son olarak genel dağıtım.
Çökme, bağlantı kurma oranı, ilk kare, input p95 ve geri dönüş oranı sürüm panosunda
izlenir.

**Bitiş ölçütü:** En az 30 gün pilot; P0 yok; oturum kurma başarısı en az %99; manuel
GDI geri dönüş nedenleri sınıflandırılmış; güvenlik incelemesi tamamlanmış.

## 5. Uygulama sırası

1. Alpha.25 çalışan kontrol tabanı olarak dondurulur; Faz 0 platform denetimli Alpha.26 test adayı olarak üretilir.
2. Faz 1 VM matrisi kurulmadan yeni input yaklaşımı “çözüldü” sayılmaz.
3. Faz 2 girdi doğrulanır; ardından Faz 3 ve Faz 4 paralel geliştirilebilir.
4. Authenticode sertifikası Faz 2 sonuna kadar temin edilir; imzasız paket yalnız
   kontrollü geliştirme testinde kalır.
5. Kararlı müşteri bağlantısı yalnız Faz 5 dağıtım kapısı geçildiğinde güncellenir.

## 6. Kaynaklar

- Microsoft .NET Framework / Windows Server sürüm tablosu:
  https://learn.microsoft.com/dotnet/framework/install/on-server-2019
- Desktop Duplication API:
  https://learn.microsoft.com/windows-hardware/drivers/display/desktop-duplication-api
- Server Core ve Desktop Experience farkı:
  https://learn.microsoft.com/windows-server/administration/server-core/what-is-server-core
- Windows Server 2012/R2 ESU:
  https://learn.microsoft.com/windows-server/get-started/extended-security-updates-overview
