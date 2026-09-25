# PeerLearn — push bildirimleri uçtan uca testi (Push:Provider = "Log")
#
# NE SINANIYOR: olay noktasından cihaz kaydının silinmesine kadar bütün hat, GERÇEK API ve
# GERÇEK veritabanıyla. Doğrulama comms.* tablolarından SQL ile yapılıyor: bir bildirimin
# "yazıldığı" değil, dağıtıcının onu ne yaptığı (Status + Outcome) iddia ediliyor. Satırın
# var olması tek başına hiçbir şey kanıtlamaz; dağıtıcı onu hiç göndermeyebilir.
#
# ─── NEDEN LOG SAĞLAYICISI ──────────────────────────────────────────────────────
# Expo'ya hiçbir şey gitmiyor. LoggingPushGonderici Expo gibi bilet döndürüyor ve token'ın
# SON EKİ hata yollarını seçiyor (LoggingPushGonderici.cs):
#   …OLU]      bilet ok, makbuz DeviceNotRegistered → makbuz işi cihazı silmeli (7)
#   …YAVAS]    yanıt 3 sn gecikir → gönderim sürerken cihaz silme (7) ve fencing (8)
#   …YABANCI]  istek düzeyi PUSH_TOO_MANY_EXPERIENCE_IDS → deneyim ayırma (9)
# Hazırlık adımı sağlayıcının gerçekten Log olduğunu bilet önekinden ('log-') doğruluyor;
# değilse paket hiçbir şey yapmadan düşer (gerçek Expo'ya sahte token göndermesin).
#
# ─── ZAMANA BAĞLI KURALLAR ──────────────────────────────────────────────────────
# Sessiz saat (22:00–09:00 TR) istek, kabul ve onay satırlarını sabaha kaydırır. Paket
# gece de koşabilsin diye bu satırların DueAt'i SQL ile şimdiye çekiliyor (VadeyiCek);
# kaydırmanın kendisi birim testlerde (SessizSaatTests). Hatırlatma işinin iki kaynağı ise
# sunucunun GERÇEK saatine bağlı ve SQL ile kaydırılamıyor:
#   • otomatik onay 24 sa hatırlatması: an sessiz saate düşerse kayıyor → yalnızca TR 08–21
#   • günlük istek özeti: yazım penceresi 05:00–08:00 UTC
# Pencere dışında bu iki kontrol [ATLANDI] basar; özet onları EKSİK sayar (geçti DEĞİL).
#
# ─── BÖLÜMLER ───────────────────────────────────────────────────────────────────
#   1. Kayıt ve oturum bağı (aydınlatma kapısı, token taşıma, çıkış sonrası PUT, "en yeni
#      token" kuralı, Rotated token'la çıkış, forget, eşzamanlı PUT)
#   2. Mesaj (REST, hub, okundu, birleştirme, kısma, engel, tercih, kanal)
#   3. İstek (ret bildirimsiz, tekrar freni, kabul, süresi dolmuş istek 409, özet)
#   4. Tamamlama ve itiraz reddi (farklı anahtarlı ikinci onay satırı, damga eşitliği)
#   5. Hatırlatmalar (tekilleştirme, yakın rezervasyon, iptal sonrası DurumDegisti, oto onay)
#   6. Cihaz silme noktaları (tek cihaz / her yerden çıkış, rol değişimi, hırsızlık tespiti,
#      hesap silme, ban, geçici askı)
#   7. Makbuz (ölü cihaz) ve gönderim sürerken cihaz silme
#   8. Fencing (kirası başkasına geçen satırı eski tur ezemez)
#   9. Yabancı Expo projesi (ayırma ve yeniden gönderme)
#  10. Test ucu sınırı
#
# BİLİNÇLİ KAPSAM DIŞI (gerekçeli):
#   • Parola sıfırlama: sıfırlama token'ı yalnızca e-postayla gidiyor (e2e-fixes.ps1 F1 ile
#     aynı engel). Aynı silme kodunu (RefreshTokenService.TumOturumlariDusurAsync) her yerden
#     çıkış, rol değişimi ve hırsızlık tespiti burada sürüyor.
#   • Banlı hesabın silinmesi: banlı hesap AccountStatusMiddleware'de 403 alıyor, HTTP'den
#     sürülemiyor. Ban anında cihaz silmesi (6) ve normal hesap silmesi (6) burada.
#   • API yeniden başlatma sonrası tekilleştirme: aynı kod yolu (ON CONFLICT) iki çağrı ve iki
#     kopyada eşzamanlı çağrıyla sınanıyor (5).
#   • Bildirim GÖVDESİ (ör. "3 yeni mesaj"): Log sağlayıcısı başlık ve gövdeyi bilerek
#     günlüğe yazmıyor; metinler birim testlerde (BildirimMetniTests).
#
# ─── MUTASYON KANITI (CLAUDE.md §Testler) ───────────────────────────────────────
# Şu değişikliklerin her biri ilgili bölümü KIRMIZIYA çeviriyor (ölçüldü, 2026-09-25; her
# mutasyondan sonra dosya bayt bayt geri yüklendi):
#   M1  UNIQUE(RecipientUserId, DedupeKey) index'i düşürülünce → 5: hatırlatma işi 500
#       (ON CONFLICT'in dayanacağı kısıt yok), bölüm yarıda kesildi.
#   M1b index VE ON CONFLICT birlikte kaldırılınca (sessiz hâl) → 5: 4 yerine 16 hatırlatma
#       satırı, 60 dk hatırlatması 2 yerine 8 kez Sent.
#   M2  dağıtıcıdaki engel kontrolü kaldırılınca → 2: engellenmiş kişinin mesajı Sent.
#   M3  Logout'taki (UserId, HwidHash) push silmesi kaldırılınca → 1 ve 6 (5 kırmızı).
#   M4  sonuç yazımındaki LeaseOwner koşulu kaldırılınca → 8: eski tur yeni sahibin satırını
#       Sent'e ezdi.
#   M5  deneyim ayırma ve bölme kaldırılınca (istek hatası hep geçici) → 9: iki satır Pending
#       kaldı, yabancı cihaz silinmedi.
#   M6  OturumBagi "en yeni token" yerine "herhangi aktif token" → 1: çıkış yapılmış cihaz
#       yeniden kaydoldu, test ucu 1 cihaz saydı, dağıtıcı Sent yazdı.
#
# Kullanım (proje kökünden; API :5000, Push:Provider=Log, PostgreSQL):
#   powershell -ExecutionPolicy Bypass -File .\tools\e2e-bildirim.ps1
#   ... -Bolum 2,8                           # yalnızca bu bölümler
#   ... -Api http://127.0.0.1:5093 -Api2 http://127.0.0.1:5092 -PgPort 5495 -PgUser postgres -PgDb dogrula -PgPassword x
# İkinci kopya (-Api2, varsayılan :5001) yoksa iki kopyalı kontroller [ATLANDI] basar.
#
# PS 5.1: SQL'de tanımlayıcılar çift-çift tırnak (""Notifications""), literaller tek tırnak.

param(
    [string]$Api = 'http://localhost:5000',
    [string]$Api2 = 'http://localhost:5001',
    [string]$PgHost = 'localhost',
    [int]$PgPort = 5432,
    [string]$PgUser = 'peerlearn',
    [string]$PgDb = 'peerlearn',
    [string]$PgPassword = 'PeerLearnDev2026',
    [string]$Bolum = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
[System.Net.ServicePointManager]::DefaultConnectionLimit = 100

$API = $Api.TrimEnd('/')
$API2 = $Api2.TrimEnd('/')
$env:PGPASSWORD = $PgPassword
$env:PGCLIENTENCODING = 'UTF8'

$PSQL = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
if (-not (Test-Path $PSQL)) {
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    $bulunan = if ($psqlCmd) { $psqlCmd.Source } else { $null }
    if ($bulunan) { $PSQL = $bulunan } else { $PSQL = $null }
}
$script:ComposeYml = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'

$script:failures = 0
$script:kontrol = 0
$script:atlanan = 0
function Step($name) { Write-Host "`n=== $name ===" -ForegroundColor Cyan }
function OK($msg)    { Write-Host "  [OK] $msg" -ForegroundColor Green; $script:kontrol++ }
function Fail($msg)  { Write-Host "  [KALDI] $msg" -ForegroundColor Red; $script:failures++ }
function Atla($msg)  { Write-Host "  [ATLANDI] $msg" -ForegroundColor Yellow; $script:atlanan++ }
function Esit($etiket, $gelen, $beklenen) {
    if ("$gelen" -eq "$beklenen") { OK $etiket } else { Fail "$etiket — beklenen '$beklenen', gelen '$gelen'" }
}

function Bolumde([int]$n) {
    if (-not $Bolum) { return $true }
    return @($Bolum -split ',' | ForEach-Object { $_.Trim() }) -contains "$n"
}

# Bölümün ortasında beklenmedik bir hata (ör. işin 500 dönmesi) bölümü KIRMIZI kapatır ve
# sonraki bölümler yine koşar. Yakalanmasaydı betik PowerShell hatasıyla düşer, özet
# satırı hiç basılmaz ve hangi iddianın koşmadığı görünmezdi.
function BolumHatasi($n, $kayit) {
    $konum = if ($kayit.InvocationInfo) { " (satır $($kayit.InvocationInfo.ScriptLineNumber))" } else { '' }
    Fail "$n. bölüm yarıda kesildi$($konum): $($kayit.Exception.Message)"
}

# Hazırlıkta (bölüm dışı) bir hata: kırmızı satır bas ve çık.
trap {
    Write-Host "  [HATA] beklenmedik hata (satır $($_.InvocationInfo.ScriptLineNumber)): $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# ------------------------------------------------------------------ HTTP

function Api {
    param($Method, $Path, $Body, $Token, $Taban = $API)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Uri = "$Taban$Path"; Method = $Method; Headers = $headers; TimeoutSec = 60 }
    if ($null -ne $Body) {
        # UTF-8 bayt: PS 5.1 dize gövdeyi Latin-1 kodluyor.
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 6))
        $params['ContentType'] = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @params
}

