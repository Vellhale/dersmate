# PeerLearn uçtan uca duman testi
# Senaryo: Ayşe (matematik anlatır, kimya öğrenir) <-> Berk (kimya anlatır, matematik öğrenir)
#
# KAPSAM NOTU — YENİ PUAN EKONOMİSİ
# Ders almak ücretsizleşti: öğrenciden kredi düşmüyor, escrow (CreditHold) yazılmıyor,
# onayda eğitmene puan BASILIYOR (her 30 dk için 50). Bu yüzden tek bir iddia KALDIRILDI
# ve yerine yenisi konmadı:
#   - "yetersiz kredi ikinci rezervasyonu engeller" (INSUFFICIENT_CREDITS). Ödeme yolu
#     tümüyle kalktığı için bu hatayı üretebilecek kod yolu da kalmadı — karşılığı yok,
#     sahte bir eşdeğer yazmak testi yalancı yapardı. Rezervasyonu sınırlayan fren artık
#     davranışsal tavan (MintGuard / MINT_LIMIT_REACHED); 7. adımda o ölçülüyor.
# Geri kalan eski iddiaların hiçbiri silinmedi, yeni modeldeki karşılıklarıyla
# değiştirildi: "öğrencinin bakiyesi 1 düştü" -> "eğitmene 100 puan basıldı VE
# TotalEarnedCredits 100 arttı VE öğrencinin bakiyesi DEĞİŞMEDİ".

$ErrorActionPreference = 'Stop'
$API = 'http://localhost:5000'
$PSQL = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'

<#
  psql'e üç yoldan erişilebilir; ilk bulunan kullanılır:
    1. Windows kurulumu   — geliştiricinin makinesinde tipik yol.
    2. PATH üzerinde psql — Linux/macOS ve CI koşucuları (postgresql-client).
    3. docker compose     — makinede psql yok ama compose yığını ayakta.

  Üçüncü yol OLMADAN bu paket, docs/GELISTIRME-ORTAMI.md'nin tarif ettiği Docker
  kurulumunda hiç koşamıyordu: yalnızca yerel PostgreSQL 17 kurulumu varsayılıyor ve
  betik "psql bulunamadi" ile ortasında düşüyordu. Testin koşamaması, testin
  başarısız olmasından daha sinsi — özet onu KIRMIZI değil, hiç görünmemiş sayıyor.
#>
if (-not (Test-Path $PSQL)) {
    # PS 5.1 UYUMU: `?.` null-koşullu operatörü PowerShell 7 ile geldi. Paketler
    # CLAUDE.md gereği 5.1 altında koşuyor ve orada bu bir SÖZDİZİMİ hatası —
    # betik hiç başlamaz, yani "psql yok" durumunu ele alacak kod hiç çalışmaz.
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    $bulunan = if ($psqlCmd) { $psqlCmd.Source } else { $null }
    if ($bulunan) { $PSQL = $bulunan } else { $PSQL = $null }
}
$script:ComposeYml = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'

$script:failures = 0
function Step($name)  { Write-Host "`n=== $name ===" -ForegroundColor Cyan }
function OK($msg)     { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Fail($msg)   { Write-Host "  [HATA] $msg" -ForegroundColor Red; $script:failures++ }

function Api {
    param($Method, $Path, $Body, $Token)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Uri = "$API$Path"; Method = $Method; Headers = $headers; TimeoutSec = 60 }
    if ($null -ne $Body) {
        # UTF-8 bayt olarak gönder: PS 5.1 string gövdeyi Latin-1 kodluyor ve Türkçe
        # karakterler (ör. 'Ü') geçersiz UTF-8 bayta dönüşüp sunucuda 400'e yol açıyor.
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 6))
        $params['ContentType'] = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @params
}

# Hata BEKLENEN çağrılar için: kod + mesaj döndürür
function ApiExpectError {
    param($Method, $Path, $Body, $Token)
    try {
        Api -Method $Method -Path $Path -Body $Body -Token $Token | Out-Null
        return $null
    } catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { return @{ code = 'NO_RESPONSE'; detail = $_.Exception.Message } }
        $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
        $raw = $reader.ReadToEnd()
        $obj = $null
        try { $obj = $raw | ConvertFrom-Json } catch {}
        if ($obj) { return @{ code = $obj.title; detail = $obj.detail; status = [int]$resp.StatusCode } }
        return @{ code = 'PARSE_FAIL'; detail = $raw }
    }
}

