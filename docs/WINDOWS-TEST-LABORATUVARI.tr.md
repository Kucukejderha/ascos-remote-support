# RotaLink Windows test laboratuvarı

Bu laboratuvar, bir Windows sürümünün yalnız açılışını değil RotaLink destek
sözleşmesinin tamamını kanıtlamak için kullanılır.

## Makine hazırlığı

- Her hedef için temiz x64 VM ve geri dönülebilir snapshot kullanın.
- Sunucu kurulumlarında **Desktop Experience** seçin. Server Core destek dışıdır.
- Server 2012/R2 imajında geçerli ESU ve bütün sistemlerde güncel güvenlik yamaları
  bulunmalıdır. Native müşteri istemcisi için .NET veya VC++ Runtime kurulmaz.
- Konsol ve RDP testlerini ayrı snapshot/rapor olarak saklayın. Matris kapısına aktif
  etkileşimli oturum raporu verilir.
- İmzasız native önizleme yalnız kontrollü laboratuvarda ve yönetici onayıyla çalıştırılır.

## Otomatik ön kontrol

Geliştirici, tek dosyalık laboratuvar paketini şu komutla üretir:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-compatibility-kit.ps1 `
  -NativeClientPath C:\RotaLink-Build\RotaLink.exe `
  -Version 1.2.0-native.7
```

Paketleyici önce PE dosyasının x64, CLR içermeyen ve 10 MB altında bir native istemci
olduğunu doğrular. Oluşan `RotaLink-1.2.0-native.7-Uyumluluk-Test-Kiti.zip` hedef VM'ye
kopyalanıp açılır; müşterinin sistemindeki .NET sürümü yalnız raporlanır ve sonuç kapısı değildir.
Depo kurmadan ön kontrol başlatmak için örneğin:

```cmd
RotaLink-Uyumluluk-Testi.cmd server-2019
```

JSON raporu masaüstündeki `RotaLink-Test-Sonuclari` klasörüne yazılır.
Raporun `schemaVersion` değeri `2` olmalıdır. `client` bölümü test edilen EXE'nin tam
sürümünü, boyutunu ve SHA-256 karmasını; `diagnostics` bölümü çalışan süreçleri, geçici
servis durumunu ve EXE'nin yanındaki birleşik native günlüğün son 200 satırını içerir. Kontrol
denemesinden hemen sonra, RotaLink hâlâ açıkken bu rapor üretilmelidir. `schemaVersion: 1`
veya `dotnet-48` kontrolü içeren bir rapor eski managed test kitine aittir ve native istemci
arızasını teşhis etmek için kullanılmaz.

Yönetici PowerShell penceresinde depo kökünden çalıştırın:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Test-RotaLinkCompatibility.ps1 `
  -TargetId server-2019 `
  -OutputPath C:\RotaLink-Test\rotalink-compatibility-server-2019.json
```

Geçerli hedef kimlikleri `tests/windows-compatibility-matrix.json` dosyasındadır.
Prob makineyi değiştirmez; OS/oturum/ekran/GPU bilgilerini, yüklü .NET sürümünü yalnız
bilgi amaçlı ve RotaLink sunucusu sağlık erişimini kaydeder. Native istemci için .NET
kontrolü yayın engeli değildir. Çıkış kodu `0` uygun, `2` en az bir P0 engeli demektir.

Her VM'deki JSON raporunu tek bir `compatibility-results` klasöründe toplayın:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Test-RotaLinkCompatibilityMatrix.ps1 `
  -ResultsDirectory C:\RotaLink-Test\compatibility-results
```

Sekiz hedef raporunun tamamı yoksa sonuç `Incomplete`, P0 arızası veya yanlış OS raporu
varsa `Fail` olur. Yerel geliştirme sırasında eksik raporları yalnız görünür kılmak için
`-AllowIncomplete` kullanılabilir; bu seçenek sürüm yayınlama yetkisi vermez.

## Her VM'de manuel P0/P1 senaryoları

1. RotaLink açılır, yalnız bir örnek çalışır ve doğru tam sürüm görünür.
2. Operatör bağlanır; ilk kare ve gerçek ekran oranı doğrulanır.
3. Tek/çift/sağ tık, sürükleme, tekerlek ve klavye denenir.
4. Masaüstü simgesi seçilir/açılır; sağ tık menüsü boşluğa tıklayınca kapanır.
5. En az iki farklı uygulama görev çubuğundan küçültülüp geri açılır.
6. Başlat menüsü açılır ve boş masaüstü tıklamasıyla kapanır.
7. Standart ve yönetici uygulamalarında kontrol; UAC; kilitle/aç; RDP kes/bağlan denenir.
8. %100 ve %150 DPI ile kaynak ekranın dört kenarı ve görev çubuğu görünür olmalıdır.
9. İstemci kapatıldığında operatör en geç 10 saniyede bağlantının bittiğini göstermelidir.
10. Başka bir uzaktan erişim ürünü açılmadan bütün senaryolar tekrar geçmelidir.

Kanıt klasöründe uyumluluk JSON'u, RotaLink birleşik tanılama kaydı, ekran kaydı ve test
eden kişinin tarihli sonucu bulunur. Bir P0 başarısızlığı sürüm adayını durdurur.

Native.4 ile ana uygulama, geçici SYSTEM servisi ve etkileşimli helper bütün tanılama
satırlarını müşterinin çalıştırdığı `RotaLink.exe` dosyasının yanındaki tek
`RotaLink-Native.log` dosyasına yazar. EXE klasöründe yazma izni yoksa uygulama bunu açılışta
görünür hata olarak bildirir; başka bir profile veya Program Files altına sessiz log bırakmaz.

Native.5 testinde ilk uzak input sonrasında günlükte
`Native input IPC client authenticated by kernel identity` satırı görülmelidir. Bu satırdaki
PID ana `RotaLink` sürecinin PID'siyle, `Session` ise aktif etkileşimli oturumla aynı olmalıdır.
`Rejected input pipe client` görülürse satırdaki `Stage`, `ExpectedPid`, `ActualPid`,
`PipeSession`, `HelperSession` ve `Win32` alanlarının tamamı hata raporuna eklenmelidir.

Native.6 testinde masaüstü simgesi, görev çubuğundaki küçültülmüş uygulama ve sabitlenmiş
kısayol ayrı ayrı tıklanmalıdır. Her denemede `Native physical click target` ve hemen ardından
`Stage=native-physical-click-ok` görülmelidir. Hedef sınıfın (`SysListView32`,
`MSTaskListWClass`, `Shell_TrayWnd` veya gözlenen başka sınıf) bulunduğu satır test sonucuna
eklenmelidir. API sonucu başarılı olduğu halde arayüz değişmiyorsa bu gözlem ayrıca açıkça
belirtilmelidir; yalnız `Accepted=true` kullanıcı davranışının gerçekleştiği kanıtı sayılmaz.

Native.7 testinde masaüstü tek tıklaması için `Stage=native-desktop-selection-ok`, aynı simgeye
hızlı ikinci tıklama için `Stage=native-desktop-invoke-ok`, görev çubuğu uygulaması veya
kısayolu için `Stage=native-taskbar-invoke-ok` beklenir. `native-*-pattern-unavailable`,
`native-*-failed` veya `shell-*-failed` aşamalarından biri görülürse ilgili `Error`, `Class`
ve `Name` alanlarıyla birlikte raporlanmalıdır. Otomasyon başarı satırına ek olarak hedef
arayüzdeki gerçek seçme/açma davranışı da gözle doğrulanmalıdır.
