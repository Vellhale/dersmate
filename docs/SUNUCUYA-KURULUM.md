# Sunucuya kurulum — sıfırdan yayına

Bu dosya **işletim** kılavuzudur: elinde bir sunucu ve alan adı varken dersmate'i
yayına almanın sırası. Ayarların ne işe yaradığı ve neden zorunlu olduğu ayrı dosyada:
`docs/URETIME-CIKIS.md`.

Kurulum Docker Compose ile: `docker-compose.prod.yml` dört servisi birlikte kaldırıyor
(PostgreSQL, Redis, API, nginx) ve sertifika yenilemesini beşinci bir servis üstleniyor.

> **Neden Docker?** Alternatif, sunucuya .NET runtime + PostgreSQL + Redis + nginx'i tek
> tek kurup systemd birimleri yazmaktı. Tek sunuculuk bir kurulumda ikisi de çalışır ama
> Docker yolunda **sürüm sürprizi olmuyor**: geliştirmede çalışan PostgreSQL 16, üretimde
> de PostgreSQL 16. Elle kurulumda sunucunun dağıtımı hangi sürümü veriyorsa o gelir ve
> fark aylar sonra, bir sorgu davranışı değiştiğinde anlaşılır.

---

## 0. Önce senin yapman gerekenler

Bunlar kod işi değil; olmadan hiçbir adım ilerlemez.

| Ne | Not |
|---|---|
| **Alan adı** | Örn. `dersmate.com`. Arayüz kökte, API `api.` alt alanında. |
| **Sunucu** | En az 2 vCPU / 4 GB RAM / 40 GB disk. Ubuntu 22.04 veya 24.04. |
| **DNS kayıtları** | `dersmate.com`, `www.dersmate.com` ve `api.dersmate.com` → sunucunun IP'si (A kaydı). |
| **SMTP hesabı** | Doğrulama e-postaları için. Kendi sunucundan göndermeye çalışma — spam'e düşer. |

**SMTP olmadan çıkma.** Doğrulama token'ı yalnızca e-postayla gidiyor; SMTP çalışmıyorsa
kayıt olan hiç kimse hesabını doğrulayamaz ve platform kullanılamaz durumda açılır.

---

## 1. Sunucuyu hazırla

```bash
sudo apt update && sudo apt install -y docker.io docker-compose-v2 git
sudo usermod -aG docker $USER   # oturumu kapatıp aç
```

> **Paket adı `docker-compose-v2`, `docker-compose-plugin` DEĞİL.** İkincisi Docker'ın
> kendi deposuna ait; Ubuntu'nun deposunda yok ve `apt` onu bulamayınca **hiçbir paketi
> kurmaz** — `docker.io` da kurulmamış olur, oysa hata satırı yalnızca eksik paketten
> söz eder. Ubuntu 24.04'te Compose v2 eklentisi `docker-compose-v2` adıyla geliyor.
> (Ölçüldü: 2026-09-04, Ubuntu 24.04.4 LTS.)

Kurulumu doğrula — ikisi de çıktı vermeli:

```bash
docker --version && docker compose version
```

`docker compose` **boşluklu**: bu kılavuzdaki bütün komutlar v2 biçimini kullanıyor,
tireli `docker-compose` (v1) değil.

Güvenlik duvarı — **yalnızca 22, 80, 443**:

```bash
sudo ufw allow 22/tcp && sudo ufw allow 80/tcp && sudo ufw allow 443/tcp
sudo ufw enable
```

> ⚠️ **5432 ve 6379 asla açılmaz.** `docker-compose.prod.yml` bu portları dışarıya
> vermiyor, ama güvenlik duvarı ikinci kapı: bir gün birisi teşhis için portu açar ve
> kapatmayı unutur. Veritabanı internete açık kalmasın.

---

## 2. Depoyu al ve ayarları üret

```bash
git clone https://github.com/Vellhale/dersmate.git && cd dersmate
```

Ayar dosyasını **betikle** üret — sırları elle uydurma:

```bash
pwsh ./tools/uretim-ayarlari-uret.ps1 -AlanAdi dersmate.com
```

PowerShell yoksa `.env.production` dosyasını elle yaz; JWT anahtarı için:

```bash
openssl rand -base64 48 | tr '+/' '-_' | tr -d '='
```

Sonra **SMTP alanlarını doldur** (betik onları boş bırakıyor — sağlayıcıdan gelen
değerler):

