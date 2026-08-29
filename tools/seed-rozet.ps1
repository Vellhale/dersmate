# Branş rozetlerini ve seviye ilerlemesini EKRANDA görünür kılan tohumlama.
#
# NEDEN GEREKLİ: rozet motoru (SubjectBadgeEngine) eksiksiz çalışıyor ama rozet ancak
# TAMAMLANMIŞ ders varsa doğuyor. Demo veritabanında tek bir tamamlanmış ders yoktu
# (yalnızca rezerve + onay bekleyen), dolayısıyla profil şeridi hep boş görünüyordu ve
# özellik "yarım kalmış" gibi duruyordu. Eksik olan koddu değil, veriydi.
#
# NE ÜRETİR (Ayşe eğitmen) — eşikler 2026-08-24'te 8 / 15 saate çekildi:
#   Matematik  20 saat -> "Matematik Üstadı" (altın) + "Matematik Öğretici" (gümüş)
#   Geometri   15 saat -> "Geometri Üstadı"  (altın) + "Geometri Öğretici"  (gümüş)
#   Fizik       8 saat -> "Fizik Öğretici"   (gümüş)
#
# HEDEFLER İKİ MADALYAYI DA GÖSTERECEK ŞEKİLDE SEÇİLDİ: yalnızca altın üretmek, gümüşün
# ekranda nasıl durduğunu hiç göstermezdi; yalnızca gümüş de tersini. Fizik bilerek tek
# kademede bırakıldı — "bir sonraki rozete ne kadar kaldı" ilerleme satırının da gerçek
# veriyle görünmesi gerekiyor.
#
# ÖNCE seed-demo.ps1 çalışmış olmalı (Ayşe hesabı ve katalog oradan geliyor).
#
# TEKRAR KOŞULABİLİR: her branşta eksik kalan saat kadar ders üretir. İkinci koşumda
# hedefler zaten doluysa hiçbir şey yapmaz.

$ErrorActionPreference = 'Stop'
$API = 'http://localhost:5000'

# ---------------------------------------------------------------------------
# MİNTGUARD'I DÜRÜSTÇE AŞMANIN YOLU: dersi rezerve ettikten HEMEN SONRA geçmişe kaydır.
#
# MintGuard, açılmak istenen dersin saati etrafında ±24 saatlik kayan bir pencerede
# sayıyor (eğitmen başına 8, çift başına 2). Otuz dersi aynı saate rezerve etmeye
# çalışmak dokuzuncuda MINT_LIMIT_REACHED ile düşerdi.
#
# Freni DEVRE DIŞI BIRAKMIYORUZ — hiçbir kural değiştirilmiyor, hiçbir kolon elle
# yazılmıyor. Yalnızca her ders, bir sonraki rezervasyonun penceresinin dışına taşınıyor;
# gerçek hayatta da aylara yayılmış otuz ders böyle görünürdü. Ders kaydının kendisi
# uygulama yolundan geçiyor: rezervasyon -> kanıt -> onay -> basım -> rozet motoru.
# ---------------------------------------------------------------------------

$GUN_ARALIGI = 3     # ardışık dersler arasındaki gün farkı (pencere 1 gün; 3 fazlasıyla güvenli)
$SURE = 60           # dakika. 60 dk = 100 puan; 20 saat için 20 ders demek.

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

function Register($email, $name, $hwid) {
    try {
        $r = Api POST '/api/auth/register' @{ email = $email; password = 'Demo12345'; displayName = $name; termsVersion = '2026-08-27'; ageConfirmed = $true }
        # DOĞRULAMA ŞART: doğrulanmamış hesap her yazma ucundan 403 EMAIL_NOT_VERIFIED alır.
        # Kayıt yanıtı jetonu geliştirme ortamında doğrudan döndürüyor (üretimde e-postayla).
        if ($r.verificationToken) {
            Api POST '/api/auth/verify-email' @{ token = $r.verificationToken } | Out-Null
        }
        if ($r.accessToken) { return $r }
    } catch { }

    # HESAP ZATEN VARSA doğrulanmamış olabilir — yarım kalmış bir koşumdan kalma. Kayıt
    # yanıtındaki jeton bir daha gelmiyor, o yüzden yeniden gönderim ucundan isteniyor.
    # Uç, geliştirme ortamında jetonu yanıtta döndürüyor; zaten doğrulanmışsa boş döner.
    try {
        $yeni = Api POST '/api/auth/resend-verification' @{ email = $email }
        if ($yeni.verificationToken) {
            Api POST '/api/auth/verify-email' @{ token = $yeni.verificationToken } | Out-Null
        }
    } catch { }

    return (Api POST '/api/auth/login' @{ email = $email; password = 'Demo12345'; hwidHash = $hwid })
}

