# AŞAMA 3 — Frontend & Integrations

> React 18 · Vite 5 · Tailwind CSS 3 · @microsoft/signalr
> Türkçe arayüz, JWT kimlik doğrulama, gerçek zamanlı sohbet

## 1. Kurulum

### 1.1 PostgreSQL (KURULDU)

PostgreSQL 17.11 kuruldu ve çalışıyor. Kurulum notu: EDB installer'ın **Windows servisi
oluşturma adımı yönetici hakkı istediği için başarısız oldu**; bu yüzden cluster kullanıcı
alanında oluşturuldu. Tam işlevli bir PostgreSQL — yalnızca otomatik başlayan bir servis değil.

| | |
|---|---|
| Binary'ler | `C:\Program Files\PostgreSQL\17\bin` |
| Veri dizini | `%LOCALAPPDATA%\PeerLearnBuild\pg\data` |
| Bağlantı | `localhost:5432` · db `peerlearn` · kullanıcı `peerlearn` |
| Şifre | `PeerLearnDev2026` (yalnızca yerel geliştirme) |

**Bilgisayarı yeniden başlattığında PostgreSQL kendiliğinden açılmaz.** Şu betiği çalıştır:

```bash
powershell -ExecutionPolicy Bypass -File tools/start-postgres.ps1
```