```
SMTP_HOST=…
SMTP_USERNAME=…
SMTP_PASSWORD=…
```

---

## 3. nginx yapılandırmasını yerleştir

```bash
mkdir -p deploy/certbot/www deploy/certbot/conf
cp tools/ornek-nginx.conf deploy/nginx.conf
sed -i 's/dersmate\.com/KENDI-ALAN-ADIN.com/g' deploy/nginx.conf
```

`deploy/` klasörü `.gitignore`'da: içinde alan adların ve sertifikaların **özel
anahtarları** olacak.

---

## 4. Sertifikayı al (⚠️ SIRA ÖNEMLİ)

nginx, sertifika dosyaları yokken **açılmaz** ve certbot'un doğrulaması için nginx'in
ayakta olması gerekir. Yumurta–tavuk. Çözüm: önce yalnızca HTTP ile aç.

```bash
# 4a. Geçici olarak yalnızca 80 portunu dinleyen bir nginx ile ACME doğrulaması
docker run --rm -p 80:80 -v "$PWD/deploy/certbot/www:/var/www/certbot" \
  -v "$PWD/deploy/certbot/conf:/etc/letsencrypt" certbot/certbot certonly \
  --standalone -d dersmate.com -d www.dersmate.com -d api.dersmate.com \
  --agree-tos -m SENIN@EPOSTAN.com --no-eff-email
```

> **Önce `--dry-run` ile dene.** Let's Encrypt haftada 5 başarısız denemeden sonra alan
> adını saatlerce kilitliyor; DNS henüz yayılmadıysa gerçek denemeyi harcamış olursun.

---

## 5. Arayüzü derle

Arayüz API adresini **derleme zamanında** paketin içine gömüyor.

```bash
cd frontend
echo "VITE_API_URL=https://api.dersmate.com" > .env.production
npm ci && npm run build
grep -c localhost dist/assets/*.js   # 0 bekleniyor
cd ..
```

> `vite.config.js` bir kapı taşıyor: `VITE_API_URL` tanımsızsa ya da localhost içeriyorsa
> **derleme hata verip duruyor**. Yani bozuk paket üretilemiyor — ama yine de yukarıdaki
> `grep` ile doğrula.

Derlemeyi kendi makinende yapıp `dist/` klasörünü sunucuya kopyalamak da olur; sunucuya
Node kurmak zorunda değilsin.

---

## 6. Önce ŞEMA, sonra uygulama (⚠️ SIRA ÖNEMLİ)

Kısaltmak için `alias` tanımla — her komutta üç bayrak yazmayasın:

```bash
alias dc='docker compose -f docker-compose.prod.yml --env-file .env.production'
```

**Önce yalnızca veri servisleri**, sonra göç, sonra uygulama:

```bash
dc up -d db cache                                   # yalnızca PostgreSQL + Redis
dc run --rm api dotnet PeerLearn.Api.dll --migrate  # şema + katalog tohumlama
dc up -d --build                                    # tüm yığın
dc ps                                               # hepsi healthy olmalı
```

> ### ⛔ Bu sırayı bozarsan kayıt ucu 500 döner
>
> Yığını önce tamamen başlatıp **sonra** göç koşarsan uygulama açılır, `/health/ready`
> yeşil yanar, arayüz sorunsuz görünür — ama **her kayıt denemesi 500 verir**:
>
> ```
> The NpgsqlDbType 'Citext' isn't present in your database.
> ```
>
> Sebebi kodda değil: `citext` eklentisi göçle oluşuyor, ama API o andan **önce**
> açılmış ve veritabanının tip kataloğunu önbelleğe almış durumda. Bağlantı havuzu
> eski haritayla çalışmaya devam ediyor.
>
> Yanlışlıkla o sıraya düştüysen çare basit: `dc restart api`.
>
> (Bu hata yerel doğrulamada gerçekten üretildi ve teşhisi zor: hata mesajı
> "eklenti yok" diyor, oysa eklenti VAR — eksik olan uygulamanın ondan haberi.)

**Uygulama açılmazsa panikleme, günlüğü oku.** `ProductionGuard` eksik/güvensiz her
ayarı numaralı bir listeyle yazıp uygulamayı başlatmıyor — bu bir arıza değil, tasarım:
sessizce güvensiz çalışmaktansa durmak.

