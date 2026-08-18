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
    Scheduling/    BookSession, CompleteSession, ApproveSession, CancelSession, SweepSessions
    Economy/       WalletQueries, ExpireCredits (30 gün job komutu)
    Moderation/    Disputes (aç/çöz), BanUser (hesap + HWID)
PeerLearn.Infrastructure/
  Services/        JwtTokenService, PasswordHasher (PBKDF2), LocalProofStorage, LoggingEmailSender
  Locking/         RedisLockProvider (SET NX PX + Lua release) | InProcessLockProvider (tek instance)
  Jobs/            CreditExpiryJob (15 dk), SessionSweepJob (10 dk) — BackgroundService
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
  **Accepted** eşleşmenin tarafı olmalı. Hub yalnızca taşıma katmanı; persist + yetki
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