Kalıcı servis istersen (yönetici PowerShell'de, tek sefer) — betiğin başındaki
`pg_ctl register` komutu bunu yapar.

Şema ve ders kataloğu, API Development modunda ilk açılışta otomatik kuruldu
(migration + seed). Sıfırdan kurmak için veritabanını silip API'yi yeniden başlatmak yeter.

### 1.2 Frontend çalışma alanı

Proje Google Drive sanal diskinde (`G:`) durduğu için **npm, node_modules'ü proje klasörüne
kuramıyor** (`EBADF` / `ENOTEMPTY` — Drive'ın sanal dosya sistemi binlerce küçük dosyanın hızlı
oluşturulup silinmesini kaldırmıyor; bu denendi ve doğrulandı).

Çözüm, backend'de `Directory.Build.props` ile yapılanın aynısı: **kaynak Drive'da kalır,
üretilen dosyalar yerel diske gider.**

```bash
powershell -ExecutionPolicy Bypass -File frontend/setup-dev.ps1
```

Betik şunu kurar:

```
C:\Users\<kullanıcı>\AppData\Local\PeerLearnBuild\frontend-dev\
  node_modules\        ← gerçek, yerel disk
  src\                 ← JUNCTION → G:\...\frontend\src   (Drive'daki gerçek kaynak)
  package.json, vite.config.js, ...  ← Drive'dan kopya
```

Kodu **her zaman Drive'daki `frontend/src` içinde düzenle**; junction sayesinde değişiklik
anında yansır. (`vite.config.js` içindeki `resolve.preserveSymlinks: true` bunun için şarttır —
onsuz Rollup yolu G:'ye çözer ve `node_modules`'ü bulamaz.)

### 1.3 Çalıştırma

```bash
dotnet run --project src/PeerLearn.Api
```

```bash
npm --prefix "%LOCALAPPDATA%\PeerLearnBuild\frontend-dev" run dev
```

Arayüz: <http://localhost:5173> · API + Swagger: <http://localhost:5000/swagger>

> `src/PeerLearn.Api/Properties/launchSettings.json` eklendi. Bu dosya olmadan `dotnet run`
> uygulamayı **Production** modunda başlatıyordu; otomatik migration, seed ve Swagger hiç
> çalışmıyordu.

## 2. Frontend Mimarisi

```
frontend/src/
  lib/
    api.js          Tek fetch sarmalayıcı: JWT ekler, ProblemDetails'i ApiError'a çevirir,
                    401'de oturum düşürme olayı yayınlar. Tüm endpoint'ler tek nesnede.
    hwid.js         Cihaz parmak izi (SHA-256). Login'de zorunlu — HWID ban kontrolü için.
    format.js       Tarih/kredi biçimleme + durum etiketlerinin Türkçe karşılıkları.
  state/
    AuthContext.jsx Oturum (localStorage), login/logout, 401 dinleyicisi.
    useAsync.js     { data, error, loading, reload } — minimum veri çekme kancası.
  hooks/
    useChatHub.js   SignalR bağlantısı, otomatik yeniden bağlanma, "KOD|mesaj" hata ayrıştırma.
  components/
    ui.jsx          Button, Card, Badge, Modal, Field, Loading, ErrorBox, EmptyState, Notice.
    Layout.jsx      Üst menü + her sayfada görünen kredi rozeti (bakiye ana karardır).
  pages/            Login, Register, VerifyEmail, Dashboard, Portfolio, Discover,
                    Matches, Chat, Sessions, Wallet, Admin
```

Bilinçli olarak React Query/Redux **yok**: bu ölçekte `useAsync` + bağlam yeterli ve okunabilir.

## 3. Ekran ↔ İş Kuralı Haritası

| Ekran | Karşıladığı kural |
|---|---|
| Kayıt → Doğrula | Modül 1.3 — doğrulama sonrası tek seferlik hoş geldin kredisi (arayüz krediyi ve ömrünü gösterir) |
| Portföyüm | Modül 1.1 — iki yönlü portföy (Verebileceğim / Almak istediğim), 1-5 seviye |
| Keşfet | Modül 1.2 — çapraz eşleşme; karşılıklı takas olanlar **"Karşılıklı takas"** rozetiyle üstte |
| Eşleşmeler | İstek gönder/kabul/reddet; kabulde sohbet otomatik açılır |
| Sohbet | Modül 2.1/2.2 — SignalR canlı mesaj; toplantı linkleri tıklanabilir (kanal bağımsızlığı) |
| Derslerim | Modül 2.3 + 3 — rezervasyon, **Time-Lock geri sayımı**, doğrulama kodu, kanıt yükleme, onay, itiraz |
| Cüzdan | Modül 4 — kullanılabilir/bloke bakiye, kredi partileri, **vade uyarısı**, hesap hareketleri |
| Yönetim | Modül 4.3 — itiraz kuyruğu, kanıt incelemesi (tekrar kullanılan görsel işaretli), karar |

### Time-Lock arayüzde nasıl görünür?

Ders kartı, planlanan bitişe kalan süreyi canlı gösterir:
*"⏳ Time-Lock: 'Dersi Tamamladım' 42 dakika sonra açılır."*
Buton, sunucudan gelen `canComplete` bayrağına göre kilitlidir. Bayraklar backend'de
`SessionRules`'un **kendisinden** türetilir (`GetMySessions`), yani arayüz sunucunun
reddedeceği bir butonu asla göstermez.

## 4. Entegrasyon Detayları

- **Hata gösterimi:** Backend Türkçe `detail` mesajını döndürür; `ErrorBox` bunu ve hata kodunu
  (`INSUFFICIENT_CREDITS`, `TIME_LOCK_ACTIVE`, …) gösterir. Frontend'de mesaj kopyalanmaz —
  tek doğru kaynak backend'dir.
- **SignalR:** WebSocket header taşıyamadığı için token `accessTokenFactory` ile query'den gider
  (backend yalnızca `/hubs` yolunda kabul eder). Hub kapalıysa mesaj gönderimi REST'e düşer;
  REST yolu da aynı yayınları yaptığı için karşı taraf yine anında görür.
- **Saat dilimi:** Kullanıcı yerel saatiyle seçer, `toISOString()` ile UTC ("…Z") gönderilir.
  Backend `DateTimeKind`'ı üç durumda da doğru ele alır (bu aşamada düzeltildi).
- **HWID:** `crypto.subtle` ile SHA-256; güvensiz bağlamda (https/localhost dışı) kriptografik
  olmayan yedeğe düşer. Dürüst sınır: tarayıcı parmak izi taklit edilebilir; bu, ban kaçağını
  zorlaştıran bir katmandır, tek güvence değildir.

## 5. İnceleme Sonrası Düzeltilen Arayüz Hataları

Çok ajanlı inceleme çalıştırıldı; **doğrulama (adversarial verify) ajanları oturum limitine
takıldığı için bulguların hiçbiri makine tarafından teyit/çürütme almadı.** Bu yüzden her bulgu
kodda tek tek elle doğrulandı; gerçek çıkanlar düzeltildi:

| Sorun | Neden ciddiydi | Düzeltme |
|---|---|---|
| **Öğrenci kanıtı göremiyordu** | Çift taraflı onayın tamamı buna dayanır; öğrenci körlemesine kredi devrediyordu | Katılımcı kanıt uçları eklendi + "Kanıtı incele ve onayla" modalı (görsel, tekrar-kullanım uyarısı, doğrulama kodu) |
| **Modal state sızıntısı** | "Vazgeç" sonrası seçili dosya bileşende kalıyordu → **bir sonraki derse yanlış kanıt** yüklenebilirdi | Modallar koşullu render + `key={sessionId}` (unmount garantili) |
| **Rezervasyon yön hatası** | Karşı taraf, kendi anlatacağı konuya öğrenci olarak kaydolabiliyordu (kredi yanlış cüzdandan bloke) | Konu artık yönden türetiliyor: başlatan → `requestedTopic`, yanıtlayan → `offeredTopic` |
| **48 saatlik otomatik onay görünmüyordu** | Öğrencinin kredisi sessizce gidebilirdi | Sunucu `autoApproveDeadlineUtc` döndürüyor; kartta canlı geri sayım (config değişirse arayüz yalan söylemez) |
| **Her mesajda ekran spinner'a dönüyordu** | Yazma kutusu odağı ve kaydırma konumu kayboluyordu | `useAsync.reload({ silent: true })` — arka plan tazelemesi spinner göstermez |
| **Geçmiş yeniden çekilince canlı mesaj siliniyordu** | Yeniden bağlanmada gelen mesaj kalıcı kaybolabilirdi | Effect'ler ayrıldı (geçmiş yalnızca sohbet değişince) + `mergeById` ile birleştirme |
| **Gruba katılım bağlantı kurulmadan deneniyordu** | Sessizce başarısız oluyordu; doğruluk tesadüfe kalmıştı | Join yalnızca `status === 'connected'` iken; hata rozette gösteriliyor |
| **Yeniden bağlanma bütçesi bitince akış ölüyordu** | ~40 sn'lik kesinti sohbeti F5'e kadar öldürüyordu | `onclose` üzerinden üstel geri çekilmeli **sınırsız** yeniden bağlanma |
| **Açık sohbette okunmamış rozeti** | Kullanıcı baktığı sohbette rozet görüyordu | Gelen mesaj görünürken `markRead` + aktif sohbette rozet daima 0 |
| **Başlıkta bayat bakiye** | Aynı ekranda iki farklı kredi sayısı görünüyordu | `WalletContext` — tek kaynak; kredi değiştiren her işlemde tazeleniyor |
| **Doğrulama çıkmazı** | Kayıt sekmesini kapatan kullanıcı `/dogrula`ya ulaşamıyordu | Giriş sayfasına kalıcı link + `EMAIL_NOT_VERIFIED` hatasında doğrudan CTA |

## 6. Bu Aşamada Düzeltilen Backend Hataları

1. **Eksik sorgu endpoint'leri:** Arayüzün ihtiyaç duyduğu "portföyüm", "eşleşmelerim" ve
   "derslerim" listeleri backend'de **yoktu**; üçü de eklendi (`GetMyPortfolio`, `GetMyMatches`,
   `GetMySessions`).
