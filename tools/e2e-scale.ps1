# PeerLearn — Ölçek ve veri paketi (E grubu)
#
# Kapsam:
#   A. Derslerim sayfalama — aktif/geçmiş ayrımı, geçmişin sayfalanması, sessiz kesme yok
#   B. Eşleşme sonlandırma — sohbet salt okunur, rezervasyon kapalı, açık ders korumalı
#   C. Eşleşme isteğinin süresi dolumu — süpürücü Pending'i Expired yapar, yeniden istek açılır
#   D. Süpürücüde geri çekilme — takılan kayıt partiyi TIKAMAZ
#   E. Depo temizliği — saklama süresi, artık dosya, emniyet sürgüsü
#   F. Ekonomi paneli — tek taramaya indirgenen sorgular aynı sayıları veriyor
#
# PUAN EKONOMİSİNE UYARLAMA (bu paketi doğrudan etkileyen üç şey):
#   • Öğrenci ders için ÖDEMİYOR: escrow, hold, yetersiz kredi diye bir şey yok. A bölümü
#     bunun yerine "rezervasyon öğrencinin bakiyesine DOKUNMADI" değişmezini doğruluyor.
#   • Onay eğitmene puan BASAR (30 dk → 50, 60 dk → 100) ve unvanın dayandığı birikimli
#     sayacı (identity."Users"."TotalEarnedCredits") artırır. D bölümü artık bunu da ölçüyor.
#   • SUİSTİMAL FRENİ (MintGuard): aynı eğitmen-öğrenci çifti 24 saatte en fazla 2 ders
#     açabilir. Sayım "ScheduledStartUtc >= şimdi−24 saat" koşuluyla yapılıyor ve ÜST SINIRI
#     YOK — yani gelecekteki tüm dersler sayılıyor, ileri tarihe yaymak tavandan kaçırmıyor.
#     Bu yüzden A bölümündeki üç ders ÜÇ AYRI EĞİTMENE dağıtıldı.
#
# KALDIRILAN KAPSAM (yeni modelde karşılığı kalmadı):
#   • D bölümündeki "cüzdansız kullanıcı" zehri ve onun ön koşul doğrulaması: onay yolu
#     artık öğrenci cüzdanını hiç okumuyor, eğitmen cüzdanını da yoksa oluşturuyor
#     (CreditLedgerService.EnsureWalletAsync). Cüzdansızlık hiçbir tarafta hata üretmediği
#     için o kurulum sessizce ETKİSİZ kalırdı — yani zehirsiz bir "zehirli kayıt" testi.
#     Yerine ders başına tek basımı koruyan benzersiz index'i tetikleyen bir zehir kondu;
#     bölümün asıl iddiası (takılan kayıt partiyi tıkamaz) aynen duruyor.
#
# Ön koşullar: PostgreSQL, API :5000.

$ErrorActionPreference = 'Stop'
$Api = 'http://localhost:5000'
$Psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'

<#
  psql üç yoldan aranır; ilk bulunan kullanılır:
    1. Windows kurulumu   — geliştiricinin makinesinde tipik yol.
    2. PATH üzerinde psql — Linux/macOS ve CI koşucuları (postgresql-client).
    3. docker compose     — makinede psql yok ama compose yığını ayakta (bkz. Sql).

  Üçüncü yol olmadan bu paket, docs/GELISTIRME-ORTAMI.md nin tarif ettiği Docker
  kurulumunda hiç koşamıyordu: yalnızca yerel PostgreSQL 17 varsayılıyordu ve betik
  "psql bulunamadi" ile ortasında düşüyordu. Testin KOŞAMAMASI, başarısız olmasından
  daha sinsi — özet onu kırmızı değil, hiç görünmemiş sayar.
#>
if (-not (Test-Path $Psql)) {
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    $bulunan = if ($psqlCmd) { $psqlCmd.Source } else { $null }
    if ($bulunan) { $Psql = $bulunan } else { $Psql = $null }
}
$script:ComposeYml = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'
$env:PGPASSWORD = 'PeerLearnDev2026'

$script:Pass = 0; $script:Fail = 0
function OK($m) { $script:Pass++; Write-Host "  [OK] $m" -ForegroundColor Green }
function Fail($m) { $script:Fail++; Write-Host "  [KALDI] $m" -ForegroundColor Red }
function Section($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

function Sql($q) {
    if (-not $Psql) {
        # Docker yolu: sorgu STDIN den geçer. -c ile argüman olarak geçirmek,
        # identity."Users" gibi tırnaklı adlardaki tırnakları kabuğa yedirir.
        $out = $q | docker compose -f $script:ComposeYml exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
        return ($out -join '').Trim()
    }
    $f = Join-Path $env:TEMP "pl-scale-$([Guid]::NewGuid().ToString('N')).sql"
    [IO.File]::WriteAllText($f, $q, [Text.UTF8Encoding]::new($false))
    try { & $Psql -h localhost -U peerlearn -d peerlearn -t -A -f $f } finally { Remove-Item $f -Force }
}
function Send($method, $path, $body, $token) {
    $h = @{}; if ($token) { $h['Authorization'] = "Bearer $token" }
    if ($null -eq $body) {
        return Invoke-RestMethod -Uri "$Api$path" -Method $method -Headers $h
    }
    $json = $body | ConvertTo-Json -Depth 8
    Invoke-RestMethod -Uri "$Api$path" -Method $method -Headers $h -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json))
}
function Get_($path, $token) { Invoke-RestMethod -Uri "$Api$path" -Headers @{ Authorization = "Bearer $token" } }
function HataKodu($e) { [int]$e.Exception.Response.StatusCode }
function NewHwid { -join ((1..64) | ForEach-Object { '0123456789abcdef'[(Get-Random -Max 16)] }) }
function NewUser($prefix, $stamp) {
    $hwid = NewHwid; $email = "$prefix$stamp@test.dev"
    $r = Send Post '/api/auth/register' @{ email = $email; password = 'Demo12345'; displayName = "$prefix $stamp"; termsVersion = '2026-08-27'; ageConfirmed = $true; hwidHash = $hwid } $null
    Send Post '/api/auth/verify-email' @{ email = $email; code = $r.verificationToken } $null | Out-Null
    $l = Send Post '/api/auth/login' @{ email = $email; password = 'Demo12345'; hwidHash = $hwid } $null
    [pscustomobject]@{ Email = $email; Token = $l.accessToken; UserId = $l.userId; Hwid = $hwid }
}

