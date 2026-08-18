# PeerLearn — Zamana bağlı background job'ların uçtan uca testi
#
# YÖNTEM: Saati beklemek yerine kayıtların zaman damgaları GEÇMİŞE alınır, sonra job
# admin ucundan tetiklenir. Job'ın gerçek kodu (SweepSessionsCommand / ExpireCreditsCommand)
# çalışır — yalnızca tetikleyici zamanlayıcı atlanır.
#
# Senaryolar:
#   A. 48 saatlik otomatik onay: süresi dolan onaylanır ve EĞİTMENE puan basılır, dolmayan DOKUNULMAZ
#   B. Düşmüş rezervasyon: 7 gün geçmiş Booked ders Expired olur; iade edilecek kredi yok, akış yine de çalışır
#   C. Vade yakımı: yalnızca VADELİ (eski tip) lotlar yanar; unvan sayacı bundan etkilenmez
#   D. Süresiz ders kazancı hiçbir süpürmede YANMAZ  (yeni ekonomi için eklenen kontrol)
#   E. Job'lardan sonra ekonomi değişmezleri
#
# ---------------------------------------------------------------------------------
# KAPSAM DEĞİŞİKLİĞİ — escrow'lu sıfır toplamdan basımlı puan ekonomisine geçiş
#
# Aşağıdaki iddiaların yeni modelde karşılığı YOK. Silinenlerin gerekçesi ve yerlerine
# yazılan kontroller:
#
#  * "Rezervasyonda kredi bloke edilir (escrow/hold)" → öğrenci artık hiçbir şey ödemiyor,
#    hold yazan tek bir kod yolu bile kalmadı. YERİNE: rezervasyonun öğrenci bakiyesine
#    DOKUNMADIĞI ve hiç CreditHold satırı OLUŞMADIĞI doğrulanıyor (A ve B).
#  * "Düşen rezervasyonda escrow öğrenciye iade edilir" → iade edilecek kredi yok.
#    YERİNE: süpürmenin dersi yine de Expired yaptığı ve bakiyenin DEĞİŞMEDİĞİ,
#    defterde hiç hareket üretilmediği doğrulanıyor (B).
#  * ESKİ D SENARYOSU — "vade oyunu: krediyi rezervasyonla bloke edip vadeden kaçırma,
#    iade edilince ilk süpürmede yanma" — TÜMÜYLE KALDIRILDI, karşılığı yok. Oyunun
#    dayandığı mekanizmanın hiçbir parçası kalmadı: bloke edilecek kredi, boşaltılacak
#    lot ve iptalde iade yolu yok; öğrenci kredisi ders akışında hiç hareket etmiyor.
#    Cüzdan yanıtındaki "pendingExpirySweep" alanı da aynı sebeple düştü.
#    D'nin yerini yeni modelin asıl vade sorusu aldı: süresiz kazanç yanıyor mu?
#  * "Transfer bacakları toplamı = 0" ve "küresel arz = mint − yakım" → sıfır toplam
#    terk edildi, her onay yoktan puan üretiyor. YERİNE panelin (ledgerBalanced) ölçtüğü
#    yeni defter değişmezi: cüzdan toplamı == defterdeki TÜM hareketlerin toplamı (E).
#  * "Bloke bakiye = aktif hold toplamı" → aktif hold kavramı bitti. YERİNE: sistemde
#    bloke tablosu ve kolonunun şemadan kalktığı kontrol
#    ediliyor (E) — göçün gerçekten tamamlandığının kanıtı da bu.
# ---------------------------------------------------------------------------------

$ErrorActionPreference = 'Stop'
$API = 'http://localhost:5000'
$PSQL = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'

# Ödül ölçeği tek yerde: 30 dk = 50 puan. Beklenen tutarlar bu sabitlerden türetilir ki
# ölçek değişirse testin tek bir yeri güncellensin.
$MINT_30 = 50
$MINT_60 = 100

$script:failures = 0
function Step($name) { Write-Host "`n=== $name ===" -ForegroundColor Cyan }
function OK($msg)    { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Fail($msg)  { Write-Host "  [HATA] $msg" -ForegroundColor Red; $script:failures++ }

function Api {
    param($Method, $Path, $Body, $Token)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Uri = "$API$Path"; Method = $Method; Headers = $headers; TimeoutSec = 120 }
    if ($null -ne $Body) {
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 6))
        $params['ContentType'] = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @params
}

