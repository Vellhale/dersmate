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

## 6. Yığını başlat

```bash
docker compose -f docker-compose.prod.yml --env-file .env.production up -d --build
docker compose -f docker-compose.prod.yml ps        # hepsi healthy olmalı
docker compose -f docker-compose.prod.yml logs -f api
```

**Açılmazsa panikleme, günlüğü oku.** `ProductionGuard` eksik/güvensiz her ayarı
numaralı bir listeyle yazıp uygulamayı başlatmıyor — bu bir arıza değil, tasarım:
sessizce güvensiz çalışmaktansa durmak.

---

## 7. Şemayı kur

```bash
docker compose -f docker-compose.prod.yml exec api dotnet PeerLearn.Api.dll --migrate
```

> Uygulama göçleri **kendiliğinden uygulamıyor**: iki instance aynı anda göç koşarsa
> yarış çıkar. Göç ayrı ve bilinçli bir adım.

---

## 8. İlk yöneticiyi aç

Yönetici **arayüzden normal kayıt olur**, e-postasını doğrular, sonra sunucuda:

```bash
docker compose -f docker-compose.prod.yml exec api \
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
docker compose -f docker-compose.prod.yml exec -T db \
  pg_dump -U dersmate dersmate | gzip > yedek-$(date +%F).sql.gz

# Kanıt dosyaları — AYRI ve unutulması kolay
docker run --rm -v dersmate_proof-storage:/veri -v "$PWD:/cikti" alpine \
  tar czf /cikti/kanitlar-$(date +%F).tar.gz -C /veri .
```

Bunu bir cron'a bağla ve **geri yüklemeyi bir kez dene**. Denenmemiş yedek, yedek değildir.

---

## Bilinen sınırlar

- **Tek instance varsayımı.** Kanıt dosyaları yerel diskte; ikinci bir instance
  diğerinin yazdığı kanıtı bulamaz (404). Ölçeklenirken nesne depolamaya geçilmeli.
- **Göç geri alma korumaları var.** Forum ve kayıt onayı göçleri, veri varken geri
  alınmayı reddediyor (`RAISE EXCEPTION`). Bu kasıtlı: o veriler geri getirilemez.
- **JWT durumsuz, 2 saat.** Parola değişince açık oturumlar düşmüyor.
- **Hesap silme ucu yok.** Gizlilik metni e-posta ile talep diyor; talebi elle
  karşılaman gerekiyor.