# Konu üretimi: her testin kendi konusu olsun ki portföy çakışması olmasın.
function NewTopic($ad) {
    Sql @"
INSERT INTO catalog."Topics" ("Id","SubjectId","Name","SortOrder","IsActive","CreatedAtUtc")
SELECT gen_random_uuid(), s."Id", '$ad', 970, TRUE, now()
FROM catalog."Subjects" s ORDER BY s."Name" LIMIT 1;
"@ | Out-Null
    (Sql "SELECT ""Id"" FROM catalog.""Topics"" WHERE ""Name"" = '$ad';").Trim()
}

# Kabul edilmiş bir eşleşme kurar (öğrenci = istek başlatan, eğitmen = yanıtlayan).
function NewMatch($ogrenci, $egitmen, $topicId) {
    Send Post '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 5 } $egitmen.Token | Out-Null
    $mid = Send Post '/api/matches' @{ responderUserId = $egitmen.UserId; requestedTopicId = $topicId } $ogrenci.Token
    Send Post "/api/matches/$mid/respond" @{ accept = $true } $egitmen.Token | Out-Null
    $mid
}

# İlanı gönüllüye çeker. ARTIK KREDİ YETERSİZLİĞİ İÇİN DEĞİL — öğrenci hiçbir şey ödemiyor,
# dolayısıyla ders sayısını sınırlayan bir bakiye de yok. Bugünkü işlevi tersi: gönüllü ilan
# üzerinden açılan derste rezervasyon anında LessonSessions."IsVolunteer" TRUE kopyalanır ve
# onay hiç puan BASMAZ. Yani bu yardımcı, ekonomiye dokunmaması gereken kurulum derslerini
# basım yan etkisinden arındırmak için kullanılıyor; basımı ÖLÇEN bölümlerde çağrılmamalı.
function GonulluYap($egitmen, $topicId) {
    Sql "UPDATE matchmaking.""PortfolioEntries"" SET ""IsVolunteer"" = TRUE WHERE ""UserId"" = '$($egitmen.UserId)' AND ""TopicId"" = '$topicId';" | Out-Null
}

$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
Write-Host "PeerLearn — ölçek ve veri paketi" -ForegroundColor White
Write-Host "koşum: $stamp"

$admin = NewUser 'scaleadmin' $stamp
Sql "UPDATE identity.""Users"" SET ""Role"" = 'Admin' WHERE ""Id"" = '$($admin.UserId)';" | Out-Null
$adminT = (Send Post '/api/auth/login' @{ email = $admin.Email; password = 'Demo12345'; hwidHash = $admin.Hwid } $null).accessToken

# ---------------------------------------------------------------------------
Section 'A. Derslerim: aktif tam, geçmiş sayfalı'

$ogrA = NewUser 'scaa1' $stamp
$topicA = NewTopic "Olcek A $stamp"

# ÜÇ AYRI EĞİTMEN — MintGuard yüzünden zorunlu. Suistimal freni aynı eğitmen-öğrenci
# çiftine 24 saatte 2 ders veriyor; sayımın üst zaman sınırı olmadığı için ileri tarihe
# yaymak da kurtarmıyor (gelecekteki her ders sayılıyor). Testin asıl amacı BİR ÖĞRENCİNİN
# ders listesini sayfalamak; dersleri farklı eğitmenlere dağıtmak bu amacı bozmuyor, üstelik
# listenin tek eşleşmeye bağlı olmadığını da gösteriyor.
$egtlerA = @(1..3 | ForEach-Object { NewUser "scaa2$_" $stamp })
$matchlerA = @($egtlerA | ForEach-Object { NewMatch $ogrA $_ $topicA })

# Yanıt biçimi: aktif + geçmiş ayrı. Eski hâlde düz bir dizi dönüyordu.
$liste0 = Get_ '/api/sessions' $ogrA.Token
if ($null -ne $liste0.active -and $null -ne $liste0.past) { OK 'yanıt aktif/geçmiş olarak ayrıldı' }
else { Fail 'yanıt hâlâ tek düz liste' }
if ($liste0.past.pageSize -gt 0 -and $null -ne $liste0.past.totalCount) { OK 'geçmiş sayfalı (pageSize/totalCount döndü)' }
else { Fail 'geçmiş sayfalama alanları yok' }

# Rezervasyonun öğrenci cüzdanına dokunmadığını ölçebilmek için ÖNCEKİ hâl saklanıyor.
# Tek sorguda iki alan: iki ayrı okuma arasında araya girecek bir iş kalmasın.
$bakiyeSorgusuA = "SELECT ""AvailableBalance"" FROM economy.""Wallets"" WHERE ""UserId"" = '$($ogrA.UserId)';"
$bakiyeOnceA = (Sql $bakiyeSorgusuA).Trim()

# 3 ders: 1 aktif (Booked), 2 nihai (Completed/Cancelled) — geçmişe düşmeli.
$sesIds = @()
for ($i = 0; $i -lt 3; $i++) {
    $s = Send Post '/api/sessions' @{ matchId = $matchlerA[$i]; topicId = $topicA; scheduledStartUtc = ([DateTime]::UtcNow.AddDays($i + 1).ToString('o')); durationMinutes = 60 } $ogrA.Token
    $sesIds += $s.sessionId

    if ($i -eq 0) {
        # Ödül ölçeği ve alan adı: 60 dk → 100 puan, ve tutar artık "creditCost" değil
        # "mintAmount". Ad, tutarın ÖĞRENCİDEN ALINMADIĞINI söylediği için testin konusu.
        if ($s.mintAmount -eq 100) { OK 'rezervasyon yanıtı mintAmount=100 verdi (60 dk)' }
        else { Fail "mintAmount: $($s.mintAmount) — 60 dk için 100 bekleniyordu" }
        if ($s.isVolunteer -eq $false) { OK 'rezervasyon yanıtı isVolunteer alanını taşıyor' }
        else { Fail "isVolunteer: $($s.isVolunteer)" }
        if ($null -eq $s.creditCost) { OK 'eski creditCost alanı yanıttan kalktı' }
        else { Fail "creditCost hâlâ dönüyor: $($s.creditCost)" }
    }
}

