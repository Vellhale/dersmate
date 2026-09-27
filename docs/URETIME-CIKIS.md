# Üretime çıkış

Bu dosya, PeerLearn'ü canlıya alırken **zorunlu** olan ayarları listeler. Uygulama, bu
ayarlar eksikken üretim ortamında **açılmaz** (bkz. `src/PeerLearn.Api/Startup/ProductionGuard.cs`)
— sessizce güvensiz çalışmak yerine net bir hatayla durur.

## 1. Zorunlu ortam değişkenleri

Depodaki `appsettings.json` **geliştirme** dosyasıdır; üretim değerleri oraya YAZILMAZ,
ortam değişkeniyle verilir. ASP.NET'te iç içe anahtarlar çift alt çizgiyle yazılır.

| Değişken | Neden zorunlu |
|---|---|
| `Jwt__Key` | Depodaki anahtar herkese açık; onunla üretilen her token taklit edilebilir. En az 32 karakter, rastgele. |
| `Jwt__ExposeVerificationTokenInResponse=false` | `true` iken doğrulama token'ı kayıt yanıtında döner ve e-posta sahipliği hiç kanıtlanmaz. |
| `ConnectionStrings__Postgres` | Depodaki bağlantı dizesi geliştirme parolasını içeriyor. |
| `ConnectionStrings__Redis` | Depodaki değer `localhost:6379`. **Üretimde localhost kalırsa kapı açılmaz.** Boş bırakılırsa kilit ve önbellek süreç içine düşer — yalnızca TEK instance için güvenlidir ve bunu açıkça beyan etmek gerekir. |
| `Cors__Origins__0` | Üretim arayüz alan adı. localhost kalırsa gerçek arayüz API'ye erişemez. |
| `Email__Provider=Smtp` | `Log` iken e-posta gönderilmez; doğrulama token'ı yalnızca e-postayla gittiği için **hiç kimse hesabını doğrulayamaz**. |
| `Email__Host`, `Email__Port`, `Email__Username`, `Email__Password`, `Email__FromAddress` | SMTP bağlantısı. |
| `Email__PublicWebUrl` | Doğrulama e-postasındaki bağlantı buradan kurulur (`https://alanadi/dogrula?token=…`). Boşsa e-posta çıplak token taşır ve kullanıcı 300 karakterlik JWT'yi elle kopyalamak zorunda kalır — mobilde pratikte yapılamıyor. |

Push bildirimleri bu listede **değil**: verilmezse uygulama açılır, yalnızca telefona
bildirim gitmez (açılışta uyarı yazılır). Açma sırası ve Expo erişim token'ı için §11.

### Hız sınırı (⚠️ ZORUNLU — varsayılanlara güvenmeyin)

| Değişken | Varsayılan | Anlamı |
|---|---|---|
| `RateLimit__AuthPerMinute` | 10 | Kayıt/giriş/doğrulama yeniden gönderimi + hesap silme — **IP** başına dakikada |
| `RateLimit__GlobalPerMinute` | 300 | Diğer tüm uçlar — **kimlik doğrulanmışsa kullanıcı**, değilse IP başına dakikada |
| `RateLimit__YuksekSinirBilerek` | `false` | Güvenli tavanların üstüne çıkmaya açık izin (olay anı kaldıracı) |

### Genel sınır neden kullanıcı başına (CGNAT)

Genel sınır 2026-09-05'e kadar IP başınaydı ve bu, mobil uygulamayı mağazaya çıkarmadan
**önce** kapatılması gereken bir arızaydı. Mobil operatörler CGNAT kullanıyor: binlerce
abone tek genel IP'nin arkasında. Maliyet karşılaştırması:

| işlem | kullanıcı başına maliyet |
|---|---|
| giriş | ~2 saatte 1 istek |
| uygulamayı açmak | hiçbir şeye dokunmadan ≥4 istek |
| gezinmek | dakikada 10–20 istek |

