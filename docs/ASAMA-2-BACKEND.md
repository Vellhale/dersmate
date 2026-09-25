# AŞAMA 2 — Backend Core Logic

> Time-Lock · SignalR Chat · Atomik Kredi Transferi
> CQRS (MediatR 12) · JWT Auth · Background Job'lar

## 1. Katman Yapısı

```
PeerLearn.Application/                 ← iş mantığının tamamı (Infrastructure'a bağımlı DEĞİL)
  Abstractions/    IAppDbContext, IClock, IDistributedLockProvider, ITokenService, ...
  Common/          AppException + hata kodları, ConcurrencyRetry, LockKeys, CodeGenerator
  Economy/         CreditLedgerService (ekonominin TEK yazma noktası), FifoAllocator (saf, testli)
  Scheduling/      SessionRules (time-lock dahil tüm durum makinesi kuralları — saf, testli)
  Features/        Vertical slice: her use-case tek dosya (Command + Result + Handler)
    Identity/      Register, VerifyEmail (hoş geldin kredisi), Login (HWID ban kontrolü)
    Matchmaking/   Portfolio, GetMatchSuggestions (çapraz eşleşme), MatchRequests
    Communication/ SendMessage, ConversationQueries (liste + geçmiş + okundu)
      Bildirimler/ push: defter kuralları (saf, testli), cihaz/tercih uçları, dağıtıcı, işler (§8)
    Scheduling/    BookSession, CompleteSession, ApproveSession, CancelSession, SweepSessions
    Economy/       WalletQueries, ExpireCredits (30 gün job komutu)
    Moderation/    Disputes (aç/çöz), BanUser (hesap + HWID)
PeerLearn.Infrastructure/
  Services/        JwtTokenService, PasswordHasher (PBKDF2), LocalProofStorage, LoggingEmailSender,
                   ExpoPushGonderici | LoggingPushGonderici, ExpoYanitCozumleyici, BildirimSinyali
  Locking/         RedisLockProvider (SET NX PX + Lua release) | InProcessLockProvider (tek instance)
  Jobs/            CreditExpiryJob (15 dk), SessionSweepJob (10 dk), Push* işleri (§8) — BackgroundService
  Persistence/     PeerLearnDbContext (IAppDbContext), Migrations/InitialCreate, DbSeeder
PeerLearn.Api/
  Hubs/ChatHub     SignalR (grup: "conv:{id}")
  Controllers/     auth, catalog, portfolio, matches, conversations, sessions, wallet, admin
  Middleware/      ExceptionHandlingMiddleware (AppException/409 eşlemeleri → ProblemDetails)
```

## 2. Üç Çekirdek Mekanizma

### 2.1 Time-Lock (SessionRules.EnsureTimeLockPassed)

"Dersi Tamamladım" **sunucu saatiyle** `UtcNow >= ScheduledEndUtc` sağlanmadan çalışmaz;
istemciden gelen hiçbir zaman bilgisine güvenilmez. Tamamlama tek adımda üç şey ister:
time-lock geçmiş olmalı + doğrulama kodu (Session ID) eşleşmeli + kanıt SS yüklenmeli.
Kanıtın SHA-256'sı hesaplanır; aynı hash başka bir derste görülmüşse `DuplicateProofDetected`
işaretlenir (sahte kanıt sinyali). Kurallar saf fonksiyon olarak `SessionRules`'ta — 21 birim
testin konusu.

### 2.2 Atomik Kredi Transferi (ApproveSessionHandler + CreditLedgerService.CaptureHoldAsync)

Öğrenci onayında tek `ReadCommitted` transaction içinde:
hold `Captured` → öğrenci `Locked -= N` + `LessonSpending(−N)` → eğitmen `Available += N`
+ `LessonEarning(+N)` + **30 gün vadeli yeni lot** → ders `Completed`.
İki bacak aynı `CorrelationId`'yi taşır; toplamları daima 0.

Savunma katmanları (sırayla):
1. **Redis distributed lock** — iki cüzdan, Guid sırasına göre alınır (deadlock önlemi).
2. **xmin optimistic concurrency** — `ConcurrencyRetry` çakışmada ChangeTracker'ı temizleyip
   TÜM veriyi yeniden okuyarak 3 kez dener.