# PowerShell 5.1'de -Form yok: multipart gövdeyi elle kuruyoruz.
function UploadProof {
    param($SessionId, $Code, $Token)
    $pngB64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='
    $fileBytes = [Convert]::FromBase64String($pngB64)
    $boundary = [Guid]::NewGuid().ToString('N')
    $enc = [System.Text.Encoding]::UTF8
    $ms = New-Object System.IO.MemoryStream

    $head = "--$boundary`r`nContent-Disposition: form-data; name=`"verificationCode`"`r`n`r`n$Code`r`n" +
            "--$boundary`r`nContent-Disposition: form-data; name=`"proof`"; filename=`"proof.png`"`r`nContent-Type: image/png`r`n`r`n"
    $headBytes = $enc.GetBytes($head)
    $tailBytes = $enc.GetBytes("`r`n--$boundary--`r`n")

    $ms.Write($headBytes, 0, $headBytes.Length)
    $ms.Write($fileBytes, 0, $fileBytes.Length)
    $ms.Write($tailBytes, 0, $tailBytes.Length)
    $body = $ms.ToArray()
    $ms.Dispose()

    Invoke-RestMethod -Uri "$API/api/sessions/$SessionId/complete" -Method Post `
        -Headers @{ Authorization = "Bearer $Token" } `
        -ContentType "multipart/form-data; boundary=$boundary" -Body $body -TimeoutSec 60
}

# SQL'i dosyadan çalıştır: PowerShell 5.1 native komutlara argüman geçerken gömülü
# çift tırnakları yiyor ve PostgreSQL identifier'ları küçük harfe katlanıyordu.
function Sql($query) {
    if (-not $PSQL) {
        # Docker yolu: sorgu STDIN'den geçer. `-c` ile argüman olarak geçirmek,
        # identity."Users" gibi tırnaklı adlardaki tırnakları kabuğa yedirir.
        $out = $query | docker compose -f $script:ComposeYml exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
        return $out
    }
    $f = Join-Path $env:TEMP ("pl_" + [Guid]::NewGuid().ToString('N') + ".sql")
    Set-Content -Path $f -Value $query -Encoding UTF8
    $out = & $PSQL -h localhost -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 -f $f 2>&1
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    return $out
}

# Unvanın dayandığı birikimli sayacı DOĞRUDAN kullanıcı satırından okur: cüzdan DTO'su
# aynı sayıyı dönse de, iddia "veritabanına yazıldı" olduğu için kaynağından okunmalı.
# Sayı çözülemezse -1 döner; ham [int] cast'i boş çıktıda tüm koşumu keserdi
# ($ErrorActionPreference = 'Stop'), oysa burada istenen tek bir adımın başarısızlığı.
function EarnedCounter($userId) {
    $raw = ((Sql "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$userId';") -join '').Trim()
    if ($raw -match '^-?\d+$') { return [int]$raw }
    return -1
}

# Hata bekleyen herhangi bir çağrıyı sarmalar (multipart dahil).
function InvokeExpectError([scriptblock]$call) {
    try { & $call | Out-Null; return $null }
    catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { return @{ code = 'NO_RESPONSE'; detail = $_.Exception.Message } }
        $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
        $raw = $reader.ReadToEnd()
        try {
            $o = $raw | ConvertFrom-Json
            return @{ code = $o.title; detail = $o.detail; status = [int]$resp.StatusCode }
        } catch {
            return @{ code = 'PARSE_FAIL'; detail = $raw; status = [int]$resp.StatusCode }
        }
    }
}

# ---------------------------------------------------------------- 1. KAYIT + DOĞRULAMA
Step '1. Kayıt + e-posta doğrulama + hoş geldin kredisi'
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$ayse = @{ email = "ayse$stamp@test.dev"; password = 'Parola12345'; displayName = 'Ayşe Yılmaz' }
$berk = @{ email = "berk$stamp@test.dev"; password = 'Parola12345'; displayName = 'Berk Demir' }

$ayseReg = Api POST '/api/auth/register' $ayse
$berkReg = Api POST '/api/auth/register' $berk
OK "iki kullanıcı kaydedildi"

$v1 = Api POST '/api/auth/verify-email' @{ token = $ayseReg.verificationToken }
$v2 = Api POST '/api/auth/verify-email' @{ token = $berkReg.verificationToken }
if ($v1.welcomeCreditGranted -and $v2.welcomeCreditGranted) { OK 'hoş geldin kredisi verildi' } else { Fail 'hoş geldin kredisi verilmedi' }

# Tek seferlik guard: aynı token'la ikinci doğrulama kredi ÜRETMEMELİ
$v1again = Api POST '/api/auth/verify-email' @{ token = $ayseReg.verificationToken }
if (-not $v1again.welcomeCreditGranted) { OK 'ikinci doğrulama kredi üretmedi (tek seferlik guard)' } else { Fail 'İKİNCİ KEZ KREDİ ÜRETİLDİ' }

