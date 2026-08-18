# Geliştirme ortamı — sıfırdan kurulum

Temiz bir makinede projeyi çalışır hâle getirme sırası. Adımlar sıralıdır; her biri
öncekinin bitmesini bekler.

Üretim ayarları burada **yok** — onlar `docs/URETIME-CIKIS.md` içinde. Bu dosyadaki her
parola geliştirme parolasıdır ve depoda açıkta durması bilinçlidir.

## 0. Gerekenler

| Araç | Sürüm | Not |
|---|---|---|
| .NET SDK | 8.x | `dotnet --version` |
| Node.js | 18+ | `node --version` |
| Docker Desktop | güncel | Windows'ta WSL2 arka ucu ile |

## 0.5. Sadece arayüze bakacaksan — kestirme

Tasarımı gözden geçirmek ya da birine ekranı göstermek için tüm yığını kurmana gerek yok:

```bash
cd frontend
npm install
npm run onizleme          # → http://localhost:4173
```

.NET, PostgreSQL, Redis ve Docker gerekmez. `onizleme.mjs` derlenmiş arayüzü ve **sahte bir
API'yi** aynı porttan verir; giriş ekranında herhangi bir e-posta/şifre kabul edilir ve
sabit bir demo profili gelir (branş rozetleri, değerlendirmeler, ders portföyü dolu).

> ⚠️ **Veri sahtedir, hiçbir şey doğrulanmaz ve kaydedilmez.** Bu bir test aracı değil,
> göz kararı bakma aracı. Gerçek akışları sınamak için aşağıdaki tam kurulumu izle.
>
> Sohbet (SignalR) ve avatar görselleri taklit edilmiyor; o iki uç bilerek 404 döner ve
> konsolda hata görürsün — beklenen davranış.

### Daha da kestirme: tek dosya

```bash
npm run onizleme:tekdosya      # → frontend/dersmate-onizleme.html
```

Üretilen HTML **çift tıklayınca açılır** — sunucu, komut, kurulum yok. CSS ve JS içine
gömülü, sahte API de içinde; ağa hiç çıkmaz. Birine göstermek ya da başka bir makineye
atmak için en pratiği bu.

Depo köküne bir kopya `dersmate-onizleme.html` olarak konuldu; kaynak değiştikçe yukarıdaki
komutla yenile.

`onizleme.mjs` derlemeyi de kendisi yapar: `vite build`'i `VITE_API_URL` boş olacak
şekilde alt süreçte koşar, böylece istekler sayfanın kendi kökenine — yani bu sunucuya —
gider. Ayrı bir ortam dosyası yok; normal `npm run build` etkilenmez.

## 1. Veritabanı ve önbellek

```bash
docker compose up -d
docker compose ps          # ikisi de "healthy" olana kadar bekle (~10 sn)
```

`docker-compose.yml` iki servis kaldırır — **uygulamanın kendisini değil**:

| Servis | İmaj | Port | Ne taşır |
|---|---|---|---|
| `db` | postgres:16-alpine | 127.0.0.1:5432 | Tüm kalıcı veri |
| `cache` | redis:7-alpine | 127.0.0.1:6379 | Dağıtık kilit + önbellek |

API ve arayüz kendi makinende (`dotnet run` / `npm run dev`) koşar. İkisini de konteynere
almak, her kod değişikliğinde imaj yeniden kurmak demekti; iki kişilik bir ekipte bu
kazandırdığından fazlasını götürür.

Kimlik bilgileri `src/PeerLearn.Api/appsettings.json` içindeki bağlantı dizesiyle
**birebir aynı** olmak zorunda. Birini değiştirirsen diğerini de değiştir — aksi halde
API açılışta `password authentication failed` ile düşer ve sebebi bariz olmaz.

### Bilinmesi gereken üç karar

**Portlar `127.0.0.1`'e bağlı.** Salt `5432:5432` yazmak veritabanını ağdaki herkese
açar ve Windows güvenlik duvarı bunu sormaz.