3. **DB constraint'leri** — hold başına tek capture, `(RelatedSessionId, Type)` unique,
   `AvailableBalance >= 0` check'i.

`CreditLedgerService` SaveChanges/Commit çağırmaz; atomiklik sınırını use-case çizer
(sözleşme sınıf başındaki yorumda).

### 2.3 SignalR Chat (ChatHub)

- Erişim kuralı tek yerde: `ConversationAccess` — kullanıcı, konuşmanın bağlı olduğu
  **Accepted** arkadaşlığın tarafı olmalı. Hub yalnızca taşıma katmanı; persist + yetki
  MediatR komutunda.
- JWT, websocket'lerde `access_token` query parametresiyle taşınır (yalnızca `/hubs` path'i).
- Redis yapılandırılırsa hem lock provider hem SignalR backplane otomatik devreye girer
  (`ConnectionStrings:Redis`).
- Kanal bağımsızlığı: Zoom/Meet/Discord linkleri sıradan mesajdır; platform video barındırmaz.

## 3. Background Job'lar

| Job | Aralık | İş |
|---|---|---|
| CreditExpiryJob | 15 dk | Vadesi dolan lotları yakar (`Expiry` ledger kaydı + consumption izi). Cüzdan başına ayrı lock+transaction: tek sorunlu cüzdan süpürmeyi düşürmez. |
| SessionSweepJob | 10 dk | (a) 48 saattir onaylanmamış dersleri `AsSystem` bayrağıyla otomatik onaylar — atomik transferin TEK yolu olan ApproveSessionCommand'ı yeniden kullanır. (b) Saati geçip hiç tamamlama istenmemiş rezervasyonları `Expired` yapıp escrow'u iade eder. |
| PushReminderJob | 1 dk | Yaklaşan ders, otomatik onay ve günlük istek özeti hatırlatmalarını deftere **önceden** yazar (§8.3). |
| NotificationDispatchJob | sinyal ya da 5 sn | Vadesi gelen defter satırlarını kiralar, eler, Expo'ya gönderir (§8.4). Ardışık hatada 5 sn → 1 dk geri çekilir. |
| PushReceiptJob | 15 dk (+ günde bir) | Biletlerin makbuzunu sorar, ölü cihazı siler; günde bir defter/bilet/kısma temizliği. |

MVP'de `BackgroundService + PeriodicTimer`; iş mantığı MediatR komutlarında olduğundan
Hangfire/Quartz'a geçiş yalnızca tetikleyici değişikliğidir.

## 4. Auth Notları

- Şifre: ASP.NET Identity `PasswordHasher` (PBKDF2). Login'de kullanıcı var/yok bilgisi
  sızdırılmaz (tek tip hata).
- E-posta doğrulama: DB tablosu gerektirmeyen imzalı **purpose token** (24 saat).
  Purpose token'lar FARKLI audience taşır (`PeerLearn.purpose`) — access token olarak
  kullanılamazlar. Doğrulama anında hoş geldin kredisi aynı transaction'da verilir.
- HWID: login'de aktif `HwidBans` kontrolü + `UserDevices` kaydı. Admin banı, kullanıcının
  tüm bilinen cihazlarını kalıcı HWID banına çevirir.
- MVP kolaylığı: verification token register yanıtında döner (gerçek e-posta entegrasyonunda
  kaldırılacak — kodda işaretli).

## 5. Çalıştırma

```bash
docker run -d --name peerlearn-pg -e POSTGRES_DB=peerlearn -e POSTGRES_USER=peerlearn -e POSTGRES_PASSWORD=CHANGE_ME -p 5432:5432 postgres:16
```

```bash
dotnet run --project src/PeerLearn.Api
```

Development'ta açılışta migration uygulanır + katalog seed edilir; Swagger `/swagger`'da.
Redis opsiyoneldir (boşsa in-process lock, tek instance için yeterli). Test akışı:
register → verify-email (token yanıtta) → login (Bearer) → portfolio → suggestions →
match → respond(accept) → book → (süre dolunca) complete → approve → wallet.

