# RotaLink native müşteri istemcisi mimarisi

## Ürün sözleşmesi

- Müşteri yalnız `RotaLink.exe` dosyasını indirip açar.
- .NET, Visual C++ Redistributable, Java, tarayıcı eklentisi veya sürücü kurulmaz.
- İndirilen paket x64, Authenticode imzalı, tek EXE ve en fazla 10 MB olur.
- Uygulama açılınca destek kodu ve paylaşım otomatik başlar; pencere kapanınca oturum biter.
- UAC istemi yalnız Windows'un kontrol çalışma zamanını başlatmak için gösterilebilir.
- Uygulama gizli çalışmaz ve kullanıcının başlatmadığı bir oturumu kabul etmez.

## Tek ikili, üç çalışma kipi

| Kip | Başlatan | Görev |
|---|---|---|
| Etkileşimli UI | Kullanıcı | Destek kodu, durum, günlük ve oturum yaşam döngüsü |
| `--service` | Windows Service Control Manager | WTS oturum takibi ve yetkili helper yönetimi |
| `--helper --session N` | Servis | Aktif kullanıcı oturumunda DXGI yakalama ve `SendInput` |

Windows servisi bir EXE'yi diskten çalıştırmak zorundadır. İndirilen dosya kullanıcı tarafından
yazılabilir `Downloads` dizininde servis olarak bırakılmaz. UI, UAC onayından sonra aynı imzalı
EXE'nin hash'i doğrulanmış çalışma kopyasını `%ProgramFiles%\Rotaniz\RotaLink\Runtime\<sürüm>`
dizinine atomik olarak yerleştirir ve hizmeti bu korumalı yoldan başlatır. Bu işlem kullanıcıdan
ayrı bir paket veya kurulum sihirbazı istemez. Sürüm yükseltme ve geri alma aynı imzalı tek EXE
tarafından yönetilir.

## Yerel modüller

```text
RotaLink.exe
├── Native UI / DPI / tek örnek
├── CNG P-256 cihaz kimliği
├── WinHTTP HTTPS + iki WebSocket
│   ├── control: güvenilir ve öncelikli input
│   └── video: kuyruğu 1 olan düşürülebilir kareler
├── Windows Service / WTS oturum yöneticisi
├── Interactive helper
│   ├── OpenInputDesktop + SendInput
│   ├── DXGI Desktop Duplication
│   └── Media Foundation H.264
└── Sürümlü named-pipe / shared-memory IPC
```

Native kod yalnız Windows'un parçası olan Win32, WinHTTP, CNG, DXGI, D3D11 ve Media
Foundation API'lerini kullanır. Modern API giriş noktaları Server 2012'de bulunmayabiliyorsa
`GetProcAddress` ile dinamik çözülür ve eski karşılığına geri dönülür.

## Derleme ve bağımlılık kapıları

- MSVC `/MT` statik CRT kullanılır; müşteri makinesinde VC++ Runtime aranmaz.
- PE32+ makine tipi `AMD64` olmalıdır.
- PE CLR veri dizini sıfır olmalıdır; managed assembly kabul edilmez.
- `dumpbin /DEPENDENTS` çıktısında `VCRUNTIME`, `MSVCP` veya `UCRTBASE` bulunamaz.
- EXE 10 MB sınırını aşarsa CI yayını durdurur.
- Windows SDK ve CMake yalnız geliştirici/CI makinesinin aracıdır; müşteriye gitmez.

## Geçiş sırası

1. Native pencere, platform denetimi, günlük ve sunucu sağlık bağlantısı.
2. CNG cihaz kaydı, challenge imzası, 9 haneli kod ve çift WinHTTP WebSocket.
3. Aynı EXE'nin service/helper kipleri, güvenli named-pipe ACL ve aktif WTS oturumu.
4. Mevcut C++ DXGI/H.264 motorunun helper kipine doğrudan bağlanması.
5. Atomik `SendInput`, koordinat dönüşümü, klavye ve basılı tuş temizliği.
6. Windows 10/11 ve Server 2012–2025 VM kabul matrisi.
7. Authenticode imza, güvenli güncelleme ve pilot yayın.

Eski C# Alpha hattı davranış karşılaştırması için kaynakta kalır; müşteri paketi veya native
istemcinin yanına konulan bir çalışma zamanı değildir.
