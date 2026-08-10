# RotaLink mevcut sistem arıza raporu

Tarih: 10 Ağustos 2026  
İncelenen hat: RotaLink istemci v0.4.3–v0.9.1, ASP.NET signaling/relay ve tarayıcı tabanlı operatör

## Yönetici özeti

Mevcut ürün oturum kurabiliyor, 9 haneli kodu doğrulayabiliyor ve bazı koşullarda ekran karesi aktarabiliyor. Buna rağmen güvenilir bir uzaktan kontrol ürünü değildir. Ana arıza sunucu erişimi veya destek kodu değildir: operatörden gönderilen fare olayları istemciye ulaşmakta, fakat Windows bunların uygulanmasını `ERROR_ACCESS_DENIED (5)` ile reddetmektedir. Ekran yakalama da aynı aktif masaüstü/desktop bağlamı probleminden etkilenmektedir.

Kod tabanı ayrıca iki yarım mimari arasında kalmıştır. Yayındaki akış GDI + JPEG + tek WebSocket + tarayıcı canvas modelini kullanırken depodaki yeni C++ kodu yalnızca DXGI/H.264 yakalama denemesidir. Native operatör, decoder/render, ayrı input kanalının istemci entegrasyonu ve servis–oturum yardımcısı henüz tamamlanmamıştır.

Karar: Beğenilen istemci penceresi ve 9 haneli kod deneyimi korunabilir; ekran yakalama, taşıma ve kontrol motoru mevcut uygulamadan ayrılarak yeniden kurulmalıdır. Eski kontrol motorunu yama yoluyla sürdürmek uygun değildir.

## Kanıtlar

Kullanıcı tarafından iletilen v0.5.1, v0.6.0, v0.6.2 ve v0.7.0 günlüklerinde aşağıdaki ortak sıra görülmektedir:

1. Destek oturumu hazırlanıyor.
2. Host WebSocket bağlantısı kuruluyor.
3. İlk ekran karesi gönderiliyor.
4. Operatörden ilk `move` olayı istemciye ulaşıyor.
5. `SendInput` çağrısı `Sent=0/1, Win32Error=5` sonucu veriyor.
6. İstemci `Accepted=False` yanıtı gönderiyor.
7. Yakalama hattı ayrıca `Screen capture is temporarily unavailable` ve `Erişim engellendi` hatası üretiyor.

Bu sıra, input paketinin tarayıcıdan signaling sunucusuna ve oradan istemciye ulaştığını kanıtlar. Arıza ağdan sonraki Windows desktop/input uygulama katmanındadır.

v0.4.3 ve sonraki günlüklerde ilk ham karenin `2.073.605` bayt olduğu görülmektedir. Bu değer `960 × 540 × 4 + 5` ile aynıdır; ilk kare sıkıştırılmadan tek WebSocket üzerinden gönderilmiştir. Bu da ilk görüntü gecikmesini ve kontrol trafiğinin video arkasında beklemesini açıklamaktadır.

## Kök nedenler

### P0 — Yanlış ve kalıcı Windows desktop bağlamı

Yakalama ve input iş parçacıkları `OpenDesktop("Default")` ile bir kez açılan desktop'a bağlanmaktadır. Bu yaklaşım `OpenInputDesktop` ile o anda gerçekten kullanıcı girdisi alan desktop'ı izlemez; oturum, RDP, kilit ekranı, UAC/secure desktop ve desktop geçişlerinde handle eski veya erişilemez kalabilir.

Sonuçları:

- `SendInput` olayları hata 5 ile reddedilir.
- `BitBlt` yakalaması hata 5 ile kesilir.
- İstemci penceresine tıklamak desktop/focus durumunu değiştirdiğinde geçici bir kare alınabilmesi, yakalamanın tıklamaya bağımlı olduğu izlenimini verir.
- Uygulamanın yönetici olarak başlatılması problemi çözmez; süreç yüksek yetkili olsa bile doğru aktif interactive desktop ve session içinde değilse input uygulanamaz.

### P0 — Görüntü ve input aynı güvenilir WebSocket üzerinde

Eski `legacy` kanalında büyük binary ekran paketleri ile küçük input mesajları aynı sıralı WebSocket'i paylaşır. WebSocket head-of-line blocking nedeniyle eski/gönderilmekte olan bir kare tamamlanmadan sonraki kontrol olayı ilerleyemez. `SemaphoreSlim` de istemci tarafında video ve kontrol yanıtlarını aynı gönderim kilidine sokmaktadır.

Sonuç: Windows input kabul etse bile yoğun veya yavaş bağlantıda kontrol gecikmesi yükselir.

### P0 — Native v1 hattı ürün akışına bağlı değil

`RotaLink.NativeHost` şu anda beş saniyelik bir DXGI → NV12 → H.264 ölçüm/probe programıdır. Sunucu oturumuna bağlanmaz. Depoda native operatör decoder/renderer bulunmaz. Tarayıcı operatörü H.264 paketini çözmek için tasarlanmamıştır ve hâlâ `legacy` kanalını açmaktadır.

Sonuç: Son native commitler yayınlanan kullanıcı deneyimini henüz iyileştirmemektedir.

### P1 — `PostMessage` kontrol yedeği gerçek kullanıcı girdisi değildir