2. **Production modunda başlama:** `launchSettings.json` eksikti → migration/seed/Swagger hiç
   çalışmıyordu.
3. **DB yokken çökme:** Development açılışında bağlantı hatası artık uygulamayı düşürmez;
   net bir Türkçe hata loglanır.
4. **Saat dilimi kayması:** `BookSession`, offset'li (`+03:00`) tarihleri UTC sanıp saatleri
   kaydırabilirdi.
5. **Belirsiz job seçimi:** `ExpireCredits` sorgusundaki `Distinct().Take()` sıralamasızdı
   (EF10102); cüzdanlar turdan tura atlanabilirdi.
6. **Enum'lar isteklerde kabul edilmiyordu (uçtan uca testte yakalandı):** API yanıtlarda
   enum'ları string döndürüyor ("Offer", "SessionNotHeld") ama isteklerde SAYI bekliyordu.
   Arayüzün gönderdiği her enum alanı 400 alacaktı — portföy ekleme, itiraz açma ve admin
   kararı dahil. `JsonStringEnumConverter` eklendi; sözleşme artık simetrik.
7. **Vite sayfayı saniyede bir yeniliyordu:** Drive'ın zaman damgalarını oynatması + 400 ms
   polling, formu doldurmayı bile imkânsız kılıyordu. Polling 3 sn'ye çekildi ve
   `awaitWriteFinish` eklendi.