## 6. Adversarial İnceleme Sonrası Sertleştirmeler

15 ajanlı çok boyutlu inceleme (ekonomi-atomiklik, güvenlik, SignalR, EF, kapsam) sonrası
uygulanan düzeltmeler:

- **Dismissed itiraz açığı (kritik):** "Ders hiç yapılmadı" (Booked kökenli) itirazı admin
  reddederse ders artık onay kuyruğuna DEĞİL Booked'a döner — aksi halde eğitmen hiç
  tamamlama istemediği ders için 48 saat sonra otomatik capture ile kredi kazanabilirdi.
  Ayrım `CompletionRequestedAtUtc == null` üzerinden yapılır.
- **Otomatik onay çift doğrulaması:** `EnsureCanApprove(asSystem)` artık kilit altındaki taze
  okumada da 48 saatlik eşiği ve tamamlama isteğinin varlığını doğrular.
- **Süpürme dayanıklılığı:** job döngüleri artık öğe başına tüm istisnaları yakalayıp loglar
  ve `ClearChangeTracker` yapar — tek zehirli kayıt tüm otomatik onay/iade/vade akışını
  süresiz bloke edemez (head-of-line blocking düzeltildi).
- **Hub hata iletimi:** `AppExceptionHubFilter` iş kuralı hatalarını `"CODE|mesaj"` formatlı
  `HubException`'a çevirir; istemci anlamlı hata gösterebilir. `SendMessage` artık DTO döner
  ve persist sonrası yayın hatası çağrıyı düşürmez (mükerrer mesaj önlemi).
- **WebSocket ömrü:** `CloseOnAuthenticationExpiration=true` — JWT süresi dolunca (veya ban
  sonrası token yenilenemeyince) açık bağlantı kapanır.
- **Sahte kanıt sinyali kalıcı:** `SessionProof.IsDuplicateHash` kolonu eklendi; sinyal
  yükleyen eğitmene İFŞA EDİLMEZ, yalnızca admin endpoint'inde görünür
  (`GET api/admin/sessions/{id}/proofs`).
- **HWID zorunlu:** login'de `HwidHash` artık opsiyonel değil — boş bırakarak ban kontrolü
  atlatılamaz (parmak izinin taklit edilebilirliği bilinen sınırdır; katmanlardan biridir).
- **JWT token tipi ayrımı:** purpose token'lar farklı audience (`PeerLearn.purpose`) taşır;
  access token olarak kullanılamazlar. Doğrulama token'ı yanıtla yalnızca
  `Jwt:ExposeVerificationTokenInResponse=true` iken döner (prod'da false).
- **REST fallback = hub davranışı:** REST'ten mesaj gönderimi de aynı SignalR yayınlarını yapar.
- **Login timing eşitleme:** kullanıcı yokken de dummy hash doğrulaması koşulur.
- **Öneri sorgusu sınırlandı:** aday kümesi sunucuda `limit×3`'e daraltılır (popüler konu
  senaryosunda sınırsız materyalizasyon yoktu).
- **Redis kilidi:** TTL 60 sn'ye çıkarıldı; TTL aşımıyla el değiştiren kilit uyarı loglar.

### Bilinen MVP sınırları (prod öncesi ele alınacak)

1. **Hesap enumerasyonu:** register mevcut e-posta için 409 döner (UX tercihi). Prod'da nötr
   yanıt + rate limiting gerekli.
2. **Kanıt magic-byte doğrulaması yok:** content-type istemci beyanı; dosyalar hiçbir yerden
   serve edilmediği için bugün istismar edilemez. Kanıt görüntüleme endpoint'i yazılırken
   magic-byte kontrolü + `Content-Disposition: attachment` + `nosniff` şart.
3. **Mesaj idempotency anahtarı yok:** yayın hatası artık çağrıyı düşürmediği için pratik risk
   düşük; istemci tarafı `ClientMessageId` ileride eklenebilir.
4. **Rate limiting yok** (register/login/mesaj): AŞAMA 3 ile birlikte `AddRateLimiter` eklenecek.