function Sql($query) {
    $f = Join-Path $env:TEMP ("pl_" + [Guid]::NewGuid().ToString('N') + ".sql")
    Set-Content -Path $f -Value $query -Encoding UTF8
    $out = & $PSQL -h localhost -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 -f $f 2>&1
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    return ($out -join '').Trim()
}

function UploadProof {
    param($SessionId, $Code, $Token)
    $png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==')
    $bd = [Guid]::NewGuid().ToString('N')
    $enc = [System.Text.Encoding]::UTF8
    $ms = New-Object System.IO.MemoryStream
    $h = $enc.GetBytes("--$bd`r`nContent-Disposition: form-data; name=`"verificationCode`"`r`n`r`n$Code`r`n--$bd`r`nContent-Disposition: form-data; name=`"proof`"; filename=`"p.png`"`r`nContent-Type: image/png`r`n`r`n")
    $t = $enc.GetBytes("`r`n--$bd--`r`n")
    $ms.Write($h,0,$h.Length); $ms.Write($png,0,$png.Length); $ms.Write($t,0,$t.Length)
    $body = $ms.ToArray(); $ms.Dispose()
    Invoke-RestMethod -Uri "$API/api/sessions/$SessionId/complete" -Method Post `
        -Headers @{ Authorization = "Bearer $Token" } `
        -ContentType "multipart/form-data; boundary=$bd" -Body $body -TimeoutSec 60
}

$script:seq = 0
function NewUser($prefix) {
    $script:seq++
    $email = "$prefix$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())j$($script:seq)@test.dev"
    $hwid = (([Guid]::NewGuid().ToString('N')) * 2).Substring(0, 64)
    $reg = Api POST '/api/auth/register' @{ email = $email; password = 'Parola12345'; displayName = "$prefix Kullanici" }
    Api POST '/api/auth/verify-email' @{ token = $reg.verificationToken } | Out-Null
    $login = Api POST '/api/auth/login' @{ email = $email; password = 'Parola12345'; hwidHash = $hwid }
    return @{ email = $email; userId = $login.userId; token = $login.accessToken }
}

function Wallet($token) { Api GET '/api/wallet' $null $token }

# Her çağrıda YENİ bir eğitmen-öğrenci çifti kurulur.
#
# SUİSTİMAL FRENİ (MintGuard) YÜZÜNDEN ŞART: aynı çift 24 saat içinde en fazla 2, aynı
# eğitmen en fazla 8 ders açabiliyor (aşılırsa 429/MINT_LIMIT_REACHED). Kullanıcıları
# paylaştırıp derslerin hepsini tek çifte yıkmak testi rastgele yerlerde kırardı; taze
# çift kurmak hem ucuz hem de senaryoları birbirinden yalıtıyor.
#
# studentBalanceBefore: rezervasyon ÖNCESİ öğrenci bakiyesi. "Rezervasyon bakiyeye
# dokunmaz" değişmezi ancak öncesi/sonrası karşılaştırmasıyla kanıtlanabilir — hoş geldin
# kredisi yapılandırmadan geldiği için sabit sayı beklemek kırılgan olurdu.
function NewBookedSession($topicId, $durationMinutes = 60) {
    $student = NewUser 'ogr'
    $tutor   = NewUser 'egt'
    Api POST '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Offer'; selfAssessedLevel = 5; note = $null } $tutor.token | Out-Null
    Api POST '/api/portfolio/entries' @{ topicId = $topicId; direction = 'Seek'; selfAssessedLevel = 2; note = $null } $student.token | Out-Null
    $m = Api POST '/api/matches' @{ responderUserId = $tutor.userId; requestedTopicId = $topicId; offeredTopicId = $null } $student.token
    Api POST "/api/matches/$m/respond" @{ accept = $true } $tutor.token | Out-Null
    $balanceBefore = (Wallet $student.token).currentBalance
    $start = [DateTime]::UtcNow.AddHours(2).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    $b = Api POST '/api/sessions' @{ matchId = $m; topicId = $topicId; scheduledStartUtc = $start; durationMinutes = $durationMinutes } $student.token
    return @{
        student              = $student
        tutor                = $tutor
        sessionId            = $b.sessionId
        code                 = $b.verificationCode
        mintAmount           = $b.mintAmount
        isVolunteer          = $b.isVolunteer
        studentBalanceBefore = $balanceBefore
    }
}