`SendInput` başarısız olduğunda kod, hedef pencereye `PostMessage` göndermeyi denemektedir. Bu yöntem sistem çapında gerçek fare durumunu, capture/focus zincirini, hit testing'i ve Windows kabuğu davranışını üretmez.

Sonuç: Başlat menüsünü açtıktan sonra masaüstüne tıklamanın menüyü kapatmaması gibi tutarsızlıklar oluşur. Bu yedek production kontrol motoru olarak kullanılmamalıdır.

### P1 — Operatör koordinat modeli görüntü geometrisini temsil etmiyor

Tarayıcı koordinatları doğrudan canvas'ın CSS dikdörtgeninden `0..65535` aralığına çevrilmektedir. Protokolde gerçek yakalanan monitörün sanal masaüstündeki `left/top/width/height`, DPI, rotasyon, seçili monitör ve görüntü içindeki letterbox alanı taşınmamaktadır.

Sonuç: Input çalışır hale gelse bile çoklu monitör, farklı en-boy oranı, DPI ölçekleme ve tam ekran durumlarında tıklama hedefi kayabilir. Alt bölüm/görev çubuğunun görünmemesi veya doğru tıklanamaması bu modelle güvenilir biçimde çözülemez.

### P1 — Oturum sağlığı gerçeği yansıtmıyor

Operatör “bağlandı/canlı” durumunu WebSocket açılması veya kare alınmasıyla belirlemektedir. Capture heartbeat, son kare zamanı, input round-trip ve host kapanış nedeni ayrı ayrı izlenmez. İlk input sonucu yalnızca bir kez raporlanır.

Sonuç: İstemci kapandığında, capture durduğunda veya input sürekli reddedildiğinde operatör bir süre bağlantıyı aktif gösterebilir; kullanıcı gerçek arızayı ayırt edemez.

### P2 — Testler gerçek uzaktan kontrolü doğrulamıyor

Mevcut smoke test; kimlik doğrulama, WebSocket relay, paket bütünlüğü ve küçük yerel GDI kontrollerini sınar. İki gerçek interactive Windows oturumu arasında şu kritik davranışları ölçmez:

- fare hareketi ve tıklamanın hedefte gerçekleşmesi,
- klavye ve modifier tuşları,
- Başlat menüsü/focus/capture davranışı,
- görev çubuğunun görüntü ve koordinat doğruluğu,
- DPI ve çoklu monitör,
- istemci kapanınca operatörün kapanması,
- gecikme, FPS, bitrate ve dropped-frame ölçümleri.

Bu nedenle testler yeşil olsa bile ürünün temel işlevi bozuk kalabilmektedir.

## Mevcut bileşenler için karar

| Bileşen | Karar | Gerekçe |
|---|---|---|
| İstemci penceresi/markalama | Koru | Kullanıcı tarafından beğeniliyor; oturum kodu deneyimi yeterli |
| Cihaz kimliği ve 9 haneli kod API'si | Koru ve sertleştir | Temel eşleştirme çalışıyor |
| ASP.NET yetkilendirme sınırı | Koru | Sorunun kaynağı değil |
| GDI + JPEG capture | Değiştir | Gecikmeli, CPU ağırlıklı ve desktop geçişlerinde kararsız |
| Tek `legacy` WebSocket | Kaldır | Video input'u bloke ediyor |
| Tarayıcı canvas operatörü | Kontrol motoru olarak değiştir | Hızlı prototip için uygun, düşük gecikmeli native kontrol için yetersiz |
| `PostMessage`/`mouse_event` yedekleri | Kaldır | Gerçek Windows input semantiği sağlamıyor |
| Native DXGI/H.264 probe | Yeniden kullan | İyi başlangıç; fakat ürün entegrasyonu tamamlanmalı |

## Yeniden yapılandırma öncesi kabul kriterleri

Yeni mimariye geçiş tamamlandı sayılmadan önce iki fiziksel veya bağımsız Windows 10/11 makinede aşağıdakiler ölçülmelidir:

- İlk görüntü p95: en fazla 1 saniye.
- Hareketli masaüstünde en az 20 FPS; hedef 30 FPS.
- Fare tıklaması görsel geri bildirim p95: aynı ağda en fazla 150 ms.
- Video kuyruğu: en fazla bir güncel kare; eski kareler düşürülmeli.
- Input, video kanalından tamamen bağımsız olmalı.
- Başlat menüsü, görev çubuğu, masaüstü ve normal uygulamalar gerçek yerel fareyle aynı davranmalı.
- %100, %125 ve %150 DPI ile koordinat testi geçmeli.
- Tek ve çoklu monitörde tüm yakalanan alan ile input alanı birebir eşleşmeli.
- İstemci penceresi kapanınca operatör en fazla 2 saniyede “bağlantı kapandı” durumuna geçmeli.
- UAC secure desktop desteklenmiyorsa kullanıcıya açıkça gösterilmeli; desteklenecekse imzalı servis + interactive helper modeli kullanılmalı.

## Sonuç

Mevcut hata tek bir koordinat veya JavaScript hatası değildir. Windows interactive session/desktop sahipliği, capture yöntemi, video taşıma ve input taşıma sınırları yanlış kurulmuştur. İstemci arayüzü korunarak altyapının ayrı kanallı, aktif oturum yardımcılı ve ölçülebilir bir mimariyle yeniden oluşturulması gereklidir.