# YENİ DEĞİŞMEZ: rezervasyon öğrencinin bakiyesine DOKUNMAZ. Eskiden burada kredi bloke
# edilir (Available düşer, Locked artardı); artık ne düşen ne bloke edilen bir şey var.
$bakiyeSonraA = (Sql $bakiyeSorgusuA).Trim()
if ($bakiyeSonraA -eq $bakiyeOnceA) { OK "3 rezervasyon öğrenci bakiyesini değiştirmedi ($bakiyeOnceA)" }
else { Fail "öğrenci bakiyesi $bakiyeOnceA → $bakiyeSonraA — rezervasyon cüzdana dokunuyor" }

# Bakiye toplamı aynı kalsa bile blokenin kendisi yazılmış olabilirdi (Available → Locked
# taşıması iki alanı da değiştirir ama toplamı korur), o yüzden hold satırı AYRICA sayılıyor.
# Yalnızca bu koşumun dersleri sorgulanıyor: göç öncesinden kalan kayıtlar sonucu bulandırmasın.
$holdSayisi = (Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';").Trim()
if ($holdSayisi -eq '0') { OK 'rezervasyonda hiç CreditHold yazılmadı' }
else { Fail "$holdSayisi adet CreditHold yazılmış — escrow yolu hâlâ canlı" }

Sql "UPDATE scheduling.""LessonSessions"" SET ""Status"" = 'Completed' WHERE ""Id"" = '$($sesIds[1])';" | Out-Null
Sql "UPDATE scheduling.""LessonSessions"" SET ""Status"" = 'Cancelled' WHERE ""Id"" = '$($sesIds[2])';" | Out-Null

$liste1 = Get_ '/api/sessions?pastPage=1&pastPageSize=1' $ogrA.Token
$aktifSayi = @($liste1.active).Count
if ($aktifSayi -eq 1) { OK "aktif ders tam döndü ($aktifSayi)" } else { Fail "aktif ders sayısı: $aktifSayi" }
if ($liste1.past.totalCount -eq 2) { OK 'geçmiş toplamı doğru (2)' } else { Fail "geçmiş toplam: $($liste1.past.totalCount)" }

# ASIL KUSUR: sayfa boyutu geçmişi kısıtlamalı ama AKTİFİ ASLA kısıtlamamalı.
if (@($liste1.past.items).Count -eq 1) { OK 'geçmiş sayfa boyutuna uydu (1 kayıt)' }
else { Fail "geçmiş ilk sayfa: $(@($liste1.past.items).Count)" }
if ($liste1.activeTotal -eq 1) { OK 'activeTotal gerçek sayıyı bildiriyor' } else { Fail "activeTotal: $($liste1.activeTotal)" }

# Liste satırı da yeni sözleşmeye uymalı: tutar mintAmount adıyla geliyor, yanında
# isVolunteer var, eski creditCost yok. Arayüz bu satırdan "kaç puan kazanılacak" yazıyor;
# alan sessizce eski adda kalsaydı öğrenciye "ödeyeceğin tutar" olarak görünmeye devam ederdi.
$satirA = @($liste1.active)[0]
if ($satirA.mintAmount -eq 100 -and $null -eq $satirA.creditCost) { OK 'liste satırı mintAmount taşıyor, creditCost kalktı' }
else { Fail "liste satırı mintAmount=$($satirA.mintAmount), creditCost=$($satirA.creditCost)" }
if ($null -ne $satirA.isVolunteer) { OK 'liste satırında isVolunteer alanı var' } else { Fail 'liste satırında isVolunteer yok' }

# İkinci sayfa BAŞKA kaydı vermeli (aynı kaydı tekrar vermemeli).
$liste2 = Get_ '/api/sessions?pastPage=2&pastPageSize=1' $ogrA.Token
$s1 = @($liste1.past.items)[0].sessionId
$s2 = @($liste2.past.items)[0].sessionId
if ($s1 -ne $s2) { OK 'ikinci sayfa farklı kaydı verdi (sayfalama gerçek)' } else { Fail 'iki sayfa aynı kaydı verdi' }

# Aktif ders geçmişe SIZMAMALI ve tersi de olmamalı.
$aktifDurumlar = @($liste1.active | ForEach-Object { $_.status })
$sizinti = @($aktifDurumlar | Where-Object { $_ -in @('Completed', 'Cancelled', 'Expired') })
if ($sizinti.Count -eq 0) { OK 'nihai durumdaki ders aktif listesine sızmadı' } else { Fail "aktifte nihai durum: $($sizinti -join ',')" }

# ---------------------------------------------------------------------------
Section 'B. Eşleşme sonlandırma'

$ogrB = NewUser 'scab1' $stamp
$egtB = NewUser 'scab2' $stamp
$topicB = NewTopic "Olcek B $stamp"
$matchB = NewMatch $ogrB $egtB $topicB

$sohbetB = (Get_ '/api/conversations' $ogrB.Token | Where-Object { $_.matchId -eq $matchB }).conversationId
Send Post "/api/conversations/$sohbetB/messages" @{ content = 'kapatmadan önceki mesaj' } $ogrB.Token | Out-Null
OK 'açık eşleşmede mesaj gönderilebiliyor'

# AÇIK DERS VARKEN KAPATILAMAZ. Gerekçe değişti ama kural aynı kaldı: eskiden askıda kalan
# şey öğrencinin bloke kredisiydi, şimdi eğitmenin HENÜZ BASILMAMIŞ ödülü. Eşleşme kapanınca
# ders ne onaylanabilir ne itiraz edilebilir hâle gelirdi; emeği verilmiş ders karşılıksız kalırdı.
$sesB = Send Post '/api/sessions' @{ matchId = $matchB; topicId = $topicB; scheduledStartUtc = ([DateTime]::UtcNow.AddDays(1).ToString('o')); durationMinutes = 60 } $ogrB.Token
try {
    Send Post "/api/matches/$matchB/close" $null $ogrB.Token | Out-Null
    Fail 'açık ders varken eşleşme kapatılabildi (eğitmenin bekleyen ödülü sahipsiz kalırdı)'
} catch { if ((HataKodu $_) -eq 409) { OK 'açık ders varken kapatma reddedildi (409)' } else { Fail "beklenen 409, gelen $(HataKodu $_)" } }

Send Post "/api/sessions/$($sesB.sessionId)/cancel" @{ reason = 'test' } $ogrB.Token | Out-Null

$kapatma = Send Post "/api/matches/$matchB/close" $null $ogrB.Token
if ($kapatma.status -eq 'Closed') { OK 'eşleşme sonlandırıldı' } else { Fail "durum: $($kapatma.status)" }

# İDEMPOTENT: iki taraf aynı anda basarsa ikincisi hata almamalı.
$kapatma2 = Send Post "/api/matches/$matchB/close" $null $egtB.Token
if ($kapatma2.alreadyClosed) { OK 'ikinci kapatma hata değil, idempotent' } else { Fail 'ikinci kapatma alreadyClosed dönmedi' }

# Kapatan da karşı taraf da YAZAMAZ.
foreach ($kisi in @(@{ N = 'kapatan'; T = $ogrB.Token }, @{ N = 'karşı taraf'; T = $egtB.Token })) {
    try {
        Send Post "/api/conversations/$sohbetB/messages" @{ content = 'kapandıktan sonra' } $kisi.T | Out-Null
        Fail "$($kisi.N) kapalı sohbete mesaj yazabildi"
    } catch { if ((HataKodu $_) -eq 403) { OK "$($kisi.N) kapalı sohbete yazamıyor (403)" } else { Fail "beklenen 403, gelen $(HataKodu $_)" } }
}

# ...ama GEÇMİŞ OKUNABİLİR kalmalı. Kapatmayı "geçmişi sil"e çevirmek, tacizciye kanıt
# yok etme düğmesi vermek olurdu.
$gecmis = Get_ "/api/conversations/$sohbetB/messages" $ogrB.Token
if (@($gecmis).Count -ge 1) { OK 'kapalı sohbetin geçmişi hâlâ okunabiliyor' } else { Fail 'kapalı sohbetin geçmişi kayboldu' }

$sohbetListe = @(Get_ '/api/conversations' $ogrB.Token | Where-Object { $_.conversationId -eq $sohbetB })
if ($sohbetListe.Count -eq 1) { OK 'kapalı sohbet listede kaldı' } else { Fail 'kapalı sohbet listeden düştü' }
if ($sohbetListe[0].isClosed) { OK 'liste kapalı olduğunu bildiriyor (isClosed)' } else { Fail 'isClosed bayrağı gelmedi' }

# Okundu işaretleme kapalı sohbette de çalışmalı: aksi halde rozet kalıcı takılırdı.
Send Post "/api/conversations/$sohbetB/read" $null $egtB.Token | Out-Null
OK 'kapalı sohbette okundu işaretlenebiliyor'

# Beklenen 409'un MintGuard'ın 429'una dönüşme riski yok: çiftin tek dersi iptal edildi
# (iptaller tavan sayımına girmiyor) ve eşleşme durumu kontrolü frenden ÖNCE çalışıyor.
try {
    Send Post '/api/sessions' @{ matchId = $matchB; topicId = $topicB; scheduledStartUtc = ([DateTime]::UtcNow.AddDays(2).ToString('o')); durationMinutes = 60 } $ogrB.Token | Out-Null
    Fail 'kapalı eşleşmeden ders rezerve edilebildi'
} catch { if ((HataKodu $_) -eq 409) { OK 'kapalı eşleşmeden ders rezerve edilemiyor (409)' } else { Fail "beklenen 409, gelen $(HataKodu $_)" } }

# Kapalı eşleşme "aktif eşleşmeler" listesinde görünmemeli.
$aktifEslesme = @((Get_ '/api/matches' $ogrB.Token).active | Where-Object { $_.matchId -eq $matchB })
if ($aktifEslesme.Count -eq 0) { OK 'kapalı eşleşme aktif listeden düştü' } else { Fail 'kapalı eşleşme hâlâ aktif listede' }

# Yabancı kapatamaz.
$yabanci = NewUser 'scab3' $stamp
$ogrB2 = NewUser 'scab4' $stamp
$topicB2 = NewTopic "Olcek B2 $stamp"
$matchB2 = NewMatch $ogrB2 $egtB $topicB2
try {
    Send Post "/api/matches/$matchB2/close" $null $yabanci.Token | Out-Null
    Fail 'taraf olmayan eşleşmeyi kapatabildi'
} catch { if ((HataKodu $_) -eq 403) { OK 'taraf olmayan kapatamıyor (403)' } else { Fail "beklenen 403, gelen $(HataKodu $_)" } }

# ---------------------------------------------------------------------------
Section 'C. Yanıtsız eşleşme isteğinin süresi dolmalı'

$ogrC = NewUser 'scac1' $stamp
$egtC = NewUser 'scac2' $stamp
$topicC = NewTopic "Olcek C $stamp"
Send Post '/api/portfolio/entries' @{ topicId = $topicC; direction = 'Offer'; selfAssessedLevel = 4 } $egtC.Token | Out-Null

$matchC = Send Post '/api/matches' @{ responderUserId = $egtC.UserId; requestedTopicId = $topicC } $ogrC.Token

# Aynı kişiye aynı konu için ikinci istek Pending'ken engelli.
try {
    Send Post '/api/matches' @{ responderUserId = $egtC.UserId; requestedTopicId = $topicC } $ogrC.Token | Out-Null
    Fail 'bekleyen istek varken ikincisi atılabildi'
} catch { if ((HataKodu $_) -eq 409) { OK 'bekleyen istek mükerrer atılamıyor (409)' } else { Fail "beklenen 409, gelen $(HataKodu $_)" } }

# İsteği 20 gün geriye al (eşik 14 gün).
Sql "UPDATE matchmaking.""Matches"" SET ""CreatedAtUtc"" = now() - interval '20 days' WHERE ""Id"" = '$matchC';" | Out-Null

$sup = Send Post '/api/admin/jobs/session-sweep' $null $adminT
if ($sup.expiredMatches -ge 1) { OK "süpürücü $($sup.expiredMatches) isteği süresi dolmuş saydı" }
else { Fail "expiredMatches: $($sup.expiredMatches)" }

$durumC = (Sql "SELECT ""Status"" FROM matchmaking.""Matches"" WHERE ""Id"" = '$matchC';").Trim()
if ($durumC -eq 'Expired') { OK 'istek Expired oldu' } else { Fail "durum: $durumC" }

# RespondedAtUtc null KALMALI: süre dolumu bir yanıt değildir; ⚡ rozet ortancası bunu
# "görmezden gelinmiş istek" saymaya devam etmeli.
$yanitC = (Sql "SELECT COALESCE(""RespondedAtUtc""::text, 'NULL') FROM matchmaking.""Matches"" WHERE ""Id"" = '$matchC';").Trim()
if ($yanitC -eq 'NULL') { OK 'RespondedAtUtc null kaldı (süre dolumu yanıt sayılmıyor)' }
else { Fail "RespondedAtUtc yazılmış: $yanitC — hızlı yanıt ortancası bozulur" }

# ASIL KAZANÇ: süresi dolan istek gönderenin önünü açmalı.
$matchC2 = Send Post '/api/matches' @{ responderUserId = $egtC.UserId; requestedTopicId = $topicC } $ogrC.Token
if ($matchC2 -and $matchC2 -ne $matchC) { OK 'süresi dolduktan sonra yeniden istek atılabildi' }
else { Fail 'yeniden istek atılamadı' }

# Taze bir istek SÜPÜRÜLMEMELİ (eşik gerçekten uygulanıyor mu).
Send Post '/api/admin/jobs/session-sweep' $null $adminT | Out-Null
$durumC2 = (Sql "SELECT ""Status"" FROM matchmaking.""Matches"" WHERE ""Id"" = '$matchC2';").Trim()
if ($durumC2 -eq 'Pending') { OK 'taze istek süpürülmedi (eşik uygulanıyor)' } else { Fail "taze istek durumu: $durumC2" }

# ---------------------------------------------------------------------------
Section 'D. Süpürücüde geri çekilme: takılan kayıt partiyi tıkamamalı'

# Kurulum: onay bekleyen İKİ ders. Birincisi "zehirli", ikincisi sağlıklı. Zehirli olan
# tarihte ÖNCE geliyor, yani sıralamada başta; geri çekilme olmasaydı her turda partiyi
# o dolduracak ve sağlıklı olan hiç işlenmeyecekti.
$ogrD = NewUser 'scad1' $stamp
$egtD = NewUser 'scad2' $stamp
$topicD = NewTopic "Olcek D $stamp"
$matchD = NewMatch $ogrD $egtD $topicD

# GÖNÜLLÜ YAPILMIYOR (eskiden yapılıyordu): bu bölümün ölçtüğü şey otomatik onayın gerçekten
# BASIM denemesi. Gönüllü derste basım yolu hiçbir şey yazmadan döner; ne aşağıdaki zehir
# tetiklenir ne de sağlıklı dersin "işlendi" kanıtı bir anlam taşırdı.
# İki ders, MintGuard'ın çift başına 24 saatlik tavanına (2) tam oturuyor.
$dersler = @()
foreach ($i in 1..2) {
    $s = Send Post '/api/sessions' @{ matchId = $matchD; topicId = $topicD; scheduledStartUtc = ([DateTime]::UtcNow.AddDays($i).ToString('o')); durationMinutes = 60 } $ogrD.Token
    $dersler += $s.sessionId
}

# İkisini de onay bekler yap; zehirli olanın tamamlama isteği DAHA ESKİ.
Sql @"
UPDATE scheduling."LessonSessions"
SET "Status" = 'AwaitingApproval', "CompletionRequestedAtUtc" = now() - interval '10 days'
WHERE "Id" = '$($dersler[0])';
UPDATE scheduling."LessonSessions"
SET "Status" = 'AwaitingApproval', "CompletionRequestedAtUtc" = now() - interval '5 days'
WHERE "Id" = '$($dersler[1])';
"@ | Out-Null

# ZEHİRLEME — şema bozmadan, ama YENİ bir mekanizmayla.
#
# Eski zehir (dersin öğrencisini cüzdansız bir kullanıcıya çevirmek) artık hiçbir şey
# yapmıyor: onay yolu öğrenci cüzdanını HİÇ okumuyor (ders ücretsiz) ve eğitmen cüzdanını
# da SingleAsync ile değil EnsureWalletAsync ile alıyor — yoksa oluşturuyor. O kurulum
# bugün "zehirsiz zehir" olur, testi sessizce geçerdi.
#
# Yeni zehir, yeni modelin kendi korumasını kullanıyor: ders başına ikinci basımı yasaklayan
# kısmi benzersiz index (economy."CreditLots"."SourceSessionId", Source='LessonEarning').
# Derse ait bir kazanç lotu ÖNCEDEN yazılırsa süpürücünün basım INSERT'i her turda
# benzersizlik ihlaliyle düşer. Şema değişmiyor, FK'lar geçerli (cüzdan ve ders gerçek),
# ders satırına dokunulmuyor. RemainingAmount 0 seçildi: bakiye ve vade sayımlarına
# karışmasın, F bölümündeki panel karşılaştırmalarını kirletmesin.
$cuzdanD = (Sql "SELECT ""Id"" FROM economy.""Wallets"" WHERE ""UserId"" = '$($egtD.UserId)';").Trim()
Sql @"
INSERT INTO economy."CreditLots"
  ("Id","WalletId","InitialAmount","RemainingAmount","Source","SourceSessionId","EarnedAtUtc","ExpiresAtUtc","CreatedAtUtc")
VALUES
  (gen_random_uuid(), '$cuzdanD', 100, 0, 'LessonEarning', '$($dersler[0])', now(), NULL, now());
"@ | Out-Null

$zehirLot = (Sql "SELECT COUNT(*) FROM economy.""CreditLots"" WHERE ""SourceSessionId"" = '$($dersler[0])';").Trim()
if ($zehirLot -eq '1') { OK 'zehir kurulumu hazır (derse ait kazanç lotu önceden yazıldı)' }
else { Fail "kurulum geçersiz: zehir lotu sayısı $zehirLot — bu kayıt takılmaz, test anlamsızlaşır" }

$supD1 = Send Post '/api/admin/jobs/session-sweep' $null $adminT

$durumZehir = (Sql "SELECT ""Status"" FROM scheduling.""LessonSessions"" WHERE ""Id"" = '$($dersler[0])';").Trim()
$durumSaglam = (Sql "SELECT ""Status"" FROM scheduling.""LessonSessions"" WHERE ""Id"" = '$($dersler[1])';").Trim()

if ($durumSaglam -eq 'Completed') { OK 'sağlıklı ders zehirli kayda rağmen işlendi' }
else { Fail "sağlıklı ders durumu: $durumSaglam — sıra tıkanmış olabilir" }

if ($supD1.autoApproved -ge 1) { OK "süpürücü turu $($supD1.autoApproved) dersi onayladı" }
else { Fail "autoApproved: $($supD1.autoApproved) — parti hiç ilerlememiş" }

# Zehirli kayıt YARIM İŞLENMİŞ olmamalı: basım düştüyse durum da değişmemeli (tek transaction).
# Completed görünüp puanı basılmamış bir ders, sessizce kaybolan emek demektir.
if ($durumZehir -eq 'AwaitingApproval') { OK 'zehirli ders yarım işlenmedi (durum korundu)' }
else { Fail "zehirli ders durumu: $durumZehir — basım düşerken durum değişmiş" }

# "İşlendi" yeni modelde yalnızca durum değişikliği değil, BASIM demek: durum Completed olup
# puan basılmasaydı eğitmen dersi vermiş ama karşılığını almamış olurdu — sessiz ve
# geri dönüşü olmayan hasar. 60 dk → 100 puan, ve unvanın dayandığı sayaç aynı miktarda artmalı.
$kazancD = (Sql "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$($egtD.UserId)';").Trim()
if ($kazancD -eq '100') { OK 'onayda eğitmene 100 puan basıldı (TotalEarnedCredits)' }
else { Fail "TotalEarnedCredits: $kazancD — 100 bekleniyordu" }

# Kazanç lotu VADESİZ açılmalı: kazanılan puan yanmaz, aksi halde unvan kendiliğinden düşerdi.
$lotD = (Sql "SELECT COUNT(*) FROM economy.""CreditLots"" WHERE ""SourceSessionId"" = '$($dersler[1])' AND ""ExpiresAtUtc"" IS NULL;").Trim()
if ($lotD -eq '1') { OK 'kazanç süresiz lot olarak açıldı (ExpiresAtUtc NULL)' }
else { Fail "süresiz kazanç lotu sayısı: $lotD" }

# Öğrenci tarafı onayda da tamamen dışarıda: basımın karşı bacağı YOK.
$ogrDBakiye = (Sql "SELECT COUNT(*) FROM economy.""CreditTransactions"" t JOIN economy.""Wallets"" w ON w.""Id"" = t.""WalletId"" WHERE w.""UserId"" = '$($ogrD.UserId)' AND t.""RelatedSessionId"" = '$($dersler[1])';").Trim()
if ($ogrDBakiye -eq '0') { OK 'onay öğrenci defterine hiçbir hareket yazmadı' }
else { Fail "öğrenci tarafında $ogrDBakiye hareket var — basımın karşı bacağı yazılıyor" }

$hataSatiri = (Sql "SELECT COUNT(*) FROM scheduling.""SweepFailures"" WHERE ""RecordId"" = '$($dersler[0])';").Trim()
if ($hataSatiri -eq '1') { OK 'takılan kayıt için geri çekilme satırı yazıldı' }
else { Fail "SweepFailures satır sayısı: $hataSatiri" }

$sonraki = (Sql "SELECT CASE WHEN ""NextAttemptAtUtc"" > now() THEN 'ILERIDE' ELSE 'SIMDI' END FROM scheduling.""SweepFailures"" WHERE ""RecordId"" = '$($dersler[0])';").Trim()
if ($sonraki -eq 'ILERIDE') { OK 'sonraki deneme ileri bir ana ertelendi' } else { Fail "NextAttemptAtUtc: $sonraki" }

# ASIL KUSUR: ikinci turda takılan kayıt sorguya HİÇ girmemeli.
$sayac1 = (Sql "SELECT ""FailureCount"" FROM scheduling.""SweepFailures"" WHERE ""RecordId"" = '$($dersler[0])';").Trim()
Send Post '/api/admin/jobs/session-sweep' $null $adminT | Out-Null
$sayac2 = (Sql "SELECT ""FailureCount"" FROM scheduling.""SweepFailures"" WHERE ""RecordId"" = '$($dersler[0])';").Trim()
if ($sayac1 -eq $sayac2) { OK "takılan kayıt ikinci turda hiç denenmedi (sayaç $sayac1 sabit)" }
else { Fail "sayaç $sayac1 → $sayac2 — geri çekilme uygulanmıyor" }

# Bekleme süresi dolunca YENİDEN denenmeli (vazgeçmiyoruz).
Sql "UPDATE scheduling.""SweepFailures"" SET ""NextAttemptAtUtc"" = now() - interval '1 minute' WHERE ""RecordId"" = '$($dersler[0])';" | Out-Null
Send Post '/api/admin/jobs/session-sweep' $null $adminT | Out-Null
$sayac3 = (Sql "SELECT ""FailureCount"" FROM scheduling.""SweepFailures"" WHERE ""RecordId"" = '$($dersler[0])';").Trim()
if ([int]$sayac3 -gt [int]$sayac2) { OK "bekleme dolunca yeniden denendi (sayaç $sayac2 → $sayac3)" }
else { Fail "sayaç $sayac2 → $sayac3 — kayıt kalıcı olarak terk edilmiş" }

# Panel operatöre görünür kılmalı.
$metrikD = Get_ '/api/admin/metrics' $adminT
if ($metrikD.stuckSweepRecords -ge 1) { OK 'takılı kayıt hakem panelinde görünüyor' }
else { Fail "stuckSweepRecords: $($metrikD.stuckSweepRecords)" }

# Zehirli ders artık paketin geri kalanını (özellikle F'deki sayımları) etkilemesin diye
# nihai bir duruma alınıyor. Şema hiç değişmediği için geri alınacak DDL yok.
Sql "UPDATE scheduling.""LessonSessions"" SET ""Status"" = 'Cancelled' WHERE ""Id"" = '$($dersler[0])';" | Out-Null
Sql "DELETE FROM scheduling.""SweepFailures"" WHERE ""RecordId"" = '$($dersler[0])';" | Out-Null

# Zehir lotu da silinmeli: cüzdana hiç yazılmamış (RemainingAmount 0) bir kazanç lotu geride
# kalırsa lot geçmişini okuyan başka testler ve raporlar bunu gerçek bir kazanç sanar.
Sql "DELETE FROM economy.""CreditLots"" WHERE ""SourceSessionId"" = '$($dersler[0])';" | Out-Null
OK 'test kurulumu temizlendi'

# ---------------------------------------------------------------------------
Section 'E. Depo temizliği'

# Depo kökü: LocalProofStorage, ProofStorage:RootPath'i API'nin çalışma dizinine göre
# çözüyor ve dev'de bu dizin proje klasörü.
$storageRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\PeerLearn.Api\proof-storage'
if (-not (Test-Path $storageRoot)) { New-Item -ItemType Directory -Force -Path $storageRoot | Out-Null }

# Artık dosya: hiçbir kaydın işaret etmediği, YAŞLI bir dosya.
$artikDizin = Join-Path $storageRoot '2020\01'
New-Item -ItemType Directory -Force -Path $artikDizin | Out-Null
$artikDosya = Join-Path $artikDizin "artik$stamp.png"
[IO.File]::WriteAllBytes($artikDosya, [byte[]](0x89, 0x50, 0x4E, 0x47))
(Get-Item $artikDosya).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-30)