function ShiftSessionToPast($sessionId) {
    Sql "UPDATE scheduling.""LessonSessions"" SET ""ScheduledStartUtc"" = now() - interval '2 hours', ""ScheduledEndUtc"" = now() - interval '1 hour' WHERE ""Id"" = '$sessionId';" | Out-Null
}
function SessionStatus($sessionId) { Sql "SELECT ""Status"" FROM scheduling.""LessonSessions"" WHERE ""Id"" = '$sessionId';" }

# Unvan sayacı doğrudan tablodan okunur: cüzdan bakiyesi vade yakımıyla düşebilir ama
# TotalEarnedCredits asla azalmamalı — ikisini ayrı ayrı görmek şart.
function EarnedDb($userId) { Sql "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$userId';" }

# Cüzdanın HAM bakiyesi. /api/wallet vadesi geçmiş lotları saymadığı için, yakım
# öncesi/sonrası farkını ölçerken API yanıtı değil bu değer kullanılmalı.
# Sorgu her hâlükârda TEK satır döndürür: cüzdan yoksa boş çıktı gelir ve [int] dönüşümü
# betiği ortasından düşürürdü.
function RawBalanceDb($userId) { [int](Sql "SELECT COALESCE((SELECT ""AvailableBalance"" FROM economy.""Wallets"" WHERE ""UserId"" = '$userId'), 0);") }

# ---------------------------------------------------------------- HAZIRLIK
Step 'Hazırlık: admin + konu'
# Koşu başlangıcı: E'deki "bu koşuda hiç harcama/hold yazılmadı" kontrolleri, paylaşılan
# geliştirme veritabanındaki ESKİ kayıtlara takılmasın diye zaman damgasıyla sınırlanıyor.
$runStart = Sql "SELECT now();"
$topicId = ((Api GET '/api/catalog/topics') | Where-Object { $_.topic -eq 'Türev' })[0].topicId
$admin = NewUser 'jobadmin'
Sql "UPDATE identity.""Users"" SET ""Role"" = 'Admin' WHERE ""Id"" = '$($admin.userId)';" | Out-Null
$adminT = (Api POST '/api/auth/login' @{ email = $admin.email; password = 'Parola12345'; hwidHash = (('a' * 64)) }).accessToken
OK 'admin hazır'

try {
    Api POST '/api/admin/jobs/session-sweep' $null (NewUser 'sivil').token | Out-Null
    Fail 'admin olmayan job tetikleyebildi'
} catch { OK 'job uçları admin olmayana kapalı' }

# ---------------------------------------------------------------- A
Step 'A. 48 saatlik otomatik onay -> eğitmene basım'
# Süresi DOLAN ders (60 dk => 100 puan)
$due = NewBookedSession $topicId 60
if ($due.mintAmount -eq $MINT_60) { OK "rezervasyon 60 dk için $MINT_60 puanlık basım sözü verdi (mintAmount)" }
else { Fail "mintAmount: $($due.mintAmount)" }
# "-not" yerine "$false" ile karşılaştırma: alan yanıttan tümden kalkarsa $null gelir ve
# "-not $null" sessizce doğru olurdu — alanın VARLIĞI da doğrulanmış olsun.
if ($due.isVolunteer -eq $false) { OK 'ders gönüllü değil (isVolunteer=false)' } else { Fail "isVolunteer: $($due.isVolunteer)" }

# DEĞİŞMEZ 1 ve 2: rezervasyon öğrencinin bakiyesine dokunmaz, hold yazmaz.
$sAfterBook = Wallet $due.student.token
if ($sAfterBook.currentBalance -eq $due.studentBalanceBefore) { OK "rezervasyon öğrencinin bakiyesine dokunmadı ($($due.studentBalanceBefore))" }
else { Fail "bakiye rezervasyonla değişti: $($due.studentBalanceBefore) -> $($sAfterBook.currentBalance)" }

$ogrHold = Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';"
if ($ogrHold -eq '0') { OK 'rezervasyon hiç CreditHold yazmadı' } else { Fail "hold satırı: $ogrHold" }

ShiftSessionToPast $due.sessionId
UploadProof -SessionId $due.sessionId -Code $due.code -Token $due.tutor.token | Out-Null
Sql "UPDATE scheduling.""LessonSessions"" SET ""CompletionRequestedAtUtc"" = now() - interval '50 hours' WHERE ""Id"" = '$($due.sessionId)';" | Out-Null

