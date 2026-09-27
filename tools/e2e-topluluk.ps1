# PeerLearn — Topluluk akışı: ilk yorum önizlemesi (firstComment)
#
# Akış yanıtındaki her gönderi 2026-09-26'dan beri gönderinin İLK yorumunu taşıyor
# (ForumPostDto.FirstComment; kurallar ForumQueries.cs → ForumOnizleme). Kart bunu iki
# satırlık önizleme ve "{n} yorumun tümünü gör" satırı olarak çiziyor.
#
# ─── KANIT STANDARDI ──────────────────────────────────────────────────────────
# Her dışlama iddiasının yanında bir SERBEST iddia var: aynı yorum dışlama sebebi
# kalkınca önizlemeye GİRİYOR. Yalnızca "şu yorum gelmedi" demek, yorumun başka bir
# sebeple (sıralama, yanlış gönderi) gelmemiş olabileceğini dışlamaz.
#
# ─── KAPSAM ───────────────────────────────────────────────────────────────────
#   A. Yorumsuz gönderide firstComment null
#   B. Sıra: en eski GÖRÜNÜR yorum; perdeli (UnderReview) ve kaldırılmış yorum atlanır
#   C. Engel: bakanın engellediği kişinin yorumu atlanır; engel kalkınca geri gelir
#   D. Engel çift yönlü: beni engelleyenin yorumu da bana gösterilmez, başkasına gösterilir
#   E. Perdeli GÖNDERİNİN önizlemesi yok; perde yokken var
#   F. Gövde tek satıra indirgenir, 200 grafemde "…" ile kesilir; sayaç (commentCount) aynen
#
# Ön koşullar: PostgreSQL, API :5000.

$ErrorActionPreference = 'Stop'
$API = 'http://localhost:5000'
$PSQL = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'

# psql üç yoldan aranır (diğer paketlerle aynı gerekçe: testin KOŞAMAMASI,
# başarısız olmasından daha sinsi — özet onu kırmızı değil, hiç görünmemiş sayar).
if (-not (Test-Path $PSQL)) {
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    $bulunan = if ($psqlCmd) { $psqlCmd.Source } else { $null }
    if ($bulunan) { $PSQL = $bulunan } else { $PSQL = $null }
}
$script:ComposeYml = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'

$script:failures = 0
function Step($name) { Write-Host "`n=== $name ===" -ForegroundColor Cyan }
function OK($msg)    { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Fail($msg)  { Write-Host "  [KALDI] $msg" -ForegroundColor Red; $script:failures++ }

function Esit($etiket, $gelen, $beklenen) {
    if ("$gelen" -eq "$beklenen") { OK $etiket } else { Fail "$etiket — beklenen '$beklenen', gelen '$gelen'" }
}

function Api {
    param($Method, $Path, $Body, $Token)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Uri = "$API$Path"; Method = $Method; Headers = $headers; TimeoutSec = 60 }
    if ($null -ne $Body) {
        # UTF-8 bayt olarak: PS 5.1 string gövdeyi Latin-1 kodluyor ve Türkçe
        # karakterler sunucuda bozuluyor.
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 6))
        $params['ContentType'] = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @params
}

function Sql($query) {
    if (-not $PSQL) {
        $out = $query | docker compose -f $script:ComposeYml exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
        return $out
    }
    $f = Join-Path $env:TEMP ("pl_topluluk_" + [Guid]::NewGuid().ToString('N') + ".sql")
    [IO.File]::WriteAllText($f, $query, [Text.UTF8Encoding]::new($false))
    $out = & $PSQL -h localhost -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 -f $f 2>&1
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    return $out
}

# Test betikleri idempotent DEĞİLDİR (CLAUDE.md): koşuma özel damga.
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$surum = (& "$PSScriptRoot\yasal-surum.ps1")

function NewHwid { -join ((1..64) | ForEach-Object { '0123456789abcdef'[(Get-Random -Max 16)] }) }