# ---------------------------------------------------------------- 2. GİRİŞ
Step '2. Giriş (HWID ile)'
$aT = (Api POST '/api/auth/login' @{ email = $ayse.email; password = $ayse.password; hwidHash = 'a' * 64 }).accessToken
$bT = (Api POST '/api/auth/login' @{ email = $berk.email; password = $berk.password; hwidHash = 'b' * 64 }).accessToken
if ($aT -and $bT) { OK 'iki kullanıcı giriş yaptı' } else { Fail 'giriş başarısız' }

$w = Api GET '/api/wallet' $null $aT
# Cüzdan DTO'su değişti: availableBalance/lockedBalance/pendingExpirySweep kalktı.
# Bakiye currentBalance'ta, unvanın dayandığı birikim totalEarnedCredits'te.
if ($w.currentBalance -eq 1) { OK "Ayşe bakiye: $($w.currentBalance) puan" } else { Fail "beklenen currentBalance 1, gelen $($w.currentBalance)" }

# Escrow kavramıyla birlikte kalkan alanlar GERÇEKTEN kalkmalı: eski adlar dönmeye devam
# ederse arayüz sessizce eski modeli göstermeye devam eder ve kimse fark etmez.
if ($null -eq $w.availableBalance -and $null -eq $w.lockedBalance) { OK 'eski escrow alanları (available/locked) DTO''dan kalktı' }
else { Fail "eski alanlar hâlâ dönüyor: available=$($w.availableBalance) locked=$($w.lockedBalance)" }

# Hoş geldin kredisi bir hediye, kazanç değil: unvan sayacına yazılmamalı.
if ($w.totalEarnedCredits -eq 0) { OK 'hoş geldin kredisi unvan sayacına sayılmadı' }
else { Fail "beklenen totalEarnedCredits 0, gelen $($w.totalEarnedCredits)" }

# Seviye sunucudan geliyor; 0 puanda 1. seviye ve sonraki eşik 100 olmalı.
# Seviye artık bir TAMSAYI: eski test unvan metnini sınamaktan kaçınıyordu (ad bir ürün
# kararı, eşik ise sözleşme) — rakamda böyle bir ayrım gerekmiyor, ikisi de sözleşme.
if ([int]$w.level -eq 1 -and $w.nextLevelAt -eq 100) { OK "başlangıç seviyesi: $($w.level) — sonraki eşik $($w.nextLevelAt)" }
else { Fail "seviye alanları eksik/yanlış: level=$($w.level) next=$($w.nextLevelAt)" }

# ---------------------------------------------------------------- 3. PORTFÖY
Step '3. İki yönlü portföy'
# KOŞUMA ÖZEL konular: paylaşılan katalog konuları kullanılırsa önceki koşulardan biriken
# onlarca aday öneri listesini doldurup (limit ile kesiliyor) bu koşunun eşleşmesini
# dışarıda bırakıyordu — algoritma doğru çalışsa bile test kırılıyordu.
function NewTopic($subjectName, $label) {
    $id = [Guid]::NewGuid().ToString()
    $q = @"
INSERT INTO catalog."Topics" ("Id", "SubjectId", "Name", "SortOrder", "IsActive", "CreatedAtUtc")
SELECT '$id', s."Id", '$label', 99, TRUE, now()
FROM catalog."Subjects" s WHERE s."Name" = '$subjectName' LIMIT 1;
"@
    Sql $q | Out-Null
    return [PSCustomObject]@{ topicId = $id; topic = $label }
}

$turev   = NewTopic 'Matematik' "E2E Turev $stamp"
$organik = NewTopic 'Kimya'     "E2E Organik $stamp"
OK "koşuma özel konular oluşturuldu ($($turev.topic) / $($organik.topic))"

Api POST '/api/portfolio/entries' @{ topicId = $turev.topicId;   direction = 'Offer'; selfAssessedLevel = 5; note = 'AYT seviyesi' } $aT | Out-Null
Api POST '/api/portfolio/entries' @{ topicId = $organik.topicId; direction = 'Seek';  selfAssessedLevel = 2; note = $null } $aT | Out-Null
Api POST '/api/portfolio/entries' @{ topicId = $organik.topicId; direction = 'Offer'; selfAssessedLevel = 4; note = $null } $bT | Out-Null
Api POST '/api/portfolio/entries' @{ topicId = $turev.topicId;   direction = 'Seek';  selfAssessedLevel = 1; note = $null } $bT | Out-Null
OK 'Ayşe: Türev verir / Organik ister — Berk: tersi'

$dup = ApiExpectError POST '/api/portfolio/entries' @{ topicId = $turev.topicId; direction = 'Offer'; selfAssessedLevel = 5; note = $null } $aT
if ($dup.code -eq 'PORTFOLIO_DUPLICATE') { OK 'mükerrer portföy girişi engellendi' } else { Fail "beklenen PORTFOLIO_DUPLICATE, gelen $($dup.code)" }