# Taze dosya: referanssız AMA genç — SİLİNMEMELİ (yükleme yarışı koruması).
$tazeDosya = Join-Path $artikDizin "taze$stamp.png"
[IO.File]::WriteAllBytes($tazeDosya, [byte[]](0x89, 0x50, 0x4E, 0x47))

$temizlik = Send Post '/api/admin/jobs/storage-cleanup' $null $adminT

if (-not (Test-Path $artikDosya)) { OK 'artık dosya silindi' } else { Fail 'artık dosya duruyor' }
if (Test-Path $tazeDosya) { OK 'taze dosya KORUNDU (yükleme yarışı penceresi)' }
else { Fail 'taze dosya silindi — yüklenmekte olan kanıt kaybolurdu' }

Remove-Item $tazeDosya -Force -ErrorAction SilentlyContinue

# Saklama süresi: nihai durumdaki dersin eski kanıtı silinmeli, satır KALMALI.
$ogrE = NewUser 'scae1' $stamp
$egtE = NewUser 'scae2' $stamp
$topicE = NewTopic "Olcek E $stamp"
$matchE = NewMatch $ogrE $egtE $topicE

# Bu bölümün dersleri yalnızca kanıt dosyalarının TAŞIYICISI; durumları doğrudan SQL ile
# kuruluyor. Gönüllü işaretlenmelerinin sebebi kredi yetersizliği değil (öyle bir sınır yok):
# ileride bir onay yolu bu derslere dokunursa ekonomiye puan basılmasın, F bölümündeki
# panel-veritabanı karşılaştırması bu paketin yan etkisiyle kaymasın.
GonulluYap $egtE $topicE
$sesE = Send Post '/api/sessions' @{ matchId = $matchE; topicId = $topicE; scheduledStartUtc = ([DateTime]::UtcNow.AddDays(1).ToString('o')); durationMinutes = 60 } $ogrE.Token