function NewUser($ad) {
    $hwid = NewHwid
    $mail = "topl$([Guid]::NewGuid().ToString('N').Substring(0,10))@test.dev"
    $reg = Api POST '/api/auth/register' @{
        email = $mail; password = 'Parola12345'; displayName = $ad
        termsVersion = $surum; ageConfirmed = $true; hwidHash = $hwid
    }
    Api POST '/api/auth/verify-email' @{ email = $mail; code = $reg.verificationToken } | Out-Null
    $login = Api POST '/api/auth/login' @{ email = $mail; password = 'Parola12345'; hwidHash = $hwid }
    return [PSCustomObject]@{ Ad = $ad; Token = $login.accessToken; UserId = $login.userId }
}

function NewPost($yazar, $baslik) {
    Api POST '/api/community/posts' @{
        tag = 'Question'; title = $baslik
        body = "Bu gönderi önizleme testinin gövdesidir ($stamp)."
    } $yazar.Token
}

function NewComment($postId, $yazar, $govde) {
    Api POST "/api/community/posts/$postId/comments" @{ body = $govde } $yazar.Token
}

# Yorum saatini sabitler: sıra iddiaları istek zamanlamasına bağlı kalmasın.
function YorumSaati($yorumId, $saniye) {
    Sql "UPDATE community.""Comments"" SET ""CreatedAtUtc"" = now() - interval '1 hour' + interval '$saniye seconds' WHERE ""Id"" = '$yorumId';" | Out-Null
}

function YorumDurumu($yorumId, $durum) {
    Sql "UPDATE community.""Comments"" SET ""Status"" = '$durum' WHERE ""Id"" = '$yorumId';" | Out-Null
}

# Gönderiyi bakanın akışında bulur. Sıra "en yeni" ve gönderiler bu koşumda yazıldı, yani
# ilk sayfada; yine de başka bir koşum araya girerse diye 50'lik sayfa isteniyor.
function Kart($postId, $bakan) {
    $akis = Api GET '/api/community/posts?sort=Newest&page=1&pageSize=50' $null $bakan.Token
    $kart = @($akis.items | Where-Object { $_.postId -eq $postId })
    if ($kart.Count -ne 1) { throw "gönderi $postId akışın ilk sayfasında bulunamadı" }
    return $kart[0]
}

# Hata iletisi için metnin başı. Substring kısa metinde FIRLATIR ve betiği ortada
# düşürürdü: kalan kontroller hiç koşmaz, özet yalnızca çıkış kodunu görürdü.
function Bas($metin) {
    if ($null -eq $metin) { return '(null)' }
    if ($metin.Length -le 40) { return $metin }
    return $metin.Substring(0, 40)
}

# İlk yorumun kimliği; önizleme yoksa '(yok)'.
function IlkYorum($postId, $bakan) {
    $k = Kart $postId $bakan
    if ($null -eq $k.firstComment) { return '(yok)' }
    return $k.firstComment.commentId
}

Write-Host "PeerLearn — Topluluk ilk yorum önizlemesi" -ForegroundColor White
Write-Host "API: $API   damga: $stamp"

# ---------------------------------------------------------------------------
Step 'Hazırlık: yedi kullanıcı'

$yazar    = NewUser "Yazar Tpl$stamp"
$bakan    = NewUser "Bakan Tpl$stamp"
$engelli  = NewUser "Engelli Tpl$stamp"
$perdeli  = NewUser "Perdeli Tpl$stamp"
$kaldirma = NewUser "Kaldirilan Tpl$stamp"
$ayse     = NewUser "Ayşe Tpl$stamp"
$baska    = NewUser "Baska Tpl$stamp"
OK "yedi kullanıcı kuruldu (damga $stamp)"

# ---------------------------------------------------------------------------
Step 'A. Yorumsuz gönderi'

$p1 = NewPost $yazar "Önizleme testi bir $stamp"
$kartBos = Kart $p1 $bakan
if ($kartBos.PSObject.Properties.Name -contains 'firstComment') { OK 'yanıtta firstComment alanı var' }
else { Fail 'yanıtta firstComment alanı YOK' }
Esit 'yorumsuz gönderide firstComment null' (IlkYorum $p1 $bakan) '(yok)'