# ---------------------------------------------------------------- 4. ÇAPRAZ EŞLEŞME
Step '4. Çapraz eşleşme algoritması'
$sug = Api GET '/api/portfolio/suggestions?limit=10' $null $aT
# İsimle DEĞİL, kayıt sırasında dönen userId ile eşleştir: önceki koşulardan kalan
# aynı isimli kullanıcılar filtreyi çoğaltıp isteği bozuyordu.
$berkSug = @($sug | Where-Object { $_.userId -eq $berkReg.userId })[0]
if ($berkSug) { OK "Berk önerildi (anlatabilir: $($berkSug.theyCanTeach[0].topicName))" } else { Fail 'Berk önerilmedi' }
if ($berkSug.isCrossMatch) { OK 'karşılıklı takas (isCrossMatch) tespit edildi' } else { Fail 'çapraz eşleşme tespit edilemedi' }

# ---------------------------------------------------------------- 5. EŞLEŞME
Step '5. Eşleşme isteği ve kabul'
$matchId = Api POST '/api/matches' @{ responderUserId = $berkSug.userId; requestedTopicId = $organik.topicId; offeredTopicId = $turev.topicId } $aT
$resp = Api POST "/api/matches/$matchId/respond" @{ accept = $true } $bT
if ($resp.status -eq 'Accepted' -and $resp.conversationId) { OK "eşleşme kabul edildi, sohbet açıldı" } else { Fail 'eşleşme/sohbet açılmadı' }
$convId = $resp.conversationId

# ---------------------------------------------------------------- 6. SOHBET
Step '6. Sohbet (kanal bağımsızlığı)'
Api POST "/api/conversations/$convId/messages" @{ content = 'Merhaba! Yarın 20:00 uygun mu? https://meet.google.com/abc-defg-hij' } $aT | Out-Null
$msgs = Api GET "/api/conversations/$convId/messages?page=1&pageSize=10" $null $bT
if ($msgs.Count -ge 1) { OK "Berk mesajı görüyor: $($msgs[0].content.Substring(0,20))..." } else { Fail 'mesaj görünmüyor' }
$convs = Api GET '/api/conversations' $null $bT
if ($convs[0].unreadCount -ge 1) { OK "okunmamış rozeti: $($convs[0].unreadCount)" } else { Fail 'okunmamış sayacı çalışmadı' }

# ---------------------------------------------------------------- 7. REZERVASYON (BASIM SÖZÜ)
Step '7. Rezervasyon: öğrenciden hiçbir şey düşmez'
# "Değişmedi" iddiası ancak öncesi ölçülürse kanıtlanır; rezervasyon öncesi durum alınıyor.
$wBefore = Api GET '/api/wallet' $null $aT

$start = [DateTime]::UtcNow.AddHours(2).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$booking = Api POST '/api/sessions' @{ matchId = $matchId; topicId = $organik.topicId; scheduledStartUtc = $start; durationMinutes = 60 } $aT
OK "ders rezerve edildi, doğrulama kodu: $($booking.verificationCode)"

# Alan adı creditCost -> mintAmount: bu tutar öğrencinin ödediği DEĞİL, onayda eğitmene
# BASILACAK puandır. 60 dk = 100 (her 30 dk için 50).
if ($booking.mintAmount -eq 100) { OK '60 dakikalık dersin ödülü 100 puan olarak hesaplandı' }
else { Fail "beklenen mintAmount 100, gelen $($booking.mintAmount)" }

# Gönüllülük artık CreditCost=0'dan çıkarılmıyor, ayrı bayrakla taşınıyor.
if ($booking.isVolunteer -eq $false) { OK 'ders gönüllü değil (isVolunteer=false) — onayda basım yapılacak' }
else { Fail "beklenen isVolunteer false, gelen $($booking.isVolunteer)" }

# 1. DEĞİŞMEZ: rezervasyon öğrencinin bakiyesine DOKUNMAZ.
$wAfter = Api GET '/api/wallet' $null $aT
if ($wAfter.currentBalance -eq $wBefore.currentBalance -and $wAfter.totalEarnedCredits -eq $wBefore.totalEarnedCredits) {
    OK "rezervasyon öğrencinin bakiyesini değiştirmedi ($($wAfter.currentBalance) puan)"
} else { Fail "rezervasyon öğrenci bakiyesine dokundu: $($wBefore.currentBalance) -> $($wAfter.currentBalance)" }