8. **Yönetici kanıt görselini göremiyordu (itiraz testinde yakalandı):** Admin paneli yalnızca
   dosya adı ve SHA-256 hash gösteriyordu; "sahte kanıt" itirazına karar veren kişi görseli
   göremiyordu. Katılımcılar için eklediğim görsel ucu adminleri kapsamıyordu (katılımcı
   kontrolüne takılıyorlardı). `GET /api/admin/sessions/{id}/proofs/{proofId}/content` eklendi
   ve panelde görsel gösteriliyor.

## 7. Uçtan Uca Doğrulama (gerçek veritabanıyla çalıştırıldı)

```bash
powershell -ExecutionPolicy Bypass -File tools/e2e-smoke.ps1
```

Betik iki kullanıcı yaratıp tüm yolculuğu gerçek API + gerçek PostgreSQL üzerinde koşturur.
**Sonuç: 12 adımın tamamı geçti.** Doğrulanan iş kuralları:

| # | Doğrulanan kural |
|---|---|
| 1 | Kayıt → e-posta doğrulama → hoş geldin kredisi; **ikinci doğrulama kredi üretmiyor** |
| 2 | HWID ile giriş; bakiye 1 kredi |
| 3 | İki yönlü portföy; mükerrer giriş `PORTFOLIO_DUPLICATE` ile engelleniyor |
| 4 | Çapraz eşleşme algoritması karşılıklı takası (`isCrossMatch`) buluyor |
| 5 | Eşleşme kabulünde sohbet otomatik açılıyor |
| 6 | Mesajlaşma + okunmamış sayacı |
| 7 | Rezervasyonda kredi bloke; ikinci rezervasyon `INSUFFICIENT_CREDITS` (**çifte harcama yok**) |
| 8 | **TIME-LOCK**: ders bitmeden tamamlama `TIME_LOCK_ACTIVE` ile reddediliyor |
| 9 | Kanıt: yanlış kod reddediliyor, taraf olmayan reddediliyor, otomatik onay tarihi hesaplanıyor |
| 10 | Öğrenci kanıtı görüntüleyebiliyor; **üçüncü kişi göremiyor** |
| 11 | Atomik transfer; kazanç 30 gün vadeli; **ikinci onay reddediliyor** |
| 12 | **Sıfır toplam**: transfer bacakları = 0, küresel arz = mint − yakım, cüzdan = lot toplamı |

Arayüz de gerçek veriyle tarayıcıda doğrulandı: panel (bloke kredi + geri sayım), Derslerim
(eğitmende Time-Lock uyarısı ve **kilitli** "Dersi tamamladım" butonu, öğrencide farklı
doğrulama kodu metni), Sohbet (SignalR "Canlı bağlantı", toplantı linki, okundu işaretleme).

### İtiraz ve yönetim akışı

```bash
powershell -ExecutionPolicy Bypass -File tools/e2e-dispute.ps1
```

**31 kontrolün tamamı geçti.** Doğrulananlar:

| Senaryo | Doğrulanan kural |
|---|---|
| A | İtirazı yalnızca öğrenci açabilir; ikinci itiraz engellenir; **itiraz açıkken kredi transferi donar** (onay `INVALID_SESSION_STATE`, kredi bloke kalır); admin olmayan panele 403 alır; öğrenci lehine kararda kredi iade edilir, eğitmene geçmez, ders `Cancelled`; ikinci karar reddedilir |
| B | Eğitmen lehine kararda escrow capture edilir, kazanç 30 gün vadeli lot olur, ders `Completed` |
| C | Reddedilen itirazda ders onay kuyruğuna döner, **48 saatlik sayaç sıfırlanır**, sonrasında normal onay çalışır |
| D | "Ders yapılmadı" (kanıtsız) itirazı reddedilirse ders `Booked`'a döner — eğitmene hak etmediği otomatik capture kapısı **açılmaz** |
| E | Ban: banlı hesap hem kendi hem temiz cihazdan giremez; **aynı cihazdan açılan yeni hesabın girişi de engellenir** (ban kaçağı kapalı); temiz cihazdan başka hesap etkilenmez |
| F | Tüm itiraz akışlarından sonra: transfer toplamı 0, arz = mint − yakım, cüzdan = lot toplamı, **bloke bakiye = aktif hold toplamı** |