# ---------------------------------------------------------------------------
Step 'B. Sıra ve durum: en eski GÖRÜNÜR yorum'

# Yazılış sırası (saatler SQL ile sabitleniyor): engelli, perdeli, kaldırılan, Ayşe, başka.
$uzunGovde = "Bence önce   paragraf`r`n`r`nsorularına bak.`n" + ('ç' * 300)
$c1 = NewComment $p1 $engelli  'Engellenen kişinin yorumu.'
$c2 = NewComment $p1 $perdeli  'Perdelenecek yorum burada.'
$c3 = NewComment $p1 $kaldirma 'Kaldırılacak yorum burada.'
$c4 = NewComment $p1 $ayse     $uzunGovde
$c5 = NewComment $p1 $baska    'En son yazılan yorum.'
YorumSaati $c1 1; YorumSaati $c2 2; YorumSaati $c3 3; YorumSaati $c4 4; YorumSaati $c5 5

# Hiç süzgeç yokken en eski yorum geliyor (başka biri için: engeli yok).
Esit 'engeli olmayan kişiye en eski yorum (c1) geliyor' (IlkYorum $p1 $baska) $c1

YorumDurumu $c2 'UnderReview'
YorumDurumu $c3 'Removed'

# ---------------------------------------------------------------------------
Step 'C. Engel: bakanın engellediği kişinin yorumu'

Api POST '/api/blocks' @{ userId = $engelli.UserId; note = $null } $bakan.Token | Out-Null

# Bakan için: c1 engelli, c2 perdeli, c3 kaldırılmış → ilk uygun yorum c4 (Ayşe).
Esit 'engelli, perdeli ve kaldırılmış yorum atlandı → c4' (IlkYorum $p1 $bakan) $c4

# SERBEST: perde kalkınca c2 önizlemeye giriyor — c2'yi dışarıda tutan şey durumuydu.
YorumDurumu $c2 'Visible'
Esit 'perde kalkınca c2 önizlemeye girdi' (IlkYorum $p1 $bakan) $c2
YorumDurumu $c2 'UnderReview'

# SERBEST: kaldırılmış yorum yeniden görünür olunca önizlemeye giriyor.
YorumDurumu $c3 'Visible'
Esit 'kaldırma geri alınınca c3 önizlemeye girdi' (IlkYorum $p1 $bakan) $c3
YorumDurumu $c3 'Removed'
Esit 'c3 yeniden kaldırılınca önizleme yine c4' (IlkYorum $p1 $bakan) $c4

# Başka biri için engel yok: c1 hâlâ ilk (engel bakana özgü).
Esit 'engel yalnızca bakanın önizlemesini değiştirdi (başkasına c1)' (IlkYorum $p1 $baska) $c1

# ---------------------------------------------------------------------------
Step 'D. Engel çift yönlü'

# Bakan, engelli'yi engelledi. Engelli'nin akışında bakanın yorumu da görünmemeli.
$p2 = NewPost $ayse "Önizleme testi iki $stamp"
$d1 = NewComment $p2 $bakan 'Bakanın yazdığı ilk yorum.'
$d2 = NewComment $p2 $baska 'Başkasının yazdığı ikinci yorum.'
YorumSaati $d1 1; YorumSaati $d2 2

Esit 'engellenen kişi, onu engelleyenin yorumunu görmüyor → d2' (IlkYorum $p2 $engelli) $d2
Esit 'aynı gönderide başkasına d1 geliyor' (IlkYorum $p2 $baska) $d1

# SERBEST: engel kalkınca iki taraf da ilk yorumu görüyor.
Api DELETE "/api/blocks/$($engelli.UserId)" $null $bakan.Token | Out-Null
Esit 'engel kalkınca engelli kişiye d1 geliyor' (IlkYorum $p2 $engelli) $d1
Esit 'engel kalkınca bakana c1 geliyor' (IlkYorum $p1 $bakan) $c1

# ---------------------------------------------------------------------------
Step 'E. Perdeli gönderi'