> Göç neden ayrı adım: uygulama açılışında kendiliğinden göç koşsaydı, aynı anda
> başlayan iki instance aynı şemayı yarıştırırdı ve hatalı bir göç fark edilmeden
> canlıya inerdi.

---

## 7. SMTP'yi SINA (⛔ ilk kullanıcıdan önce)

```bash
dc run --rm api dotnet PeerLearn.Api.dll --test-email senin@epostan.com
```

Bu adım atlanabilir görünüyor; değil. `ProductionGuard` yalnızca SMTP ayarlarının
**var olduğunu** kontrol ediyor, çalıştığını değil. Gönderim hatası da bilerek
yutuluyor (kayıt tamamlanmışken e-posta yüzünden isteği düşürmek, kullanıcıyı "hesabın
açıldı ama kayıt başarısız" çelişkisinde bırakırdı). İkisi birleşince:

> yanlış SMTP → kayıt **200 döner**, kullanıcı oluşur, e-posta gitmez → hata yalnızca
> günlükte → **hiç kimse hesabını doğrulayamaz** ve site "çalışıyor" görünür.

Komut gönderimi yutmadan deniyor ve açıkça `SMTP ÇALIŞIYOR` ya da `SMTP ÇALIŞMIYOR`
yazıyor. Gelen kutusunu (ve spam klasörünü) da kontrol et: sunucunun kabul etmesi,
teslim edildiği anlamına gelmez.

---

## 8. İlk yöneticiyi aç

Yönetici **arayüzden normal kayıt olur**, e-postasını doğrular, sonra sunucuda:

```bash
dc exec api \
  dotnet PeerLearn.Api.dll --promote-admin senin@epostan.com
```

Rol değişikliği denetim izine yazılıyor. Ayrıntı: `docs/URETIME-CIKIS.md` §10.

---

## 9. Yayına çıkmadan son kontrol

**Dışarıdan** (kendi bilgisayarından):

```bash
curl -I https://www.dersmate.com                   # 200  ← kanonik adres
curl -I http://www.dersmate.com                    # 301 → https
curl -I https://dersmate.com                       # 301 → https://www.dersmate.com/
curl -I https://www.dersmate.com/gizlilik          # 200  ← SPA geri düşüşü
curl -s https://api.dersmate.com/health            # {"status":"ok"}
curl -s https://api.dersmate.com/api/v1/catalog/categories   # 200
```

> **`https://dersmate.com` 200 DEĞİL, 301 döner.** www kanonik adres; www olmayan istek
> ona yönlendiriliyor (bkz. §3). 200 beklemek yanlış — CORS listesi tek adres aldığı için
> bu yönlendirme kasıtlı.

**Sunucudan** (SSH ile bağlıyken) — bu uç dışarıdan çağrılamaz:

```bash
curl -s http://127.0.0.1:5000/health/ready         # Healthy
```

> ⚠️ **`/health/ready` dışarıdan 403 verir ve bu DOĞRUDUR.** `tools/ornek-nginx.conf` onu
> `allow 127.0.0.1; deny all;` ile kısıtlıyor: derin sağlık kontrolü her çağrıda PostgreSQL
> ve Redis'e gidiyor, dışarı açık bırakmak hem altyapıyı ele verir hem ücretsiz bir yük
> kapısı olur. Dışarıdan deneyip 403 görürsen sistem **çalışıyor** demektir — arıza arama.
> (Gerçek kurulumda bu yanlış komut yüzünden bir kez arandı: 2026-09-04.)

Sonra **elle** iki şey dene — ikisi de otomatik doğrulanamıyor:

1. **Kayıt ol ve doğrulama e-postasının geldiğini gör.** ProductionGuard SMTP ayarlarının
   *varlığını* kontrol ediyor, gerçekten çalıştığını değil.
2. **Sohbet aç ve mesaj gönder.** SignalR WebSocket yükseltmesi vekil yapılandırmasına
   bağlı; yanlışsa sohbet sessizce "bağlanıyor"da kalır.

### Sertifika yenilemesini SINA (⛔ atlanırsa 90 gün sonra site düşer)

Sertifikayı `--standalone` ile aldın (certbot 80 portunu kendi tuttu). Yenileme ise
`--webroot` ile yapılacak, çünkü artık nginx 80'i tutuyor. **İkisi farklı yol** ve
uyuşup uyuşmadıkları ancak sınanınca anlaşılır:

```bash
dc run --rm --entrypoint certbot certbot renew --webroot -w /var/www/certbot --dry-run
```

Beklenen: `Congratulations, all simulated renewals succeeded`

> **`--entrypoint certbot` ŞART.** `docker compose run <servis> <komut>` yalnızca
> *command*'i değiştirir, *entrypoint*'i değil. Bu servisin entrypoint'i sonsuz bir
> döngü (`while :; do certbot renew --quiet; sleep 12h; done`); yazdığın argümanlar o
> kabuk betiğine konumsal parametre olarak geçer ve **sessizce yok sayılır**. Komut
> hiç hata vermez, sadece 12 saatlik uykuya oturur ve sen sınama yaptığını sanırsın.
> (Gerçek kurulumda bu yaşandı: 2026-09-04.)

Bu adım neden atlanamaz: yenileme 12 saatte bir kendiliğinden koşuyor ve `--quiet`
çalıştığı için **başarısız olsa da ses çıkarmaz**. Site 89 gün sorunsuz çalışır, sonra
bir sabah `NET::ERR_CERT_DATE_INVALID` verir. Sınama, o sabahı bugünden görmenin tek
yolu.

#### ⛔ Ama dry-run YETMEZ: yenilenen sertifikanın YÜKLENMESİ ayrı bir iş

Yukarıdaki sınama **doğrulama yolunu** kanıtlar, **teslim yolunu** değil. `--dry-run`
hiçbir sertifika yazmaz, dolayısıyla nginx'in yeni dosyayı okuyup okumadığını hiç
denemez. Ölçülerek bulunan arıza (2026-09-04 denetimi):

nginx sertifikayı yalnızca açılışta ve `reload` anında belleğe alır. certbot yeniler,
dosyayı yazar, log tertemiz görünür — ama nginx **eskisini sunmaya devam eder** ve
`restart: unless-stopped` konteyneri kendi başına yeniden başlatmaz. Sonuç, bitiş
gününde site + API + SignalR'ın birlikte düşmesidir.

Çözüm `docker-compose.prod.yml`'de: `web` servisi artık 6 saatte bir `nginx -s reload`
çalıştıran bir döngü taşıyor. Kurulumdan sonra ayrıca bir şey yapman gerekmiyor, ama
**ilk gerçek yenilemeden sonra doğrula** — yenileme bitişe 30 gün kala olur, yani
sertifikanın bitiş tarihinden bir ay öncesi:

```bash
echo | openssl s_client -servername www.dersmate.com -connect www.dersmate.com:443 2>/dev/null \
  | openssl x509 -noout -dates
```

`notBefore` hâlâ ilk kurulum tarihini gösteriyorsa yeniden yükleme çalışmıyordur ve
düzeltmek için **bir ayın** vardır. Bu kontrolü takvime yaz; sessiz arıza ancak
dışarıdan bakan bir ölçümle görülür.

---

## 10. Yedek (⛔ İLK KULLANICIDAN ÖNCE)

```bash
./tools/yedek-al.sh                 # varsayılan hedef: /var/backups/dersmate
./tools/yedek-al.sh /mnt/yedek      # başka bir diske
```

Betik veritabanı dökümünü ve kanıt dosyalarını **birlikte** alır, ikisinin de bütünlüğünü
doğrular ve 14 günden eski yedekleri siler (`SAKLAMA_GUN` ile değiştirilir). Kanıt arşivi
**boş çıkarsa betik durur** (`exit 1`) ve temizliği çalıştırmaz — geçerli olmayan bir
yedek uğruna eldeki geçerli yedekleri budamamak için.

> **⚠️ Bu sürümden önce kurduysan:** varsayılan hedef `./yedekler` idi, yani depo kökünün
> **içi**. Orada kişisel veri var (e-posta adresleri, parola özetleri, öğrenci belgeleri) ve
> `tools/dagit.sh` her dağıtımda oraya yazıyordu. Bir kez taşı:
>
> ```bash
> mkdir -p /var/backups/dersmate
> mv /opt/dersmate/yedekler/* /var/backups/dersmate/ 2>/dev/null || true
> rmdir /opt/dersmate/yedekler
> ```
>
> Taşımazsan yeni yedekler yeni yere gider ama eskiler depo içinde kalır ve saklama
> temizliği onları budamaz.

Cron'a bağla — bu adım atlanırsa geriye tek seferlik, elle alınmış ve unutulmuş bir yedek
kalır:

```bash
15 3 * * * /opt/dersmate/tools/yedek-al.sh >> /var/log/dersmate-yedek.log 2>&1
```

⚠️ **Elle `pg_dump ... | gzip > dosya` YAZMAYIN.** Kabuk, borunun SON komutunun çıkış kodunu
döndürür: `pg_dump` başarısız olsa bile `gzip` boş girdiden geçerli bir `.gz` üretip 0 ile
çıkar. Geriye yedek sanılan ~20 baytlık bir dosya kalır ve bunu ancak geri yüklemeye
çalıştığın gün öğrenirsin. Betik `set -o pipefail` kullanıyor ve boyutu ayrıca doğruluyor.

⚠️ **Kullanıcı ve veritabanı adını elle yazma.** Compose bunları `.env.production` içindeki
`POSTGRES_USER` / `POSTGRES_DB` değişkenlerinden alıyor; betik de aynı kaynaktan okuyor.
Elle yazılan bir ad, sessizce yanlış veritabanını yedeklemenin en kolay yoludur.

**Geri yüklemeyi bir kez dene.** Denenmemiş yedek, yedek değildir — betik bitiminde geri
yükleme komutlarını da yazdırıyor.

---

## Bu kurulum yerelde uçtan uca sınandı (2026-08-29)

Aşağıdakiler `docker-compose.prod.yml` ile gerçek konteynerler ayağa kaldırılarak
ölçüldü — yazılıp doğrulanmamış adım bırakılmadı:

| Ne | Sonuç |
|---|---|
| HTTP → HTTPS | `301` |
| ACME doğrulama yolu yönlendirmenin önünde | `404` (301 değil) — sertifika yenileme çalışır |
| Arayüz + SPA geri düşüşü (`/gizlilik`) | `200` |
| API vekili (`proxy_pass http://api:8080`) | `200` |
| `/health/ready` (PostgreSQL + Redis) | `Healthy` |
| Şema kurulumu (`--migrate`) | 31 tablo |
| Kayıt (onay alanlarıyla, nginx üzerinden) | `200`, onay veritabanında |
| Onaysız kayıt | `400` |
| SMTP sınaması (yanlış ayarla) | `SMTP ÇALIŞMIYOR`, **3 sn**'de |
| Hız sınırı (`AuthPerMinute=10`) | 10 istek geçti, sonraki 4'ü `429` |
| Sahte `X-Forwarded-For` ile atlatma | **engellendi** — üçü de `429` |
| Farklı kaynak IP kendi kovası | `401` — IP başına bölümleme çalışıyor |
| Günlük döndürme | 5 servisin hepsinde `10m × 3` |
| Konteyner kullanıcısı | `dersmate` (kök değil) |

Son iki satır özellikle önemliydi: `UseForwardedHeaders` ile IP başına hız sınırı
"ikisi birlikte anlamlı" diye belgelenmişti ama birlikte hiç ölçülmemişti. Artık
ölçüldü — hem sahtecilik engelleniyor hem de bir kullanıcının sınırı diğerlerini
kilitlemiyor.

---

## Bilinen sınırlar

- **Tek instance varsayımı.** Kanıt dosyaları yerel diskte; ikinci bir instance
  diğerinin yazdığı kanıtı bulamaz (404). Ölçeklenirken nesne depolamaya geçilmeli.
- **Göç geri alma korumaları var.** Forum ve kayıt onayı göçleri, veri varken geri
  alınmayı reddediyor (`RAISE EXCEPTION`). Bu kasıtlı: o veriler geri getirilemez.
- **JWT durumsuz, 2 saat.** Parola değişince açık oturumlar düşmüyor.
- **Hesap silme ucu yok.** Gizlilik metni e-posta ile talep diyor; talebi elle
  karşılaman gerekiyor.
- **Günlükte bir Data Protection uyarısı görürsün** — zararsız:
  `Storing keys in a directory ... may not be persisted outside of the container`.
  Anahtarlar konteynerle birlikte kayboluyor ama bu uygulama Data Protection'ı hiçbir
  yerde kullanmıyor: kimlik doğrulama JWT ve token'lar `Jwt__Key` ile imzalanıyor,
  yani her dağıtımda anahtarların yenilenmesi kimseyi oturumdan düşürmüyor.
  ⚠️ İleride çerez tabanlı oturum ya da antiforgery eklenirse bu uyarı ZARARSIZ
  OLMAKTAN ÇIKAR ve anahtarların kalıcı bir hacme alınması gerekir.
