# PeerLearn — Bildirilen kusurların regresyon testi
#
# Bu paket, kod denetiminde bulunup düzeltilen kusurları KİLİTLER. Her bölüm, düzeltme
# geri alınırsa kırmızıya dönecek biçimde yazıldı — "artık çalışıyor" demek yetmez,
# "bir daha bozulamaz" olmalı.
#
# Kapsam:
#   A. Portföyden çıkarılan konu yeniden eklenebiliyor (409 çıkmazı)
#   B. Cüzdan yeni ekonomiyi doğru gösteriyor: basılan puan, birikimli toplam, unvan
#   C. Tekrar-kanıt sinyali yükleyene sızmıyor
#   D. Doğrulama e-postası yeniden gönderilebiliyor (hesap kilitlenmiyor)
#   E. Yaptırım ANINDA etkili ve geri alınabilir (ban / askı / rol)
#
# KAPSAMDAN ÇIKARILAN İDDİA (yeni ekonomi sözleşmesi):
#   B bölümü eskiden "cüzdanda görünen bakiye HARCANABİLİR bakiye olmalı" kusurunu
#   kilitliyordu: vadesi geçmiş ama süpürücü henüz koşmamış kredi, kullanıcıya
#   harcayabilirmiş gibi gösteriliyordu. Yeni modelde öğrenci ders için HİÇBİR ŞEY
#   ödemiyor (escrow/hold yok) ve derste kazanılan puanın vadesi de yok (süresiz lot).
#   Yani "gösterilen bakiye ile gerçekten harcanabilen bakiye ayrışması" diye bir kusur
#   sınıfı artık üretilemiyor; ölçtüğü alanlar (availableBalance / lockedBalance /
#   pendingExpirySweep) cüzdan DTO'sundan tamamen kalktı. Bölüm silinmedi, aynı soruyu
#   yeni modelde soracak biçimde yeniden yazıldı: cüzdan, emeğin karşılığını (basılan
#   puan, birikimli toplam, ondan türeyen unvan) DOĞRU gösteriyor mu.
#
# NOT (kodlama): Bu dosya UTF-8 BOM ile saklanmalı. PS 5.1, BOM'suz betiği ANSI okuyup
# Türkçe harfleri bozar; B bölümünde seviye adları ('1. Seviye') SUNUCUDAN dönen
# değerle karşılaştırıldığı için bozulan bir literal testi sahte kırmızıya düşürür.

$ErrorActionPreference = 'Stop'
$Api = 'http://localhost:5000'
$Psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'