$p3 = NewPost $baska "Önizleme testi üç $stamp"
$e1 = NewComment $p3 $ayse 'Perdeli gönderinin altındaki yorum.'

# SERBEST önce: perde yokken önizleme var.
Esit 'perde yokken önizleme var' (IlkYorum $p3 $bakan) $e1

Sql "UPDATE community.""Posts"" SET ""Status"" = 'UnderReview' WHERE ""Id"" = '$p3';" | Out-Null
$kartPerdeli = Kart $p3 $bakan
if ($kartPerdeli.underReview -eq $true) { OK 'perdeli gönderi akışta perdeli görünüyor' }
else { Fail "underReview: $($kartPerdeli.underReview)" }
if ($null -eq $kartPerdeli.firstComment) { OK 'perdeli gönderinin önizlemesi yok' }
else { Fail "perdeli gönderi önizleme taşıyor: $($kartPerdeli.firstComment.commentId)" }

# ---------------------------------------------------------------------------
Step 'F. Gövde, yazar ve sayaç'

# c4'ü görmek için c1..c3'ü dışarıda bırak (engeli yeniden kur).
Api POST '/api/blocks' @{ userId = $engelli.UserId; note = $null } $bakan.Token | Out-Null
$kart = Kart $p1 $bakan
$ilk = $kart.firstComment
Esit 'önizleme c4' $ilk.commentId $c4

# Satır sonları ve art arda boşluk tek boşluğa indi; 200 grafemde "…".
if ($ilk.body -notmatch "[`r`n`t]" -and $ilk.body -notmatch '  ') { OK 'gövde tek satıra indirgendi' }
else { Fail "gövdede satır sonu ya da çift boşluk kaldı: '$(Bas $ilk.body)'" }
if ($ilk.body.StartsWith('Bence önce paragraf sorularına bak. ç')) { OK 'gövde başı doğru (Türkçe karakterler bozulmadı)' }
else { Fail "gövde başı: '$(Bas $ilk.body)'" }
$grafem = (New-Object System.Globalization.StringInfo($ilk.body)).LengthInTextElements
if ($grafem -eq 200 -and $ilk.body.EndsWith([string][char]0x2026)) { OK 'gövde 200 grafemde "…" ile kesildi' }
else { Fail "gövde $grafem grafem, son karakter '$($ilk.body[-1])'" }

Esit 'yazar kimliği' $ilk.author.userId $ayse.UserId
Esit 'yazar adı' $ilk.author.displayName $ayse.Ad
if ($null -ne $ilk.author.level -and $null -ne $ilk.author.isStaff) { OK 'yazar seviye ve yönetici işaretini taşıyor' }
else { Fail 'yazar alanları eksik' }
if ($ilk.createdAtUtc) { OK 'createdAtUtc var' } else { Fail 'createdAtUtc yok' }

# "{n} yorumun tümünü gör": sayaç önizlemede atlananları da sayıyor (sayaç yalnızca artıyor).
Esit 'commentCount beş yorumu sayıyor' $kart.commentCount 5

# /api/v1 aynı alanı taşıyor (mobil onu çağırıyor).
$v1 = Api GET '/api/v1/community/posts?sort=Newest&page=1&pageSize=50' $null $bakan.Token
$v1Kart = @($v1.items | Where-Object { $_.postId -eq $p1 })
if ($v1Kart.Count -eq 1 -and $v1Kart[0].firstComment.commentId -eq $c4) { OK '/api/v1 akışı aynı önizlemeyi verdi' }
else { Fail '/api/v1 akışında önizleme farklı ya da yok' }

# ---------------------------------------------------------------------------
Write-Host "`n================================" -ForegroundColor Yellow
if ($script:failures -eq 0) { Write-Host 'TÜM ADIMLAR BAŞARILI' -ForegroundColor Green }
else { Write-Host "$($script:failures) ADIM BAŞARISIZ" -ForegroundColor Red }
Write-Host "================================" -ForegroundColor Yellow
if ($script:failures -gt 0) { exit 1 }
