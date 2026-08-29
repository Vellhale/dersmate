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
sudo apt update && sudo apt install -y docker.io docker-compose-plugin git
sudo usermod -aG docker $USER   # oturumu kapatıp aç
```

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

```bash
curl -I https://dersmate.com                       # 200, HTTPS
curl -I http://dersmate.com                        # 301 → https
curl -s https://api.dersmate.com/health            # {"status":"ok"}
curl -s https://api.dersmate.com/health/ready      # Healthy
```

Sonra **elle** iki şey dene — ikisi de otomatik doğrulanamıyor:

1. **Kayıt ol ve doğrulama e-postasının geldiğini gör.** ProductionGuard SMTP ayarlarının
   *varlığını* kontrol ediyor, gerçekten çalıştığını değil.
2. **Sohbet aç ve mesaj gönder.** SignalR WebSocket yükseltmesi vekil yapılandırmasına
   bağlı; yanlışsa sohbet sessizce "bağlanıyor"da kalır.

---

## 10. Yedek (⛔ İLK KULLANICIDAN ÖNCE)

```bash
# Veritabanı
dc exec -T db pg_dump -U dersmate dersmate | gzip > yedek-$(date +%F).sql.gz

# Kanıt dosyaları — AYRI ve unutulması kolay
docker run --rm -v dersmate_proof-storage:/veri -v "$PWD:/cikti" alpine \
  tar czf /cikti/kanitlar-$(date +%F).tar.gz -C /veri .
```

Bunu bir cron'a bağla ve **geri yüklemeyi bir kez dene**. Denenmemiş yedek, yedek değildir.

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