Yönetim paneli tarayıcıda da uçtan uca çalıştırıldı: kuyruk → kanıt inceleme → karar →
kredi iadesi (DB'den doğrulandı: öğrenci 1/0, eğitmen transfer almadı, ders `Cancelled`).

Denemek için hazır hesaplar (`tools/seed-demo.ps1`, şifre `Demo12345`):

| Hesap | İçerik |
|---|---|
| `ayse@demo.dev` / `berk@demo.dev` | Eşleşme, sohbet, rezerve ders (Time-Lock geri sayımı) |
| `ali@demo.dev` / `veli@demo.dev` | Karar bekleyen itiraz |
| `admin@demo.dev` | Yönetim paneli |

### Zamana bağlı background job'lar

```bash
powershell -ExecutionPolicy Bypass -File tools/e2e-jobs.ps1
```

**26 kontrolün tamamı geçti.** Yöntem: saati beklemek yerine kayıtların zaman damgaları
geçmişe alınır ve job admin ucundan tetiklenir — job'ın **gerçek kodu** çalışır, yalnızca
zamanlayıcı atlanır. Her senaryoda "kapsam dışı kayda dokunulmadığı" da ayrıca doğrulanır.

| Senaryo | Doğrulanan kural |
|---|---|
| A | 48 saati dolan ders otomatik onaylanır, kredi eğitmene geçer, defterde iki bacak yazılır; **2 saatlik derse dokunulmaz** |
| B | 7 günü geçen `Booked` ders `Expired` olur ve escrow öğrenciye iade edilir; **2 günlük rezervasyona dokunulmaz** |
| C | Vadesi dolan lot yakılır, taze lot korunur, `Expiry` hareketi yazılır; ikinci çalıştırma hiçbir şey yakmaz (**idempotent**) |
| D | **Vade oyunu kapalı**: bloke kredi süpürmeden etkilenmez (lot zaten boşaltılmıştır), ama iptal edilip vadesi geçmiş lota döndüğü an ilk süpürmede yanar — sahte rezervasyonla kredi ömrü uzatılamaz |
| E | Job'lardan sonra: transfer toplamı 0, **arz = mint − yakım (Expiry dahil defter tutuyor)**, cüzdan = lot toplamı, bloke = aktif hold toplamı, negatif bakiye yok |

Job'lar normalde arka planda koşar (oturum süpürmesi 10 dk, kredi vadesi 15 dk). Manuel
tetikleme uçları hem test hem operasyon içindir (bir aksaklıktan sonra beklemeden telafi):

```
POST /api/admin/jobs/session-sweep     -> { autoApproved, expired }
POST /api/admin/jobs/credit-expiry     -> { walletsProcessed, creditsExpired }
```

### Eşzamanlılık (race condition) testi

```bash
powershell -ExecutionPolicy Bypass -File tools/e2e-concurrency.ps1
```

**31 kontrolün tamamı geçiyor.** Kritik kurulum: Redis yapılandırılmadığı için kilit
**süreç içidir**; tek instance'a paralel istek atmak yalnızca semaforu ölçer. Bu yüzden test
**iki API instance'ı** (5000 + 5001) arasında istekleri bölerek yarışı süreç sınırının
dışına taşır — geriye yalnızca veritabanı savunmaları (xmin + check/unique/exclude) kalır.

```bash
# ikinci instance (test için)
dotnet run --project src/PeerLearn.Api --no-launch-profile --urls http://localhost:5001
```

| # | Yarış | Sonuç |
|---|---|---|
| 1 | 1 kredi ile 8 paralel rezervasyon | Tam 1 başarılı, 7× `INSUFFICIENT_CREDITS`, tek aktif escrow |
| 2 | Aynı derse 8 paralel onay | Tam 1 transfer, defterde tam 2 bacak, tek kazanç lotu |
| 3 | Paralel e-posta doğrulama (6×) | Tam 1 hoş geldin kredisi, tek lot (partial unique tuttu) |
| 4 | Onay + itiraz aynı anda | Tek kazanan, tutarlı son durum (`CONCURRENCY_CONFLICT` gözlendi → xmin devrede) |
| 5 | Öğrenci onayı + otomatik onay job'ı | Transfer tam bir kez |
| 6 | 4 paralel vade süpürmesi | Tek `Expiry` hareketi, çifte yakım yok |
| 7 | Paralel mükerrer portföy/eşleşme | Tek kayıt (`DUPLICATE` = PostgreSQL 23505 → **DB, uygulama kontrolünü aşan yarışı yakaladı**) |
| 9 | İki öğrenci → aynı eğitmenin aynı saati | `SCHEDULE_CONFLICT` (aşağıdaki düzeltmeden sonra) |
| 10 | Öğrenci + eğitmen aynı anda iptal | Escrow tam bir kez iade, tek reversal kaydı |
| 11 | İtiraz + süpürücünün iadesi | Tutarlı sonuç; asla "Disputed + Released" karması yok |
| 8 | Tüm yarışlardan sonra | 8 küresel değişmez korunuyor |

#### Bu test GERÇEK bir açık buldu (ve kapatıldı)

Çok ajanlı analizde iki bağımsız ajan aynı noktayı işaret etti, test de onu doğruladı:
**iki farklı öğrenci, aynı eğitmenin aynı saatine paralel rezervasyon yapabiliyordu.**

Sebep: `BookSession` yalnızca **öğrenci cüzdanının** kilidini alır. İki farklı öğrencinin
kilit anahtarları farklıdır, eğitmenin hiçbir satırı yazılmadığı için xmin de devreye girmez
ve `ReadCommitted` altındaki çakışma sorgusu karşı tarafın **commit edilmemiş** kaydını göremez.
Sonuç: eğitmen tek saatte iki derse kilitleniyor, iki öğrencinin kredisi bloke oluyor ve
eğitmen iki dersi de tamamlayıp 1 saatlik emeğe 2 kredi kazanabiliyordu.

Kilitle çözülemez (anahtarlar meşru şekilde farklı). Çözüm, AŞAMA 1'de "sonraya bırakıldı"
diye not edilen kısıt: `PreventOverlappingSessions` migration'ı `btree_gist` ile eğitmen ve
öğrenci için birer `EXCLUDE` kısıtı ekler. `23P01` hatası middleware'de `SCHEDULE_CONFLICT`
409'una çevrilir.

### Çok instance'lı kurulum (Redis)

Uygulama iki yerde Redis kullanır:

| Kullanım | Redis yoksa ne olur |
|---|---|
| `IDistributedLockProvider` | Süreç içi kilide düşer — **yalnızca tek instance'ta güvenli** |
| SignalR backplane | Yayınlar instance'lar arası taşınmaz — **sohbet bölünür** |

Kurulu: **Memurai 4.1.2** (Redis 7.2.5 protokolü, Windows servisi, otomatik başlangıç).
Kurulum betiği: `tools/setup-redis.ps1` (kendini yükseltir, UAC onayı ister).
Yapılandırma: `appsettings.json` → `"Redis": "localhost:6379"`.

#### Backplane: önce/sonra ölçüldü

`tools/signalr-backplane-probe.js` istemciyi **5001'e** bağlar, mesajı **5000** üzerinden
gönderir. Aynı test, aynı kod — tek fark Redis:

| | Sonuç |
|---|---|
| Redis **yokken** | `[HATA] mesaj 10000 ms icinde ULASMADI — sohbet instance'lara bolunuyor` |
| Redis **varken** | `[OK] BACKPLANE CALISIYOR — 5000 uzerinden gonderilen mesaj 5001 istemcisine ulasti` |

Yani Redis'siz çok instance'lı kurulumda kullanıcılar, hangi instance'a düştüklerine göre
birbirlerinin mesajlarını canlı göremez (mesaj kaybolmaz, yenileyince gelir — canlılık gider).

#### Redis'in gerçekten kullanıldığının kanıtı

Testlerden sonra `INFO commandstats` ve `PUBSUB CHANNELS`:

```
cmdstat_set:     calls=152      # SET NX PX  -> kilit alma
cmdstat_evalsha: calls=78       # Lua release script -> sahiplik doğrulayarak bırakma
cmdstat_del:     calls=80
PeerLearn.Api.Hubs.ChatHub:all
PeerLearn.Api.Hubs.ChatHub:internal:groups
PeerLearn.Api.Hubs.ChatHub:internal:return:ABDULLAH_75507927...   # instance 1
PeerLearn.Api.Hubs.ChatHub:internal:return:ABDULLAH_ef575448...   # instance 2
```

İki ayrı `internal:return` kanalı = iki instance aynı backplane'e abone.

#### Dağıtık kilit hata kodlarını temizledi

Aynı eşzamanlılık testi, Redis öncesi ve sonrası (31/31 her iki durumda da geçiyor —
fark **kullanıcının gördüğü hata**):

| Yarış | Redis yokken | Redis varken |
|---|---|---|
| Onay + itiraz | `INVALID_SESSION_STATE`×2 + **`CONCURRENCY_CONFLICT`**×1 | `INVALID_SESSION_STATE`×3 |
| Öğrenci + eğitmen iptali | **`DUPLICATE`**×1 (ham 23505) | `INVALID_SESSION_STATE`×1 |

Kilit ilk savunma katmanı olarak geri geldiği için kaybeden istek artık anlamlı bir alan
hatası alıyor; DB kısıtları son savunma hattı olarak yerinde duruyor.

Portföy/eşleşme mükerrerliğinin `DUPLICATE` kalması bilinçli: bunlar cüzdan kilidi almayan,
krediye dokunmayan yollar — orada unique index zaten doğru savunmadır.

#### `OpenDispute` de kilit + retry altına alındı

İtiraz açmak escrow'un kaderini belirlediği (donar → sonra iade ya da transfer edilir) hâlde
tek dayanağı `LessonSession.xmin` idi: kilit yok, açık transaction yok, retry yok. Yarışı
kaybeden kullanıcı anlamlı bir alan hatası yerine ham `CONCURRENCY_CONFLICT` alıyor ve
**itirazı sessizce kayboluyordu**.

Artık `ApproveSession` ve `ResolveDispute` ile **aynı iki cüzdan kilidini aynı sırada** alır
(sıralı alım deadlock'u önler), açık transaction içinde çalışır ve kilit altında taze okuyup
kuralları yeniden doğrular. Böylece bu üç yol ve süpürücünün iade fazı karşılıklı dışlanır.

| İtiraz + süpürücü yarışı | Sonuç |
|---|---|
| Öncesi | `CONCURRENCY_CONFLICT`×1 — itiraz kayboldu, ders `Expired` |
| Sonrası | **Hata yok** — itiraz kilidi önce aldı, süpürücü sırası gelince düşürecek ders bulamadı (`Disputed` + escrow `Active`) |

Dispute satırı ile durum geçişi artık açıkça aynı atomik sınırda; `Completed` bir ders
üzerinde çözülemeyen "zombi" `Open` itiraz kalma ihtimali kapandı.

#### Sessiz geri düşüş riski kapatıldı

Redis yapılandırılmadığında uygulama artık açılışta **açık uyarı loglar**
(`ConnectionStrings:Redis BOŞ — süreç içi kilit kullanılıyor…`). Öncesinde çok instance'lı
bir kurulumda ilk savunma katmanının devre dışı olduğu hiçbir yerden anlaşılmıyordu.
Ayrıca Redis bağlantısı `AbortOnConnectFail=false` ile kurulur: Redis anlık düşerse uygulama
açılışta çökmez, kopma/geri gelme loglanır.

### Yük ve performans

```bash
node tools/loadtest.js 16 6      # 16 eşzamanlı, senaryo başına 6 sn
```

Veri hacmi: **20.343 kullanıcı · 60.313 portföy girişi** (yük verisi SQL ile eklendi;
bu kullanıcılara cüzdan AÇILMAZ ki ekonomi değişmezleri ve mevcut testler bozulmasın).

16 eşzamanlı istek altında, sıfır hata:

| Uç | p50 | p95 | rps |
|---|---|---|---|
| `GET /wallet` | 3.0 | 4.2 | 5216 |
| `GET /catalog/topics` | 2.9 | 4.0 | 5356 |
| `GET /sessions` | 3.3 | 7.0 | 4292 |
| `GET /conversations` | 4.0 | 6.2 | 3700 |
| `GET /matches` | 4.2 | 11.0 | 3094 |
| `POST` mesaj (yazma) | 9.2 | 29.0 | 1362 |
| `GET /portfolio/suggestions` | 16.9 | 31.1 | 879 |
| `GET /suggestions` (yoğun konu) | 54.3 | 72.5 | 293 |

Tek darboğaz **çapraz eşleşme** sorgusu; diğer her şey 3-11 ms bandında. Maliyet, aranan
konuyu kaç kişinin sunduğuyla ölçekleniyor (10.062 kişilik konuda 54 ms).

#### Ölçülen ve düzeltilen: aday daraltma sorgusu

`EXPLAIN ANALYZE` planı sorunu net gösterdi: `HashAggregate` **10.062 satır** üretip
top-N sort ile 60'a iniyordu — yani 60 sonuç için 10 bin grup materyalize ediliyordu.

Sorgu, `PortfolioEntries` üzerinden `GROUP BY` yerine `Users` üzerinden `EXISTS` (semi-join)
biçiminde yeniden yazıldı. Aynı sonuç, tekilleştirme maliyeti yok:

| | Öncesi | Sonrası |
|---|---|---|
| DB planı | HashAggregate 10.062 satır — 12,5 ms | Hash Semi Join — 8,1 ms |
| Uçtan uca p50 (tek istek) | 28,9 ms | **16,0 ms** |
| Yük altında (seyrek senaryo) | 40,6 ms / 393 rps | **16,9 ms / 879 rps** |

Denenip **vazgeçilen**: `Users(AverageRating DESC, RatingCount DESC) WHERE Status='Active'`
index'i. Planlayıcı bunu kullanmadı (index'ten yürüyüp erken durmak yerine semi-join +
top-N sort'u tercih etti), kullanılmayan index yazma maliyeti getireceği için düşürüldü.

#### Doğrulanmayan hipotez (dürüst kayıt)

Analiz, `GET /sessions`'taki aksiyon bayraklarının try/catch ile hesaplanmasının satır başına
~4 exception ürettiğini ve 200 derslik listede ~10 ms'e mal olduğunu öngördü. İlk ölçümüm
bunu destekler görünüyordu (3 ders 2,8 ms / 200 ders 13,0 ms) ama **ölçüm aracı kirliydi**:
o fark PowerShell'in 112 KB'lık yanıtı işleme maliyetiydi. Düşük maliyetli istemciyle gerçek
sunucu maliyeti 3 ders 3,1 ms / 200 ders 3,6 ms çıktı — exception'ların ölçülebilir etkisi yok.

`SessionRules` yine de "ihlal üret / fırlat" ayrımına çevrildi (akış kontrolü için exception
kullanmamak daha temiz ve tek kaynak korunuyor), ama bu bir **performans düzeltmesi değildir**.

| Hâlâ doğrulanmadı | Neden |
|---|---|
| Redis düştüğünde davranış | `AbortOnConnectFail=false` ve kopma logları eklendi ama kesinti senaryosu denenmedi |
| Tarayıcıda iki kullanıcının canlı yazışması | Backplane programatik istemciyle doğrulandı; iki tarayıcı yan yana denenmedi |
| İncelemenin adversarial doğrulaması | Doğrulayıcı ajanlar oturum limitine takıldı; bulgular elle denetlendi |

## 9. Çalıştırma Özeti (güncel kurulum)

| Bileşen | Durum |
|---|---|
| PostgreSQL 17.11 | Kullanıcı alanında cluster — `tools/start-postgres.ps1` (servis DEĞİL) |
| Redis (Memurai 4.1.2) | Windows servisi, otomatik başlangıç — makine açılışında hazır |
| API | `dotnet run --project src/PeerLearn.Api` → :5000 |
| API (2. instance) | `dotnet run --project src/PeerLearn.Api --no-launch-profile --urls http://localhost:5001` |
| Frontend | `npm --prefix "%LOCALAPPDATA%\PeerLearnBuild\frontend-dev" run dev` → :5173 |

| Test | Komut | Kontrol |
|---|---|---|
| Birim | `dotnet test` | 22 |
| Ana akış | `tools/e2e-smoke.ps1` | 12 |
| İtiraz + yönetim | `tools/e2e-dispute.ps1` | 31 |
| Background job'lar | `tools/e2e-jobs.ps1` | 26 |
| Eşzamanlılık | `tools/e2e-concurrency.ps1` | 31 |
| SignalR backplane | `node tools/signalr-backplane-probe.js` | 1 |
| Yük / performans | `node tools/loadtest.js 16 6` | 10 senaryo |

## 8. Sıradaki İşler

1. PostgreSQL kurulumu + uçtan uca duman testi.
2. Rate limiting (`AddRateLimiter`) ve register enumeration'ın kapatılması (AŞAMA 2 notu).
3. Kanıt yüklemede magic-byte doğrulaması (şu an yalnızca content-type + boyut).
4. Doğrulama e-postasının token yerine tam bağlantı göndermesi + "yeniden gönder" ucu.
5. Bildirimler (ders yaklaşıyor, onay bekliyor, kredin yanacak).
6. Mobil uyum ince ayarı ve erişilebilirlik geçişi.