# Süresi DOLMAYAN ders (dokunulmamalı)
$fresh = NewBookedSession $topicId 60
ShiftSessionToPast $fresh.sessionId
UploadProof -SessionId $fresh.sessionId -Code $fresh.code -Token $fresh.tutor.token | Out-Null
Sql "UPDATE scheduling.""LessonSessions"" SET ""CompletionRequestedAtUtc"" = now() - interval '2 hours' WHERE ""Id"" = '$($fresh.sessionId)';" | Out-Null
OK 'iki ders onay bekliyor (biri 50 saatlik, biri 2 saatlik)'

# Basım ÖNCESİ anlık görüntü: beklenen tutarlar mutlak değil FARK olarak doğrulanır.
$tutorBefore = Wallet $due.tutor.token

$sweep = Api POST '/api/admin/jobs/session-sweep' $null $adminT
OK "süpürme çalıştı: $($sweep.autoApproved) otomatik onay, $($sweep.expired) düşen rezervasyon"

if ((SessionStatus $due.sessionId) -eq 'Completed') { OK '48 saati dolan ders otomatik onaylandı' }
else { Fail "durum: $(SessionStatus $due.sessionId)" }
if ((SessionStatus $fresh.sessionId) -eq 'AwaitingApproval') { OK 'süresi dolmayan derse DOKUNULMADI' }
else { Fail "taze ders durumu: $(SessionStatus $fresh.sessionId)" }

# DEĞİŞMEZ 3: doğru miktar BASILDI ve unvan sayacı aynı miktarda arttı.
$tutorAfter = Wallet $due.tutor.token
$basilan = $tutorAfter.currentBalance - $tutorBefore.currentBalance
if ($basilan -eq $MINT_60) { OK "otomatik onayda eğitmene $MINT_60 puan basıldı" }
else { Fail "basılan puan: $basilan" }

$sayacArtisi = $tutorAfter.totalEarnedCredits - $tutorBefore.totalEarnedCredits
if ($sayacArtisi -eq $MINT_60) { OK "totalEarnedCredits aynı miktarda arttı ($MINT_60)" }
else { Fail "sayaç artışı: $sayacArtisi" }

$sayacDb = EarnedDb $due.tutor.userId
if ($sayacDb -eq "$MINT_60") { OK 'identity."Users"."TotalEarnedCredits" tabloda da doğru' }
else { Fail "tablodaki sayaç: $sayacDb" }

# Unvan puandan türetiliyor; 100 puan ilk kademede kalmalı (0-500 Çırak).
if ($tutorAfter.rankTitle -eq 'Çırak' -and $tutorAfter.nextRankAt -eq 500) { OK "unvan: $($tutorAfter.rankEmoji) $($tutorAfter.rankTitle), sonraki eşik 500" }
else { Fail "unvan: $($tutorAfter.rankTitle) / sonraki: $($tutorAfter.nextRankAt)" }

# Basılan lot SÜRESİZ açılmalı — D senaryosunun dayanağı da bu.
$dueLotExpiry = Sql "SELECT COALESCE(""ExpiresAtUtc""::text, 'SURESIZ') FROM economy.""CreditLots"" WHERE ""SourceSessionId"" = '$($due.sessionId)' LIMIT 1;"
if ($dueLotExpiry -eq 'SURESIZ') { OK 'kazanç lotu vadesiz açıldı (ExpiresAtUtc NULL)' }
else { Fail "kazanç lotunun vadesi: $dueLotExpiry" }

# Öğrenci tarafı: onayda da hiçbir şey değişmez, ders ücretsiz.
$ws = Wallet $due.student.token
if ($ws.currentBalance -eq $due.studentBalanceBefore) { OK 'öğrencinin bakiyesi onayda da değişmedi (ders ücretsiz)' }
else { Fail "öğrenci bakiyesi: $($ws.currentBalance), beklenen: $($due.studentBalanceBefore)" }
if ($ws.totalEarnedCredits -eq 0) { OK 'ders alan tarafın unvan sayacı artmadı' }
else { Fail "öğrenci sayacı: $($ws.totalEarnedCredits)" }

# Defterde TEK bacak olmalı: karşılığı olmayan bir basım (eski modelde harcama+kazanç çiftiydi).
$legs = Sql "SELECT COUNT(*) FROM economy.""CreditTransactions"" WHERE ""RelatedSessionId"" = '$($due.sessionId)';"
if ($legs -eq '1') { OK 'defterde tek bacak var (yalnızca basım)' } else { Fail "bacak sayısı: $legs" }

$legType = Sql "SELECT ""Type"" FROM economy.""CreditTransactions"" WHERE ""RelatedSessionId"" = '$($due.sessionId)' LIMIT 1;"
if ($legType -eq 'LessonEarning') { OK 'bacağın tipi LessonEarning' } else { Fail "bacak tipi: $legType" }

