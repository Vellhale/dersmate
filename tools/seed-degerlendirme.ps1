# Ayşe'nin profiline gerçek değerlendirmeler ekler.
#
# NEDEN AYRI BETİK: seed-rozet.ps1 ders GEÇMİŞİ üretiyor (rozet ve seviye için);
# değerlendirme ise o geçmişin üstüne binen ayrı bir ürün yüzeyi — puan ortalaması,
# etiket histogramı, yorum listesi. İkisini birleştirmek, rozet tohumlamasını her
# koşumda gereksiz yere yavaşlatırdı.
#
# ÖNCE seed-rozet.ps1 çalışmış olmalı: değerlendirme yalnızca TAMAMLANMIŞ derse
# yazılabiliyor ve dersleri o betik üretiyor.
#
# KURALLAR SUNUCUDA, BURADA DEĞİL (CreateReview.cs):
#   - ders Completed olmalı
#   - yorumu yalnızca dersin ÖĞRENCİSİ yazabilir
#   - ders başına tek yorum
#   - üç puan da 1-5 arasında, en fazla 6 etiket, yorum en fazla 1000 karakter
# Betik hiçbirini taklit etmiyor; API'ye gidiyor ve sunucu ne derse o oluyor.
#
# TEKRAR KOŞULABİLİR: yorumu olmayan derslere yazar, hedef sayıya ulaşınca durur.

$ErrorActionPreference = 'Stop'
$API = 'http://localhost:5000'

function Api {
    param($Method, $Path, $Body, $Token)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Uri = "$API$Path"; Method = $Method; Headers = $headers; TimeoutSec = 60 }
    if ($null -ne $Body) {
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 6))
        $params['ContentType'] = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @params
}

function Giris($email, $hwid) {
    Api POST '/api/auth/login' @{ email = $email; password = 'Demo12345'; hwidHash = $hwid }
}

# --- psql: yerel kurulum -> PATH -> docker compose ---
$WindowsPsql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'
if (-not (Test-Path $WindowsPsql)) {
    # PS 5.1 uyumu: `?.` operatörü PowerShell 7 ile geldi, burada kullanılamaz.
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    $WindowsPsql = if ($psqlCmd) { $psqlCmd.Source } else { $null }
}
$ComposeYml = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'

function Sql($query) {
    if (-not $WindowsPsql) {
        return (($query | docker compose -f $ComposeYml exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1) -join "`n")
    }
    $f = Join-Path $env:TEMP ("pl_rev_" + [Guid]::NewGuid().ToString('N') + ".sql")
    Set-Content -Path $f -Value $query -Encoding UTF8
    $out = & $WindowsPsql -h localhost -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 -f $f 2>&1
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    return ($out -join "`n")
}

# ---------------------------------------------------------------------------
# DEĞERLENDİRMELER
#
# Hepsi 5 yıldız DEĞİL — ve bu bilinçli. Kusursuz bir ortalama profilin inandırıcılığını
# düşürüyor; ayrıca yıldız dağılımı ve etiket histogramı ancak farklı puanlar varken bir
# şey gösteriyor (tek değerde histogram tek çubuk olur ve tasarımı hiç sınamaz).
#
# Alt puanlar (anlatım / zamanlama) genel puandan bağımsız veriliyor: ürün üç ayrı ölçü
# sorduğuna göre, tohumlama da üçünün ayrışabildiğini göstermeli. Üçüncü yorumdaki
# "zamanlama 3" tam olarak bunun için var.
#
# YORUM METNİ DERSİN BRANŞIYLA EŞLEŞMELİ. İlk denemede dördü de en yeni derslere
# yazıldı; hepsi Matematik çıktı ve profilde "Temel Kavramlar" başlığının altında
# geometriden/fizikten bahseden bir yorum göründü. Demo verisi kendi içinde
# çelişiyorsa ekranı değerlendirmek zorlaşıyor — hangi tuhaflığın tasarımdan, hangisinin
# veriden geldiği anlaşılmıyor. Bu yüzden her yorum bir BRANŞA bağlı ve ders o branştan
# seçiliyor.
#
# Metinler konu ADINI anmıyor, dersin konusunu anıyor: katalog güncellenince konu adları
# değişiyor (seed-demo.ps1 bu yüzden bir kez kırılmıştı) ve sabit bir ad yorumu yine
# çelişkiye düşürürdü.
$YORUMLAR = @(
    @{
        Brans = 'Matematik'; Puan = 5; Anlatim = 5; Zamanlama = 5
        Etiketler = @('KnowsSubject', 'PatientAndClear', 'GreatExamples', 'WouldBookAgain')
        Metin = 'Konuyu bildigimi saniyordum ama soru cozerken tikaniyordum. Ayse once nerede kopardigimi buldu, sonra oradan devam etti. Bir saatte kafamdaki dugum cozuldu; cozdugu sorulari adim adim yazdirdi, defterime bakinca tek basima tekrar edebiliyorum.'
    },
    @{
        Brans = 'Matematik'; Puan = 5; Anlatim = 5; Zamanlama = 4
        Etiketler = @('PatientAndClear', 'SharedResources', 'WouldBookAgain')
        Metin = 'Ayni soruyu uc kez sordum, hicbirinde sikildigini hissettirmedi. Ders sonunda benzer sorulardan olusan bir liste de paylasti. Birkac dakika gec basladik ama sonunu ayni kadar uzattigi icin eksik kalan olmadi.'
    },
    @{
        Brans = 'Geometri'; Puan = 4; Anlatim = 5; Zamanlama = 3
        Etiketler = @('KnowsSubject', 'GreatExamples')
        Metin = 'Sekil uzerinden gitmesi cok isime yaradi, daha once ezberledigim bagintilarin nereden geldigini ilk kez gordum. Tek eksik derse yaklasik on dakika gec baslamamizdi; anlatim tarafinda soyleyecek bir sozum yok.'
    },
    @{
        Brans = 'Fizik'; Puan = 5; Anlatim = 4; Zamanlama = 5
        Etiketler = @('StartedOnTime', 'KnowsSubject', 'WouldBookAgain')
        Metin = 'Formul ezberlemekten sikilmistim, Ayse formulun nereden turetildigini gosterince akilda kalici oldu. Tam saatinde baslayip tam saatinde bitirdi. Bir sonraki konu icin de ders alacagim.'
    }
)