## 7. AŞAMA 3 Önizlemesi

React (Vite + Tailwind): auth ekranları, portföy/öneri, sohbet (@microsoft/signalr),
rezervasyon takvimi, ders tamamlama/onay akışı, cüzdan. CORS `http://localhost:5173`
için hazır; SignalR `accessTokenFactory` ile bağlanacak.

## 8. Push Bildirimleri (2026-09-25)

Uygulama kapalıyken de gelen bildirimler: sunucu Expo Push servisine gönderir, Expo FCM'e
(Android) ve APNs'e (iOS) iletir. Kod `Features/Communication/Bildirimler/`; üretim
ayarları ve açma sırası `docs/URETIME-CIKIS.md` §11; bilinerek kabul edilen sınırlar
`docs/DEVAM-EDILECEK.md` → "Push bildirimleri".

### 8.1 Akış

```
olay noktası (handler) ── BildirimKuyrugu.Ekle ──▶ comms.Notifications (Pending)
   │                       olay kaydıyla AYNI SaveChanges / transaction
   └── commit SONRASI IBildirimSinyali.Uyandir()
hatırlatma işi (1 dk) ── INSERT … ON CONFLICT DO NOTHING ──▶ comms.Notifications
dağıtım işi (sinyal / 5 sn) ── kirala → bağlam → ele → kıs → gönder → DURUM YAZ → bilet, ölü cihaz
makbuz işi (15 dk) ── 15 dk–24 sa yaşındaki biletleri sor → DeviceNotRegistered: cihazı sil
```

Handler Expo'yu **hiç** çağırmaz: istek yavaşlardı ve sunucu yeniden başlarsa bildirim
kaybolurdu. Olay ile satır tek yazımda: olay kaydedilip bildirimi kaybolan (ya da tersi)
bir pencere yok.

| olay noktası | tür | alıcı |
|---|---|---|
| `SendMessage` (REST ve hub aynı handler) | NewMessage | karşı taraf |
| `CreateMatchRequest` | MatchRequest | isteği alan |
| `RespondMatch` — yalnızca kabul | MatchAccepted | isteği gönderen |
| `CompleteSession`, itiraz reddi (`ResolveDispute` → Dismissed) | ApprovalPending | öğrenci |
| `BookSession` | LessonBooked | eğitmen |
| `CancelSession` | LessonCancelled | iptal etmeyen taraf |
| hatırlatma işi | LessonSoon (60/10 dk), AutoApproveSoon (24/2 sa), MatchExpiringDigest | iki taraf / öğrenci / isteği alan |
| `POST /push/test` | Test | kullanıcının kendisi |

**Ret bildirilmez**: engelleme de bekleyen istekleri `Declined` yazıyor; ret bildirimi
engeli karşı tarafa sızdırırdı.

### 8.2 Defter (`comms.Notifications`)

Satır **içerik taşımaz**: tür, alıcı, aktör, kayıt kimliği, sohbet, olay damgası, `DueAtUtc`
(en erken gönderim), `ExpiresAtUtc` (bu andan sonra gönderilmez). Metin GÖNDERİM ANINDA
güncel veriden kurulur: mesaj okunmuşsa, ders iptal edilmişse, onay damgası yenilenmişse
satır gitmez. Mesaj içeriği hiçbir koşulda okunmaz ve yüke girmez.

| Status | anlamı |
|---|---|
| `Pending` | kuyrukta (kiralı olabilir) |
| `Sent` | en az bir cihazdan Expo bileti `ok` (teslim değil; teslim makbuzda) |
| `Skipped` | bilerek gönderilmedi — `Outcome` nedenini söyler |
| `Failed` | kalıcı hata ya da 6 geçici denemenin sonu — `LastError` (maskeli) |