# DEĞİŞMEZ 5: ikinci onay ikinci basım yapmaz. Süpürücü için bu aynı zamanda
# idempotency testi — job her 10 dakikada bir yeniden koşuyor.
$sweepTekrar = Api POST '/api/admin/jobs/session-sweep' $null $adminT
$tutorTekrar = Wallet $due.tutor.token
$legsTekrar = Sql "SELECT COUNT(*) FROM economy.""CreditTransactions"" WHERE ""RelatedSessionId"" = '$($due.sessionId)';"
if ($tutorTekrar.currentBalance -eq $tutorAfter.currentBalance -and
    $tutorTekrar.totalEarnedCredits -eq $tutorAfter.totalEarnedCredits -and
    $legsTekrar -eq '1') { OK "ikinci süpürme ikinci basım yapmadı (bu turda $($sweepTekrar.autoApproved) onay; ders başına tek basım)" }
else { Fail "ikinci süpürme sonrası bakiye/sayaç/bacak: $($tutorTekrar.currentBalance)/$($tutorTekrar.totalEarnedCredits)/$legsTekrar" }

# ---------------------------------------------------------------- B
Step 'B. Düşmüş rezervasyon (7 gün) -> Expired; iade edilecek kredi YOK'
$stale = NewBookedSession $topicId 60
Sql "UPDATE scheduling.""LessonSessions"" SET ""ScheduledStartUtc"" = now() - interval '9 days', ""ScheduledEndUtc"" = now() - interval '8 days' WHERE ""Id"" = '$($stale.sessionId)';" | Out-Null

$before = Wallet $stale.student.token
if ($before.currentBalance -eq $stale.studentBalanceBefore) { OK 'bekleyen rezervasyon bakiyeyi bloke etmiyor' }
else { Fail "bakiye: $($before.currentBalance), rezervasyon öncesi: $($stale.studentBalanceBefore)" }

# Taze bir rezervasyon da olsun: 7 günü doldurmadığı için DOKUNULMAMALI
$recent = NewBookedSession $topicId 60
Sql "UPDATE scheduling.""LessonSessions"" SET ""ScheduledStartUtc"" = now() - interval '2 days', ""ScheduledEndUtc"" = now() - interval '2 days' + interval '1 hour' WHERE ""Id"" = '$($recent.sessionId)';" | Out-Null

$sweep2 = Api POST '/api/admin/jobs/session-sweep' $null $adminT
OK "süpürme: $($sweep2.expired) rezervasyon düşürüldü"

if ((SessionStatus $stale.sessionId) -eq 'Expired') { OK '8 günlük rezervasyon Expired oldu' }
else { Fail "durum: $(SessionStatus $stale.sessionId)" }
if ((SessionStatus $recent.sessionId) -eq 'Booked') { OK '2 günlük rezervasyona dokunulmadı (7 gün dolmadı)' }
else { Fail "taze rezervasyon durumu: $(SessionStatus $recent.sessionId)" }

# DEĞİŞMEZ 7: iade edilecek kredi yok — ama akış hatasız tamamlanmalı ve bakiye
# rezervasyon öncesiyle AYNI kalmalı (ne düşmüş ne artmış olmalı).
$after = Wallet $stale.student.token
if ($after.currentBalance -eq $stale.studentBalanceBefore) { OK 'düşen rezervasyonda iade yok, bakiye baştan sona sabit' }
else { Fail "cüzdan: $($after.currentBalance), rezervasyon öncesi: $($stale.studentBalanceBefore)" }

$staleTx = Sql "SELECT COUNT(*) FROM economy.""CreditTransactions"" WHERE ""RelatedSessionId"" = '$($stale.sessionId)';"
if ($staleTx -eq '0') { OK 'düşen ders defterde hiç hareket üretmedi' } else { Fail "hareket sayısı: $staleTx" }

$staleTutorSayac = EarnedDb $stale.tutor.userId
if ($staleTutorSayac -eq '0') { OK 'düşen derste eğitmene basım yapılmadı' } else { Fail "eğitmen sayacı: $staleTutorSayac" }