# --- psql: yerel kurulum ya da docker compose (bkz. seed-demo.ps1'deki aynı gerekçe) ---
$WindowsPsql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'PeerLearnDev2026'
$Compose = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'
$YerelPsql = Test-Path $WindowsPsql

function Sql($query) {
    # `-c` KULLANILMIYOR: sorgular şema adlarını çift tırnakla yazıyor (identity."Users") ve
    # bunu argüman olarak geçirmek tırnakları kabuğa yediriyor.
    if ($YerelPsql) {
        $f = Join-Path $env:TEMP ("pl_rozet_" + [Guid]::NewGuid().ToString('N') + ".sql")
        Set-Content -Path $f -Value $query -Encoding UTF8
        $out = & $WindowsPsql -h localhost -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 -f $f 2>&1
        Remove-Item $f -Force -ErrorAction SilentlyContinue
    } else {
        $out = $query | docker compose -f $Compose exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
    }
    return ($out -join '').Trim()
}

# PowerShell 5.1'de -Form yok: multipart gövdeyi elle kuruyoruz (e2e-smoke.ps1 ile aynı).
function KanitYukle($SessionId, $Code, $Token) {
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

# ---------------------------------------------------------------- HAZIRLIK
Write-Host 'dersmate - bran$ rozeti tohumlamasi' -ForegroundColor White
Write-Host ''

$ayse = Register 'ayse@demo.dev' 'Ayse Yilmaz' ('a' * 64)
$ayseT = $ayse.accessToken
$ayseId = [string]$ayse.userId
Write-Host "egitmen: Ayse ($ayseId)"

$topics = Api GET '/api/catalog/topics'

# Branş başına BİR konu seç. Konu adına değil BRANŞA bağlanıyoruz: müfredat güncellemesi
# konu adlarını değiştiriyor (seed-demo.ps1 bu yüzden bir kez kırılmıştı), branş enum'u ise
# şema seviyesinde sabit.
function BransKonusu($brans) {
    $aday = @($topics | Where-Object { $_.subject -eq $brans })
    if ($aday.Count -eq 0) { throw "Katalogda '$brans' dersine ait konu yok. Katalog esitlendi mi?" }
    return $aday[0]
}

$hedefler = @(
    @{ Brans = 'Matematik'; Saat = 20 },
    @{ Brans = 'Geometri';  Saat = 15 },
    @{ Brans = 'Fizik';     Saat = 8  }
)

# Mevcut durumu OKU: tekrar koşumda sıfırdan başlamayalım.
$mevcutDk = @{}
foreach ($h in $hedefler) {
    $dk = Sql @"
SELECT COALESCE(SUM(ls."DurationMinutes"), 0)
FROM scheduling."LessonSessions" ls
JOIN catalog."Topics" t ON t."Id" = ls."TopicId"
JOIN catalog."Subjects" s ON s."Id" = t."SubjectId"
WHERE ls."TutorUserId" = '$ayseId' AND ls."Status" = 'Completed' AND s."Branch" = '$($h.Brans)';
"@
    $mevcutDk[$h.Brans] = [int]$dk
    Write-Host ("  {0,-10} mevcut: {1,4} dk  hedef: {2,4} dk" -f $h.Brans, $dk, ($h.Saat * 60))
}

# ---------------------------------------------------------------- ÖĞRENCİ HAVUZU
# Altı öğrenci yeterli: dersler 3 gün arayla dizildiği için aynı çiftin iki dersi asla
# aynı 24 saatlik pencereye düşmüyor. HWID kullanıcı başına AYRI olmak zorunda — ban
# kaydı cihaza yazılıyor, aynı HWID'i paylaşan hesaplar birbirini etkiler.
#
# ADLAR GERÇEK GÖRÜNMELİ. Bu hesaplar yalnızca ders üretmiyor; ders geçmişi üzerine
# yazılan değerlendirmelerde (bkz. seed-degerlendirme.ps1) YORUMCU ADI olarak Ayşe'nin
# profilinde herkese görünüyorlar. "Rozet Ogrencisi 3" yazan bir yorum, ekranı
# değerlendirirken tasarımı değil veriyi konuşturuyor.
$ADLAR = @('Deniz Aksoy', 'Kerem Bulut', 'Ada Yildirim', 'Melis Ozkan', 'Baris Tunc', 'Ipek Sarikaya')

$ogrenciler = @()
for ($i = 1; $i -le 6; $i++) {
    $hwid = ('r{0:d2}' -f $i) * 21 + 'r'
    $ogrenciler += Register "rozet$i@demo.dev" $ADLAR[$i - 1] $hwid
}
Write-Host "ogrenci havuzu: $($ogrenciler.Count) hesap"

# YARIM KALMIŞ KOŞUMUN ARTIKLARINI TEMİZLE.
#
# Betik ortasında düşerse geride tamamlanmamış bir "Booked" ders kalıyor ve bir sonraki
# koşumu iki ayrı yerden bloke ediyor: rezervasyon SCHEDULE_CONFLICT alıyor, kaydırma ise
# veritabanındaki çakışma kısıtına (EX_LessonSessions_TutorNoOverlap) takılıyor.
#
# İptal SİLME DEĞİL: kısıt kısmi (yalnızca Booked/AwaitingApproval için geçerli), yani
# Cancelled'a çekmek kaydı yoldan çıkarırken denetim izini koruyor.
$temiz = Sql @"
UPDATE scheduling."LessonSessions" ls
SET "Status" = 'Cancelled'
FROM identity."Users" u
WHERE u."Id" = ls."StudentUserId"
  AND u."Email" LIKE 'rozet%@demo.dev'
  AND ls."Status" IN ('Booked', 'AwaitingApproval');
"@
if ($temiz -match 'UPDATE (\d+)' -and [int]$Matches[1] -gt 0) {
    Write-Host "yarim kalmis $($Matches[1]) ders iptal edildi" -ForegroundColor DarkYellow
}
Write-Host ''

# ---------------------------------------------------------------- DERS ÜRETİMİ
$sira = 0
$uretilen = 0

foreach ($h in $hedefler) {
    $konu = BransKonusu $h.Brans
    $eksikDk = ($h.Saat * 60) - $mevcutDk[$h.Brans]
    if ($eksikDk -le 0) {
        Write-Host ("{0,-10} zaten dolu, atlandi" -f $h.Brans) -ForegroundColor DarkGray
        continue
    }

    $dersSayisi = [Math]::Ceiling($eksikDk / $SURE)
    Write-Host ("{0,-10} {1} ders uretilecek (konu: {2})" -f $h.Brans, $dersSayisi, $konu.topic) -ForegroundColor Cyan

    # Ayşe bu konuyu SUNMALI: eşleşme isteği ancak sunulan bir konuya açılabiliyor.
    try {
        Api POST '/api/portfolio/entries' @{ topicId = $konu.topicId; direction = 'Offer'; selfAssessedLevel = 5; note = $null } $ayseT | Out-Null
    } catch { }   # zaten varsa 409

    for ($d = 1; $d -le $dersSayisi; $d++) {
        $sira++
        $ogr = $ogrenciler[($sira - 1) % $ogrenciler.Count]
        $ogrT = $ogr.accessToken
        $ogrId = [string]$ogr.userId

        # --- eşleşme (varsa yeniden kurma) ---
        $matches = Api GET '/api/matches' $null $ogrT
        $aktif = @($matches.active | Where-Object { [string]$_.otherUserId -eq $ayseId -and [string]$_.topicId -eq [string]$konu.topicId })

        if ($aktif.Count -gt 0) {
            $matchId = $aktif[0].matchId
        } else {
            $bekleyen = @($matches.outgoing | Where-Object { [string]$_.otherUserId -eq $ayseId })
            if ($bekleyen.Count -gt 0) {
                $matchId = $bekleyen[0].matchId
            } else {
                $matchId = Api POST '/api/matches' @{ responderUserId = $ayseId; requestedTopicId = $konu.topicId; offeredTopicId = $null } $ogrT
            }
            try { Api POST "/api/matches/$matchId/respond" @{ accept = $true } $ayseT | Out-Null } catch { }
        }

        # --- rezervasyon: her ders KENDİ gününe, sabit bir saate ---
        #
        # Sabit tek slot denendi ve iki ayrı yerden kırıldı: yarım kalmış bir koşumdan
        # artakalan "Booked" ders aynı aralığı tutuyor (SCHEDULE_CONFLICT) ve aynı gün
        # içindeki ikinci ders MintGuard'ın penceresine giriyor. Günü kaydırmak ikisini
        # birden çözüyor.
        #
        # SAAT 06:15 seçildi çünkü seed-demo.ps1 kendi derslerini başka saatlere koyuyor;
        # aynı katılımcının iki dersi çakışırsa rezervasyon reddedilir.
        $baslangic = (Get-Date).ToUniversalTime().Date.AddDays($sira * 2).AddHours(6).AddMinutes(15).ToString('yyyy-MM-ddTHH:mm:ssZ')
        $rez = Api POST '/api/sessions' @{
            matchId = $matchId; topicId = $konu.topicId
            scheduledStartUtc = $baslangic; durationMinutes = $SURE
        } $ogrT

        # --- geçmişe kaydır: time-lock açılsın, sonraki rezervasyonun penceresinden çıksın ---
        # İKİ KOLON DA KAYDIRILMALI. Time-Lock "ScheduledEndUtc"e bakıyor; yalnızca
        # başlangıcı geçmişe almak dersi tamamlanabilir YAPMAZ ve tamamlama
        # TIME_LOCK_ACTIVE ile düşer. MintGuard ise başlangıca bakar — yani birini
        # kaydırıp diğerini unutmak, iki ayrı yerde iki ayrı sessiz hata demektir.
        $gunOnce = $sira * $GUN_ARALIGI
        $kaydir = Sql @"
UPDATE scheduling."LessonSessions"
SET "ScheduledStartUtc" = now() - interval '$gunOnce days',
    "ScheduledEndUtc"   = now() - interval '$gunOnce days' + interval '$SURE minutes'
WHERE "Id" = '$($rez.sessionId)';
"@
        if ($kaydir -notmatch 'UPDATE 1') { throw "ders gecmise kaydirilamadi: $kaydir" }

        # --- kanıt + onay: uygulama yolunun tamamı ---
        KanitYukle $rez.sessionId $rez.verificationCode $ayseT | Out-Null
        $onay = Api POST "/api/sessions/$($rez.sessionId)/approve" $null $ogrT

        $uretilen++
        Write-Host ("  {0,2}/{1}  {2,-10} {3} gun once  +{4} puan" -f $d, $dersSayisi, $h.Brans, $gunOnce, $onay.creditsMinted)
    }
}

# ---------------------------------------------------------------- SONUÇ
Write-Host ''
if ($uretilen -eq 0) {
    Write-Host 'Yeni ders uretilmedi - hedefler zaten doluydu.' -ForegroundColor DarkGray
} else {
    Write-Host "$uretilen ders tamamlandi ve onaylandi." -ForegroundColor Green
}

# ÇOK SATIRLI SONUÇ İÇİN AYRI OKUMA: `Sql` çıktıyı '' ile birleştiriyor (tek değerli
# sorgular için doğru), çok satırlı bir sonuçta ise satırlar birbirine yapışıyordu —
# "Fizik / CirakGeometri / Cirak" gibi. Burada satır sonu korunuyor.
$rozetSatirlari = Sql @"
SELECT string_agg(b."Branch" || ' / ' || b."Level", ' | ' ORDER BY b."Branch", b."Level")
FROM community."UserSubjectBadges" b
WHERE b."UserId" = '$ayseId';
"@
$rozetler = ($rozetSatirlari -split '\|') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
$puan = Sql "SELECT ""TotalEarnedCredits"" FROM identity.""Users"" WHERE ""Id"" = '$ayseId';"

Write-Host ''
Write-Host 'AYSE`NIN DURUMU' -ForegroundColor White
Write-Host "  kazanilan puan : $puan"
Write-Host '  rozetler       :'
foreach ($r in $rozetler) { Write-Host "    - $r" }
Write-Host ''
Write-Host 'Profili gormek icin: http://localhost:5173/profil  (ayse@demo.dev / Demo12345)' -ForegroundColor Green
