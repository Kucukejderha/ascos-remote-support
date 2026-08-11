# RotaLink kontrol sorunu: kesin kök neden ve düzeltme

Tarih: 11 Ağustos 2026  
Düzeltilen sürüm: `1.1.0-alpha.8`

## Kanıt zinciri

1. Operatörün fare koordinatları hedef bilgisayara ulaşıyor ve imleç doğru yere taşınıyordu. Bu, tarayıcı olaylarının, signaling sunucusunun, kontrol WebSocket kanalının ve koordinat dönüşümünün çalıştığını gösterir.
2. Hedef günlüklerinde yerel `SendInput` çağrıları `Sent=0/1, Win32Error=5` ve `sendinput-blocked-by-uipi` sonuçları veriyordu. Bu, olayın Windows input enjeksiyonu sınırında reddedildiğini gösterir.
3. `1.1.0-alpha.7` içinde bu sınırı aşmak için gömülü geçici SYSTEM servisi ve etkileşimli oturum helper'ı bulunuyordu; fakat helper IPC bağlantısı hiç kurulmuyordu.
4. Kod incelemesinde taşınabilir başlatıcının servisi `RotaLinkInputRuntime` adıyla SCM'ye kaydettiği, servis ikilisinin ise `StartServiceCtrlDispatcher` ve `RegisterServiceCtrlHandlerEx` çağrılarında `RotaLinkRemoteSupport` adını kullandığı bulundu.
5. Windows Service Control Manager, kayıtlı servis adı ile dispatch tablosundaki ad farklı olduğunda servis ikilisini ilgili servis olarak kabul etmez. Bu nedenle `StartService` çağrısı ilk anda başarılı görünse bile servis hemen duruyor, SYSTEM helper başlamıyor ve uygulama UIPI tarafından engellenen yerel yola düşüyordu.

## Uygulanan net çözüm

- SCM kayıt adı ve servis dispatch adı `RotaLinkInputRuntime` olarak eşitlendi.
- Başlatıcı artık yalnızca `StartService` çağrısının dönüşüne güvenmiyor; servis `SERVICE_RUNNING` durumuna ulaşana kadar bekliyor.
- Servis başlangıçta durursa gerçek `Win32ExitCode` ve `ServiceSpecificExitCode` günlüğe yazılıyor; sahte “servis başladı” sonucu oluşmuyor.
- SYSTEM servis aktif konsol oturumunda `RotaLink.SessionHelper.exe` başlatıyor.
- Operatör input paketleri ayrı kontrol WebSocket'inden ana istemciye, oradan oturuma özel named pipe üzerinden SYSTEM helper'a ve son olarak aktif input desktop üzerinde `SendInput` API'sine gidiyor.
- Paketlenmiş EXE içinde hem `RotaLink.Service.exe` hem `RotaLink.SessionHelper.exe` bulunduğu ve iki derlenmiş ikilinin servis sözleşmesinin aynı olduğu otomatik olarak doğrulandı.

## Doğrulama ölçütü

Başarılı bir `alpha.8` çalışmasında tanılama günlüğünde şu sıra görülmelidir:

1. `Temporary SYSTEM input service is RUNNING`
2. `Privileged SessionHelper input IPC connected`
3. Operatör girdisinde `Stage=system-helper-ok`

Bu üç aşamadan biri yoksa artık hata başarı gibi gösterilmez; tam aşama ve Windows hata kodu kayda geçirilir.

### alpha.9 tanılama iyileştirmesi

Yüksek DPI ölçeklemesinde sürüm bilgisinin kesilmesi esnek alt bilgi yerleşimiyle giderildi. Bu görsel hata kontrol sorununun nedeni değildir. İstemci artık SYSTEM kontrol motorunun hazır olup olmadığını başlıkta gösterir ve “Tanılama günlüğü (tümü)” bağlantısıyla kullanıcı, servis ve helper günlüklerini tek dosyada toplar. Servis çalıştığı halde named pipe kurulamıyorsa yerel `SendInput` yedeğine düşülmez; `system-helper-ipc-unavailable` sonucu açıkça raporlanır.

### alpha.10 düzeltmesi

`alpha.9` birleşik günlüğü SYSTEM servisinin çalıştığını, ancak ilk input paketinde `NamedPipeClientStream.ReadTimeout` özelliğinin desteklenmemesi nedeniyle istemcinin `InvalidOperationException` ile oturumu kapattığını kanıtladı. Desteklenmeyen stream timeout özellikleri kaldırıldı. Helper acknowledgement okuması asynchronous named pipe üzerinde iki saniyelik açık deadline ile uygulanarak pipe arızasının tüm uzak oturumu düşürmesi engellendi.

### alpha.11 düzeltmesi

`alpha.10` günlüğü kontrol paketlerinin named pipe üzerinden SYSTEM helper'a ulaştığını, ancak helper'ın tüm olaylara ayrıntısız `false` döndürdüğünü gösterdi. Kod incelemesinde iki bağımsız hata bulundu:

1. Tekrarlı paket korumasındaki sıra numarası helper süreci boyunca global tutuluyordu. Pipe yeniden bağlandığında istemci sıra numarasını yeniden `1` ile başlattığı için geçerli girdiler `sequence-rejected` olarak sessizce reddedilebiliyordu. Sıra denetimi pipe bağlantısı kapsamına taşındı.
2. Helper `winsta0\\default` başlangıç bilgisiyle oluşturulsa da süreç pencere istasyonunu açıkça etkileşimli `WinSta0` istasyonuna bağlamıyordu. `OpenWindowStation("WinSta0")` ve `SetProcessWindowStation` başlangıçta, input iş parçacığı oluşturulmadan önce uygulanıyor; her olayda mevcut input desktop yine `OpenInputDesktop` ve `SetThreadDesktop` ile yenileniyor.

Helper cevap protokolü `v2` oldu. Artık sonuçla birlikte `sequence-rejected`, `open-input-desktop-failed`, `set-thread-desktop-failed`, `sendinput-failed` gibi kesin aşama ve Win32 hata kodu operatör ekranına taşınır. Böylece `system-helper-rejected / Error=0` biçimindeki tanısız sonuç kaldırıldı.

## Değiştirilmeyen teknoloji için gerekçe

WebSocket veya signaling teknolojisi kök neden değildir. Görüntü ve kontrol olayları hedefe ulaştığı için WebRTC, UDP ya da başka bir taşıma katmanına geçmek `ERROR_ACCESS_DENIED/UIPI` sorununu çözmezdi. Video ve kontrol kanalları zaten ayrıdır. Düzeltme Windows servis/oturum sınırında yapılmıştır.