# ---------------------------------------------------------------- C
Step 'C. Vade yakımı: yalnızca VADELİ (eski tip) lotlar'
# Ders kazançları artık süresiz açılıyor; sistemde vadesi olan tek lot tipi hoş geldin
# kredisi (ve göç öncesinden kalan eski kazançlar). Senaryo bu yüzden hoş geldin lotu
# üzerinden kuruluyor — eski hâlinde kazanç lotunun vadesi geçmişe alınıyordu ki artık
# öyle bir alan dolu değil.
$tutorId = $due.tutor.userId
$vadeliLot = Sql "SELECT l.""Id"" FROM economy.""CreditLots"" l JOIN economy.""Wallets"" w ON w.""Id"" = l.""WalletId"" WHERE w.""UserId"" = '$tutorId' AND l.""Source"" = 'WelcomeBonus' AND l.""RemainingAmount"" > 0 LIMIT 1;"
if ($vadeliLot) { OK 'vadeli (hoş geldin) lot bulundu' } else { Fail 'vadeli lot bulunamadı — senaryo kurulamadı' }

# Lot bulunamadıysa (kurulum bozuksa) sorgular UUID'ye çevrilemeyen boş metinle patlar ve
# betik ortasından düşerdi; ::text karşılaştırması hiçbir satır eşleştirmeden geçer, böylece
# hata aşağıdaki denetimlerde [HATA] olarak görünür.
$vadeliTutar = [int](Sql "SELECT COALESCE((SELECT ""RemainingAmount"" FROM economy.""CreditLots"" WHERE ""Id""::text = '$vadeliLot'), 0);")
$hamOnce = RawBalanceDb $tutorId
$sayacOnce = EarnedDb $tutorId

Sql "UPDATE economy.""CreditLots"" SET ""ExpiresAtUtc"" = now() - interval '1 day' WHERE ""Id""::text = '$vadeliLot';" | Out-Null
OK "vadeli lotun ($vadeliTutar puan) vadesi geçmişe alındı; süresiz kazanç lotuna dokunulmadı"

$expiry = Api POST '/api/admin/jobs/credit-expiry' $null $adminT
OK "vade süpürmesi: $($expiry.walletsProcessed) cüzdan, $($expiry.creditsExpired) kredi yakıldı"

$hamSonra = RawBalanceDb $tutorId
if (($hamOnce - $hamSonra) -eq $vadeliTutar) { OK "yalnızca vadesi dolan lot yakıldı ($hamOnce -> $hamSonra)" }
else { Fail "bakiye farkı: $($hamOnce - $hamSonra), beklenen: $vadeliTutar" }

$kazancKalan = Sql "SELECT ""RemainingAmount"" FROM economy.""CreditLots"" WHERE ""SourceSessionId"" = '$($due.sessionId)' LIMIT 1;"
if ($kazancKalan -eq "$MINT_60") { OK 'süresiz ders kazancı aynı turda korundu' } else { Fail "kazanç lotu kalanı: $kazancKalan" }

$remaining = Sql "SELECT ""RemainingAmount"" FROM economy.""CreditLots"" WHERE ""Id""::text = '$vadeliLot';"
if ($remaining -eq '0') { OK 'vadesi dolan lot sıfırlandı' } else { Fail "lot kalanı: $remaining" }

$expiryTx = Sql "SELECT COUNT(*) FROM economy.""CreditTransactions"" t JOIN economy.""Wallets"" w ON w.""Id"" = t.""WalletId"" WHERE w.""UserId"" = '$tutorId' AND t.""Type"" = 'Expiry';"
if ([int]$expiryTx -ge 1) { OK 'defterde Expiry hareketi yazıldı' } else { Fail 'Expiry hareketi yok' }

# Unvan bir başarı ölçüsü: yakım bakiyeyi düşürür ama kazanılmış unvanı geri almamalı.
$sayacSonra = EarnedDb $tutorId
if ($sayacSonra -eq $sayacOnce) { OK "vade yakımı unvan sayacını düşürmedi ($sayacSonra)" }
else { Fail "sayaç $sayacOnce -> $sayacSonra" }

$expiry2 = Api POST '/api/admin/jobs/credit-expiry' $null $adminT
if ($expiry2.creditsExpired -eq 0) { OK 'ikinci çalıştırma hiçbir şey yakmadı (idempotent)' }
else { Fail "ikinci turda $($expiry2.creditsExpired) kredi yakıldı" }

