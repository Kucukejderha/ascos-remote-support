# RotaLink Windows test laboratuvarı

Bu laboratuvar, bir Windows sürümünün yalnız açılışını değil RotaLink destek
sözleşmesinin tamamını kanıtlamak için kullanılır.

## Makine hazırlığı

- Her hedef için temiz x64 VM ve geri dönülebilir snapshot kullanın.
- Sunucu kurulumlarında **Desktop Experience** seçin. Server Core destek dışıdır.
- Server 2012/R2 imajında geçerli ESU, bütün sistemlerde güncel güvenlik yamaları ve
  .NET Framework 4.8 bulunmalıdır.
- Konsol ve RDP testlerini ayrı snapshot/rapor olarak saklayın. Matris kapısına aktif
  etkileşimli oturum raporu verilir.
- Alpha paketi yalnız kontrollü laboratuvarda ve yönetici onayıyla çalıştırılır.

## Otomatik ön kontrol

Geliştirici, tek dosyalık laboratuvar paketini şu komutla üretir:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-compatibility-kit.ps1
```

Oluşan `artifacts/RotaLink-Alpha26-Uyumluluk-Test-Kiti.zip` hedef VM'ye kopyalanıp
açılır. Depo kurmadan ön kontrol başlatmak için örneğin:

```cmd
RotaLink-Uyumluluk-Testi.cmd server-2019
```

JSON raporu masaüstündeki `RotaLink-Test-Sonuclari` klasörüne yazılır.

Yönetici PowerShell penceresinde depo kökünden çalıştırın:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Test-RotaLinkCompatibility.ps1 `
  -TargetId server-2019 `
  -OutputPath C:\RotaLink-Test\rotalink-compatibility-server-2019.json
```

Geçerli hedef kimlikleri `tests/windows-compatibility-matrix.json` dosyasındadır.
Prob makineyi değiştirmez; OS/.NET/oturum/ekran/GPU bilgilerini ve RotaLink sunucusu
sağlık erişimini kaydeder. Çıkış kodu `0` uygun, `2` en az bir P0 engeli demektir.

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