# Hata BEKLENEN ya da durum kodu gereken çağrılar: hiç fırlatmaz.
function ApiDurum {
    param($Method, $Path, $Body, $Token, $Taban = $API)
    try {
        Api -Method $Method -Path $Path -Body $Body -Token $Token -Taban $Taban | Out-Null
        return @{ status = 200; code = '' }
    } catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { return @{ status = 0; code = 'NO_RESPONSE'; detail = $_.Exception.Message } }
        $raw = ''
        try {
            $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
            $raw = $reader.ReadToEnd()
        } catch {}
        $obj = $null
        try { $obj = $raw | ConvertFrom-Json } catch {}
        $kod = if ($obj) { $obj.title } else { $raw }
        return @{ status = [int]$resp.StatusCode; code = $kod }
    }
}

# Gerçek eşzamanlılık: Invoke-RestMethod sıralı. İstekler aynı anda yola çıkar, hepsi beklenir.
$script:Http = New-Object System.Net.Http.HttpClient
$script:Http.Timeout = [TimeSpan]::FromSeconds(60)
function Eszamanli([object[]]$istekler) {
    $gorevler = New-Object 'System.Collections.Generic.List[System.Threading.Tasks.Task]'
    foreach ($i in $istekler) {
        $m = New-Object System.Net.Http.HttpRequestMessage -ArgumentList ([System.Net.Http.HttpMethod]::new($i.Method)), $i.Url
        if ($i.Token) { $m.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue -ArgumentList 'Bearer', $i.Token }
        if ($null -ne $i.Body) {
            $m.Content = New-Object System.Net.Http.StringContent -ArgumentList ($i.Body | ConvertTo-Json -Depth 6), ([System.Text.Encoding]::UTF8), 'application/json'
        }
        $gorevler.Add($script:Http.SendAsync($m))
    }
    [System.Threading.Tasks.Task]::WaitAll($gorevler.ToArray())
    return @($gorevler | ForEach-Object { [int]$_.Result.StatusCode })
}

# ------------------------------------------------------------------ SQL

function Sql($query) {
    if (-not $PSQL) {
        $out = $query | docker compose -f $script:ComposeYml exec -T db psql -U $PgUser -d $PgDb -t -A -v ON_ERROR_STOP=1 2>&1
        return (($out | ForEach-Object { "$_" }) -join "`n").Trim()
    }
    $f = Join-Path $env:TEMP ("pl_bildirim_" + [Guid]::NewGuid().ToString('N') + ".sql")
    [System.IO.File]::WriteAllText($f, $query, (New-Object System.Text.UTF8Encoding($false)))
    $out = & $PSQL -w -h $PgHost -p $PgPort -U $PgUser -d $PgDb -t -A -v ON_ERROR_STOP=1 -f $f 2>&1
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    return (($out | ForEach-Object { "$_" }) -join "`n").Trim()
}

function Tek($query) { return ((Sql $query) -split "`n")[0].Trim() }

# Bildirim satırı: "Status|Outcome" (Outcome yoksa boş).
function Durum($kosul) {
    return Tek "SELECT ""Status"" || '|' || COALESCE(""Outcome"", '') FROM comms.""Notifications"" WHERE $kosul ORDER BY ""CreatedAtUtc"" LIMIT 1;"
}

# Satır işlenene (Pending'den çıkana) kadar bekler; son durumu döndürür.
function BekleSatir($kosul, [int]$saniye = 45) {
    $bitis = (Get-Date).AddSeconds($saniye)
    do {
        $r = Durum $kosul
        if ($r -and -not $r.StartsWith('Pending')) { return $r }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $bitis)
    if (-not $r) { return '(satır yok)' }
    return $r
}

# Koşula uyan TÜM satırlar işlenene kadar bekler.
function BekleHepsi($kosul, [int]$saniye = 60) {
    $bitis = (Get-Date).AddSeconds($saniye)
    do {
        $bekleyen = Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE $kosul AND ""Status"" = 'Pending';"
        if ($bekleyen -eq '0') { return $true }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $bitis)
    return $false
}

# Sessiz saatte sabaha kaymış satırı şimdiye çeker (kaydırmanın kendisi birim testte).
function VadeyiCek($kosul) {
    Sql "UPDATE comms.""Notifications"" SET ""DueAtUtc"" = now() WHERE $kosul AND ""Status"" = 'Pending' AND ""DueAtUtc"" > now();" | Out-Null
}

function CihazSayisi($userId) { return Tek "SELECT COUNT(*) FROM comms.""PushDevices"" WHERE ""UserId"" = '$userId';" }

# Satır kiralanana (dağıtıcı sahiplenene) kadar bekler; "LeaseOwner|Status" ya da $null.
function BekleKira($id, [int]$saniye = 20) {
    $bitis = (Get-Date).AddSeconds($saniye)
    do {
        $r = Tek "SELECT COALESCE(""LeaseOwner""::text, '') || '|' || ""Status"" FROM comms.""Notifications"" WHERE ""Id"" = '$id';"
        if ($r -and -not $r.StartsWith('|')) { return $r }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $bitis)
    return $null
}

function SonTestSatiri($userId) {
    return Tek "SELECT ""Id"" FROM comms.""Notifications"" WHERE ""RecipientUserId"" = '$userId' AND ""Type"" = 'Test' ORDER BY ""CreatedAtUtc"" DESC LIMIT 1;"
}

# ------------------------------------------------------------------ Kullanıcılar

$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$tag = "Pbx$stamp"
$script:seq = 0
$SURUM = & "$PSScriptRoot\yasal-surum.ps1"

function NewHwid { return (([Guid]::NewGuid().ToString('N')) * 2) }
function PushToken($ek = '') { return "ExponentPushToken[$([Guid]::NewGuid().ToString('N'))$ek]" }

function Giris($u, $hwid) {
    return Api POST '/api/auth/login' @{ email = $u.email; password = 'Parola12345'; hwidHash = $hwid; rememberMe = $true }
}

function NewUser($prefix) {
    $script:seq++
    $email = "pb$prefix$stamp$($script:seq)@test.dev"
    $reg = Api POST '/api/auth/register' @{ email = $email; password = 'Parola12345'; displayName = "$prefix Deneme"; termsVersion = $SURUM; ageConfirmed = $true }
    Api POST '/api/auth/verify-email' @{ email = $email; code = $reg.verificationToken } | Out-Null
    $hwid = NewHwid
    $u = [pscustomobject]@{ email = $email; userId = $null; token = $null; refresh = $null; hwid = $hwid; push = $null }
    $g = Giris $u $hwid
    $u.userId = $g.userId; $u.token = $g.accessToken; $u.refresh = $g.refreshToken
    return $u
}

function Aydinlatma($u) { Api PUT '/api/push/prompt' @{ karar = 'Acildi' } $u.token | Out-Null }