# ---------------------------------------------------------------- D
Step 'D. Süresiz ders kazancı YANMAZ'
# YENİ MODELİN ASIL VADE SORUSU. Kazanç lotları ExpiresAtUtc = NULL ile açılıyor ve
# SQL'de "NULL <= now()" hiçbir zaman doğru olmadığı için süpürücünün sorgusuna
# girmemeleri gerekir. Kontrol iki bacaklı kuruluyor:
#   (1) kazanç lotu ÇOK ESKİ gösterilir (400 gün önce kazanılmış) — yaşı büyük ama vadesi yok,
#   (2) aynı turda gerçekten yanacak VADELİ bir lot bırakılır ki süpürücünün boşa
#       çalışmadığı görülsün. Bu ikinci bacak olmadan test, job hiçbir şey yapmasa da
#       "geçer" görünürdü.
$forever = NewBookedSession $topicId 30
if ($forever.mintAmount -eq $MINT_30) { OK "30 dk ders $MINT_30 puanlık basım sözü verdi" }
else { Fail "mintAmount: $($forever.mintAmount)" }

ShiftSessionToPast $forever.sessionId
UploadProof -SessionId $forever.sessionId -Code $forever.code -Token $forever.tutor.token | Out-Null

# Burada onay job'dan değil öğrenciden geliyor: yanıt alanının adı (creditsMinted) ve
# tutarı da doğrulanmış olsun.
$appr = Api POST "/api/sessions/$($forever.sessionId)/approve" $null $forever.student.token
if ($appr.creditsMinted -eq $MINT_30) { OK "onay yanıtı creditsMinted = $MINT_30" } else { Fail "creditsMinted: $($appr.creditsMinted)" }

$foreverLot = Sql "SELECT COALESCE(""ExpiresAtUtc""::text, 'SURESIZ') FROM economy.""CreditLots"" WHERE ""SourceSessionId"" = '$($forever.sessionId)' LIMIT 1;"
if ($foreverLot -eq 'SURESIZ') { OK 'kazanç lotu vadesiz (ExpiresAtUtc NULL)' } else { Fail "vade: $foreverLot" }

# (1) Lotu 400 gün yaşlandır: eski 30 gün kuralı hâlâ işleseydi bu lot kesin yanardı.
Sql "UPDATE economy.""CreditLots"" SET ""EarnedAtUtc"" = now() - interval '400 days' WHERE ""SourceSessionId"" = '$($forever.sessionId)';" | Out-Null
# (2) Aynı turda yanması gereken vadeli bir lot: öğrencinin hoş geldin kredisi.
Sql "UPDATE economy.""CreditLots"" l SET ""ExpiresAtUtc"" = now() - interval '1 day' FROM economy.""Wallets"" w WHERE w.""Id"" = l.""WalletId"" AND w.""UserId"" = '$($forever.student.userId)' AND l.""RemainingAmount"" > 0;" | Out-Null

$kazancOnce = RawBalanceDb $forever.tutor.userId
$e3 = Api POST '/api/admin/jobs/credit-expiry' $null $adminT
if ([int]$e3.creditsExpired -ge 1) { OK "süpürücü bu turda gerçekten yaktı ($($e3.creditsExpired) puan) — kontrol boşa geçmiyor" }
else { Fail 'süpürücü hiçbir şey yakmadı; süresiz lot kontrolü anlamsız kalır' }

$kazancKalanD = Sql "SELECT ""RemainingAmount"" FROM economy.""CreditLots"" WHERE ""SourceSessionId"" = '$($forever.sessionId)' LIMIT 1;"
if ($kazancKalanD -eq "$MINT_30") { OK '400 gün yaşındaki süresiz kazanç YANMADI' } else { Fail "kazanç lotu kalanı: $kazancKalanD" }

$kazancSonra = RawBalanceDb $forever.tutor.userId
if ($kazancSonra -eq $kazancOnce) { OK 'eğitmenin bakiyesi süpürmeden etkilenmedi' } else { Fail "bakiye $kazancOnce -> $kazancSonra" }

$sayacD = EarnedDb $forever.tutor.userId
if ($sayacD -eq "$MINT_30") { OK "unvan sayacı $MINT_30 (süpürme sonrası da aynı)" } else { Fail "sayaç: $sayacD" }

# Süresiz lot hiçbir yakım hareketine kaynak olmamalı — tek ders değil, TÜM defter için.
$suresizYakim = Sql "SELECT COUNT(*) FROM economy.""CreditLotConsumptions"" c JOIN economy.""CreditLots"" l ON l.""Id"" = c.""CreditLotId"" JOIN economy.""CreditTransactions"" t ON t.""Id"" = c.""CreditTransactionId"" WHERE l.""ExpiresAtUtc"" IS NULL AND t.""Type"" = 'Expiry';"
if ($suresizYakim -eq '0') { OK 'defterde hiçbir süresiz lot yakımı yok' } else { Fail "$suresizYakim süresiz lot yakılmış" }