$kanitDizin = Join-Path $storageRoot '2020\02'
New-Item -ItemType Directory -Force -Path $kanitDizin | Out-Null
$kanitDosya = Join-Path $kanitDizin "kanit$stamp.png"
[IO.File]::WriteAllBytes($kanitDosya, [byte[]](0x89, 0x50, 0x4E, 0x47))
$kanitKey = "2020/02/kanit$stamp.png"

Sql @"
INSERT INTO scheduling."SessionProofs"
  ("Id","SessionId","UploadedByUserId","StorageKey","ContentType","FileSizeBytes","Sha256Hash","Status","IsDuplicateHash","CreatedAtUtc")
VALUES
  (gen_random_uuid(), '$($sesE.sessionId)', '$($egtE.UserId)', '$kanitKey', 'image/png', 4,
   repeat('e', 64), 'Pending', FALSE, now() - interval '200 days');
UPDATE scheduling."LessonSessions" SET "Status" = 'Completed' WHERE "Id" = '$($sesE.sessionId)';
"@ | Out-Null

# İtirazlı ders korunmalı: aynı yaşta ikinci bir kanıt, DISPUTED bir derste.
$sesE2 = Send Post '/api/sessions' @{ matchId = $matchE; topicId = $topicE; scheduledStartUtc = ([DateTime]::UtcNow.AddDays(3).ToString('o')); durationMinutes = 60 } $ogrE.Token
$kanitDosya2 = Join-Path $kanitDizin "itiraz$stamp.png"
[IO.File]::WriteAllBytes($kanitDosya2, [byte[]](0x89, 0x50, 0x4E, 0x47))
Sql @"
INSERT INTO scheduling."SessionProofs"
  ("Id","SessionId","UploadedByUserId","StorageKey","ContentType","FileSizeBytes","Sha256Hash","Status","IsDuplicateHash","CreatedAtUtc")
