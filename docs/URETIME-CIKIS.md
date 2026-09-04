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

### Hız sınırı (⚠️ ZORUNLU — varsayılanlara güvenmeyin)

| Değişken | Varsayılan | Anlamı |
|---|---|---|
| `RateLimit__AuthPerMinute` | 10 | Kayıt/giriş/doğrulama yeniden gönderimi — IP başına dakikada |
| `RateLimit__GlobalPerMinute` | 300 | Diğer tüm uçlar — IP başına dakikada |

⚠️ **"Ortam değişkeni verilmezse koddaki güvenli varsayılanlar geçerlidir" DOĞRU DEĞİL.**
`appsettings.json` üretimde de taban yapılandırma olarak yüklenir ve içindeki geliştirme
değerleri (`AuthPerMinute: 2000`, `GlobalPerMinute: 20000`) koddaki 10/300 varsayılanlarını
EZER. Depoda `appsettings.Production.json` yok. Yani ikisini de **açıkça ortam değişkeniyle
vermek zorundasınız**; vermezseniz giriş ucu dakikada 2000 parola denemesine açık kalır.

Sınır **IP başına** uygulanır (2026-08-27'de düzeltildi; öncesinde kimlik ucundaki sınır
tüm site için tek kovaydı). Ters vekil arkasında bunun çalışması `UseForwardedHeaders'a
bağlı — bkz. §3 (TLS ve ters vekil).

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
| Oturum süpürmesi | 10 dk | Otomatik onay, düşen rezervasyon, yanıtsız eşleşme isteği, biten askı | `POST /api/admin/jobs/session-sweep` |
| Depo bakımı | 24 saat | Saklama süresi dolan kanıt görselleri + artık dosyalar | `POST /api/admin/jobs/storage-cleanup` (yalnızca Admin) |

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