# ---------------------------------------------------------------- E
Step 'E. Job''lardan sonra ekonomi değişmezleri'
# DEĞİŞMEZ 8 — panelin ledgerBalanced'ı ile AYNI ölçüm: cüzdanlardaki toplam, defterdeki
# tüm hareketlerin toplamına eşit olmalı. "Basılan − yakılan" değil: model artık yoktan
# üretiyor, denetlenmesi gereken şey her kredinin kayıtlı bir hareketten geldiği.
$defter = Sql "SELECT (SELECT COALESCE(SUM(""AvailableBalance""),0) FROM economy.""Wallets"") - (SELECT COALESCE(SUM(""Amount""),0) FROM economy.""CreditTransactions"");"
if ($defter -eq '0') { OK 'cüzdan toplamı = defter toplamı (ledgerBalanced)' } else { Fail "defter farkı: $defter" }

$lotCheck = Sql "SELECT COUNT(*) FROM economy.""Wallets"" w WHERE w.""AvailableBalance"" <> (SELECT COALESCE(SUM(l.""RemainingAmount""),0) FROM economy.""CreditLots"" l WHERE l.""WalletId"" = w.""Id"");"
if ($lotCheck -eq '0') { OK 'cüzdan bakiyesi = lot toplamı' } else { Fail "$lotCheck cüzdanda tutarsızlık" }

# Unvan sayacının kaynağı defter olmalı: sayaç, o kullanıcının LessonEarning toplamına
# eşit. Basımın iki yolu var (onay ve itiraz kararı) ve biri sayacı atlarsa buradan görülür.
$sayacCheck = Sql "SELECT COUNT(*) FROM identity.""Users"" u WHERE u.""TotalEarnedCredits"" <> COALESCE((SELECT SUM(t.""Amount"") FROM economy.""CreditTransactions"" t JOIN economy.""Wallets"" w ON w.""Id"" = t.""WalletId"" WHERE w.""UserId"" = u.""Id"" AND t.""Type"" = 'LessonEarning'), 0);"
if ($sayacCheck -eq '0') { OK 'TotalEarnedCredits = defterdeki LessonEarning toplamı' } else { Fail "$sayacCheck kullanıcıda sayaç/defter uyuşmazlığı" }

# Escrow'un gerçekten bittiğinin kanıtı: ne aktif hold var, ne bloke bakiye.
$aktifHold = Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';"
if ($aktifHold -eq '0') { OK 'sistemde hiç aktif hold yok' } else { Fail "$aktifHold aktif hold" }

$lockedCheck = Sql "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='economy' AND table_name='Wallets' AND column_name='LockedBalance';"
if ($lockedCheck -eq '0') { OK 'hiçbir cüzdanda bloke bakiye yok' } else { Fail "$lockedCheck cüzdanda bloke bakiye duruyor" }

# Bu koşuda yazılan kayıtlar: ders akışı ne harcama bacağı ne de hold üretmeli.
# Zaman sınırı, paylaşılan veritabanındaki göç öncesi eski kayıtlar için.
$yeniHarcama = Sql "SELECT COUNT(*) FROM economy.""CreditTransactions"" WHERE ""CreatedAtUtc"" >= '$runStart' AND ""Type"" = 'LessonSpending';"
if ($yeniHarcama -eq '0') { OK 'bu koşuda hiç LessonSpending yazılmadı' } else { Fail "$yeniHarcama harcama bacağı yazılmış" }

$yeniHold = Sql "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='economy' AND table_name='CreditHolds';"
if ($yeniHold -eq '0') { OK 'bu koşuda hiç CreditHold yazılmadı' } else { Fail "$yeniHold yeni hold satırı" }

$negative = Sql "SELECT COUNT(*) FROM economy.""Wallets"" WHERE ""AvailableBalance"" < 0;"
if ($negative -eq '0') { OK 'hiçbir cüzdanda negatif bakiye yok' } else { Fail "$negative negatif cüzdan" }

# ---------------------------------------------------------------- SONUÇ
Write-Host "`n================================" -ForegroundColor Yellow
if ($script:failures -eq 0) { Write-Host "TÜM ADIMLAR BAŞARILI" -ForegroundColor Green }
else { Write-Host "$($script:failures) ADIM BAŞARISIZ" -ForegroundColor Red }
Write-Host "================================" -ForegroundColor Yellow