function Cihaz($u, $pushToken, $kapali = @(), $erisim = $null, $hwid = $null, $platform = 'Android') {
    if (-not $erisim) { $erisim = $u.token }
    if (-not $hwid) { $hwid = $u.hwid }
    # $null geçilirse @($null) tek elemanlı dizi olur ve [null] gönderilirdi.
    $liste = @(@($kapali) | Where-Object { $null -ne $_ })
    return Api PUT '/api/push/devices' @{ token = $pushToken; platform = $platform; hwidHash = $hwid; kapaliKanallar = $liste } $erisim
}

# Aydınlatmayı açmış ve cihaz kaydetmiş kullanıcı.
function Hazir($prefix, $ek = '') {
    $u = NewUser $prefix
    Aydinlatma $u
    $u.push = PushToken $ek
    $r = Cihaz $u $u.push
    if (-not $r.kayitli) { Fail "hazırlık: $prefix cihazı kaydedilemedi" }
    return $u
}

# Konusuz istek + kabul → sohbet kimliği.
function Arkadas($a, $b) {
    $m = Api POST '/api/matches' @{ responderUserId = $b.userId; requestedTopicId = $null; offeredTopicId = $null } $a.token
    $r = Api POST "/api/matches/$m/respond" @{ accept = $true } $b.token
    return $r.conversationId
}

function Mesaj($u, $sohbet, $icerik = 'gizli içerik 12345') {
    return (Api POST "/api/conversations/$sohbet/messages" @{ content = $icerik } $u.token).id
}

function Admin {
    $a = NewUser 'adm'
    Sql "UPDATE identity.""Users"" SET ""Role"" = 'Admin' WHERE ""Id"" = '$($a.userId)';" | Out-Null
    $g = Giris $a $a.hwid
    $a.token = $g.accessToken; $a.refresh = $g.refreshToken
    return $a
}

function Teklif($u, $konu) {
    Api POST '/api/portfolio/entries' @{ topicId = $konu; direction = 'Offer'; selfAssessedLevel = 5; note = $null } $u.token | Out-Null
}

# Öğrenci → eğitmen konulu arkadaşlık + rezervasyon. Başlangıç şimdiden $dakika sonra.
function DersKur($ogr, $egt, $konu, [int]$dakika = 120) {
    Teklif $egt $konu
    $m = Api POST '/api/matches' @{ responderUserId = $egt.userId; requestedTopicId = $konu; offeredTopicId = $null } $ogr.token
    Api POST "/api/matches/$m/respond" @{ accept = $true } $egt.token | Out-Null
    $start = [DateTime]::UtcNow.AddMinutes($dakika).ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    $b = Api POST '/api/sessions' @{ matchId = $m; topicId = $konu; scheduledStartUtc = $start; durationMinutes = 60 } $ogr.token
    return @{ id = $b.sessionId; kod = $b.verificationCode; match = $m }
}

function DersiGecmiseAl($id) {
    Sql "UPDATE scheduling.""LessonSessions"" SET ""ScheduledStartUtc"" = now() - interval '2 hours', ""ScheduledEndUtc"" = now() - interval '1 hour' WHERE ""Id"" = '$id';" | Out-Null
}

function KanitYukle($sessionId, $kod, $token) {
    $png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==')
    $bd = [Guid]::NewGuid().ToString('N')
    $enc = [System.Text.Encoding]::UTF8
    $ms = New-Object System.IO.MemoryStream
    $h = $enc.GetBytes("--$bd`r`nContent-Disposition: form-data; name=`"verificationCode`"`r`n`r`n$kod`r`n--$bd`r`nContent-Disposition: form-data; name=`"proof`"; filename=`"p.png`"`r`nContent-Type: image/png`r`n`r`n")
    $t = $enc.GetBytes("`r`n--$bd--`r`n")
    $ms.Write($h, 0, $h.Length); $ms.Write($png, 0, $png.Length); $ms.Write($t, 0, $t.Length)
    $body = $ms.ToArray(); $ms.Dispose()
    Invoke-RestMethod -Uri "$API/api/sessions/$sessionId/complete" -Method Post `
        -Headers @{ Authorization = "Bearer $token" } `
        -ContentType "multipart/form-data; boundary=$bd" -Body $body -TimeoutSec 60 | Out-Null
}

# ================================================================== HAZIRLIK
Write-Host "PeerLearn — push bildirimleri e2e (API $API)" -ForegroundColor White
Step 'Hazırlık'

try { Invoke-RestMethod "$API/health" -TimeoutSec 10 | Out-Null; OK "API ayakta ($API)" }
catch { Fail "API'ye ulaşılamadı: $API"; exit 1 }

$script:Api2Var = $false
try { Invoke-RestMethod "$API2/health" -TimeoutSec 5 | Out-Null; $script:Api2Var = $true; OK "ikinci kopya ayakta ($API2)" }
catch { Write-Host "  ikinci kopya yok ($API2): iki kopyalı kontroller atlanacak" -ForegroundColor DarkYellow }

$admin = Admin
OK 'yönetici hazır'