VALUES
  (gen_random_uuid(), '$($sesE2.sessionId)', '$($egtE.UserId)', '2020/02/itiraz$stamp.png', 'image/png', 4,
   repeat('f', 64), 'Pending', FALSE, now() - interval '200 days');
UPDATE scheduling."LessonSessions" SET "Status" = 'Disputed' WHERE "Id" = '$($sesE2.sessionId)';
"@ | Out-Null

Send Post '/api/admin/jobs/storage-cleanup' $null $adminT | Out-Null

if (-not (Test-Path $kanitDosya)) { OK 'saklama süresi dolan kanıt görseli silindi' } else { Fail 'eski kanıt görseli duruyor' }
if (Test-Path $kanitDosya2) { OK 'İTİRAZLI dersin kanıtı korundu (delil)' }
else { Fail 'itirazlı dersin kanıtı silindi — hakem delilsiz kalır' }

$satirVar = (Sql "SELECT COUNT(*) FROM scheduling.""SessionProofs"" WHERE ""StorageKey"" = '$kanitKey';").Trim()
if ($satirVar -eq '1') { OK 'kanıt SATIRI korundu (hash sahte kanıt tespiti için gerekli)' }
else { Fail "kanıt satırı silinmiş: $satirVar" }

$damga = (Sql "SELECT CASE WHEN ""ContentDeletedAtUtc"" IS NULL THEN 'NULL' ELSE 'DOLU' END FROM scheduling.""SessionProofs"" WHERE ""StorageKey"" = '$kanitKey';").Trim()
if ($damga -eq 'DOLU') { OK 'satır "içerik silindi" olarak damgalandı' } else { Fail "damga: $damga" }