$script:Pass = 0; $script:Fail = 0
function OK($m) { $script:Pass++; Write-Host "  [OK] $m" -ForegroundColor Green }
function Fail($m) { $script:Fail++; Write-Host "  [KALDI] $m" -ForegroundColor Red }
function Section($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

function Sql($q) {
    $f = Join-Path $env:TEMP "pl-fix-$([Guid]::NewGuid().ToString('N')).sql"
    [IO.File]::WriteAllText($f, $q, [Text.UTF8Encoding]::new($false))
    try { & $Psql -h localhost -U peerlearn -d peerlearn -t -A -f $f } finally { Remove-Item $f -Force }
}
function Send($method, $path, $body, $token) {
    $h = @{}; if ($token) { $h['Authorization'] = "Bearer $token" }
    # Gövdesiz istek (DELETE): null'ı JSON'a çevirmeye çalışmak ConvertTo-Json'da patlar.
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
    $r = Send Post '/api/auth/register' @{ email = $email; password = 'Demo12345'; displayName = "$prefix $stamp"; hwidHash = $hwid } $null
    Send Post '/api/auth/verify-email' @{ token = $r.verificationToken } $null | Out-Null
    $l = Send Post '/api/auth/login' @{ email = $email; password = 'Demo12345'; hwidHash = $hwid } $null
    [pscustomobject]@{ Email = $email; Token = $l.accessToken; UserId = $l.userId; Hwid = $hwid }
}

# Dersi geçmişe alır: time-lock penceresi açılmadan tamamlama reddedilir.
function GecmiseAl($sessionId) {
    Sql @"
UPDATE scheduling."LessonSessions"
SET "ScheduledStartUtc" = now() - interval '2 hours',
    "ScheduledEndUtc"   = now() - interval '1 hour'
WHERE "Id" = '$sessionId';
"@ | Out-Null
}

# Kanıt yükleme multipart olduğu için bu paket eskiden bu yolu hiç kullanmıyordu (bkz. C
# bölümü, satırı doğrudan DB'ye yazıyor). B bölümünde artık şart: puan BASIMI yalnızca
# onayla gerçekleşiyor, onay da tamamlanmış (kanıtlı) ders istiyor. Cüzdanı SQL ile
# doldurmak, tam da sınamak istediğimiz basım yolunu atlayıp defteri sahteleştirirdi.
function KanitYukle($sessionId, $kod, $token) {
    $png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==')
    $sinir = [Guid]::NewGuid().ToString('N')
    $enc = [Text.Encoding]::UTF8
    $bas = "--$sinir`r`nContent-Disposition: form-data; name=`"verificationCode`"`r`n`r`n$kod`r`n" +
           "--$sinir`r`nContent-Disposition: form-data; name=`"proof`"; filename=`"proof.png`"`r`nContent-Type: image/png`r`n`r`n"
    $ms = New-Object System.IO.MemoryStream
    $basB = $enc.GetBytes($bas); $sonB = $enc.GetBytes("`r`n--$sinir--`r`n")
    $ms.Write($basB, 0, $basB.Length); $ms.Write($png, 0, $png.Length); $ms.Write($sonB, 0, $sonB.Length)
    $govde = $ms.ToArray(); $ms.Dispose()
    Invoke-RestMethod -Uri "$Api/api/sessions/$sessionId/complete" -Method Post `
        -Headers @{ Authorization = "Bearer $token" } `
        -ContentType "multipart/form-data; boundary=$sinir" -Body $govde -TimeoutSec 60
}

$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
Write-Host "PeerLearn — kusur regresyon paketi" -ForegroundColor White
Write-Host "koşum: $stamp"

# ---------------------------------------------------------------------------
Section 'A. Portföyden çıkarılan konu yeniden eklenebilmeli'

$u = NewUser 'fixa' $stamp
$topics = Get_ '/api/catalog/topics' $u.Token
$topic = $topics | Select-Object -First 1

$e1 = Send Post '/api/portfolio/entries' @{ topicId = $topic.topicId; direction = 'Offer'; selfAssessedLevel = 3; note = 'ilk hâli' } $u.Token
OK 'konu portföye eklendi'

Send Delete "/api/portfolio/entries/$e1" $null $u.Token | Out-Null
$sonra = @(Get_ '/api/portfolio/entries' $u.Token | Where-Object { $_.entryId -eq $e1 })
if ($sonra.Count -eq 0) { OK 'konu listeden düştü' } else { Fail 'kaldırma listeden düşürmedi' }

# ASIL KUSUR: kaldırılan konu yeniden eklenemiyordu (soft-delete + filtresiz unique index).
$e2 = Send Post '/api/portfolio/entries' @{ topicId = $topic.topicId; direction = 'Offer'; selfAssessedLevel = 5; note = 'yeni hâli' } $u.Token
OK 'kaldırılan konu YENİDEN eklenebildi'

# @() ŞART: PS 5.1'de tek nesne dönen Where-Object sonucunun .Count'u boştur (dizi değil).
$liste = Get_ '/api/portfolio/entries' $u.Token
$geri = @($liste | Where-Object { $_.topicId -eq $topic.topicId -and $_.direction -eq 'Offer' })
if ($geri.Count -eq 1) { OK 'tek satır döndü (kopya oluşmadı)' } else { Fail "satır sayısı: $($geri.Count)" }
if ($geri[0].selfAssessedLevel -eq 5 -and $geri[0].note -eq 'yeni hâli') { OK 'yeni seviye ve not uygulandı' }
else { Fail "seviye=$($geri[0].selfAssessedLevel) not=$($geri[0].note)" }

$dbSatir = (Sql "SELECT COUNT(*) FROM matchmaking.""PortfolioEntries"" WHERE ""UserId"" = '$($u.UserId)' AND ""TopicId"" = '$($topic.topicId)' AND ""Direction"" = 'Offer';").Trim()
if ($dbSatir -eq '1') { OK 'veritabanında da tek satır (canlandırıldı, kopyalanmadı)' } else { Fail "db satır: $dbSatir" }

# Aktif kayıt hâlâ çift eklenemez.
try {
    Send Post '/api/portfolio/entries' @{ topicId = $topic.topicId; direction = 'Offer'; selfAssessedLevel = 2 } $u.Token | Out-Null
    Fail 'aktif kayıt ikinci kez eklenebildi'
} catch { if ((HataKodu $_) -eq 409) { OK 'aktif kayıt hâlâ çift eklenemiyor (409)' } else { Fail "beklenen 409, gelen $(HataKodu $_)" } }

# ---------------------------------------------------------------------------
Section 'B. Cüzdan emeğin karşılığını doğru göstermeli (basım / toplam / unvan)'

# NEDEN BU BÖLÜM DEĞİŞTİ: eski hâli "görünen bakiye harcanabilir mi" sorusunu sınıyordu.
# Yeni ekonomide harcama yolu YOK (öğrenci ödemiyor, escrow yok) ve kazanç lotlarının
# vadesi de yok — o kusur sınıfı imkânsız. Kusurun ardındaki asıl endişe duruyor:
# "cüzdan, kullanıcıya GERÇEĞİ mi gösteriyor?" Bu yüzden aynı yerde artık cüzdanın yeni
# sözleşmesi kilitleniyor: basılan puan, birikimli toplam ve ondan türeyen unvan.

$ogrB = NewUser 'fixb1' $stamp   # öğrenci: hiçbir şey ödemeyecek
$egtB = NewUser 'fixb2' $stamp   # eğitmen: puan basılacak taraf

Send Post '/api/portfolio/entries' @{ topicId = $topic.topicId; direction = 'Offer'; selfAssessedLevel = 5 } $egtB.Token | Out-Null
Send Post '/api/portfolio/entries' @{ topicId = $topic.topicId; direction = 'Seek'; selfAssessedLevel = 1 } $ogrB.Token | Out-Null
$midB = Send Post '/api/matches' @{ responderUserId = $egtB.UserId; requestedTopicId = $topic.topicId } $ogrB.Token
Send Post "/api/matches/$midB/respond" @{ accept = $true } $egtB.Token | Out-Null

# Escrow çağının alanları DTO'dan tamamen kalkmalı: birinin geri sızması, arayüzün yine
# kullanıcıya var olmayan bir "bloke/harcanabilir" ayrımı anlatması demek olur.
$w0 = Get_ '/api/wallet' $egtB.Token
$eskiAlanlar = @(@('availableBalance', 'lockedBalance', 'pendingExpirySweep') | Where-Object { $null -ne $w0.$_ })
if ($eskiAlanlar.Count -eq 0) { OK 'escrow çağının alanları cüzdandan kalktı' }
else { Fail "cüzdan hâlâ eski alan dönüyor: $($eskiAlanlar -join ', ')" }

$eksikAlanlar = @(@('totalEarnedCredits', 'currentBalance', 'rankTitle', 'rankEmoji', 'activeLots') | Where-Object { $null -eq $w0.$_ })
if ($eksikAlanlar.Count -eq 0) { OK 'yeni cüzdan alanları eksiksiz dönüyor' }
else { Fail "cüzdanda eksik alan: $($eksikAlanlar -join ', ')" }
if ($w0.rankTitle -eq '1. Seviye') { OK 'yeni kullanici 1. Seviye ile basliyor' } else { Fail "başlangıç unvanı: $($w0.rankTitle)" }

# Mutlak değil FARK karşılaştırıyoruz: hoş geldin kredisinin birikimli toplama sayılıp
# sayılmadığı bu bölümün konusu değil, testi ona bağlamak kırılganlık olurdu.
$oncekiOgr = Get_ '/api/wallet' $ogrB.Token
$oncekiEgt = $w0
$hamOnce = (Sql "SELECT ""AvailableBalance"" FROM economy.""Wallets"" WHERE ""UserId"" = '$($ogrB.UserId)';").Trim()

$rez = Send Post '/api/sessions' @{ matchId = $midB; topicId = $topic.topicId; scheduledStartUtc = ([DateTime]::UtcNow.AddHours(2).ToString('o')); durationMinutes = 60 } $ogrB.Token
if ($rez.mintAmount -eq 100) { OK '60 dk ders 100 puanlık basım olarak kuruldu (her 30 dk = 50)' }
else { Fail "mintAmount: $($rez.mintAmount) (60 dk için 100 bekleniyor)" }
if ($rez.isVolunteer -eq $false) { OK 'gönüllülük artık ayrı bayrakta ve bu ders gönüllü değil' }
else { Fail "isVolunteer: $($rez.isVolunteer) (alan yoksa da buraya düşer)" }
# Ad değişikliği sessizce geri alınırsa istemci eski alanı okumaya devam eder.
if ($null -eq $rez.creditCost) { OK 'eski creditCost alanı yanıttan kalktı' } else { Fail "creditCost hâlâ dönüyor: $($rez.creditCost)" }

# ASIL DEĞİŞMEZ 1: rezervasyon öğrencinin cebine DOKUNMAZ (ödeme diye bir şey yok).
$rezSonrasiOgr = Get_ '/api/wallet' $ogrB.Token
if ($rezSonrasiOgr.currentBalance -eq $oncekiOgr.currentBalance) { OK 'rezervasyon öğrencinin bakiyesine dokunmadı' }
else { Fail "öğrenci bakiyesi değişti: $($oncekiOgr.currentBalance) -> $($rezSonrasiOgr.currentBalance)" }

$hamSonra = (Sql "SELECT ""AvailableBalance"" FROM economy.""Wallets"" WHERE ""UserId"" = '$($ogrB.UserId)';").Trim()
if ($hamSonra -eq $hamOnce) { OK "defterdeki ham bakiye de aynı ($hamSonra)" } else { Fail "ham bakiye $hamOnce -> $hamSonra" }

# ASIL DEĞİŞMEZ 2: escrow satırı hiç yazılmamalı (göç eskileri kapattı, yenisi doğmamalı).
$holdSayisi = (Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';").Trim()
if ($holdSayisi -eq '0') { OK 'rezervasyonda CreditHold oluşmadı' } else { Fail "hold satırı: $holdSayisi" }

# --- Basım: dersi geçmişe al, eğitmen kanıtı yükler, öğrenci onaylar ---
GecmiseAl $rez.sessionId
KanitYukle $rez.sessionId $rez.verificationCode $egtB.Token | Out-Null

$onay = Send Post "/api/sessions/$($rez.sessionId)/approve" $null $ogrB.Token
if ($onay.creditsMinted -eq 100) { OK 'onayda 100 puan basıldı (creditsMinted)' } else { Fail "creditsMinted: $($onay.creditsMinted)" }
if ($null -eq $onay.creditsTransferred) { OK 'eski creditsTransferred alanı kalktı (aktarım değil, basım)' }
else { Fail "creditsTransferred hâlâ dönüyor: $($onay.creditsTransferred)" }

$egtCuzdan = Get_ '/api/wallet' $egtB.Token
if ($egtCuzdan.totalEarnedCredits -eq ($oncekiEgt.totalEarnedCredits + 100)) { OK "birikimli toplam 100 arttı ($($oncekiEgt.totalEarnedCredits) -> $($egtCuzdan.totalEarnedCredits))" }
else { Fail "birikimli toplam: $($oncekiEgt.totalEarnedCredits) -> $($egtCuzdan.totalEarnedCredits) (100 artmalıydı)" }
if ($egtCuzdan.currentBalance -eq ($oncekiEgt.currentBalance + 100)) { OK 'güncel bakiye de 100 arttı' }
else { Fail "güncel bakiye: $($oncekiEgt.currentBalance) -> $($egtCuzdan.currentBalance)" }

# Cüzdandaki toplam ile kolonun kendisi ayrışırsa kullanıcı unvanını yanlış görür.
$dbToplam = (Sql "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$($egtB.UserId)';").Trim()
if ($dbToplam -eq [string]$egtCuzdan.totalEarnedCredits) { OK "cüzdandaki toplam, identity.Users.TotalEarnedCredits ile birebir ($dbToplam)" }
else { Fail "db toplam=$dbToplam, cüzdan toplam=$($egtCuzdan.totalEarnedCredits)" }

$kazanc = @($egtCuzdan.activeLots | Where-Object { $_.source -eq 'LessonEarning' })
if ($kazanc.Count -ge 1) { OK 'kazanç lotu cüzdanda görünüyor' } else { Fail 'kazanç lotu cüzdanda yok' }
if ($kazanc.Count -ge 1 -and $null -eq $kazanc[0].expiresAtUtc) { OK 'yeni kazancın vadesi yok (süresiz lot)' }
else { Fail "kazanç lotunun vadesi: $($kazanc[0].expiresAtUtc) — yeni kazançlar süresiz olmalı" }

$lotVade = (Sql "SELECT COALESCE(""ExpiresAtUtc""::text, 'NULL') FROM economy.""CreditLots"" WHERE ""SourceSessionId"" = '$($rez.sessionId)';").Trim()
if ($lotVade -eq 'NULL') { OK 'defterdeki lot da vadesiz — süpürücü bu puanı yakamaz' } else { Fail "lot vadesi: $lotVade" }

# Ders başına TEK basım: ikinci onay reddedilse bile asıl kanıt, toplamın kıpırdamaması.
try {
    Send Post "/api/sessions/$($rez.sessionId)/approve" $null $ogrB.Token | Out-Null
    Fail 'ikinci onay geçti'
} catch { OK "ikinci onay reddedildi ($(HataKodu $_))" }
$toplamSonra = (Sql "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$($egtB.UserId)';").Trim()
if ($toplamSonra -eq $dbToplam) { OK 'ikinci onay ikinci basım yapmadı (ders başına tek basım)' }
else { Fail "ikinci basım oldu: $dbToplam -> $toplamSonra" }

# Basım yoktan üretiliyor: karşı tarafın cebinden çıkmıyor.
$ogrSon = Get_ '/api/wallet' $ogrB.Token
if ($ogrSon.currentBalance -eq $oncekiOgr.currentBalance -and $ogrSon.totalEarnedCredits -eq $oncekiOgr.totalEarnedCredits) {
    OK 'öğrencinin bakiyesi ve toplamı ders boyunca hiç değişmedi'
} else { Fail "öğrenci: bakiye $($oncekiOgr.currentBalance)->$($ogrSon.currentBalance), toplam $($oncekiOgr.totalEarnedCredits)->$($ogrSon.totalEarnedCredits)" }

# --- Unvan eşikleri (alt sınır DAHİL, üst sınır HARİÇ) ---
# Puanı doğrudan kolona yazıyoruz: 10.000 puanı gerçek derslerle biriktirmek yüzlerce ders
# demekti. Burada sınanan zaten OKUMA yolu — cüzdan, birikmiş toplamı doğru unvana çeviriyor mu.
$unvanKul = NewUser 'fixunv' $stamp
$esikler = @(
    # SEVIYE SISTEMI (2026-08-21): unvan adlari yerine 1-10 arasi seviye. Esikler arasi
    # fark her adimda buyur - yukselmek gittikce zorlassin diye. Sinir ALT DAHIL, UST HARIC.
    @{ Puan = 0;     Unvan = '1. Seviye';  Sonraki = 200 },
    @{ Puan = 199;   Unvan = '1. Seviye';  Sonraki = 200 },
    @{ Puan = 200;   Unvan = '2. Seviye';  Sonraki = 500 },
    @{ Puan = 499;   Unvan = '2. Seviye';  Sonraki = 500 },
    @{ Puan = 500;   Unvan = '3. Seviye';  Sonraki = 1000 },
    @{ Puan = 1000;  Unvan = '4. Seviye';  Sonraki = 2000 },
    @{ Puan = 2000;  Unvan = '5. Seviye';  Sonraki = 3500 },
    @{ Puan = 3500;  Unvan = '6. Seviye';  Sonraki = 6000 },
    @{ Puan = 6000;  Unvan = '7. Seviye';  Sonraki = 10000 },
    @{ Puan = 10000; Unvan = '8. Seviye';  Sonraki = 16000 },
    @{ Puan = 16000; Unvan = '9. Seviye';  Sonraki = 25000 },
    @{ Puan = 25000; Unvan = '10. Seviye'; Sonraki = $null }
)
$emojiEksik = 0
foreach ($e in $esikler) {
    Sql "UPDATE identity.""Users"" SET ""TotalEarnedCredits"" = $($e.Puan) WHERE ""Id"" = '$($unvanKul.UserId)';" | Out-Null
    $wu = Get_ '/api/wallet' $unvanKul.Token
    if ([string]::IsNullOrWhiteSpace($wu.rankEmoji)) { $emojiEksik++ }
    $sonrakiMetin = if ($null -eq $e.Sonraki) { 'yok (en üst unvan)' } else { $e.Sonraki }
    if ($wu.totalEarnedCredits -eq $e.Puan -and $wu.rankTitle -eq $e.Unvan -and $wu.nextRankAt -eq $e.Sonraki) {
        OK "$($e.Puan) puan -> $($e.Unvan), sonraki eşik: $sonrakiMetin"
    } else {
        Fail "$($e.Puan) puan -> unvan=$($wu.rankTitle) toplam=$($wu.totalEarnedCredits) sonraki=$($wu.nextRankAt) (beklenen: $($e.Unvan)/$sonrakiMetin)"
    }
}
if ($emojiEksik -eq 0) { OK 'her unvan bir rozet emojisiyle dönüyor' } else { Fail "emojisiz unvan sayısı: $emojiEksik" }

# Bu kullanıcının puanını sıfırla: değeri elle şişirdik, panelin küresel toplamlarını
# ölçen diğer paketlere yalan taşımasın. (Uygulama yolunda bu kolon asla azalmaz.)
Sql "UPDATE identity.""Users"" SET ""TotalEarnedCredits"" = 0 WHERE ""Id"" = '$($unvanKul.UserId)';" | Out-Null

# ---------------------------------------------------------------------------
Section 'C. Tekrar-kanıt sinyali yükleyene sızmamalı'

# Burada sınanan şey tespitin kendisi değil, tespitin KİME gösterildiği; o yüzden tekrar-kanıt
# satırı ikinci bir ders kurmak yerine doğrudan DB'ye yazılıyor. (Gerçek yükleme yolu B
# bölümündeki KanitYukle ile, hash tespiti ise e2e-smoke ile kapsanıyor.)
$egitmen = NewUser 'fixc1' $stamp
$ogrenci = NewUser 'fixc2' $stamp

Sql @"
INSERT INTO catalog."Topics" ("Id","SubjectId","Name","SortOrder","IsActive","CreatedAtUtc")
SELECT gen_random_uuid(), s."Id", 'Fix Konu $stamp', 960, TRUE, now()
FROM catalog."Subjects" s ORDER BY s."Name" LIMIT 1;
"@ | Out-Null
$fixTopic = (Sql "SELECT ""Id"" FROM catalog.""Topics"" WHERE ""Name"" = 'Fix Konu $stamp';").Trim()

Send Post '/api/portfolio/entries' @{ topicId = $fixTopic; direction = 'Offer'; selfAssessedLevel = 5 } $egitmen.Token | Out-Null
Send Post '/api/portfolio/entries' @{ topicId = $fixTopic; direction = 'Seek'; selfAssessedLevel = 1 } $ogrenci.Token | Out-Null

$mid = Send Post '/api/matches' @{ responderUserId = $egitmen.UserId; requestedTopicId = $fixTopic } $ogrenci.Token
Send Post "/api/matches/$mid/respond" @{ accept = $true } $egitmen.Token | Out-Null
$ses = Send Post '/api/sessions' @{ matchId = $mid; topicId = $fixTopic; scheduledStartUtc = ([DateTime]::UtcNow.AddHours(2).ToString('o')); durationMinutes = 60 } $ogrenci.Token

# Tekrar kullanılmış kanıt satırı (IsDuplicateHash = TRUE).
Sql @"
INSERT INTO scheduling."SessionProofs"
  ("Id","SessionId","UploadedByUserId","StorageKey","Sha256Hash","ContentType","FileSizeBytes","IsDuplicateHash","Status","CreatedAtUtc")
VALUES (gen_random_uuid(), '$($ses.sessionId)', '$($egitmen.UserId)', 'fix-$stamp.png',
        'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'image/png', 1234, TRUE, 'Pending', now());
"@ | Out-Null

$egitmenGoruyor = Get_ "/api/sessions/$($ses.sessionId)/proofs" $egitmen.Token
$ogrenciGoruyor = Get_ "/api/sessions/$($ses.sessionId)/proofs" $ogrenci.Token

if ($egitmenGoruyor[0].isDuplicateHash -eq $false) { OK 'YÜKLEYEN eğitmen "yakalandın" sinyalini GÖRMÜYOR' }
else { Fail 'sinyal yükleyene sızıyor — görseli kırpıp tespiti boşa çıkarabilir' }
if ($ogrenciGoruyor[0].isDuplicateHash -eq $true) { OK 'karşı taraf (öğrenci) uyarıyı görüyor' }
else { Fail 'öğrenci uyarıyı göremiyor — onay kararını verecek olan o' }

$dbBayrak = (Sql "SELECT ""IsDuplicateHash"" FROM scheduling.""SessionProofs"" WHERE ""SessionId"" = '$($ses.sessionId)';").Trim()
if ($dbBayrak -eq 't') { OK 'veritabanındaki bayrak korunuyor (yalnızca GÖSTERİM daraltıldı)' }
else { Fail "db bayrağı: $dbBayrak" }

# ---------------------------------------------------------------------------
Section 'D. Doğrulama e-postası yeniden gönderilebilmeli'

# Doğrulanmamış hesap: kayıt olup doğrulama YAPMIYORUZ.
$bekleyenMail = "fixd$stamp@test.dev"
$reg = Send Post '/api/auth/register' @{ email = $bekleyenMail; password = 'Demo12345'; displayName = "Fixd $stamp"; hwidHash = (NewHwid) } $null

try {
    Send Post '/api/auth/login' @{ email = $bekleyenMail; password = 'Demo12345'; hwidHash = (NewHwid) } $null | Out-Null
    Fail 'doğrulanmamış hesap giriş yapabildi'
} catch { OK "doğrulanmamış hesap giriş yapamıyor ($(HataKodu $_))" }

try {
    Send Post '/api/auth/register' @{ email = $bekleyenMail; password = 'Demo12345'; displayName = 'tekrar'; hwidHash = (NewHwid) } $null | Out-Null
    Fail 'aynı e-postayla yeniden kayıt olunabildi'
} catch { OK "aynı e-postayla yeniden kayıt kapalı ($(HataKodu $_)) — çıkış yolu yalnızca yeniden gönderim" }

$yeniden = Send Post '/api/auth/resend-verification' @{ email = $bekleyenMail } $null
if ($yeniden.verificationToken) { OK 'yeni doğrulama token''ı üretildi' } else { Fail 'token dönmedi' }

$dogrula = Send Post '/api/auth/verify-email' @{ token = $yeniden.verificationToken } $null
if ($dogrula.welcomeCreditGranted) { OK 'yeni token ile doğrulandı ve hoş geldin kredisi verildi' }
else { Fail 'yeni token ile doğrulanamadı' }

$giris = Send Post '/api/auth/login' @{ email = $bekleyenMail; password = 'Demo12345'; hwidHash = (NewHwid) } $null
if ($giris.accessToken) { OK 'hesap artık giriş yapabiliyor (kilit açıldı)' } else { Fail 'giriş hâlâ kapalı' }

# KULLANICI NUMARALANDIRMASI: kayıtlı olmayan adres de AYNI yanıtı almalı.
$yok = Send Post '/api/auth/resend-verification' @{ email = "hicyok$stamp@test.dev" } $null
if ($null -ne $yok -and -not $yok.verificationToken) { OK 'kayıtlı olmayan adres için de hata yok, token yok (varlık sızmıyor)' }
else { Fail 'kayıtsız adres farklı davrandı — e-posta varlığı sızıyor' }

# Zaten doğrulanmış hesap için token ÜRETİLMEZ.
$zaten = Send Post '/api/auth/resend-verification' @{ email = $bekleyenMail } $null
if (-not $zaten.verificationToken) { OK 'doğrulanmış hesaba yeni token üretilmiyor' }
else { Fail 'doğrulanmış hesaba token üretildi' }

# ---------------------------------------------------------------------------
Section 'E. Yaptırım ANINDA etkili olmalı'

$admin = NewUser 'fixadm' $stamp
Sql "UPDATE identity.""Users"" SET ""Role"" = 'Admin' WHERE ""Id"" = '$($admin.UserId)';" | Out-Null
$admin.Token = (Send Post '/api/auth/login' @{ email = $admin.Email; password = 'Demo12345'; hwidHash = $admin.Hwid } $null).accessToken

$kurban = NewUser 'fixban' $stamp
# Token ban'DAN ÖNCE alındı ve JWT ömrü 2 saat: asıl sınanan şey bu token'ın anında
# işlevsiz kalması. Eskiden ban yalnızca Status'u değiştiriyordu ve bu token 2 saat
# daha ders rezerve edip sohbet edebiliyordu.
Get_ '/api/wallet' $kurban.Token | Out-Null
OK 'ban öncesi token çalışıyor'

Send Post "/api/admin/users/$($kurban.UserId)/ban" @{ reason = 'Regresyon testi: anında etki' } $admin.Token | Out-Null
try {
    Get_ '/api/wallet' $kurban.Token | Out-Null
    Fail 'BANLI kullanıcının eski token''ı hâlâ çalışıyor'
} catch { if ((HataKodu $_) -eq 403) { OK 'banlı kullanıcının eski token''ı ANINDA reddedildi (403)' } else { Fail "beklenen 403, gelen $(HataKodu $_)" } }

# --- Ban geri alınabilmeli ---
$geriAl = Send Post "/api/admin/users/$($kurban.UserId)/unban" @{ reason = 'Hatalı ban, geri alındı' } $admin.Token
if ($geriAl.devicesUnbanned -ge 1) { OK "cihaz banları da kaldırıldı ($($geriAl.devicesUnbanned))" }
else { Fail "cihaz banı kaldırılmadı: $($geriAl.devicesUnbanned)" }

Get_ '/api/wallet' $kurban.Token | Out-Null
OK 'ban kalkınca eski token yeniden çalışıyor'

$cihazBan = (Sql "SELECT COUNT(*) FROM moderation.""HwidBans"" WHERE ""RelatedUserId"" = '$($kurban.UserId)' AND ""IsActive"" = TRUE;").Trim()
if ($cihazBan -eq '0') { OK 'aktif cihaz banı kalmadı (yeni hesap açabilir)' } else { Fail "aktif cihaz banı: $cihazBan" }

# --- Geçici askı ---
$askili = NewUser 'fixask' $stamp
Send Post "/api/admin/users/$($askili.UserId)/sanction" @{ type = 'TemporaryBan'; reason = 'Regresyon testi: süreli askı'; durationHours = 24 } $admin.Token | Out-Null
try {
    Get_ '/api/wallet' $askili.Token | Out-Null
    Fail 'askıya alınan kullanıcı hâlâ erişebiliyor'
} catch { if ((HataKodu $_) -eq 403) { OK 'askıya alınan kullanıcı erişemiyor (403)' } else { Fail "beklenen 403, gelen $(HataKodu $_)" } }

# Süre dolduğunda erişim, arka plan işini BEKLEMEDEN geri gelmeli.
Sql "UPDATE identity.""Users"" SET ""SuspendedUntilUtc"" = now() - interval '1 hour' WHERE ""Id"" = '$($askili.UserId)';" | Out-Null
Get_ '/api/wallet' $askili.Token | Out-Null
OK 'süresi dolan askı, iş koşmadan da erişimi serbest bıraktı'

# Uyarı hesabı KISITLAMAZ: amacı kayda geçmek.
$uyarilan = NewUser 'fixuya' $stamp
Send Post "/api/admin/users/$($uyarilan.UserId)/sanction" @{ type = 'Warning'; reason = 'Regresyon testi: uyarı' } $admin.Token | Out-Null
Get_ '/api/wallet' $uyarilan.Token | Out-Null
OK 'uyarı erişimi kısıtlamadı'

$yaptirim = (Sql "SELECT COUNT(*) FROM moderation.""UserSanctions"" WHERE ""UserId"" = '$($uyarilan.UserId)' AND ""Type"" = 'Warning';").Trim()
if ($yaptirim -eq '1') { OK 'uyarı yaptırım kaydına yazıldı' } else { Fail "uyarı kaydı: $yaptirim" }

# --- Rol atama ---
$aday = NewUser 'fixrol' $stamp
Send Put "/api/admin/users/$($aday.UserId)/role" @{ role = 'Moderator' } $admin.Token | Out-Null
$yeniRol = (Sql "SELECT ""Role"" FROM identity.""Users"" WHERE ""Id"" = '$($aday.UserId)';").Trim()
if ($yeniRol -eq 'Moderator') { OK 'rol atandı (elle SQL gerekmiyor)' } else { Fail "rol: $yeniRol" }

try {
    Send Put "/api/admin/users/$($admin.UserId)/role" @{ role = 'Student' } $admin.Token | Out-Null
    Fail 'yönetici kendi rolünü düşürebildi'
} catch { if ((HataKodu $_) -eq 409) { OK 'kendi rolünü değiştiremiyor (sistem yöneticisiz kalmaz)' } else { Fail "beklenen 409, gelen $(HataKodu $_)" } }

$izKaydi = @((Get_ '/api/admin/audit-log?pageSize=50' $admin.Token).items | Where-Object { $_.action -in @('UserUnbanned','UserSanctioned','RoleChanged') })
if ($izKaydi.Count -ge 3) { OK "yeni yaptırım işlemleri denetim izine yazıldı ($($izKaydi.Count) kayıt)" }
else { Fail "denetim izi kaydı: $($izKaydi.Count)" }

Write-Host "`n================================" -ForegroundColor White
if ($script:Fail -eq 0) { Write-Host "TÜM ADIMLAR BAŞARILI ($script:Pass kontrol)" -ForegroundColor Green }
else { Write-Host "$script:Fail KONTROL BAŞARISIZ ($script:Pass geçti)" -ForegroundColor Red; exit 1 }
Write-Host "================================" -ForegroundColor White