# Sağlayıcı Log mu? Gerçek Expo'ya sahte token göndermeyelim; ayrıca kancalar yalnızca Log'da var.
$hz = Hazir 'hz'
Api POST '/api/push/test' @{ tur = 'mesaj' } $hz.token | Out-Null
$hzSatir = SonTestSatiri $hz.userId
$hzDurum = BekleSatir """Id"" = '$hzSatir'" 30
$bilet = Tek "SELECT ""TicketId"" FROM comms.""PushTickets"" WHERE ""NotificationId"" = '$hzSatir' LIMIT 1;"
if ($hzDurum -eq 'Sent|' -and $bilet.StartsWith('log-')) { OK "Push:Provider = Log (bilet '$($bilet.Substring(0, 4))…', test bildirimi Sent)" }
else { Write-Host "  [HATA] Push:Provider Log değil ya da dağıtıcı çalışmıyor (durum '$hzDurum', bilet '$bilet'). Paket durduruldu." -ForegroundColor Red; exit 1 }

# Koşuma özel katalog: paylaşılan konu kullanılmaz (CLAUDE.md §Testler).
$kat = [Guid]::NewGuid(); $ders = [Guid]::NewGuid()
$konular = @(1..4 | ForEach-Object { [Guid]::NewGuid() })
Sql @"
INSERT INTO catalog."EducationCategories" ("Id","ParentCategoryId","Name","Slug","SortOrder","IsActive","CreatedAtUtc")
VALUES ('$kat', NULL, '$tag Kok', 'pbx-$stamp', 950, TRUE, now());
INSERT INTO catalog."Subjects" ("Id","CategoryId","Name","SortOrder","IsActive","CreatedAtUtc")
VALUES ('$ders', '$kat', '$tag Ders', 950, TRUE, now());
INSERT INTO catalog."Topics" ("Id","SubjectId","Name","SortOrder","IsActive","CreatedAtUtc") VALUES
 ('$($konular[0])', '$ders', '$tag Konu 1', 951, TRUE, now()),
 ('$($konular[1])', '$ders', '$tag Konu 2', 952, TRUE, now()),
 ('$($konular[2])', '$ders', '$tag Konu 3', 953, TRUE, now()),
 ('$($konular[3])', '$ders', '$tag Konu 4', 954, TRUE, now());
"@ | Out-Null
OK "koşuma özel dört konu ($tag)"

# ================================================================== 1
if (Bolumde 1) { try {
    Step '1. Kayıt ve oturum bağı'

    $a = NewUser 'bag'
    $tA = PushToken
    $r = Cihaz $a $tA
    if ($r.kayitli -eq $false -and (CihazSayisi $a.userId) -eq '0') { OK 'aydınlatmasız PUT → kayitli:false, satır yok' }
    else { Fail "aydınlatmasız PUT kabul edildi (kayitli=$($r.kayitli), satır=$(CihazSayisi $a.userId))" }

    Aydinlatma $a
    $r = Cihaz $a $tA
    if ($r.kayitli -and $r.alici -match '^[0-9a-f]{16}$') { OK 'aydınlatmadan sonra kayitli:true, alıcı etiketi 16 hex' } else { Fail "kayıt: $($r | ConvertTo-Json -Compress)" }
    Esit 'cihaz satırı HWID ve platformla yazıldı' (Tek "SELECT ""HwidHash"" || '|' || ""Platform"" FROM comms.""PushDevices"" WHERE ""Token"" = '$tA';") "$($a.hwid)|Android"

    # Aynı telefonda hesap değişimi: token yeni hesaba TAŞINIR, iki hesaba birden bağlı kalmaz.
    $b = NewUser 'bagb'
    Aydinlatma $b
    $r = Cihaz $b $tA
    Esit 'B aynı token''ı kaydetti → satır B''ye taşındı' (Tek "SELECT ""UserId"" FROM comms.""PushDevices"" WHERE ""Token"" = '$tA';") $b.userId
    Esit 'A''da cihaz kalmadı' (CihazSayisi $a.userId) '0'

    # "En yeni token" kuralı: aynı HWID ile iki giriş → ilk token ARTIK ama aktif kalır.
    $c = NewUser 'bagc'
    Aydinlatma $c
    $g2 = Giris $c $c.hwid                      # c.refresh artık, g2 en yeni
    $tC = PushToken
    $r = Cihaz $c $tC $null $g2.accessToken
    if ($r.kayitli) { OK 'ikinci girişten sonra cihaz kayıtlı' } else { Fail 'ikinci girişten sonra cihaz kaydedilemedi' }
    Api POST '/api/session/logout' @{ refreshToken = $g2.refreshToken } | Out-Null
    Esit 'çıkış → bu HWID''in push satırı silindi' (CihazSayisi $c.userId) '0'
    # Erişim token'ı ömrü dolana kadar geçerli; artık token hâlâ aktif. Kural "herhangi aktif
    # token" olsaydı çıkış yapılmış telefon yeniden kaydolurdu (M6 bunu kırar).
    $r = Cihaz $c $tC $null $g2.accessToken
    if ($r.kayitli -eq $false -and (CihazSayisi $c.userId) -eq '0') { OK 'çıkıştan sonra hâlâ geçerli erişim token''ıyla PUT → kayitli:false (artık token aktif olsa da)' }
    else { Fail "çıkış yapılmış cihaz yeniden kaydoldu (kayitli=$($r.kayitli))" }

    # Dağıtıcı da aynı kuralı kullanıyor: yeni giriş, kayıt, sonra EN YENİ token'ı iptal et
    # (çıkışın cihaz silmesini atlayan bir yol gibi). Artık token aktif ama gönderim olmamalı.
    $g3 = Giris $c $c.hwid
    $r = Cihaz $c $tC $null $g3.accessToken
    Sql @"
UPDATE identity."RefreshTokens" SET "RevokedAtUtc" = now(), "RevokeReason" = 'SignedOut'
WHERE "Id" = (SELECT "Id" FROM identity."RefreshTokens" WHERE "UserId" = '$($c.userId)' AND "DeviceHwidHash" = '$($c.hwid)'
              ORDER BY "CreatedAtUtc" DESC, "Id" DESC LIMIT 1);
"@ | Out-Null
    $artikAktif = Tek "SELECT COUNT(*) FROM identity.""RefreshTokens"" WHERE ""UserId"" = '$($c.userId)' AND ""RevokedAtUtc"" IS NULL AND ""ExpiresAtUtc"" > now();"
    $t = Api POST '/api/push/test' @{ tur = 'mesaj' } $g3.accessToken
    Esit 'test ucu bağlı cihaz saymadı (en yeni token iptal)' $t.cihaz 0
    Esit 'dağıtıcı: en yeni token iptal, artık token aktif → Skipped(CihazYok)' (BekleSatir """Id"" = '$(SonTestSatiri $c.userId)'") 'Skipped|CihazYok'
    if ([int]$artikAktif -ge 1) { OK "önkoşul: $artikAktif artık token aktifti (kural gerçekten sınandı)" } else { Fail 'önkoşul: artık token yok, kural sınanmadı' }

    # Rotated token'la çıkış: halef iptal, push satırı silinir.
    $d = Hazir 'bagd'
    $yeni = Api POST '/api/session/refresh' @{ refreshToken = $d.refresh; hwidHash = $d.hwid }
    Api POST '/api/session/logout' @{ refreshToken = $d.refresh } | Out-Null
    Esit 'Rotated (eski) token''la çıkış → push satırı silindi' (CihazSayisi $d.userId) '0'
    $halef = Tek "SELECT ""RevokeReason"" FROM identity.""RefreshTokens"" WHERE ""UserId"" = '$($d.userId)' ORDER BY ""CreatedAtUtc"" DESC LIMIT 1;"
    Esit 'halef token iptal edildi' $halef 'SignedOut'
    $yenileme = ApiDurum POST '/api/session/refresh' @{ refreshToken = $yeni.refreshToken; hwidHash = $d.hwid }
    Esit 'halefle yenileme artık 401' $yenileme.status 401

    # forget: kimliksiz, 204.
    $e = Hazir 'bage'
    $w = Invoke-WebRequest -UseBasicParsing -Uri "$API/api/push/devices/forget" -Method Post -ContentType 'application/json' `
        -Body ([System.Text.Encoding]::UTF8.GetBytes((@{ token = $e.push } | ConvertTo-Json)))
    Esit 'forget kimliksiz → 204' $w.StatusCode 204
    Esit 'forget → satır silindi' (CihazSayisi $e.userId) '0'

    # Aynı (kullanıcı, HWID) için sekiz farklı token aynı anda: hata yok, tek satır.
    $f = NewUser 'bagf'
    Aydinlatma $f
    $istekler = @(1..8 | ForEach-Object {
        @{ Method = 'PUT'; Url = "$API/api/push/devices"; Token = $f.token; Body = @{ token = (PushToken); platform = 'Android'; hwidHash = $f.hwid; kapaliKanallar = @() } }
    })
    $kodlar = Eszamanli $istekler
    Esit 'sekiz eşzamanlı PUT → hepsi 200' (@($kodlar | Where-Object { $_ -eq 200 }).Count) 8
    Esit 'sekiz eşzamanlı PUT → tek satır' (CihazSayisi $f.userId) '1'
} catch { BolumHatasi 1 $_ } }

# ================================================================== 2
if (Bolumde 2) { try {
    Step '2. Mesaj'

    # Her senaryo kendi çiftinde: kısma yuvası ve okunmamış sayısı senaryolar arasında sızmasın.
    $g1 = NewUser 'msg1'; $a1 = Hazir 'msa1'; $s1 = Arkadas $g1 $a1
    $g3 = NewUser 'msg3'; $a3 = Hazir 'msa3'; $s3 = Arkadas $g3 $a3
    $g4 = NewUser 'msg4'; $a4 = Hazir 'msa4'; $s4 = Arkadas $g4 $a4
    $g5 = NewUser 'msg5'; $a5 = Hazir 'msa5'; $s5 = Arkadas $g5 $a5
    $g6 = NewUser 'msg6'; $a6 = Hazir 'msa6'; $s6 = Arkadas $g6 $a6
    $g7 = NewUser 'msg7'; $a7 = Hazir 'msa7'; $s7 = Arkadas $g7 $a7
    $g8 = NewUser 'msg8'; $a8 = Hazir 'msa8'; $s8 = Arkadas $g8 $a8

    # Ön ayarlar: tercih ve telefon kanalı kapalı.
    Api PUT '/api/push/preferences/mesajlar' @{ acik = $false } $a6.token | Out-Null
    Cihaz $a7 $a7.push @('mesajlar') | Out-Null

    # Hepsi aynı anda yola çıkar; gecikme (10 sn) boyunca okundu ve engel işaretlenir.
    $m1 = Mesaj $g1 $s1
    $m3 = Mesaj $g3 $s3
    Api POST "/api/conversations/$s3/read" $null $a3.token | Out-Null
    $m4 = @(1..3 | ForEach-Object { Mesaj $g4 $s4 "birleşecek $_" })
    $m5 = Mesaj $g5 $s5
    $m6 = Mesaj $g6 $s6
    $m7 = Mesaj $g7 $s7
    $m8 = Mesaj $g8 $s8
    Api POST '/api/blocks' @{ userId = $g8.userId; note = $null } $a8.token | Out-Null

    Esit 'REST mesajı → Sent' (BekleSatir """RecordId"" = '$m1'") 'Sent|'
    $biletSayisi = Tek "SELECT COUNT(*) FROM comms.""PushTickets"" t JOIN comms.""Notifications"" n ON n.""Id"" = t.""NotificationId"" WHERE n.""RecordId"" = '$m1';"
    Esit 'Sent satırın bileti yazıldı' $biletSayisi 1
    Esit 'gecikme içinde okundu → Skipped(Okundu)' (BekleSatir """RecordId"" = '$m3'") 'Skipped|Okundu'
    Esit 'tercih kapalı → Skipped(TercihKapali)' (BekleSatir """RecordId"" = '$m6'") 'Skipped|TercihKapali'
    Esit 'telefonda kanal kapalı → Skipped(KanalKapali)' (BekleSatir """RecordId"" = '$m7'") 'Skipped|KanalKapali'
    Esit 'gecikme içinde engellendi → Skipped(Engel)' (BekleSatir """RecordId"" = '$m8'") 'Skipped|Engel'

    $m4Kosul = """RecordId"" IN ('$($m4 -join "','")')"
    if (BekleHepsi $m4Kosul 90) {
        $gonderilen = Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE $m4Kosul AND ""Status"" = 'Sent';"
        $birlesen = Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE $m4Kosul AND ""Status"" = 'Skipped' AND ""Outcome"" = 'Birlestirildi';"
        if ($gonderilen -eq '1' -and $birlesen -eq '2') { OK 'art arda üç mesaj → tek Sent, ikisi Skipped(Birlestirildi)' }
        else { Fail "üç mesaj: Sent=$gonderilen, Birlestirildi=$birlesen" }
    } else { Fail 'üç mesajın satırları 90 sn içinde işlenmedi' }

    # Tercih geri açılınca aynı sohbet gönderilir (kapalıyken düşenin nedeni gerçekten tercihti).
    Api PUT '/api/push/preferences/mesajlar' @{ acik = $true } $a6.token | Out-Null
    $m6b = Mesaj $g6 $s6
    Esit 'tercih açılınca → Sent' (BekleSatir """RecordId"" = '$m6b'") 'Sent|'

    # Kısma: okunmuş m5'ten sonra gelen mesaj 60 sn dolmadan gönderilmez. İkinci kopya varsa
    # iki dağıtıcı aynı anda koşturulur: SKIP LOCKED + kısma yuvası çift başına tek gönderim.
    Esit 'kısma: ilk mesaj Sent' (BekleSatir """RecordId"" = '$m5'") 'Sent|'
    Api POST "/api/conversations/$s5/read" $null $a5.token | Out-Null
    $m5b = Mesaj $g5 $s5
    if ($script:Api2Var) {
        Start-Sleep -Seconds 11   # m5b vadesine girsin
        $kodlar = Eszamanli @(
            @{ Method = 'POST'; Url = "$API/api/admin/jobs/push-dispatch"; Token = $admin.token },
            @{ Method = 'POST'; Url = "$API2/api/admin/jobs/push-dispatch"; Token = $admin.token },
            @{ Method = 'POST'; Url = "$API/api/admin/jobs/push-dispatch"; Token = $admin.token },
            @{ Method = 'POST'; Url = "$API2/api/admin/jobs/push-dispatch"; Token = $admin.token })
        Esit 'iki kopyada eşzamanlı dağıtım → dört çağrı 200' (@($kodlar | Where-Object { $_ -eq 200 }).Count) 4
        $erken = Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE ""RecordId"" = '$m5b' AND ""Status"" = 'Sent';"
        Esit 'iki kopya aynı anda koştu, 60 sn dolmadan ikinci gönderim yok' $erken 0
    } else {
        Atla 'iki kopyada eşzamanlı dağıtım (ikinci kopya yok)'
    }
    $d5 = BekleSatir """RecordId"" = '$m5b'" 100
    Esit 'kısma: ikinci mesaj sonunda Sent' $d5 'Sent|'
    $ara = Tek @"
SELECT EXTRACT(EPOCH FROM (b."ProcessedAtUtc" - a."ProcessedAtUtc"))::int FROM comms."Notifications" a, comms."Notifications" b
WHERE a."RecordId" = '$m5' AND b."RecordId" = '$m5b';
"@
    if ([int]$ara -ge 58) { OK "kısma: iki gönderim arası $ara sn (≥ 60 sn yuva)" } else { Fail "kısma delindi: iki gönderim arası $ara sn" }

    # Hub: aynı handler'dan geçiyor ama iddiası ayrı (ChatHub'a satır yazımı eklenmedi).
    $sr = Join-Path (Split-Path $PSScriptRoot -Parent) 'frontend\node_modules\@microsoft\signalr'
    $node = Get-Command node -ErrorAction SilentlyContinue
    if ($node -and (Test-Path $sr)) {
        $g2 = NewUser 'msg2'; $a2 = Hazir 'msa2'; $s2 = Arkadas $g2 $a2
        $js = Join-Path $env:TEMP ("pl_hub_" + [Guid]::NewGuid().ToString('N') + '.js')
        [System.IO.File]::WriteAllText($js, @'
const [, , yol, taban, token, sohbet, icerik] = process.argv;
const signalR = require(yol);
(async () => {
  const c = new signalR.HubConnectionBuilder()
    .withUrl(taban + '/hubs/chat', { accessTokenFactory: () => token })
    .configureLogging(signalR.LogLevel.Error)
    .build();
  await c.start();
  const m = await c.invoke('SendMessage', sohbet, icerik);
  console.log(m.id);
  await c.stop();
})().catch((e) => { console.error(e.message); process.exit(1); });
'@, (New-Object System.Text.UTF8Encoding($false)))
        # PS 5.1: Stop tercihiyle yerel komutun stderr satırı istisnaya dönüşür; node'un
        # uyarıları hub denemesini düşürmesin, sonuç çıktının son satırından okunuyor.
        $onceki = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $out = & $node.Source $js $sr $API $g2.token $s2 'hub içeriği' 2>&1
        $ErrorActionPreference = $onceki
        Remove-Item $js -Force -ErrorAction SilentlyContinue
        $son = @($out) | Select-Object -Last 1
        $hubId = if ($null -ne $son) { "$son".Trim() } else { '' }
        if ($hubId -match '^[0-9a-f-]{36}$') { Esit 'hub mesajı → Sent' (BekleSatir """RecordId"" = '$hubId'") 'Sent|' }
        else { Fail "hub mesajı gönderilemedi: $out" }
    } else {
        Atla 'hub mesajı (node ya da frontend/node_modules/@microsoft/signalr yok)'
    }
} catch { BolumHatasi 2 $_ } }

# ================================================================== 3
if (Bolumde 3) { try {
    Step '3. İstek'

    # Ret bildirilmez: engel de Declined yazıyor, ret bildirimi engeli sızdırırdı.
    $i = Hazir 'isti'; $j = Hazir 'istj'
    $m = Api POST '/api/matches' @{ responderUserId = $j.userId; requestedTopicId = $null; offeredTopicId = $null } $i.token
    Api POST "/api/matches/$m/respond" @{ accept = $false } $j.token | Out-Null
    Start-Sleep -Seconds 2
    Esit 'ret → isteği gönderene hiçbir satır yok' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE ""RecipientUserId"" = '$($i.userId)';") 0
    Esit 'ret → yalnızca gelen istek satırı var' (Tek "SELECT string_agg(""Type"", ',') FROM comms.""Notifications"" WHERE ""RecordId"" = '$m';") 'MatchRequest'

    # Aynı kişiden üç konu isteği: cihazda tek bildirim.
    $k = Hazir 'istk'; $l = Hazir 'istl'
    foreach ($konu in $konular) { Teklif $l $konu }
    # Aralık: gündüz her satır oluşur oluşmaz gidiyor; bir sonraki istek öncekinin Sent'i
    # yazılmadan değerlendirilirse fren onu göremez (iki kopyalı kabul edilmiş sınır,
    # docs/DEVAM-EDILECEK.md). Paket o sınırı değil, freni sınıyor.
    $istekler = @(0..2 | ForEach-Object {
        $id = Api POST '/api/matches' @{ responderUserId = $l.userId; requestedTopicId = $konular[$_]; offeredTopicId = $null } $k.token
        Start-Sleep -Milliseconds 1500
        $id
    })
    $lKosul = """RecipientUserId"" = '$($l.userId)' AND ""Type"" = 'MatchRequest'"
    VadeyiCek $lKosul
    if (BekleHepsi $lKosul 45) {
        Esit 'üç konu isteği → tek Sent' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE $lKosul AND ""Status"" = 'Sent';") 1
        Esit 'üç konu isteği → ikisi Skipped(Tekrar)' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE $lKosul AND ""Outcome"" = 'Tekrar';") 2
        Esit 'giden, çiftin EN ESKİ isteği' (Tek "SELECT ""RecordId"" FROM comms.""Notifications"" WHERE $lKosul AND ""Status"" = 'Sent';") $istekler[0]
    } else { Fail 'istek satırları 45 sn içinde işlenmedi' }

    # Ret sonrası yeniden istek: 7 günlük fren.
    Api POST "/api/matches/$($istekler[1])/respond" @{ accept = $false } $l.token | Out-Null
    $yeniden = Api POST '/api/matches' @{ responderUserId = $l.userId; requestedTopicId = $konular[3]; offeredTopicId = $null } $k.token
    VadeyiCek """RecordId"" = '$yeniden'"
    Esit 'ret sonrası yeniden istek → Skipped(Tekrar)' (BekleSatir """RecordId"" = '$yeniden'") 'Skipped|Tekrar'

    # Kabul: isteği gönderene gider.
    $kabul = Api POST "/api/matches/$($istekler[0])/respond" @{ accept = $true } $l.token
    $kKosul = """RecordId"" = '$($istekler[0])' AND ""Type"" = 'MatchAccepted'"
    VadeyiCek $kKosul
    Esit 'kabul → istekKabul Sent' (BekleSatir $kKosul) 'Sent|'
    Esit 'kabul satırı isteği gönderene ve yeni sohbete bağlı' (Tek "SELECT ""RecipientUserId"" || '|' || ""ConversationId"" FROM comms.""Notifications"" WHERE $kKosul;") "$($k.userId)|$($kabul.conversationId)"

    # 14 günü geçmiş istek: süpürücü henüz geçmemiş olsa bile yanıtlanamaz.
    $k2 = NewUser 'istk2'; $l2 = Hazir 'istl2'
    $eski = Api POST '/api/matches' @{ responderUserId = $l2.userId; requestedTopicId = $null; offeredTopicId = $null } $k2.token
    Sql "UPDATE matchmaking.""Matches"" SET ""CreatedAtUtc"" = now() - interval '15 days' WHERE ""Id"" = '$eski';" | Out-Null
    $r = ApiDurum POST "/api/matches/$eski/respond" @{ accept = $true } $l2.token
    Esit '14 günü geçmiş isteği kabul → 409' $r.status 409
    Esit '409 kodu MATCH_NOT_PENDING' $r.code 'MATCH_NOT_PENDING'
    Esit 'süresi dolmuş kabulde satır yok' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE ""RecordId"" = '$eski' AND ""Type"" = 'MatchAccepted';") 0

    # Günlük özet: yazım penceresi sunucunun gerçek saatine bağlı (05:00–08:00 UTC).
    $utc = [DateTime]::UtcNow
    if ($utc.Hour -ge 5 -and $utc.Hour -lt 8) {
        $k3 = NewUser 'istk3'; $l3 = Hazir 'istl3'
        $ozetIstek = Api POST '/api/matches' @{ responderUserId = $l3.userId; requestedTopicId = $null; offeredTopicId = $null } $k3.token
        # Düşme anı yuva + 12 sa: (yuva + 6, yuva + 30] aralığında.
        Sql "UPDATE matchmaking.""Matches"" SET ""CreatedAtUtc"" = (date_trunc('day', now() AT TIME ZONE 'UTC') + interval '7 hours' + interval '12 hours' - interval '14 days') AT TIME ZONE 'UTC' WHERE ""Id"" = '$ozetIstek';" | Out-Null
        Api POST '/api/admin/jobs/push-reminders' $null $admin.token | Out-Null
        Api POST '/api/admin/jobs/push-reminders' $null $admin.token | Out-Null
        $ozKosul = """RecipientUserId"" = '$($l3.userId)' AND ""Type"" = 'MatchExpiringDigest'"
        Esit 'özet: iki çağrı → alıcı başına tek satır' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE $ozKosul;") 1
        # Satır yuvadan (07:00 UTC) iki saat önce yazılır ama yuva anında vadelenir.
        VadeyiCek $ozKosul
        Esit 'özet satırı gönderildi' (BekleSatir $ozKosul) 'Sent|'
    } else {
        Atla "günlük istek özeti (yazım penceresi 05:00–08:00 UTC; şimdi $($utc.ToString('HH:mm')) UTC)"
    }
} catch { BolumHatasi 3 $_ } }

# ================================================================== 4
if (Bolumde 4) { try {
    Step '4. Tamamlama ve itiraz reddi'

    $ogr = Hazir 'onyo'; $egt = Hazir 'onye'
    $ders = DersKur $ogr $egt $konular[0]
    DersiGecmiseAl $ders.id
    KanitYukle $ders.id $ders.kod $egt.token
    $oKosul = """RecordId"" = '$($ders.id)' AND ""Type"" = 'ApprovalPending'"
    VadeyiCek $oKosul
    Esit 'kanıt yüklendi → öğrenciye onay bekliyor Sent' (BekleSatir $oKosul) 'Sent|'
    Esit 'onay satırının damgası CompletionRequestedAtUtc ile AYNI (SQL eşitliği)' (Tek @"
SELECT (n."OlayDamgasiUtc" = s."CompletionRequestedAtUtc")::text FROM comms."Notifications" n
JOIN scheduling."LessonSessions" s ON s."Id" = n."RecordId" WHERE n."RecordId" = '$($ders.id)' AND n."Type" = 'ApprovalPending';
"@) 'true'

    $itiraz = Api POST "/api/sessions/$($ders.id)/dispute" @{ reason = 'FakeProof'; description = 'kanit bu derse ait degil' } $ogr.token
    Api POST "/api/admin/disputes/$itiraz/resolve" @{ resolution = 'Dismissed'; note = 'kanit gecerli' } $admin.token | Out-Null
    VadeyiCek $oKosul
    if (BekleHepsi $oKosul 45) {
        Esit 'itiraz reddi → ikinci onay satırı' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE $oKosul;") 2
        Esit 'iki satırın anahtarı farklı' (Tek "SELECT COUNT(DISTINCT ""DedupeKey"") FROM comms.""Notifications"" WHERE $oKosul;") 2
        Esit 'ikinci satır da Sent' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE $oKosul AND ""Status"" = 'Sent';") 2
        Esit 'yeni damga = yeni CompletionRequestedAtUtc = itirazın ResolvedAtUtc' (Tek @"
SELECT (n."OlayDamgasiUtc" = s."CompletionRequestedAtUtc" AND n."OlayDamgasiUtc" = d."ResolvedAtUtc")::text
FROM comms."Notifications" n
JOIN scheduling."LessonSessions" s ON s."Id" = n."RecordId"
JOIN moderation."Disputes" d ON d."Id" = '$itiraz'
WHERE n."RecordId" = '$($ders.id)' AND n."Type" = 'ApprovalPending'
ORDER BY n."CreatedAtUtc" DESC LIMIT 1;
"@) 'true'
    } else { Fail 'itiraz reddinden sonraki onay satırı 45 sn içinde işlenmedi' }
} catch { BolumHatasi 4 $_ } }

# ================================================================== 5
if (Bolumde 5) { try {
    Step '5. Hatırlatmalar'

    function Hatirlat { Api POST '/api/admin/jobs/push-reminders' $null $admin.token | Out-Null }

    $ogr = Hazir 'hato'; $egt = Hazir 'hate'
    $ders = DersKur $ogr $egt $konular[1] 120
    Esit 'rezervasyon → eğitmene ders planı Sent' (BekleSatir """RecordId"" = '$($ders.id)' AND ""Type"" = 'LessonBooked'") 'Sent|'

    # İleriye dönük kuyruklama: 2 sa sonraki dersin 60 ve 10 dk hatırlatmaları şimdiden yazılır.
    Hatirlat
    Hatirlat
    if ($script:Api2Var) {
        $kodlar = Eszamanli @(
            @{ Method = 'POST'; Url = "$API/api/admin/jobs/push-reminders"; Token = $admin.token },
            @{ Method = 'POST'; Url = "$API2/api/admin/jobs/push-reminders"; Token = $admin.token })
        Esit 'iki kopyada eşzamanlı hatırlatma işi → 200' (@($kodlar | Where-Object { $_ -eq 200 }).Count) 2
    } else { Atla 'iki kopyada eşzamanlı hatırlatma işi (ikinci kopya yok)' }
    $yKosul = """RecordId"" = '$($ders.id)' AND ""Type"" = 'LessonSoon'"
    Esit 'tekrarlı ve eşzamanlı çağrılara rağmen her hatırlatma tek satır (2 ofset × 2 kişi)' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE $yKosul;") 4
    Esit 'anlar başlangıç − 60 / − 10 dk' (Tek @"
SELECT COUNT(*) FROM comms."Notifications" n JOIN scheduling."LessonSessions" s ON s."Id" = n."RecordId"
WHERE n."RecordId" = '$($ders.id)' AND n."Type" = 'LessonSoon'
  AND ((n."DedupeKey" LIKE '%:60' AND n."DueAtUtc" = s."ScheduledStartUtc" - interval '60 minutes')
    OR (n."DedupeKey" LIKE '%:10' AND n."DueAtUtc" = s."ScheduledStartUtc" - interval '10 minutes'));
"@) 4

    # 60 dk hatırlatması gönderilsin (vadesi SQL ile şimdi), sonra iptal.
    VadeyiCek "$yKosul AND ""DedupeKey"" LIKE '%:60'"
    if (BekleHepsi "$yKosul AND ""DedupeKey"" LIKE '%:60'" 30) {
        Esit '60 dk hatırlatması iki tarafa Sent' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE $yKosul AND ""DedupeKey"" LIKE '%:60' AND ""Status"" = 'Sent';") 2
    } else { Fail '60 dk hatırlatması 30 sn içinde işlenmedi' }
    Api POST "/api/sessions/$($ders.id)/cancel" @{ reason = 'e2e iptal' } $ogr.token | Out-Null
    $iKosul = """RecordId"" = '$($ders.id)' AND ""Type"" = 'LessonCancelled'"
    Esit 'hatırlatmadan sonra iptal → karşı tarafa iptal Sent' (BekleSatir $iKosul) 'Sent|'
    Esit 'iptal bildirimi iptal etmeyen tarafa (eğitmen)' (Tek "SELECT ""RecipientUserId"" FROM comms.""Notifications"" WHERE $iKosul;") $egt.userId
    VadeyiCek "$yKosul AND ""DedupeKey"" LIKE '%:10'"
    if (BekleHepsi "$yKosul AND ""DedupeKey"" LIKE '%:10'" 30) {
        Esit 'bekleyen 10 dk hatırlatmaları Skipped(DurumDegisti)' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE $yKosul AND ""DedupeKey"" LIKE '%:10' AND ""Outcome"" = 'DurumDegisti';") 2
    } else { Fail '10 dk hatırlatması 30 sn içinde işlenmedi' }

    # 5 dk sonrasına rezervasyon: "1 saat kaldı" yanlış olurdu; eğitmenin ilk haberi rezervasyon bildirimi.
    $ogr2 = Hazir 'hato2'; $egt2 = Hazir 'hate2'
    $yakin = DersKur $ogr2 $egt2 $konular[2] 5
    Esit '5 dk sonrasına rezervasyon → eğitmene ders planı Sent' (BekleSatir """RecordId"" = '$($yakin.id)' AND ""Type"" = 'LessonBooked'") 'Sent|'
    Hatirlat
    Esit '5 dk sonrasına rezervasyon → hiç yaklaşan ders satırı yok' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE ""RecordId"" = '$($yakin.id)' AND ""Type"" = 'LessonSoon';") 0

    # Otomatik onay 24 sa hatırlatması: an sessiz saate düşmesin diye yalnızca TR 08–21.
    $trSaat = [DateTime]::UtcNow.AddHours(3).Hour
    if ($trSaat -ge 8 -and $trSaat -lt 21) {
        $ogr3 = Hazir 'hato3'; $egt3 = Hazir 'hate3'
        $d3 = DersKur $ogr3 $egt3 $konular[3]
        DersiGecmiseAl $d3.id
        KanitYukle $d3.id $d3.kod $egt3.token
        # D = damga + 48 sa; damga = şimdi − 23 sa → R24 = şimdi + 1 sa (kuyruğa girer, gündüz).
        Sql "UPDATE scheduling.""LessonSessions"" SET ""CompletionRequestedAtUtc"" = now() - interval '23 hours' WHERE ""Id"" = '$($d3.id)';" | Out-Null
        Hatirlat
        Hatirlat
        $oto = """RecordId"" = '$($d3.id)' AND ""Type"" = 'AutoApproveSoon'"
        Esit 'oto onay: iki çağrı → tek 24 sa satırı' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE $oto;") 1
        Esit 'oto onay satırının damgası DB''deki CompletionRequestedAtUtc' (Tek @"
SELECT (n."OlayDamgasiUtc" = s."CompletionRequestedAtUtc" AND n."DedupeKey" LIKE '%:24')::text
FROM comms."Notifications" n JOIN scheduling."LessonSessions" s ON s."Id" = n."RecordId" WHERE $oto;
"@) 'true'
    } else {
        Atla "otomatik onay 24 sa hatırlatması (an sessiz saate düşer; TR saat $trSaat)"
    }
} catch { BolumHatasi 5 $_ } }

# ================================================================== 6
if (Bolumde 6) { try {
    Step '6. Cihaz silme noktaları'

    # Tek cihaz çıkışı yalnızca o HWID'i siler.
    $m = Hazir 'silm'
    $hw2 = NewHwid
    $g2 = Giris $m $hw2
    Cihaz $m (PushToken) $null $g2.accessToken $hw2 | Out-Null
    Esit 'iki cihaz kayıtlı' (CihazSayisi $m.userId) 2
    Api POST '/api/session/logout' @{ refreshToken = $m.refresh } | Out-Null
    Esit 'tek cihaz çıkışı → yalnızca o cihaz silindi' (Tek "SELECT string_agg(""HwidHash"", ',') FROM comms.""PushDevices"" WHERE ""UserId"" = '$($m.userId)';") $hw2

    # Her yerden çıkış.
    $hw3 = NewHwid
    $g3 = Giris $m $hw3
    Cihaz $m (PushToken) $null $g3.accessToken $hw3 | Out-Null
    Esit 'her yerden çıkış öncesi iki cihaz' (CihazSayisi $m.userId) 2
    Api POST '/api/session/logout' @{ refreshToken = $g2.refreshToken; tumCihazlar = $true } | Out-Null
    Esit 'her yerden çıkış → hepsi silindi' (CihazSayisi $m.userId) 0

    # Rol değişimi: aynı TumOturumlariDusurAsync (parola sıfırlama da bu koddan geçiyor).
    $n = Hazir 'silr'
    Api PUT "/api/admin/users/$($n.userId)/role" @{ role = 'Moderator' } $admin.token | Out-Null
    Esit 'rol değişimi → cihaz silindi' (CihazSayisi $n.userId) 0

    # Hırsızlık tespiti: Rotated token pencere dışında yeniden sunulur → zincir düşer.
    $p = Hazir 'silh'
    Api POST '/api/session/refresh' @{ refreshToken = $p.refresh; hwidHash = $p.hwid } | Out-Null
    Sql "UPDATE identity.""RefreshTokens"" SET ""RevokedAtUtc"" = now() - interval '5 minutes' WHERE ""UserId"" = '$($p.userId)' AND ""RevokeReason"" = 'Rotated';" | Out-Null
    $r = ApiDurum POST '/api/session/refresh' @{ refreshToken = $p.refresh; hwidHash = $p.hwid }
    Esit 'Rotated token pencere dışında → 401' $r.status 401
    Esit 'hırsızlık tespiti → cihaz silindi' (CihazSayisi $p.userId) 0

    # Hesap silme: cihaz, tercih, alıcı satırları gider; aktörü olduğu bekleyen satır atlanır.
    $q = Hazir 'silq'; $qa = Hazir 'silqa'
    $sq = Arkadas $q $qa
    Api PUT '/api/push/preferences/istekler' @{ acik = $false } $q.token | Out-Null
    $aktorMesaj = Mesaj $q $sq
    $r = ApiDurum POST '/api/profile/delete' @{ password = 'Parola12345' } $q.token
    Esit 'hesap silme başarılı' $r.status 200
    Esit 'hesap silme → cihaz yok' (CihazSayisi $q.userId) 0
    Esit 'hesap silme → tercih satırı yok' (Tek "SELECT COUNT(*) FROM comms.""NotificationPreferences"" WHERE ""UserId"" = '$($q.userId)';") 0
    Esit 'hesap silme → alıcısı olduğu satır yok' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE ""RecipientUserId"" = '$($q.userId)';") 0
    Esit 'hesap silme → aktörü olduğu bekleyen mesaj Skipped(HesapPasif)' (Durum """RecordId"" = '$aktorMesaj'") 'Skipped|HesapPasif'

    # Ban: cihaz kaydı anında silinir.
    $s = Hazir 'silb'
    Api POST "/api/admin/users/$($s.userId)/ban" @{ reason = 'e2e bildirim ban' } $admin.token | Out-Null
    Esit 'ban → cihaz silindi' (CihazSayisi $s.userId) 0

    # Geçici askı: satır DURUR (askı bitince bildirim sürer), gönderim askı süresince atlanır.
    $t = Hazir 'silt'; $ta = NewUser 'silta'
    $st = Arkadas $ta $t
    Api POST "/api/admin/users/$($t.userId)/sanction" @{ type = 'TemporaryBan'; reason = 'e2e aski'; durationHours = 1 } $admin.token | Out-Null
    Esit 'geçici askı → cihaz satırı DURUYOR' (CihazSayisi $t.userId) 1
    $askida = Mesaj $ta $st
    Esit 'askıdaki alıcıya → Skipped(HesapPasif)' (BekleSatir """RecordId"" = '$askida'") 'Skipped|HesapPasif'
    Sql "UPDATE identity.""Users"" SET ""SuspendedUntilUtc"" = now() - interval '1 minute' WHERE ""Id"" = '$($t.userId)';" | Out-Null
    $sonra = Mesaj $ta $st
    Esit 'askı bitince → Sent' (BekleSatir """RecordId"" = '$sonra'") 'Sent|'
} catch { BolumHatasi 6 $_ } }

# ================================================================== 7
if (Bolumde 7) { try {
    Step '7. Makbuz ve gönderim sürerken cihaz silme'

    $v = Hazir 'mkbo' 'OLU'
    Api POST '/api/push/test' @{ tur = 'mesaj' } $v.token | Out-Null
    $vs = SonTestSatiri $v.userId
    Esit 'ölü token''a test → Sent (bilet ok)' (BekleSatir """Id"" = '$vs'") 'Sent|'
    Esit 'bilet yazıldı' (Tek "SELECT COUNT(*) FROM comms.""PushTickets"" WHERE ""NotificationId"" = '$vs';") 1
    # Makbuz 15 dakikadan genç bileti sormuyor.
    Sql "UPDATE comms.""PushTickets"" SET ""CreatedAtUtc"" = now() - interval '16 minutes' WHERE ""NotificationId"" = '$vs';" | Out-Null
    Api POST '/api/admin/jobs/push-receipts' $null $admin.token | Out-Null
    Esit 'makbuz DeviceNotRegistered → cihaz silindi' (CihazSayisi $v.userId) 0
    Esit 'sorulan bilet silindi' (Tek "SELECT COUNT(*) FROM comms.""PushTickets"" WHERE ""NotificationId"" = '$vs';") 0

    # Gönderim sürerken (Log 3 sn bekliyor) cihaz unutulur: satır Sent kalır, ikinci gönderim yok.
    $yakalandi = $false
    foreach ($deneme in 1..2) {
        $w = Hazir 'mkby' 'YAVAS'
        Api POST '/api/push/test' @{ tur = 'mesaj' } $w.token | Out-Null
        $ws = SonTestSatiri $w.userId
        $kira = BekleKira $ws
        if (-not $kira) { continue }
        Invoke-WebRequest -UseBasicParsing -Uri "$API/api/push/devices/forget" -Method Post -ContentType 'application/json' `
            -Body ([System.Text.Encoding]::UTF8.GetBytes((@{ token = $w.push } | ConvertTo-Json))) | Out-Null
        if ((Durum """Id"" = '$ws'").StartsWith('Pending')) { $yakalandi = $true; break }
    }
    if ($yakalandi) {
        Esit 'gönderim sürerken cihaz silindi → satır Sent' (BekleSatir """Id"" = '$ws'" 20) 'Sent|'
        Start-Sleep -Seconds 6
        Esit 'ikinci gönderim yok (satır Sent, deneme 0)' (Tek "SELECT ""Status"" || '|' || ""Attempts"" FROM comms.""Notifications"" WHERE ""Id"" = '$ws';") 'Sent|0'
        Esit 'tek bilet' (Tek "SELECT COUNT(*) FROM comms.""PushTickets"" WHERE ""NotificationId"" = '$ws';") 1
        Esit 'cihaz satırı yok' (CihazSayisi $w.userId) 0
    } else {
        Atla 'gönderim sürerken cihaz silme (iki denemede de gönderim yakalanamadı)'
    }
} catch { BolumHatasi 7 $_ } }