# Uç, "bulunamadı" değil "süresi doldu" demeli (410).
try {
    $proofId = (Sql "SELECT ""Id"" FROM scheduling.""SessionProofs"" WHERE ""StorageKey"" = '$kanitKey';").Trim()
    Invoke-RestMethod -Uri "$Api/api/sessions/$($sesE.sessionId)/proofs/$proofId/content" -Headers @{ Authorization = "Bearer $($ogrE.Token)" } | Out-Null
    Fail 'silinmiş kanıt indirilebildi'
} catch { if ((HataKodu $_) -eq 410) { OK 'silinmiş kanıt 410 (süresi doldu) dönüyor' } else { Fail "beklenen 410, gelen $(HataKodu $_)" } }

Remove-Item $kanitDosya2 -Force -ErrorAction SilentlyContinue

# ---------------------------------------------------------------------------
Section 'F. Ekonomi paneli tek taramaya indirgendikten sonra da doğru'

$metrik = Get_ '/api/admin/metrics' $adminT

$dbAvailable = (Sql "SELECT COALESCE(SUM(""AvailableBalance""),0) FROM economy.""Wallets"";").Trim()
$dbLocked = (Sql "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='economy' AND table_name='Wallets' AND column_name='LockedBalance';").Trim()
$dbCuzdan = (Sql "SELECT COUNT(*) FROM economy.""Wallets"";").Trim()
# BASIM TANIMI GENİŞLEDİ: sıfır toplamlı modelde tek basım kaynağı hoş geldin kredisiydi
# (ders kazancı bir transferin bacağıydı, arzı büyütmezdi). Artık ders kazancı da
# karşılıksız basım; yalnızca WelcomeBonus'a bakan eski sorgu, onaylanan her derste
# paneli haksız yere yanlış gösterirdi.
$dbMinted = (Sql "SELECT COALESCE(SUM(""Amount""),0) FROM economy.""CreditTransactions"" WHERE ""Type"" IN ('WelcomeBonus', 'LessonEarning');").Trim()
$dbBooked = (Sql "SELECT COUNT(*) FROM scheduling.""LessonSessions"" WHERE ""Status"" = 'Booked';").Trim()
$dbAwaiting = (Sql "SELECT COUNT(*) FROM scheduling.""LessonSessions"" WHERE ""Status"" = 'AwaitingApproval';").Trim()
$dbDisputed = (Sql "SELECT COUNT(*) FROM scheduling.""LessonSessions"" WHERE ""Status"" = 'Disputed';").Trim()

