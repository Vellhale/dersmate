# dersmate (PeerLearn) — çalışma kuralları

Öğrencilerin birbirine ders verdiği akran öğrenme platformu. .NET 8 modüler monolit +
PostgreSQL (modül başına ayrı şema) + Redis, React/Vite arayüz. **İletişim dili Türkçe** —
kod yorumları, commit mesajları, test çıktıları ve kullanıcıya görünen her metin Türkçe.

Bu dosya mimariyi anlatmaz (onun için `docs/`). Burada yalnızca **kodu okuyarak
anlaşılamayacak** olanlar var: ihlali sessiz olan kurallar ve daha önce ısırmış tuzaklar.

---

## Komutlar

```bash
docker compose up -d                              # PostgreSQL + Redis
dotnet run --project src/PeerLearn.Api            # API :5000
npm --prefix frontend run dev                     # arayüz :5173
```

```powershell
powershell -File .\tools\start-dev.ps1            # üçünü birden, ayrı konsollarda
powershell -File .\tools\stop-dev.ps1             # düzgün kapat (Postgres'i ÖLDÜRME)
powershell -File .\tools\restart-api.ps1          # durdur → derle → başlat
powershell -File .\tools\run-all-tests.ps1        # birim testleri + 17 e2e paketi, tek özet
```

```bash
npm --prefix frontend run test:e2e                # tarayıcı testleri (backend gerekmez)
```

**API çalışırken `dotnet build` çalıştırma** — DLL'ler kilitli, MSB3027 alırsın.
`restart-api.ps1` bunun için var.

---

## ⛔ Dokunulmaz

### `frontend/src/lib/hwid.js` → `canvasSignal()`

**Fonksiyonun tamamı sabittir.** Çizilen her şey — yazı tipi, renkler, metin,
koordinatlar — cihaz parmak izinin hash'ine giriyor. Tek bayt değişirse **üretimdeki tüm
HWID banları geçersiz olur** ve banlı kullanıcılar geri döner. Geri alınamaz.

İki değer özellikle "düzeltilesi" görünüyor, ikisi de bilerek yerinde duruyor:

| satır | neden düzeltilesi görünür | neden dokunulmaz |
|---|---|---|
| `ctx.fillText('PeerLearn', 2, 2)` | ürün adı artık **dersmate** | metin markayı değil cihazı tanımlıyor |
| `ctx.fillStyle = '#4f46e5'` | paletteki hiçbir renge karşılık gelmiyor | eski `brand-600` ile aynı olması tesadüftü; parmak izi sabiti |

Marka değişir, bu blok değişmez. Dosyanın içindeki uyarıyı da silme.

### Enum üyeleri

Enum'lar veritabanında **metin olarak** saklanıyor (`HasConversion<string>`). Bir üyeyi
verisinden önce silersen o satırlar okunamaz hâle gelir ve hata okuma anında,
başka bir yerde patlar. Önce veriyi göç ettir, sonra üyeyi kaldır.

---

## Ekonomiye dokunan kod

Öğrenci ders almak için **hiçbir şey ödemez**. Puan, dersi verene onay anında **basılır**:
30 dakikalık blok başına 50 puan (`SessionRules.MintPerBlock` / `MintBlockMinutes`), yani
30 dk = 50, 60 dk = 100. Tek bacaklı işlem — escrow/bloke kredi mekanizması kaldırıldı,
geri getirme.

Puan yazan her yol `CreditLedgerService` üzerinden geçer
(`src/PeerLearn.Application/Economy/CreditLedgerService.cs`). Başka yerden cüzdan/lot/defter
yazma. Çağıran taraf **üçünü birden** kurmak zorunda:

1. **Dağıtık kilit** (Redis) — `LockKeys` içinden uygun anahtar
2. **Açık transaction**
3. **`ConcurrencyRetry`** (xmin iyimser kilit)

Üçü de gerekli: kilit süreç içi yarışı, transaction kısmi yazmayı, retry iki instance
arasındaki çakışmayı kapatır. Örnek: `Features/Scheduling/BookSession.cs`.