# 2. DEĞİŞMEZ: bloke edilecek kredi olmadığına göre hold satırı da yazılmamalı.
# Bakiye kontrolü tek başına yetmez: escrow yolu geri gelirse önce burada iz bırakır.
$holdCount = ((Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';") -join '').Trim()
if ($holdCount -eq '0') { OK 'rezervasyonda CreditHold oluşmadı (escrow yok)' }
else { Fail "beklenen 0 hold, gelen $holdCount" }

# Süre artık ARALIK değil KÜME (30/60): 45 dk 1,5 bloğa denk geldiği için yuvarlanmaz,
# reddedilir. Kontrol tavan dolmadan yapılmalı — dolu olsaydı 429 gelir ve süre kuralı
# hiç ölçülmeden geçmiş sayılırdı.
$badDuration = ApiExpectError POST '/api/sessions' @{ matchId = $matchId; topicId = $organik.topicId; scheduledStartUtc = [DateTime]::UtcNow.AddHours(4).ToString('yyyy-MM-ddTHH:mm:ss.fffZ'); durationMinutes = 45 } $aT
if ($badDuration.code -eq 'INVALID_BOOKING') { OK '45 dakikalık ders reddedildi (izinli süreler 30/60)' }
else { Fail "beklenen INVALID_BOOKING, gelen $($badDuration.code)" }

# ESKİ "yetersiz kredi" KONTROLÜNÜN YERİNE GEÇEN İDDİA.
# Öğrenci ödemeyi bırakınca ikinci rezervasyonu durduran ekonomik fren de kalktı; yerini
# davranışsal tavan aldı (aynı eğitmen-öğrenci çifti 24 saatte en fazla 2 ders).
# Önce tavanın ALTINDAKİ ders geçmeli: fren dürüst kullanıcıyı engellememeli.
$second = Api POST '/api/sessions' @{ matchId = $matchId; topicId = $organik.topicId; scheduledStartUtc = [DateTime]::UtcNow.AddHours(6).ToString('yyyy-MM-ddTHH:mm:ss.fffZ'); durationMinutes = 60 } $aT
if ($second.sessionId) { OK 'aynı çift için ikinci ders açılabildi (tavanın altında)' } else { Fail 'ikinci ders açılamadı' }

$wSecond = Api GET '/api/wallet' $null $aT
if ($wSecond.currentBalance -eq $wBefore.currentBalance) { OK 'iki rezervasyondan sonra da bakiye aynı (harcama diye bir şey yok)' }
else { Fail "ikinci rezervasyon bakiyeyi değiştirdi: $($wSecond.currentBalance)" }

# Üçüncüsü tavana çarpmalı: karşılıklı sınırsız basımın önündeki TEK fren bu.
# Tavan ScheduledStartUtc'si son 24 saatlik pencereye düşen, iptal edilmemiş dersleri sayar;
# üç rezervasyon da bilerek aynı pencerede tutuldu.
$capped = ApiExpectError POST '/api/sessions' @{ matchId = $matchId; topicId = $organik.topicId; scheduledStartUtc = [DateTime]::UtcNow.AddHours(10).ToString('yyyy-MM-ddTHH:mm:ss.fffZ'); durationMinutes = 60 } $aT
if ($capped.code -eq 'MINT_LIMIT_REACHED' -and $capped.status -eq 429) { OK 'üçüncü ders suistimal frenine takıldı (MINT_LIMIT_REACHED / 429)' }
else { Fail "beklenen MINT_LIMIT_REACHED/429, gelen $($capped.code)/$($capped.status)" }

# ---------------------------------------------------------------- 8. TIME-LOCK
Step '8. TIME-LOCK'
# GERÇEK istek: doğru kod + geçerli kanıtla dene — tek engel time-lock olmalı.
$early = InvokeExpectError { UploadProof -SessionId $booking.sessionId -Code $booking.verificationCode -Token $bT }
if ($early.code -eq 'TIME_LOCK_ACTIVE') {
    OK "ders bitmeden tamamlama reddedildi: $($early.code) — $($early.detail)"
} else { Fail "time-lock çalışmadı: $($early.code) / $($early.detail)" }

# Dersi geçmişe taşı (60 dk beklemek yerine): time-lock penceresi açılmalı.
$sessionId = $booking.sessionId
$shift = @"
UPDATE scheduling."LessonSessions"
SET "ScheduledStartUtc" = now() - interval '2 hours',
    "ScheduledEndUtc"   = now() - interval '1 hour'
WHERE "Id" = '$sessionId';
"@
$shiftOut = Sql $shift
if ($shiftOut -match 'UPDATE 1') { OK 'ders saati geçmişe alındı (time-lock penceresi açıldı)' }
else { Fail "saat kaydırma başarısız: $shiftOut" }

# ---------------------------------------------------------------- 9. KANIT
Step '9. Kanıt yükleme (Proof of Session)'
$byStudent = InvokeExpectError { UploadProof -SessionId $sessionId -Code $booking.verificationCode -Token $aT }
if ($byStudent.code -eq 'NOT_SESSION_PARTICIPANT') { OK "öğrencinin tamamlama denemesi reddedildi: $($byStudent.code)" }
else { Fail "beklenen NOT_SESSION_PARTICIPANT, gelen $($byStudent.code)" }

$wrongCode = InvokeExpectError { UploadProof -SessionId $sessionId -Code 'YANLISKOD' -Token $bT }
if ($wrongCode.code -eq 'VERIFICATION_CODE_MISMATCH') { OK 'yanlış doğrulama kodu reddedildi' }
else { Fail "beklenen VERIFICATION_CODE_MISMATCH, gelen $($wrongCode.code)" }

$proofResult = UploadProof -SessionId $sessionId -Code $booking.verificationCode -Token $bT
OK "kanıt yüklendi (proofId: $($proofResult.proofId))"

# /api/sessions artık düz dizi değil: aktif dersler tam, geçmiş sayfalı döner
# (bkz. GetMySessions — aksiyon bekleyen ders sayfanın altında kalmamalı).
$sessions = Api GET '/api/sessions' $null $aT
# Listede artık birden çok ders var (7. adımdaki ikinci rezervasyon); süzme sonucu @() ile
# sarmalanıp ilk öğe alınıyor ki tek nesne/dizi farkı iddiaları bozmasın.
$s = @(@($sessions.active) + @($sessions.past.items) | Where-Object { $_.sessionId -eq $booking.sessionId })[0]
if ($s.status -eq 'AwaitingApproval') { OK 'ders onay bekliyor durumunda' } else { Fail "durum: $($s.status)" }
if ($s.autoApproveDeadlineUtc) { OK "otomatik onay son tarihi: $($s.autoApproveDeadlineUtc)" } else { Fail 'autoApproveDeadlineUtc boş' }
if ($s.canApprove) { OK 'öğrenci için onay bayrağı açık' } else { Fail 'canApprove kapalı' }
# Liste DTO'sunda da creditCost -> mintAmount; arayüz "maliyet" yazmaya devam etmesin.
if ($s.mintAmount -eq 100 -and $s.isVolunteer -eq $false) { OK 'listede ödül alanları doğru (mintAmount=100, gönüllü değil)' }
else { Fail "liste alanları yanlış: mintAmount=$($s.mintAmount) isVolunteer=$($s.isVolunteer)" }

# ---------------------------------------------------------------- 10. KANIT GÖRÜNTÜLEME
Step '10. Öğrenci kanıtı görüntüleyebiliyor mu?'
$proofs = Api GET "/api/sessions/$($booking.sessionId)/proofs" $null $aT
if ($proofs.Count -ge 1) { OK "öğrenci kanıt listesini görüyor (hash: $($proofs[0].sha256Hash.Substring(0,12))...)" } else { Fail 'kanıt listesi boş' }

# -UseBasicParsing şart: PS 5.1'in HTML ayrıştırıcısı ikili yanıtta NullReference atıyor.
$img = Invoke-WebRequest -Uri "$API/api/sessions/$sessionId/proofs/$($proofs[0].proofId)/content" `
    -Headers @{ Authorization = "Bearer $aT" } -UseBasicParsing -TimeoutSec 30
$ctype = $img.Headers['Content-Type']
if ($img.StatusCode -eq 200 -and $ctype -like 'image/*' -and $img.Content.Length -gt 0) {
    OK "kanıt görseli indirildi ($($img.Content.Length) bayt, $ctype)"
} else { Fail "kanıt görseli indirilemedi (status=$($img.StatusCode) type=$ctype)" }

# Yetkisiz üçüncü kişi kanıta erişememeli
$outsider = Api POST '/api/auth/register' @{ email = "cem$stamp@test.dev"; password = 'Parola12345'; displayName = 'Cem Üçüncü' }
Api POST '/api/auth/verify-email' @{ token = $outsider.verificationToken } | Out-Null
$cT = (Api POST '/api/auth/login' @{ email = "cem$stamp@test.dev"; password = 'Parola12345'; hwidHash = 'c' * 64 }).accessToken
$denied = InvokeExpectError { Api GET "/api/sessions/$sessionId/proofs" $null $cT }
if ($denied.code -eq 'NOT_SESSION_PARTICIPANT') { OK 'üçüncü kişinin kanıt erişimi reddedildi' }
else { Fail "beklenen NOT_SESSION_PARTICIPANT, gelen $($denied.code)" }

# ---------------------------------------------------------------- 11. ATOMİK BASIM
Step '11. Çift taraflı onay + atomik puan basımı'
# Basım tek bacaklı bir ARTIŞ olduğu için iddialar mutlak değil DELTA üzerinden kurulur:
# onay öncesi/sonrası farkı ölçülür, böylece koşum sırası veya paylaşılan veri etkilemez.
$waBefore = Api GET '/api/wallet' $null $aT
$wbBefore = Api GET '/api/wallet' $null $bT
$earnedBefore = EarnedCounter $berkReg.userId

$approve = Api POST "/api/sessions/$($booking.sessionId)/approve" $null $aT
# creditsTransferred -> creditsMinted: aktarılan bir şey yok, puan bu anda üretiliyor.
if ($approve.creditsMinted -eq 100) { OK "onaylandı: $($approve.creditsMinted) puan basıldı" }
else { Fail "beklenen creditsMinted 100, gelen $($approve.creditsMinted)" }

$wa = Api GET '/api/wallet' $null $aT
$wb = Api GET '/api/wallet' $null $bT

# 3. DEĞİŞMEZ (öğrenci ayağı): onay anında da öğrenciden hiçbir şey düşmez.
if ($wa.currentBalance -eq $waBefore.currentBalance -and $wa.totalEarnedCredits -eq $waBefore.totalEarnedCredits) {
    OK "Ayşe'nin bakiyesi onaydan etkilenmedi ($($wa.currentBalance) puan)"
} else { Fail "öğrenciden düşüldü: $($waBefore.currentBalance) -> $($wa.currentBalance)" }

# 3. DEĞİŞMEZ (eğitmen ayağı): bakiye VE birikimli sayaç aynı miktarda artmalı.
if ($wb.currentBalance -eq ($wbBefore.currentBalance + 100)) { OK "Berk: $($wb.currentBalance) puan (1 hoş geldin + 100 kazanç)" }
else { Fail "Berk bakiyesi $($wb.currentBalance), beklenen $($wbBefore.currentBalance + 100)" }
if ($wb.totalEarnedCredits -eq ($wbBefore.totalEarnedCredits + 100)) { OK "birikimli kazanç 100 arttı ($($wb.totalEarnedCredits))" }
else { Fail "totalEarnedCredits $($wbBefore.totalEarnedCredits) -> $($wb.totalEarnedCredits), beklenen +100" }

# DTO'ya güvenmek yetmez: unvanın dayandığı sayaç kullanıcı satırına da yazılmış olmalı.
$earnedAfter = EarnedCounter $berkReg.userId
if ($earnedAfter -eq ($earnedBefore + 100)) { OK "identity.Users.TotalEarnedCredits 100 arttı ($earnedBefore -> $earnedAfter)" }
else { Fail "veritabanı sayacı $earnedBefore -> $earnedAfter, beklenen +100" }

# Tek ders (100 puan) eğitmeni 2. seviyeye çıkarır ve sonraki eşik 200 olur.
# İLERLEMENİN GÖRÜNÜR OLMASI ürün kararının kendisi: eski merdivende ilk basamak
# 500 puandaydı, yani bir ders anlatan kullanıcı hiçbir değişiklik görmüyordu.
if ([int]$wb.level -eq 2 -and $wb.nextLevelAt -eq 200) { OK "eğitmenin seviyesi: $($wb.level) — sonraki eşik $($wb.nextLevelAt)" }
else { Fail "seviye alanları yanlış: level=$($wb.level) next=$($wb.nextLevelAt)" }

# Kazanç lotu artık SÜRESİZ açılıyor (eski model 30 gün vadeliydi): kazanılan puan yanmaz.
$earnedLot = @($wb.activeLots | Where-Object { $_.source -eq 'LessonEarning' -and $_.remainingAmount -eq 100 })[0]
if ($earnedLot) {
    if ($null -eq $earnedLot.expiresAtUtc) { OK 'kazanç lotu vadesiz açıldı (expiresAtUtc null)' }
    else { Fail "kazanç lotuna vade konmuş: $($earnedLot.expiresAtUtc)" }
} else { Fail '100 puanlık kazanç lotu bulunamadı' }

# 5. DEĞİŞMEZ: ders başına TEK basım.
$dbl = ApiExpectError POST "/api/sessions/$($booking.sessionId)/approve" $null $aT
if ($dbl.code) { OK "ikinci onay reddedildi: $($dbl.code)" } else { Fail 'İKİNCİ ONAY GEÇTİ' }
# Reddedilmiş olması yetmez: ikinci basımın YAPILMADIĞI sayaçtan doğrulanır — hata
# döndürüp yine de puan basan bir yol, sessizce ekonomiyi ikiye katlardı.
$earnedRetry = EarnedCounter $berkReg.userId
if ($earnedRetry -eq $earnedAfter) { OK 'ikinci onay ikinci basım yapmadı (sayaç sabit)' }
else { Fail "ikinci onay sayacı değiştirdi: $earnedAfter -> $earnedRetry" }

# ---------------------------------------------------------------- 11b. CÜZDAN EKSTRESİ
Step '11b. Cüzdan ekstresi (sayfalı + ders künyeli)'

# Ekstre, kullanıcının kendi hesabını denetleyebildiği TEK araç: hem sayfalanabilmeli
# hem de hareketin hangi derse ait olduğunu söylemeli.
$ext = Api GET '/api/wallet/statement?page=1&pageSize=25' $null $bT
if ($null -ne $ext.items -and $null -ne $ext.totalCount) { OK "ekstre sayfalı döndü ($($ext.totalCount) hareket)" }
else { Fail 'ekstre sayfalama alanları yok (items/totalCount)' }

$dersHareketi = $ext.items | Where-Object { $_.relatedSessionId -eq $booking.sessionId } | Select-Object -First 1
if ($dersHareketi) { OK 'ders hareketi ekstrede bulundu' } else { Fail 'ders hareketi yok' }
if ($dersHareketi.topicName) { OK "hareket hangi derse ait gösteriyor: $($dersHareketi.topicName)" }
else { Fail 'topicName boş — kullanıcı krediyi hangi dersin aldığını göremez' }
if ($dersHareketi.counterpartDisplayName) { OK "karşı taraf yazıyor: $($dersHareketi.counterpartDisplayName)" }
else { Fail 'counterpartDisplayName boş' }

# Ders ile ilgisi olmayan hareket (hoş geldin kredisi) künye TAŞIMAMALI.
$bonus = $ext.items | Where-Object { $_.type -eq 'WelcomeBonus' } | Select-Object -First 1
if ($bonus -and -not $bonus.topicName) { OK 'derse bağlı olmayan harekette künye yok' }
elseif (-not $bonus) { Fail 'hoş geldin hareketi bulunamadı' }
else { Fail 'ilgisiz harekete ders künyesi eklendi' }

# Basımın tek bacaklı olduğunun defter tarafındaki kanıtı: ÖĞRENCİNİN ekstresinde bu derse
# ait hiçbir hareket olmamalı. Eski modelde burada bir borç bacağı vardı.
$extStudent = Api GET '/api/wallet/statement?page=1&pageSize=25' $null $aT
$studentRows = @($extStudent.items | Where-Object { $_.relatedSessionId -eq $booking.sessionId })
if ($studentRows.Count -eq 0) { OK 'öğrencinin ekstresinde ders hareketi yok (ders ücretsiz)' }
else { Fail "öğrenci ekstresinde $($studentRows.Count) ders hareketi var — bir yerde hâlâ tahsilat yapılıyor" }

# ---------------------------------------------------------------- 12. DEFTER MUTABAKATI
Step '12. Defter mutabakatı (basım denetimi)'
# ESKİ İKİ KONTROLÜN YERİNE TEK VE DAHA GENEL DEĞİŞMEZ.
# Eskiden (a) transfer bacaklarının toplamı 0 ve (b) arz = mint - yakım ölçülüyordu.
# Artık transfer bacağı yok (basım tek bacaklı) ve puan bilerek yoktan üretiliyor;
# ikisi de yanlış soruyu sorar hâle geldi. Geçerli soru: cüzdanlardaki her puanın
# defterde kayıtlı bir karşılığı var mı? Hakem panelindeki ledgerBalanced da bunu ölçüyor.
$ledger = (Sql "SELECT (SELECT COALESCE(SUM(""AvailableBalance""),0) FROM economy.""Wallets"") - (SELECT COALESCE(SUM(""Amount""),0) FROM economy.""CreditTransactions"");") -join ''
if ($ledger.Trim() -eq '0') { OK 'cüzdan toplamı = defter toplamı (kayıtsız puan yok)' } else { Fail "defter farkı: $ledger" }

$lotCheck = (Sql "SELECT COUNT(*) FROM economy.""Wallets"" w WHERE w.""AvailableBalance"" <> (SELECT COALESCE(SUM(l.""RemainingAmount""),0) FROM economy.""CreditLots"" l WHERE l.""WalletId"" = w.""Id"");") -join ''
if ($lotCheck.Trim() -eq '0') { OK 'cüzdan bakiyesi = lot toplamı (değişmez korunuyor)' } else { Fail "$lotCheck cüzdanda tutarsızlık" }

# Escrow yollarının kapandığının sistem geneli kanıtı: göç tüm hold'ları Released yaptı ve
# yeni hold hiç yazılmıyor. Tek bir Active satır, kaldırılmış bir yolun geri geldiğini gösterir.
$openHolds = (Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';") -join ''
if ($openHolds.Trim() -eq '0') { OK 'sistemde açık CreditHold yok (escrow yolları kapalı)' } else { Fail "$openHolds açık hold var" }

# ---------------------------------------------------------------- SONUÇ
Write-Host "`n================================" -ForegroundColor Yellow
if ($script:failures -eq 0) { Write-Host "TÜM ADIMLAR BAŞARILI" -ForegroundColor Green }
else { Write-Host "$($script:failures) ADIM BAŞARISIZ" -ForegroundColor Red }
Write-Host "================================" -ForegroundColor Yellow