Yani tek bir CGNAT IP'sinde `GlobalPerMinute=300` tavanı **~15–30 eşzamanlı kullanıcıda**
doluyordu; kimlik tavanı ise aynı kümede dakikada 0,2 girişe denk geliyor — iki mertebe
uzak. İlk `429` dalgası giriş ekranından değil **uygulama içi gezinmeden** gelecekti ve
teşhisi zor olurdu: kullanıcıların bir kısmı çalışıyor, bir kısmı çalışmıyor, sunucu sağlıklı.

**Güvenlik gerilemesi yok:** kendi kovasını almanın bedeli geçerli bir JWT ve onu almak için
kayıt + giriş gerekiyor; o yol kimlik politikasıyla IP başına 10/dk'ya kilitli. Kimliksiz her
istek (token yok, süresi dolmuş, imza tutmuyor) eskisi gibi IP kovasına düşüyor.

⚠️ Bu, `Program.cs`'te `UseAuthentication()`'ın `UseRateLimiter()`'dan **önce** çağrılmasına
bağlı. Sıra ters çevrilirse `HttpContext.User` boş kalır, her istek IP kovasına düşer ve
düzeltme **sessizce** etkisiz olur — hata vermez. `tools/verify-production-guard.ps1`
C bölümü bunu yakalıyor (aynı IP'den iki kullanıcı; biri sınıra takılırken diğeri geçmeli).

### Sınırı bilerek yükseltmek

`ProductionGuard` güvenli tavanların üstünde bir değer görürse süreci durdurur. Kapı bir
dönem **tek yönlüydü**: sızmayı engelliyor ama bilerek yükseltmeye de izin vermiyordu, yani
canlıda bir `429` dalgası başladığında ayar dosyasıyla yapılabilecek hiçbir hafifletme yoktu
— kod + derleme + dağıtım gerekiyordu.

`RateLimit__YuksekSinirBilerek=true` o kapıyı **açık beyanla** açar ve açılışta uyarı loglar.
Sızma kazayla olur; bu bayrak kazayla yazılmaz. Geçici bir hafifletmeyse geri almayı unutmayın.

⚠️ **"Ortam değişkeni verilmezse koddaki güvenli varsayılanlar geçerlidir" DOĞRU DEĞİL.**
`appsettings.json` üretimde de taban yapılandırma olarak yüklenir ve içindeki geliştirme
değerleri (`AuthPerMinute: 2000`, `GlobalPerMinute: 20000`) koddaki 10/300 varsayılanlarını
EZER. Depoda `appsettings.Production.json` yok. Yani ikisini de **açıkça ortam değişkeniyle
vermek zorundasınız**; vermezseniz giriş ucu dakikada 2000 parola denemesine açık kalır.

Kimlik sınırı **IP başına** uygulanır (2026-08-27'de düzeltildi; öncesinde tüm site için
tek kovaydı). Ters vekil arkasında bunun çalışması `UseForwardedHeaders`'a bağlı — bkz.
§3 (TLS ve ters vekil). Genel sınırın anahtarı ise artık kullanıcı; gerekçesi yukarıda.

## 2. Arayüz derlemesi (⚠️ EN SIK ATLANAN ADIM)

Arayüz, API adresini **derleme zamanında** paketin içine gömer. Ortam değişkeni API
tarafındaki gibi çalışma zamanında okunmaz.

```bash
cd frontend
echo "VITE_API_URL=https://api.alan-adiniz.com" > .env.production
npm run build          # çıktı: frontend/dist
```

`.env.production` `.gitignore`'da — depoyu klonlayan hiç kimsede yoktur, her dağıtım
ortamında yeniden oluşturulur.

**Bu adım unutulursa ne olurdu:** paket `http://localhost:5000` gömer, her ziyaretçinin
tarayıcısı KENDİ makinesine istek atar, site açılır ama giriş dahil tek istek çalışmaz —
sunucu tarafı kusursuz kurulmuş olsa bile. 2026-08-27'de `vite.config.js'e bir kapı
eklendi: `VITE_API_URL` tanımsızsa ya da localhost içeriyorsa **üretim derlemesi hata
verip durur**, yani bozuk paket artık üretilemiyor. Yine de `dist/` klasörünü yayına
göndermeden önce içinde `localhost` geçmediğini doğrulayın:

```bash
grep -c localhost frontend/dist/assets/*.js   # 0 bekleniyor
```

## 3. TLS ve ters vekil

Uygulama TLS'i **kendisi sonlandırmaz**. Üretimde önünde bir ters vekil olmalı
(nginx / Caddy / bulut yük dengeleyici) ve o vekil:

- HTTP'yi HTTPS'e yönlendirmeli (uygulama `UseHttpsRedirection` çağırmıyor — bilerek:
  vekil başlıkları yanlış yapılandırıldığında sonsuz döngü üretirdi),
- `X-Forwarded-For` ve `X-Forwarded-Proto` başlıklarını geçirmeli.

Çalışan bir örnek yapılandırma depoda: **`tools/ornek-nginx.conf`** — HTTP→HTTPS
yönlendirmesi, başlık geçirme, SignalR WebSocket yükseltmesi ve tek sayfalık uygulama
için `try_files` kuralı dahil. Alan adlarını ve sertifika yollarını değiştirip kullanın.

Uygulama `UseForwardedHeaders` ile bu başlıkları okuyor ve `KnownProxies` listesi
**bilerek temizlenmiş** durumda (aksi halde loopback dışından gelen başlıklar sessizce
yok sayılır — en sık rastlanan tuzak). Bunun şartı şudur:

> ⚠️ **API portu dışarıya açık OLMAMALI.** Yalnızca vekil erişebilmeli (güvenlik duvarı
> ya da yalnız-loopback bind). Doğrudan ulaşabilen biri `X-Forwarded-For` uydurup IP
> başına hız sınırını atlayabilir.

`UseForwardedHeaders` olmadan iki şey **sessizce** bozulur: (a) IP başına kurulan iki hız
sınırı tek kovaya döner — `GlobalPerMinute=300` tüm site için 300/dk demek olur;
(b) `Request.Scheme` "http" kalır.

**Düz HTTP'de yayına çıkmanın bu projeye özel bir bedeli var:** `frontend/src/lib/hwid.js`
içindeki `sha256Hex`, güvenli bağlam dışında `crypto.subtle` bulamayıp **tamamen farklı**
bir hash üretir ve onu kalıcı yazar. Yani `canvasSignal()'a hiç dokunmadan, sadece HTTP'de
yayına çıkarak tüm HWID banlarını geçersizleştirirsiniz (bkz. CLAUDE.md, Dokunulmaz).
## 4. Şema kurulumu

```bash
dotnet run --project src/PeerLearn.Api -- --migrate
```

Migration'ları uygular, rozet/katalog tohumlamasını yapar ve **çıkar** — istek karşılamaz.

Uygulama açılışında migration koşulmaz: aynı anda başlayan iki instance aynı şemayı
yarıştırır ve hatalı bir migration fark edilmeden canlıya inerdi. Şema değişikliği
dağıtımın ayrı ve görünür bir adımıdır.

## 5. Sağlık uçları

| Uç | Soru | Bağımlılık yoklar mı |
|---|---|---|
| `GET /health` | Süreç ayakta mı? (canlılık) | Hayır |
| `GET /health/ready` | İstek karşılayabilir mi? (hazırlık) | Evet: PostgreSQL + Redis |

⚠️ **`/health/ready` DIŞARIYA KAPALI.** `tools/ornek-nginx.conf` onu `allow 127.0.0.1;
deny all;` ile kısıtlıyor — her çağrı PostgreSQL ve Redis'e gittiği için dışarı açık bir
uç, hem altyapıyı ele verir hem ücretsiz bir yük kapısı olur. Dış izleme (uptime servisi
vb.) bu uca **bağlanamaz**; ona `/health` verin, `/health/ready`'yi sunucunun içinden
çağırın (`curl http://127.0.0.1:5000/health/ready`).

Yük dengeleyici **`/health`**'e bakmalı. `/health/ready` DB kopukken `503 Unhealthy` döner;
Redis kopukken `Degraded` — uygulama çalışmaya devam eder ama kilit ve önbellek süreç
içine düşer, yani o anda **birden fazla instance çalıştırmak güvenli değildir**.

## 6. Bilinen sınırlar

- **Dosya depolama yereldir** (`ProofStorage:RootPath`). Birden fazla instance'ta bir
  instance'ın yazdığı kanıtı diğeri bulamaz (404) ve disk yedeklenmiyorsa kanıtlar
  kaybolur. Çok instance'a geçmeden önce nesne depolamaya (S3 vb.) taşınmalı:
  `IProofStorage`'ın ikinci bir uygulaması yeterli.
- Doğrulama yalnızca ortam ayarlarını kontrol eder; SMTP'nin gerçekten çalıştığını
  **denemez**. İlk dağıtımdan sonra bir kayıt yapıp e-postanın ulaştığı elle doğrulanmalı.

## 7. Arka plan işleri ve saklama süreleri

| İş | Sıklık | Ne yapar | Elle tetikleme |
|---|---|---|---|
| Kredi vade süpürmesi | 15 dk | 30 günü dolan lotları yakar | `POST /api/admin/jobs/credit-expiry` |
| Oturum süpürmesi | 10 dk | Otomatik onay, düşen rezervasyon, yanıtsız arkadaş isteği, biten askı | `POST /api/admin/jobs/session-sweep` |
| Depo bakımı | 24 saat | Saklama süresi dolan kanıt görselleri + artık dosyalar | `POST /api/admin/jobs/storage-cleanup` (yalnızca Admin) |
| Push hatırlatmaları | 1 dk | Yaklaşan ders (60/10 dk), otomatik onay (24/2 sa) ve günlük istek özetini deftere önceden yazar | `POST /api/admin/jobs/push-reminders` (yalnızca Admin) |
| Push dağıtımı | sinyal ya da 5 sn | Vadesi gelen defter satırlarını Expo'ya gönderir | `POST /api/admin/jobs/push-dispatch` (yalnızca Admin) |
| Push makbuzları | 15 dk (+ günde bir temizlik) | Biletlerin makbuzunu sorar, ölü cihazı siler; eski defter satırlarını temizler | `POST /api/admin/jobs/push-receipts` (yalnızca Admin; temizliği de koşar) |

Push saklama süreleri (`CleanupNotifications.cs`, `CheckPushReceipts.cs`): işlenmiş defter
satırı **30 gün**, bilet **24 saat** (sorulamayan bilet sorulmadan silinir), mesaj kısma
yuvası **1 gün**. Ömrü bir günden fazla önce dolmuş ama hiç işlenmemiş satır `Skipped(Bayat)`
yazılır — dağıtıcının uzun süre kapalı kaldığının izi.

Saklama kararları (hepsi `CleanupStorage.cs` içinde sabit):

- **Kanıt görseli 180 gün** saklanır, sonra silinir. **Satır silinmez** — SHA-256 hash'i
  sahte kanıt tespitinin tek dayanağı ve silinmesi hilekâra "bekle, aynı görseli yeniden
  kullan" kapısı açardı. Silinmiş kanıt indirilmeye çalışılırsa uç `410` döner ("süresi
  doldu"), `404` değil.
- **İtirazlı ders kapsam dışı**: kanıt, kredinin kaderini belirleyen delildir; süre dolsa
  bile hakem karar verene kadar durur.
- **7 günden genç dosyaya dokunulmaz**. Dosya depoya yazıldıktan sonra DB satırı yazılana
  kadar geçen kısa aralıkta dosya referanssızdır; bu pencere olmadan temizlik, o anda
  yüklenmekte olan kanıtı silerdi.
- Referans kümesi boş çıkarsa **hiçbir şey silinmez** ve hata loglanır: "veritabanı boş
  görünüyor" neredeyse her zaman bir bağlantı/şema sorunudur, "her dosya artık" demek değil.

**Takılan süpürme kayıtları:** bir kayıtta üst üste başarısız olan faz o kaydı üstel olarak
erteler (10 dk → 20 → 40 … en fazla 24 saat) ve `scheduling.SweepFailures`'a yazar. Hakem
panelindeki **"Takılı süpürme kaydı"** metriği sıfırdan büyükse o kayıtlarda otomatik
onay/iade **işlemiyor** demektir; `LastError` sütunu ve sunucu log'u sebebi söyler.

## 8. Dağıtım öncesi doğrulama

```bash
powershell -ExecutionPolicy Bypass -File .\tools\verify-production-guard.ps1
```

Üretim kapısının ve hız sınırının gerçekten devrede olduğunu sınar (kapı, geliştirme
ayarlarıyla açılışı durduruyor mu; kimlik ucu 429 veriyor mu; sağlık ucu sınır dışı mı).

## 9. Yedekleme (⛔ İLK KULLANICIDAN ÖNCE)

Depoda yedekleme altyapısı **yoktu**; 2026-08-27'de eklendi. İki betik var ve
**hangisini çalıştıracağın ortama göre değişir**:

```bash
# ÜRETİM (Ubuntu + Docker) — cron'a bağlanacak olan budur.
./tools/yedek-al.sh
```

```powershell
# GELİŞTİRME (Windows, yerel PostgreSQL)
powershell -ExecutionPolicy Bypass -File .\tools\yedek-al.ps1 -Hedef D:\yedek\dersmate
```

⚠️ `yedek-al.ps1` üretim sunucusunda **çalışmaz** — PowerShell betiğidir. Üretim yedeği
için `yedek-al.sh` kullan (ayrıntı: SUNUCUYA-KURULUM.md §10).

İki şeyi birlikte alır ve **ayrı alınmaları anlamsızdır**:

- `pg_dump` ile veritabanı (kullanıcılar, kredi defteri, moderasyon kayıtları),
- `proof-storage/` klasörü (ders kanıt görselleri).

Kanıt satırı veritabanında, dosyası diskte duruyor. Yalnızca birini geri yüklemek,
"kanıt var" diyen bir satırla var olmayan bir dosya ya da tersini bırakır.

**`ProofStorage__RootPath` MUTLAK bir yol olmalı.** Varsayılan `proof-storage` görelidir;
servis farklı bir çalışma dizininden başlatılırsa (systemd `WorkingDirectory`, yeni bir
publish klasörü) uygulama sessizce yeni ve BOŞ bir klasör açar — eski kanıtlar diskte
durur ama uç 404 döner.

Yedeği geri yüklemeyi **en az bir kez deneyin**. Denenmemiş yedek, yedek değildir.

## 10. İlk yönetici (⛔ ŞEMA KURULUMUNDAN HEMEN SONRA)

Taze bir veritabanında **hiç kimse yönetici değildir** ve rol atama ucu
(`PUT api/admin/users/{id}/role`) mevcut bir yöneticiyi şart koşar. Bu adım
atlanırsa `/admin` paneline kimse giremez: şikayet kuyruğu okunamaz, uyarı /
askı / ban uygulanamaz. Moderasyon zinciri kodda tamdır ama erişilemez kalır.

```bash
# 1. Yönetici olacak kişi ÖNCE arayüzden kayıt olur ve e-postasını doğrular.
# 2. Sonra sunucuda:
dotnet run --project src/PeerLearn.Api -- --promote-admin eposta@alan-adiniz.com
```

Komut `--migrate` gibi çalışıp **çıkar**, istek karşılamaz. Kullanıcı yoksa hata
verir ve hiçbir şey değiştirmez — hesabı bu komut **açmaz**, çünkü parola üretmek
ve e-posta doğrulamasını atlamak sahipliği kanıtsız kabul etmek olurdu.

Karar `moderation.AdminActionLogs` tablosuna `RoleChanged` olarak yazılır; özet
bunun bir kurulum adımı olduğunu söyler.

**Neden HTTP ucu değil:** "ilk yöneticiyi aç" ucu ne kadar korunursa korunsun
kalıcı bir yetki yükseltme yüzeyi bırakır (kurulum bayrağı unutulur, koşul bir
gün yanlış değerlendirilir). Komut satırı bu yüzeyi hiç açmaz: çalıştırmak için
zaten sunucuya erişim gerekir ve o erişim varsa veritabanına da doğrudan erişim
vardır — yani yeni bir ayrıcalık verilmiş olmaz.

⚠️ Komut üretim ortamında çalıştığı için **üretim kapısından geçer**: ortam
değişkenleri eksikse süreç bu komutta da açılmaz. Bu bilinçli — yanlış
yapılandırılmış bir kurulumda yönetici açmak, sorunu gizlemekten başka işe yaramaz.

## 11. Push bildirimleri (Expo)

Bildirimleri sunucu Expo Push servisine gönderir (`exp.host`); Expo da FCM'e (Android) ve
APNs'e (iOS) iletir. Mimari: `docs/ASAMA-2-BACKEND.md` §8.

### Ayarlar

| `.env.production` | Ortam değişkeni | Değer |
|---|---|---|
| `PUSH_PROVIDER` | `Push__Provider` | `Log` (varsayılan: hiçbir şey gönderilmez) ya da `Expo` |
| `PUSH_ACCESS_TOKEN` | `Push__AccessToken` | Expo **üretim** robotunun erişim token'ı. ⚠️ SIR |
| — | `Push__DeneyimKimligi` | `appsettings.json`'da: `@ardaerenguler/dersmate` (mobil `app.json` → owner/slug). Mobil proje taşınırsa BURASI da değişir. |

Kapı (`ProductionGuard`) üç durumda açılışı **durdurur**: tanınmayan sağlayıcı (`Firebase`
gibi bir yazım hatası sessizce `Log`'a düşmesin), `Expo` seçili ama token boş, deneyim
kimliği `@sahip/slug` biçiminde değil.

⚠️ **`Log` üretimde açılışı DURDURMAZ** (bilinçli: sunucu dağıtımı Expo token'ının hazır
olmasına bağlı kalmasın). Açılışta `Push:Provider 'Log' — push bildirimleri GÖNDERİLMİYOR`
uyarısı yazılır. Bu uyarıyı ciddiye alın: Log sağlayıcısı Expo gibi bilet döndürdüğü için
**defterdeki satırlar `Sent` görünür** — veritabanına bakan biri push'un çalıştığını sanır.

### Robotlar ve rol

expo.dev'de **iki ayrı robot kullanıcı** açılır, her biri kendi erişim token'ıyla: biri
geliştirme (geliştiricinin `appsettings.Development.json`'unda ya da ortam değişkeninde;
dosya `.gitignore`'da), biri üretim (yalnızca sunucunun `.env.production`'ında). Ayrı
olmaları token sızdığında iptalin yalnızca bir ortamı düşürmesi için.

Rol: işi gören **en düşük** rol. Viewer'ın gönderime yetip yetmediği henüz ÖLÇÜLMEDİ —
`--test-push` ile Viewer'la başlanır, 401/403 gelirse bir üst role çıkılır ve sonuç buraya
yazılır.

### ⛔ Açma sırası: önce Bearer'la gönder, SONRA Enhanced Security

1. Üretim robotunun token'ını `.env.production`'a yaz, `PUSH_PROVIDER=Expo`.
2. Gerçek bir cihaz token'ıyla sına (kuyruğu atlar, bileti ve 20 sn sonraki makbuzu yazar;
   token günlüğe maskeli düşer). Token, uygulamadan bildirimleri açmış kendi hesabının
   satırından okunur: `SELECT "Token" FROM comms."PushDevices" WHERE "UserId" = '<id>'`.

   ```bash
   # dc = docker compose -f docker-compose.prod.yml --env-file .env.production (SUNUCUYA-KURULUM.md)
   dc run --rm api dotnet PeerLearn.Api.dll --test-push 'ExponentPushToken[…]' --platform Android
   ```

   Beklenen: `Expo bildirimi kabul etti` ve makbuzda hata yok. Çıkış kodu 1 ise günlük
   nedenini söyler (token biçimi, sağlayıcı `Log`, 401, `DeviceNotRegistered`…).
3. Servisi yeniden başlat (`dc up -d api`) ve uygulamadaki **Test bildirimi gönder**
   düğmesiyle kuyruktan geçen yolu da dene.
4. **Ancak bundan sonra** expo.dev → proje ayarları → *Enhanced Security for Push
   Notifications* açılır.

Enhanced Security açıldığı anda token'sız ya da çalışmayan token'la gelen her gönderim
(yanlış kopyalanmış token, yetkisi yetmeyen rol, token'sız geliştirme sunucusu)
`401 UNAUTHORIZED` alır. Sıra bu yüzden "önce Bearer'ın kabul edildiğini gör, sonra zorunlu
kıl". Dağıtıcı 401'i mesajın suçu saymaz (geçici hata, `LogCritical`) ve satırı üstel
beklemeyle yeniden dener; altı denemede (~15 dakika) `Failed` olur. Yani yanlış sıra, açıldığı
andan itibaren **bütün** bildirimleri kaybettirir ve tek iz günlükteki CRITICAL satırlarıdır.

### Token rotasyonu

Sıra yine "önce yeni çalışsın, sonra eski ölsün":

1. expo.dev'de üretim robotuna **yeni** token üret (eskisi hâlâ geçerli).
2. `.env.production` → `PUSH_ACCESS_TOKEN` yeni değer.
3. `dc up -d api` (yeniden başlat), `--test-push` ile sına.
4. Eski token'ı expo.dev'de iptal et.

Adım 4 önce yapılırsa aradaki bütün gönderimler 401 alır (yukarıdaki bekleme zinciri).

### Ağ ve çok instance

- Sunucunun `exp.host:443`'e **giden** bağlantısı açık olmalı. Kapalıysa gönderimler
  `AgHatasi`/`ZamanAsimi` ile geçici hataya düşer ve ~15 dakikada `Failed` olur.
- Birden fazla API instance'ı güvenli: satırlar `FOR UPDATE SKIP LOCKED` ile kiralanıyor,
  hatırlatmalar `ON CONFLICT DO NOTHING` ile yazılıyor, mesaj kısması veritabanında. Tek
  fark gecikme: "kuyrukta iş var" sinyali süreç içi, diğer instance satırı en geç 5 sn'lik
  taramasında görür.

### İzlenecekler

| Belirti | Anlamı |
|---|---|
| Günlükte `CRITICAL` + 401/403 | Erişim token'ı yanlış, iptal edilmiş ya da Enhanced Security token'sız isteği reddetti |
| `comms."Notifications"` içinde `Status = 'Failed'` | `LastError` nedeni söyler (maskeli); toplu `Failed` bir kesinti ya da yapılandırma hatası |
| `LogError` + `MessageTooBig` / `InvalidCredentials` / `MismatchSenderId` | Kod ya da FCM/APNs kimlik bilgisi sorunu; cihazlar SİLİNMEZ, herkes etkilenir |
| `Status = 'Skipped'`, `Outcome = 'Bayat'` birikiyor | Dağıtıcı uzun süre çalışmamış (ömrü dolan satır gönderilmez) |