if ($metrik.availableCredits -eq [int]$dbAvailable) { OK "kullanılabilir kredi doğru ($dbAvailable)" } else { Fail "panel $($metrik.availableCredits), db $dbAvailable" }
# BLOKE KREDİ ALANI KALDIRILDI (escrow söküldü). İddia artık "değer doğru mu" değil
# "alan gerçekten yok mu": panel bu kavramı bir daha göstermemeli.
if ($null -eq $metrik.PSObject.Properties['lockedCredits']) { OK 'panel yanıtında bloke kredi alanı yok (escrow kalktı)' }
else { Fail "panel hâlâ lockedCredits döndürüyor: $($metrik.lockedCredits)" }
if ($metrik.walletCount -eq [int]$dbCuzdan) { OK "cüzdan sayısı doğru ($dbCuzdan)" } else { Fail "panel $($metrik.walletCount), db $dbCuzdan" }
if ($metrik.totalMinted -eq [int]$dbMinted) { OK "basılan kredi doğru ($dbMinted)" } else { Fail "panel $($metrik.totalMinted), db $dbMinted" }
if ($metrik.activeSessions -eq [int]$dbBooked) { OK "rezerve ders sayısı doğru ($dbBooked)" } else { Fail "panel $($metrik.activeSessions), db $dbBooked" }
if ($metrik.awaitingApproval -eq [int]$dbAwaiting) { OK "onay bekleyen ders doğru ($dbAwaiting)" } else { Fail "panel $($metrik.awaitingApproval), db $dbAwaiting" }
if ($metrik.disputedSessions -eq [int]$dbDisputed) { OK "itirazlı ders doğru ($dbDisputed)" } else { Fail "panel $($metrik.disputedSessions), db $dbDisputed" }
if ($metrik.circulatingCredits -eq ([int]$dbAvailable + [int]$dbLocked)) { OK 'dolaşımdaki = kullanılabilir + bloke' }
else { Fail "dolaşımdaki $($metrik.circulatingCredits)" }

# DEFTER DENETİMİNİN TANIMI DEĞİŞTİ. Eski iddia "sıfır toplam"dı: basılan = harcanan,
# toplam sabit. Basım artık karşılıksız olduğu için o eşitlik her onaylanan derste yanlış
# alarm verirdi. Yeni iddia daha genel: cüzdanlardaki toplam == defterdeki TÜM hareketlerin
# toplamı, yani "defterde karşılığı olmadan bir cüzdana kredi yazılmamış".
# Bayrağa körlemesine güvenilmiyor; aynı hesap veritabanı üzerinde de yapılıp karşılaştırılıyor.
$dbDefter = (Sql "SELECT COALESCE(SUM(""Amount""),0) FROM economy.""CreditTransactions"";").Trim()
$dbDolasim = [int]$dbAvailable + [int]$dbLocked
if ($dbDolasim -eq [int]$dbDefter) { OK "defter değişmezi veritabanında tutuyor ($dbDolasim)" }
else { Fail "cüzdan toplamı $dbDolasim, defter toplamı $dbDefter — karşılıksız kredi var" }

if ($metrik.ledgerBalanced) { OK 'panel defter denetimi tutuyor (cüzdan toplamı = defter toplamı)' }
else { Fail 'DEFTER TUTMUYOR' }

# ---------------------------------------------------------------------------
Write-Host "`n================================" -ForegroundColor White
Write-Host "Geçen: $script:Pass   Kalan: $script:Fail" -ForegroundColor $(if ($script:Fail -eq 0) { 'Green' } else { 'Red' })
if ($script:Fail -gt 0) { exit 1 }