**Sıralama (collation) `C`** — yani bayt sırası, her makinede birebir aynı. `tr_TR` gibi
bir yerel ayar `İ/ı/ş` harflerini "doğru" sıralar ama sonucu işletim sisteminin
ICU/glibc sürümüne bağlar: iki geliştiricinin `ORDER BY`'ı farklı sonuç verir ve
indeksler sürüm yükseltmesinde bozulur. Bir sorgu gerçekten Türkçe sıralama istiyorsa
bunu açıkça istemeli:

```sql
-- C (varsayılan):        Ilgaz, Irmak, Zeynep, istanbul, Çınar, İnci
-- COLLATE "tr-TR-x-icu": Çınar, Ilgaz, Irmak, İnci, istanbul, Zeynep
SELECT * FROM identity."Users" ORDER BY "DisplayName" COLLATE "tr-TR-x-icu";
```

**Redis'te kalıcılık kapalı** (`--save '' --appendonly no`). Redis burada yalnızca kilit
ve önbellek taşıyor; ikisi de yeniden üretilebilir. Kalıcılık açık olsaydı yeniden
başlatılan bir konteyner ESKİ kilitleri geri yükler ve "cüzdan kilidi alınamadı" hataları
sebepsiz görünürdü.

### Sık kullanılanlar

```bash
docker compose logs -f db      # veritabanı günlüğü
docker compose down            # durdur — VERİ KALIR
docker compose down -v         # durdur ve VERİYİ SİL (temiz kurulum)

# psql kabuğu (konteynerin içinden; makinene psql kurman gerekmez)
docker compose exec db psql -U peerlearn -d peerlearn
```

> `POSTGRES_INITDB_ARGS` (encoding/collation) **yalnızca hacim boşken**, ilk kurulumda
> uygulanır. Sonradan değiştirmek için `docker compose down -v` gerekir — veri silinir.

## 2. Şema ve tohumlama

```bash
dotnet run --project src/PeerLearn.Api -- --migrate
```

Migration'ları uygular, rozet/katalog tohumlamasını yapar ve **çıkar** — istek karşılamaz.
Uygulama açılışında migration koşulmaz; gerekçe `docs/URETIME-CIKIS.md`, bölüm 2.

Şema değişikliği yaptıysan, veritabanına dokunmadan hızlı doğrulama:

```bash
dotnet run --project tools/PeerLearn.SchemaCheck
```

## 3. API

```bash
dotnet run --project src/PeerLearn.Api
```

`http://localhost:5000`. Hazır olduğunu doğrula:

```bash
curl http://localhost:5000/health/ready     # PostgreSQL + Redis yoklar
```

`Degraded` dönüyorsa Redis kopuktur — uygulama çalışır ama kilit ve önbellek süreç içine
düşer. `Unhealthy` ise veritabanına ulaşamıyor; 1. adıma dön.

## 4. Arayüz

```bash
cd frontend
npm install
npm run dev                    # http://localhost:5173
```

`.env.development` zaten depoda ve `VITE_API_URL=http://localhost:5000` diyor.

## 5. Testler

```bash
# Birim testleri — bağımlılık yok
dotnet test

# Tarayıcı testleri — backend GEREKTİRMEZ (API taklit ediliyor)
cd frontend
npx playwright install chromium      # ilk kurulumda bir kez
npm run test:e2e
```

Uçtan uca ekonomi akışları PowerShell betiklerinde; bunlar **API'nin ayakta olmasını
ister** (1–3. adımlar):

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\e2e-admin-credits.ps1
```

Ayrıntı: `frontend/e2e/README.md` ve `docs/DEVAM-EDILECEK.md`.

## Takılırsan

| Belirti | Sebep |
|---|---|
| `password authentication failed` | `docker-compose.yml` ile `appsettings.json` ayrışmış |
| `connection refused` (5432) | Konteyner kalkmamış — `docker compose ps` |
| `Npgsql...database "peerlearn" does not exist` | Hacim eski bir kurulumdan kalmış — `docker compose down -v` sonra 1–2. adım |
| Sağlık ucu `Degraded` | Redis kopuk; tek instance'ta çalışmaya devam eder |
| Vite sayfayı saniyede bir yeniliyor | Google Drive senkronu; `vite.config.js`'teki polling ayarları bunun içindir |
| Build `.exe` kilitli diyor | Google Drive; `Directory.Build.props` çıktıyı `%LOCALAPPDATA%` altına yönlendirir |