# ---------------------------------------------------------------------------
Write-Host 'dersmate - degerlendirme tohumlamasi' -ForegroundColor White
Write-Host ''

$ayseId = Sql "SELECT ""Id"" FROM identity.""Users"" WHERE ""Email"" = 'ayse@demo.dev';"
$ayseId = $ayseId.Trim()
if (-not $ayseId) { throw "ayse@demo.dev bulunamadi. Once tools\seed-demo.ps1 calistirin." }

# Yorumu OLMAYAN, istenen branştaki bir ders seçer. "Ders başına tek yorum" kuralı
# sunucuda; burada tekrar uygulamıyoruz, yalnızca boşuna 409 almamak için filtreliyoruz.
function BranstanDers($brans) {
    $satir = Sql @"
-- Konu adı kolonu "Name" (API'nin döndürdüğü `topic` alanıyla karıştırma: o bir DTO adı,
-- şemadaki karşılığı Topics."Name").
SELECT ls."Id" || '|' || u."Email" || '|' || t."Name"
FROM scheduling."LessonSessions" ls
JOIN identity."Users" u ON u."Id" = ls."StudentUserId"
JOIN catalog."Topics" t ON t."Id" = ls."TopicId"
JOIN catalog."Subjects" s ON s."Id" = t."SubjectId"
LEFT JOIN scheduling."SessionReviews" r ON r."SessionId" = ls."Id"
WHERE ls."TutorUserId" = '$ayseId'
  AND ls."Status" = 'Completed'
  AND s."Branch" = '$brans'
  AND r."Id" IS NULL
ORDER BY ls."ScheduledStartUtc" DESC
LIMIT 1;
"@
    $satir = ($satir -split "`n" | Where-Object { $_.Trim() } | Select-Object -First 1)
    if (-not $satir) { return $null }
    $p = $satir.Trim() -split '\|'
    return [pscustomobject]@{ SessionId = $p[0]; Email = $p[1]; Konu = $p[2] }
}

$yazilan = 0
foreach ($y in $YORUMLAR) {
    $ders = BranstanDers $y.Brans
    if (-not $ders) {
        Write-Host ("  {0,-10} yorumsuz tamamlanmis ders yok, atlandi" -f $y.Brans) -ForegroundColor DarkGray
        continue
    }

    # HWID kullanici basina AYRI olmali (ban kaydi cihaza yaziliyor). seed-rozet.ps1
    # ile ayni formul: farkli bir HWID ile giris cihazi yeni sanardi.
    $no = [int]($ders.Email -replace '[^0-9]', '')
    $hwid = ('r{0:d2}' -f $no) * 21 + 'r'
    $ogr = Giris $ders.Email $hwid

    $sonuc = Api POST "/api/sessions/$($ders.SessionId)/review" @{
        score = $y.Puan; teachingScore = $y.Anlatim; punctualityScore = $y.Zamanlama
        tags = $y.Etiketler; comment = $y.Metin
    } $ogr.accessToken

    $yazilan++
    Write-Host ("  {0,-10} {1,-34} {2} yildiz  -> ortalama {3} ({4} degerlendirme)" -f `
        $y.Brans, $ders.Konu, $y.Puan, $sonuc.tutorNewAverage, $sonuc.tutorReviewCount)
}

Write-Host ''
if ($yazilan -gt 0) {
    Write-Host "$yazilan degerlendirme yazildi." -ForegroundColor Green
}
Write-Host 'Profil: http://localhost:5173/profil  (ayse@demo.dev / Demo12345)' -ForegroundColor Green