**Kilit anahtarı seçerken sorguyu takip et.** Suistimal tavanları `TutorUserId`'ye göre
sayıyor, bu yüzden kilit de eğitmen bazında (`LockKeys.Tutor`). Bir zamanlar çift bazındaydı
ve tavan **tamamen** etkisizdi: 12 paralel istek 12 kabul aldı. Anahtar sorgunun grupladığı
şeyi kapsamıyorsa kilit hiçbir şey korumaz.

### Rozet motoru sırası

`BadgeEngine` veriyi DB'den okur, bellekteki değişiklikleri görmez. Doğru sıra:

```
transaction aç → SaveChanges → EvaluateAsync → SaveChanges → commit
```

`SaveChanges`'ten önce çağırmak sessizce yanlış sonuç verir (rozet hiç gelmez ya da eşik bir
adım kayar). Bu hata bu projede **üç kez** yapıldı.

---

## Veritabanı

- **Şema başına modül**: `catalog`, `identity`, `matchmaking`, `comms`, `scheduling`,
  `economy`, `moderation`, `community`. Modüller birbirinin tablosuna doğrudan gitmez.
- **`timestamptz` her yerde**, alan adları `...Utc` ile biter.
- **Kısmi (filtreli) index'ler son savunma hattıdır**, tek savunma değil: uygulama katmanı
  önce kontrol eder, index yarışı kapatır. Ama `HasFilter(...)` taşıyan bir index, sorgunun
  `WHERE`'i o koşulu **birebir** içermedikçe kullanılmaz — yeni index eklerken buna dikkat.
- Göç yazarken **veri onarımı şemadan ÖNCE** gelir ve `RAISE EXCEPTION` ile değişmezleri
  sına. Bozuk veriyle sessizce ilerleyen bir göç, geri alınamaz hasar demektir.
- **`Take` ile `ExecuteUpdate` durum koşulunu satırda yeniden sınamaz.** EF bunu
  `UPDATE … FROM (SELECT … WHERE "Status" = … LIMIT n)` olarak üretiyor; koşul yalnızca alt
  sorguda kalıyor ve PostgreSQL eşzamanlı güncellenmiş satırda onu yeniden değerlendirmiyor.
  Süpürücü bu yüzden kabul edilmekte olan isteği `Expired`'a eziyordu (ölçüldü). Durum
  koşullu toplu güncelleme partisiz, tek `UPDATE … WHERE "Status" = …` olmalı.

---

## Push bildirimleri

Mimari `docs/ASAMA-2-BACKEND.md` §8, üretim ayarı ve açma sırası `docs/URETIME-CIKIS.md` §11.
Buradakiler ihlali SESSİZ kalanlar:

- **Olay noktası yalnızca deftere satır yazar.** `BildirimKuyrugu.Ekle` olayın kendi kaydıyla
  AYNI `SaveChanges`'ten (ya da transaction'dan) ÖNCE; `IBildirimSinyali.Uyandir()` commit'ten
  SONRA. Ayrı yazılan satır "olay var, bildirim yok" (ya da tersi) penceresi açar. Handler'da
  `IPushGonderici` (Expo) çağrılmaz.
- **`ConcurrencyRetry` lambdasının içinde yalnızca `Ekle`.** Yeniden deneme ChangeTracker'ı
  temizlediği için içerideki Add güvenli; içerideki `Uyandir` ise her denemede bir kez daha
  çalar. "Satır yazıldı mı" bilgisi lambdanın dönüş değeriyle dışarı taşınır
  (`ResolveDisputeHandler`: `RunAsync<bool>`).
- **`Uyandir` fırlatmaz, fırlatmamalı**: commit olmuş mesaj istemciye hata dönerse istemci
  yeniden gönderir ve mesaj iki kez yazılır.