# ================================================================== 8
if (Bolumde 8) { try {
    Step '8. Fencing'

    # Satır kiralanınca (Log gönderimi 3 sn sürüyor) sahipliği başka bir tura geçirilir. Eski
    # turun sonuç yazımı "LeaseOwner = ben AND Status = Pending" koşuluyla 0 satır etkilemeli.
    # Kira GELECEĞE çekiliyor: geçmişe çekilseydi üçüncü bir tur satırı yeniden alıp gönderebilir
    # ve iddia yarışa bağlı kalırdı.
    $x = Hazir 'fenc' 'YAVAS'
    Api POST '/api/push/test' @{ tur = 'mesaj' } $x.token | Out-Null
    $xs = SonTestSatiri $x.userId
    $kira = BekleKira $xs
    $yabanci = [Guid]::NewGuid()
    $devralindi = Tek @"
UPDATE comms."Notifications" SET "LeaseOwner" = '$yabanci', "LeaseUntilUtc" = now() + interval '10 minutes'
WHERE "Id" = '$xs' AND "Status" = 'Pending' AND "LeaseOwner" IS NOT NULL RETURNING "Id";
"@
    if ($kira -and $devralindi -eq $xs) {
        OK 'önkoşul: satır gönderim sürerken başka sahibe geçti'
        Start-Sleep -Seconds 7
        Esit 'eski tur yeni sahibin satırını EZEMEDİ (Pending, yeni sahip)' (Tek "SELECT ""Status"" || '|' || ""LeaseOwner"" FROM comms.""Notifications"" WHERE ""Id"" = '$xs';") "Pending|$yabanci"
    } else {
        Fail "önkoşul: satır kiralanmış hâlde yakalanamadı (kira '$kira', devralma '$devralindi')"
    }
    # Temizlik: sahte sahibin satırı kuyrukta kalmasın.
    Sql "UPDATE comms.""Notifications"" SET ""Status"" = 'Skipped', ""Outcome"" = 'Tekrar', ""ProcessedAtUtc"" = now(), ""LeaseOwner"" = NULL, ""LeaseUntilUtc"" = NULL WHERE ""Id"" = '$xs' AND ""Status"" = 'Pending';" | Out-Null
} catch { BolumHatasi 8 $_ } }