`Outcome`: `Bayat` (ömür doldu), `HesapPasif` (alıcı ya da aktör askıda/banlı/silinmiş),
`Engel`, `TercihKapali`, `DurumDegisti` (olay artık geçerli değil), `Okundu`, `Tekrar`
(istek freni / mükerrer), `Birlestirildi` (başka bir mesaj bildirimi kapsadı), `CihazYok`,
`KanalKapali` (telefonda o kanal kapalı), `CihazGecersiz` (başka Expo projesinin token'ı).

**Tekilleştirme**: `UNIQUE(RecipientUserId, DedupeKey)`. Anahtarların TEK üreticisi
`BildirimAnahtarlari`; aynı olay her yerde aynı metni üretir. Onay damgası mikrosaniye:
Npgsql tick'in son hanesini kestiği için bellekteki ve DB'den okunan değer aynı anahtarı
vermeli. Anahtar biçimi değişirse dağıtım sırasında eski biçimle yazılmış satır "başka olay"
sayılıp ikinci kez gider.

Diğer tablolar: `PushDevices` (token, platform, HWID, telefonda kapalı kanallar),
`PushTickets` (bilet → cihaz, makbuz için), `NotificationPreferences` (dört kategori +
aydınlatma damgası + izin sorusu ertelemesi; satır yoksa dördü açık), `MessagePushThrottles`
(alıcı × sohbet başına son gönderim).

### 8.3 İleriye dönük kuyruklama

Hatırlatmalar "anı geldi mi" taramasıyla değil, **önceden** yazılır: anı
`(now − tolerans, now + 2 sa]` aralığına giren her hatırlatma `DueAt = an`,
`ExpiresAt = an + tolerans` ile kuyruğa girer (`HatirlatmaPenceresi`). Dakikalık iş bir turu
kaçırsa da hatırlatma düşmez; iki sunucu kopyası ya da yeniden başlatma aynı satırı iki kez
yazamaz (`ON CONFLICT DO NOTHING`). Ders iptal edilmişse, onay gelmişse satır gönderim
anında `DurumDegisti` olur.

- **Yaklaşan ders**: başlangıçtan 60 ve 10 dk önce, iki tarafa. Hatırlatma anından SONRA
  yapılmış rezervasyonda o ofset atlanır ("1 saat kaldı" yanlış olurdu).
- **Otomatik onay**: onaya 24 ve 2 sa kala, öğrenciye. Otomatik onay süresi 24 saat ya da
  altındaysa 24'lük atlanır.
- **Günlük istek özeti**: 10:00 TR yuvası (yazım 08:00–11:00 TR); düşmesine 6–30 saat
  kalan istekler, alıcı başına günde tek satır. Ardışık günlerin aralıkları boşluksuz ve
  örtüşmesiz: her istek tam bir özete girer.

Sorgu aralıkları yalnızca ön süzgeç (kısmi indeksler `IX_LessonSessions_YaklasanDers`,
`IX_LessonSessions_OnayBekleyen`, `IX_Matches_PendingAge`); kesin karar bellekte. Aralığı
daraltmak hatırlatmayı sessizce kaybettirir, genişletmek yalnızca birkaç fazla satır okutur.

### 8.4 Dağıtıcı

**Kira sahipliği.** Vadesi gelmiş ve kirası olmayan en fazla 200 satır, parti başına YENİ
bir kimlikle (`LeaseOwner`) 2 dakikalığına kiralanır (`FOR UPDATE SKIP LOCKED`): iki kopya
aynı satırı alamaz. Kira bitişi **veritabanı saatiyle** (`now()`) yazılır ve sınanır; uygulama
saatiyle yazılsaydı saati ileride olan kopya, ötekinin uçuştaki gönderimi sürerken kirayı
dolmuş sayıp satırı ikinci kez gönderirdi. Her Expo çağrısından önce kalan kira yerel
Stopwatch'la ölçülür; 30 sn'den azsa o alt parti gönderilmeden bırakılır (HTTP zaman aşımı
en fazla 25 sn).

**Sonuç yazımı her şeyden önce ve fencing'li.** Expo yanıtından hemen sonra, bilet ve cihaz
silmeden ÖNCE, `WHERE "LeaseOwner" = @tur AND "Status" = 'Pending'` ile ve kapanış
jetonuna değil kendi 10 sn'lik jetonuna bağlı. Takılıp kirası dolan tur, satırı almış yeni
turun sonucunu EZEMEZ.

**Expo çağrısı kapanış jetonunu almaz.** Dağıtım sırasında gelen SIGTERM (ya da admin ucunda
kopan istemci) uçuştaki isteği kesiyordu: Expo bildirimleri almış olabiliyordu ama sonuç da
bilet de yazılmıyor, satır iki dakika sonra ikinci kez gidiyordu. Çağrı artık
`CancellationToken.None` ile yapılıyor (süreyi `HttpClient.Timeout` sınırlıyor, ≤ 25 sn;
.NET'in kapanış beklemesi 30 sn). Kapanış alt partiler ve bölmede istekler ARASINDA
denetleniyor: başlamamış gönderim yapılmaz, satırın kirası bırakılır. Kalan tek mükerrer
penceresi "Expo kabul etti, süreç sonucu yazamadan ÖLDÜ" (çökme, SIGKILL) — en az bir kez
teslimat; cihazda aynı etiketli bildirim yenisiyle yer değiştirir (ses yeniden çalar).

**Eleme sırası** (ucuzdan pahalıya): Bayat → HesapPasif → Engel (yalnızca kişiyi getiren
türler: mesaj, istek, kabul; ders metinleri engelli/engelsiz vakada bayt bayt aynı) →
TercihKapali (Test muaf) → olay durumu (Okundu, DurumDegisti, Tekrar) → sessiz saat
ertelemesi (§8.5) → CihazYok (oturum bağı, §8.7) → KanalKapali. Eleme kararları Expo'dan
ÖNCE yazılır.

**Mesaj.** Satır 10 sn gecikmeyle vadelenir (web'de sohbet açıkken okunan mesaj telefonu
çaldırmasın). Alıcı × sohbet başına 60 sn kısma yuvası; yuva gönderimden ÖNCE alınır (sonra
alınsa iki kopya aynı anda gönderirdi), alınamazsa satır yuvanın sonuna ertelenir. Gövdedeki
okunmamış sayısı gönderim anında sayılır; gönderilen bildirimin kapsadığı mesajların
bekleyen satırları `Birlestirildi` olur (en iyi çaba).

**İstek.** Aynı partide aynı çiftin en eski satırı gider; son 7 günde gönderilmiş istek
bildirimi ya da reddedilmiş istek varsa yenisi `Tekrar`.

**Hatalar** (`PushHataKurali`; kodlar sınıflandırılır, metinler değil):

| durum | karar |
|---|---|
| bilet `DeviceNotRegistered` | cihaz silinir (bağlamda okunduktan sonra yeniden kaydedilmediyse, `OluCihaz`) |
| bilet `MessageRateExceeded` | geçici |
| bilet `MessageTooBig`, `InvalidCredentials`, `MismatchSenderId`, tanınmayan | kalıcı: `Failed`, cihaz SİLİNMEZ, `LogError` |
| istek 400 `PUSH_TOO_MANY_EXPERIENCE_IDS` | yabancı projenin token'ları ayrılır (`CihazGecersiz`, cihaz silinir), kalanlar deneme sayılmadan yeniden gönderilir |
| diğer istek 400/413 | parti ikiye bölünür; yalnızca tek başına düşen mesaj kalıcı |
| 401/403, 404, 429, 5xx, zaman aşımı, ağ | geçici: bekleme `min(30 sn·2^(n−1), 30 dk)`, 6 denemede `Failed`; 401/403 `LogCritical` |

Parçalamayı (100 mesaj) gönderici değil dağıtıcı yapar ve bir satırın cihaz mesajlarını
bölmez: parçalardan biri istek düzeyinde düşerse kabul edilmiş parçanın biletleri tek bir
sonuca sığmazdı. Token günlüğe ve `LastError`'a yalnızca maskeli yazılır
(`PushTokenKurali.MetniMaskele`).

**Sinyal.** Kapasitesi 1, dolunca yazımı düşüren kanal: bin `Uyandir` tek uyanış bırakır,
hiçbir koşulda fırlatmaz (commit olmuş mesaj istemciye hata dönerse istemci yeniden gönderir).
Süreç içi: diğer kopya satırı en geç 5 sn'lik taramasında görür.

### 8.5 Sessiz saat

22:00–09:00 **TR** (sabit UTC+3; Türkiye 2016'dan beri yaz saati uygulamıyor), `SessizSaat`.

| tür | sessiz saatte |
|---|---|
| gelen istek, kabul | sonraki 09:00'a kayar |
| onay bekliyor | 09:00'a kayar; ama otomatik onaya iki saatten az bırakacaksa HEMEN |
| ders planı, iptal | başlangıca 12 saatten az varsa hemen, yoksa 09:00'a |
| otomatik onay 24 sa | 09:00'a kayar (onaydan en az 3 sa önce kalmalı) |
| otomatik onay 2 sa | önceki 21:30'a çekilir (sabaha kayarsa onaydan sonraya düşerdi) |
| yeni mesaj, yaklaşan ders | **kaymaz** |

Kural tek yerde: `BildirimKuyrugu.SessizSaatVadesi` (tür, an, son an). Üç yerde uygulanıyor:
fabrikalar (kuyruğa yazarken), dağıtıcının eleme adımı ve yeniden deneme vadesi. Yalnızca
kuyruğa yazarken uygulandığında, 21:57'de yazılıp gecikmiş (sunucu kapalıydı, Expo 5xx
verdi) ya da yeniden denemesi 22:00'ı geçmiş bir istek gece telefonu çaldırıyordu. Dağıtıcı
yalnızca vadesi sessiz saatin DIŞINDA olan satırı erteler: vadesi zaten sessiz saatte olan
satırı bir kural bilerek oraya koydu (otomatik onaya iki saatten az kala onay bekliyor, 12
saatten yakın ders) ve kural aynı cevabı verirdi. Erteleme deneme sayılmaz. Otomatik onay
hatırlatmaları ömürleri toleransla sınırlı olduğu için buna girmez (gecikme onları en fazla
30/15 dk taşıyabilir).

Metinlerde takvim sözcüğü ve mutlak saat yok ("yarın", "09:00"): kullanıcının saat dilimi
bilinmiyor ve bildirim gecikmeli okunabiliyor; süreler göreli ("3 saat sonra").

### 8.6 Yük ve etiketler

- **Android'de `collapseId` YOK.** Expo onu FCM `collapse_key`'e yazıyor; FCM çevrimdışı
  cihaz için yalnızca 4 farklı anahtar saklıyor — beşinci sohbetin mesajı hiç ulaşmayabilirdi.
  Ekrandakini değiştiren alan `tag`. `channelId` her zaman var.
- iOS: `collapseId` (apns-collapse-id), `threadId`, rozet (yalnızca mesajda, toplam okunmamış).
- `ttl` iki platformda da (iOS'ta APNs son kullanma tarihi).
- `data` yalnızca `{tur, url, alici}`. Kanal kimlikleri ve `tur` değerleri mobille birebir
  (`BildirimKanallari` ↔ mobil `src/lib/bildirimler.js`): Android tanımadığı kanalla gelen
  bildirimi sessizce düşürür.
- Etiketler (`s-` sohbet, `i-` istek çifti, `id-` özet, `d-` ders, `t-` test) ve alıcı
  etiketi HMAC-SHA256 kısaltması; anahtar `Jwt:Key`'den HKDF ile türetiliyor. GUID
  taşımaz. `Jwt:Key` değişirse bütün etiketler değişir (bir kez çift bildirim).
- Başlıklar sabit; kişi adı yalnızca mesaj ve kabul gövdesinde, temizlenmiş ve 24 grafemde
  (`BildirimMetni.AdTemizle`: URL, yön karakterleri ve rezerve adlar genel metne düşer).

### 8.7 Cihaz kaydı ve oturum bağı

`PUT /push/devices` iki kapıdan geçer, ikisinde de hiçbir şey yazmadan `kayitli:false`
döner: (a) kullanıcı bildirim aydınlatmasını görmemiş (KVKK güvencesi istemci koduna bağlı
kalmasın), (b) **oturum bağı**: bu (kullanıcı, HWID) için EN YENİ yenileme token'ı aktif
değil. "Herhangi bir aktif token" yetmez: login eski token'ları iptal etmiyor, çıkış yapılmış
telefon eski bir artık yüzünden bağlı sayılırdı. Aynı kural (`OturumBagi`) dağıtıcıda da
her turda uygulanıyor; HWID üç tabloda tek fonksiyonla biçimleniyor (`HwidKurali`).

Cihaz satırı silinir: tek cihaz çıkışı (Rotated token'la çıkışta halef zinciri de iptal),
her yerden çıkış, parola sıfırlama, hesap silme, rol değişimi, hırsızlık tespiti
(`RefreshTokenService.TumOturumlariDusurAsync`), kalıcı ban, `forget` (mobilin çevrimdışı
çıkıştan sonraki ilk açılışı), bilette ya da makbuzda `DeviceNotRegistered`, yabancı proje,
ve günlük temizlik (`CleanupNotifications`): oturum bağı kopmuş ve 5 günden uzun süredir
kaydını yenilememiş cihaz. Sonuncusu olmadan uygulamayı çıkış yapmadan silen kullanıcının
satırı (o sürede bildirim olayı yoksa DeviceNotRegistered hiç gelmez) hesap silinene kadar
kalıyordu; gizlilik §5 "oturum kapandıktan en geç 7 gün sonra" diyor. **Geçici askıda
silinmez**: askı token'ları iptal etmiyor, cihaz bağlı kalır; askı bitince bildirim sürer,
askı boyunca dağıtıcı `HesapPasif` yazar.

`DeviceNotRegistered` silmesi koşullu: satır, ölüm kanıtından (makbuzda biletin yazıldığı an,
bilette bağlamın okunduğu an) SONRA yeniden kaydedildiyse silinmez (`OluCihaz`). iOS'ta
yeniden kurulumda Expo token'ı aynı kalabiliyor ve PUT aynı satırı güncelliyor; saatler sonra
gelen eski makbuz aksi hâlde yeni ve geçerli kaydı silerdi.

### 8.8 Uçlar

| uç | iş |
|---|---|
| `PUT /api/v1/push/devices` | cihaz kaydı (aydınlatma + oturum bağı kapısı, advisory kilitli upsert) |
| `POST /api/v1/push/devices/forget` | kimliksiz; token'ı unut, her durumda 204 |
| `GET /api/v1/push/preferences` | dört kategori + aydınlatma durumu + alıcı etiketi |
| `PUT /api/v1/push/preferences/{kategori}` | tek sütunluk upsert (kolon adı beyaz listeden) |
| `PUT /api/v1/push/prompt` | aydınlatma kararı: `Acildi` / `Ertelendi` |
| `POST /api/v1/push/test` | kuyruktan geçen test bildirimi; 10 dakikada en fazla 3 |
| `POST /api/admin/jobs/push-reminders` · `push-dispatch` · `push-receipts` | işleri elle tetikleme (yalnızca Admin) |

### 8.9 Testler

- Birim (`tests/PeerLearn.UnitTests/Bildirimler/` + `HwidKuraliTests`): sessiz saat, hatırlatma
  anları ve sorgu aralıkları (kaba kuvvetle), anahtarlar, kanal/tercih süzgeci, metinler
  (yapısal gizlilik güvenceleriyle), yük, etiket (HKDF bağımsız hesapla kilitli), hata
  sınıflandırması, Expo yanıt çözümü, gönderici (sahte HTTP ucu, typed client ayarları),
  sinyal, Log sağlayıcısının test kancaları.
- Uçtan uca: `tools/e2e-bildirim.ps1` (`Push:Provider=Log`, `run-all-tests.ps1` içinde).
  Log sağlayıcısının kancaları token'ın son ekinden seçilir: `…OLU]` makbuzda
  DeviceNotRegistered, `…YAVAS]` 3 sn gecikme, `…YABANCI]` başka Expo projesi, `…GECICI]`
  istek düzeyi 503 (yeniden deneme vadesi). Mutasyon kanıtı paketin başındaki yorumda.