- **Dağıtıcıda durum yazımı her şeyden önce ve fencing'li**: Expo yanıtından hemen sonra,
  bilet ve cihaz silmeden ÖNCE, `WHERE "LeaseOwner" = @tur AND "Status" = 'Pending'` ile ve
  kapanış jetonuna değil kendi 10 sn'lik jetonuna bağlı. Sıra ya da koşul değişirse kira
  dolunca satır ikinci kez gönderilir (e2e-bildirim 8. bölüm bunu kırar).
- **HTTP zaman aşımı kira payından kısa kalmalı** (`Push:ZamanAsimiSaniye` [1, 25] sn, kira
  payı 30 sn): yanıt kira bitmeden gelmezse satır başka turda yeniden gönderilir.
- **Expo çağrısına kapanış jetonu (`stoppingToken`, `RequestAborted`) VERİLMEZ**, çağrı
  `CancellationToken.None` ile yapılır. Uçuştaki istek kesilirse Expo bildirimi almış olabilir
  ama sonuç yazılmaz ve satır iki dakika sonra ikinci kez gider. Kapanış alt partiler
  arasında denetlenir; süreyi `HttpClient.Timeout` sınırlıyor.
- **Kira bitişi veritabanı saatiyle** (`now()`): sahiplenme, kapsanan mesaj güncellemesi ve
  temizliğin kira koşulu. LINQ'te `DateTime.UtcNow` SQL'e `now()` olarak çevriliyor;
  değişkene alınırsa uygulama saatinden parametreye döner ve iki kopyanın saat farkı kira
  payından düşer (fark ~15 sn'yi geçince çift gönderim).
- **Sessiz saat kuralı tek yerde** (`BildirimKuyrugu.SessizSaatVadesi`): fabrikalar, dağıtıcının
  eleme adımı ve yeniden deneme vadesi aynı fonksiyonu çağırır. Dağıtıcı yalnızca vadesi sessiz
  saatin DIŞINDA olan satırı erteler; e2e'nin `VadeyiCek`'i (vadeyi gece `now()`'a çeker) bu
  yüzden çalışmaya devam ediyor.
- **Kısma yuvası gönderimden ÖNCE alınır**; sonra alınsaydı iki kopya aynı anda gönderirdi.
- **Kısmi indeks filtreleri sorguyla birebir**: `IX_Notifications_Bekleyen` (`"Status" =
  'Pending'`), `IX_LessonSessions_YaklasanDers`, `IX_LessonSessions_OnayBekleyen`.
- **Android yükünde `collapseId` YOK** (FCM çevrimdışı cihaz için 4 collapse anahtarı
  saklıyor; beşinci sohbetin mesajı hiç ulaşmayabilir). Ekrandakini `tag` değiştirir.
- **Kanal kimlikleri ve `data.tur` mobille birebir** (`BildirimKanallari` ↔ mobil
  `src/lib/bildirimler.js`). Android tanımadığı kanalla gelen bildirimi hata vermeden
  düşürür. Değişecekse iki tarafta aynı gün.
- **`DedupeKey` biçimi değiştirilmez** (`BildirimAnahtarlari`): değişirse dağıtım sırasında
  eski biçimle yazılmış hatırlatma "başka olay" sayılır ve ikinci kez gider. Damga
  mikrosaniye (Npgsql tick'in son hanesini kesiyor).
- **Ham SQL'e giden her `DateTime` `Kind=Utc`**: Npgsql başkasını timestamptz'ye yazmaz.
- **Oturumu bitiren yeni bir akış cihaz kaydını da silmeli.** Bugünkü noktalar:
  `RefreshTokenService.TumOturumlariDusurAsync`, Logout tek cihaz, `BanUser`. Geçici askıda
  bilerek silinmez. İkinci savunma hattı `OturumBagi` ("bu cihazın EN YENİ token'ı aktif mi";
  "herhangi aktif token" DEĞİL — e2e-bildirim 1. bölüm bunu kırar). Kaçan satırı günlük
  temizlik 5 gün sonra siler (gizlilik §5 "en geç 7 gün"; pay artarsa metin de değişir).
- **Ölü cihaz silmesi `OluCihaz.SilAsync` ile**, düz `Id` ile DEĞİL: satır ölüm kanıtından sonra
  yeniden kaydedildiyse (iOS yeniden kurulumu aynı token'ı verebiliyor) korunur.
- **`Push:Provider=Log` üretimde de açılır** ve defterde satırlar `Sent` görünür; telefona
  hiçbir şey gitmez. e2e-bildirim bu sağlayıcıyı ister (kancalar token'ın son ekinde:
  `OLU]`, `YABANCI]`, `YAVAS]`, `GECICI]`).
- Geliştirme konsolu dağıtıcının 5 sn'lik sahiplenme SQL'iyle dolar; susturmak için
  `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Warning`.

---

## Testler

`tools/*.ps1` paketleri **gerçek veritabanına** karşı koşar. Kurallar:

- **Başarısızlık işaretleri**: `[KALDI]`, `[FAIL]`, `[HATA]`. Yeni bir paket farklı bir
  etiket kullanacaksa `run-all-tests.ps1` içindeki listeye **eklenmeli** — eksik kalırsa o
  paketin başarısızlıkları özete hiç yansımaz ve kırmızı paket yeşil görünür.
- **`[ATLANDI]` başarısızlık değildir ama "geçti" de değildir**; özet bunu `EKSİK` olarak
  raporlar. Sınanmamış kodu sınanmış saymamak için.
- **Test betikleri idempotent değildir.** Sabit HWID veya sabit isim kullanma — ban kalıcı,
  aynı adlı kullanıcılar filtreleri bozar. Koşum başına üret, `userId` ile eşleştir.
- **Paylaşılan katalog konusu kullanma.** Birikmiş kullanıcılar öneri listesini doldurup
  testi kırıyor; koşuma özel konu üret. Konuyu **isimle arama** — katalog senkronizasyonu
  konuları pasifleştirebiliyor.
- **Kanıt standardı mutasyondur.** Bir testin geçmesi yetmez; ilgili kuralı bozup testin
  gerçekten kırıldığını gör. Bu projede dört test aynı anda **yanlış nedenle** geçiyordu.
- Yapısal iddia ("bu kolon artık yok") değer iddiasından ("değeri 0") güçlüdür; kaldırılan
  bir şeyi sınarken yapısal olanı yaz.
- **Sunucu saatine bağlı kontrol pencere dışında `[ATLANDI]` basar**, geçti gibi yapmaz.
  `e2e-bildirim.ps1`'de iki tane var (günlük istek özeti 05–08 UTC, otomatik onay
  hatırlatması TR 08–21): tam kapsam yalnızca 08–11 TR arasında koşar, dışında özet EKSİK der.

---

## PowerShell 5.1 tuzakları

Hepsi bu projede **en az bir kez** ısırdı:

- **Kesme işareti tek tırnaklı dizgeyi kapatır.** `'index'i'` ya da SQL içindeki
  `'economy'` betiği sessizce bozar. Çift tırnak kullan ya da ikiye katla (`''`).
- **`Write-Host` nesne akışına yazmaz.** Aynı süreçte `& betik` ile çağırınca çıktı
  yakalanamaz. Paketler `powershell.exe -File` ile **ayrı süreçte** koşmalı.
- **Satır içi ortam değişkeni öneki yok.** `PGPASSWORD=x psql` çalışmaz; `$env:` ile ayrı
  satırda ata.
- **`Set-Item Env:X ''` değişkeni SİLER**, boş değer atamaz; silinen değişkenin yerine
  `appsettings.json`'daki değer geçer. Boş bağlantı dizesi (`ConnectionStrings__Redis=`)
  gerekiyorsa süreci bash'in `env` komutuyla başlat (`env ConnectionStrings__Redis= dotnet …`).
- **`\"` kaçış DEĞİL.** Çift tırnaklı dizgede tırnak `""` ile yazılır (`"""Id"" = '$x'"`).
  C#/bash alışkanlığıyla yazılan `\"` betiği bozar ya da SQL'i sessizce değiştirir.
- **Stop tercihiyle yerel komutun stderr'i istisnaya döner** (`& node … 2>&1`). Uyarı basan
  bir aracı çağırırken `$ErrorActionPreference`'ı o satır için `Continue`'ya al.
- **`&&` ve `||` yok.** `;` ve `if ($?)` kullan.
- **Performans ölçme.** `Invoke-WebRequest` büyük yanıtlarda kendi maliyetini ekliyor
  (112 KB'de ~10 ms). Ölçümü Node ile yap.
- PostgreSQL'i **asla** `Stop-Process` ile kapatma — `pg_ctl -m fast`. Kirli kapanma
  kurtarma moduna sokar, en kötüde veri kaybettirir.

---

## Frontend

- **Kırılım kuralı**: `sm` (640px) **genişlik** içindir (kaç sütun sığar), `lg` (1024px)
  **dokunma** sınırıdır (44px hedef, 16px girdi punto). Tablet hâlâ parmakla kullanılıyor;
  dokunmayı `sm`'e bağlamak tableti masaüstü sanmaktır.
- **`vh` değil `dvh`** — mobil adres çubuğu `vh`'ye dahil değil, alt kısım kırpılır.
- **Girdilerde 16px'ten küçük punto kullanma** — iOS sayfayı otomatik yakınlaştırır.
- **Marka skalası** `frontend/tailwind.config.js`'te. `#0088CC` bilerek **500'de**, 600'de
  değil: üzerine beyaz metin 3.89:1 veriyor ve AA eşiği 4.5:1. Buton zeminleri 600/700'den
  gelir. Yeni bir renk eklerken kontrastı ölç.
- **Erişilebilirlik eşikleri testle korunuyor** (`frontend/e2e/marka.spec.js`). Palet
  değiştirirsen orası kırılır — bu kasıtlı.

---

## Depo

- **Senkron klasörüne koyma** (Google Drive, OneDrive, Dropbox). `.git` bozulabiliyor;
  ayrıca proje bir dönem Drive'daydı ve üç ayrı geçici çözüm doğurmuştu — hepsi kaldırıldı,
  geri getirme. Tarihçe `docs/ASAMA-3-FRONTEND.md` §1.2'de.
- **Depodaki parolalar geliştirme parolalarıdır** (`appsettings.json`, localhost, açıkça
  `DEV-ONLY`). Açıkta durmaları bilinçli. Üretim değerleri depoya girmez —
  `docs/URETIME-CIKIS.md`.
- **`proof-storage/` depoya girmez** — kullanıcıların yüklediği belgeler, kişisel veri.
- Ana dal `main`. Doğrudan itmek yerine dal aç ve PR aç.

---

## Nereye bakmalı

| konu | dosya |
|---|---|
| Modül sınırları, şema ayrımı, kilit stratejisi | `docs/ASAMA-1-MIMARI.md` |
| Ekonomi, moderasyon, arka plan işleri | `docs/ASAMA-2-BACKEND.md` |
| Push bildirimleri (defter, dağıtıcı, sessiz saat, etiketler) | `docs/ASAMA-2-BACKEND.md` §8 |
| Sayfalar, bileşenler, tasarım kararları | `docs/ASAMA-3-FRONTEND.md` |
| Sıfırdan kurulum | `docs/GELISTIRME-ORTAMI.md` |
| Üretim ayarları ve kapılar | `docs/URETIME-CIKIS.md` |
| Sıfırdan sunucuya kurulum (adım adım) | `docs/SUNUCUYA-KURULUM.md` |
| Açık işler ve **bilinerek kabul edilmiş sınırlar** | `docs/DEVAM-EDILECEK.md` |

Son satır önemli: `DEVAM-EDILECEK.md` içinde "eksik" gibi görünüp aslında **tartışılıp
kabul edilmiş** durumlar var. Orada kayıtlı bir sınırı hata sanıp "düzeltmeden" önce
gerekçesini oku.