# ================================================================== 9
if (Bolumde 9) { try {
    Step '9. Yabancı Expo projesi'

    # İki satır TEK ifadeyle yazılıyor: dağıtıcı ikisini aynı partide sahiplenmeli.
    $y = Hazir 'ybzi'; $z = Hazir 'ybzz' 'YABANCI'
    $ys = [Guid]::NewGuid(); $zs = [Guid]::NewGuid()
    Sql @"
INSERT INTO comms."Notifications" ("Id","Type","DedupeKey","RecipientUserId","RecordId","DueAtUtc","ExpiresAtUtc","Status","CreatedAtUtc")
VALUES ('$ys','Test','test:mesajlar:$($ys.ToString('N'))','$($y.userId)','$ys', now(), now() + interval '15 minutes','Pending', now()),
       ('$zs','Test','test:mesajlar:$($zs.ToString('N'))','$($z.userId)','$zs', now(), now() + interval '15 minutes','Pending', now());
"@ | Out-Null
    Esit 'aynı partideki bizim satır → Sent' (BekleSatir """Id"" = '$ys'" 30) 'Sent|'
    Esit 'yabancı projenin satırı → Skipped(CihazGecersiz)' (BekleSatir """Id"" = '$zs'" 30) 'Skipped|CihazGecersiz'
    Esit 'yabancı cihaz silindi' (CihazSayisi $z.userId) 0
    Esit 'bizim cihaz duruyor' (CihazSayisi $y.userId) 1
} catch { BolumHatasi 9 $_ } }

# ================================================================== 10
if (Bolumde 10) { try {
    Step '10. Test ucu sınırı'

    $tt = Hazir 'test'
    $kodlar = @(1..4 | ForEach-Object { (ApiDurum POST '/api/push/test' @{ tur = 'istek' } $tt.token).status })
    Esit 'ilk üç test çağrısı 200' (@($kodlar[0..2] | Where-Object { $_ -eq 200 }).Count) 3
    Esit '10 dakikada dördüncü çağrı → 429' $kodlar[3] 429
    Esit 'reddedilen çağrı satır yazmadı' (Tek "SELECT COUNT(*) FROM comms.""Notifications"" WHERE ""RecipientUserId"" = '$($tt.userId)' AND ""Type"" = 'Test';") 3
} catch { BolumHatasi 10 $_ } }

# ------------------------------------------------------------------ Özet
Write-Host "`n================================" -ForegroundColor Yellow
Write-Host "$($script:kontrol) kontrol geçti, $($script:failures) kaldı, $($script:atlanan) atlandı"
if ($script:failures -eq 0) { Write-Host 'TÜM ADIMLAR BAŞARILI' -ForegroundColor Green }
else { Write-Host "$($script:failures) ADIM BAŞARISIZ" -ForegroundColor Red }
Write-Host "================================" -ForegroundColor Yellow
if ($script:failures -gt 0) { exit 1 }
exit 0
