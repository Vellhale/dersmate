# dersmate — artık dosya süpürücüsünün referans kümesi testi
#
# NEDEN VAR: süpürücü, hiçbir DB kaydının işaret etmediği dosyaları siliyor. Referans
# kümesi eksik kalırsa GERÇEK dosyalar "artık" sayılır ve gider. Bu bir kez oldu:
# TeacherCandidateProfiles.DocumentStorageKey kümeye hiç eklenmemişti, dolayısıyla
# öğrencilerin yüklediği kimlik/öğrenci belgeleri 7 gün sonra sessizce siliniyordu.
# Satır "belgesi var" demeye devam ediyor, dosya yok; yedekten de dönmüyor.
#
# ⛔ TEK BAŞINA "DOSYA DURUYOR" İDDİASI YETMEZ. Süpürücü hiç çalışmasa da o iddia
# geçerdi. Bu yüzden test İKİ dosya kuruyor ve ikisini de aynı yaşa getiriyor:
#
#     A. aday belgesi   → referanslı  → KALMALI
#     B. sahte artık    → referanssız → SİLİNMELİ
#
# Böylece test iki yönden de kırılıyor:
#   • referans kümesinden aday belgesi çıkarılırsa  → A silinir, test KALDI der
#   • süpürücü hiç çalışmazsa (ya da iptal ederse)  → B kalır, test KALDI der
#
# Mutasyonla doğrulandı: CleanupStorage.cs'teki adayBelgeleri sorgusu kaldırılınca
# A gerçekten siliniyor ve bu test kırılıyor.
#
# YÖNTEM: 7 günlük koruma penceresini beklemek yerine dosyaların son değiştirilme
# zamanı geçmişe alınıyor (job'ın yaş ölçütü LastWriteTimeUtc). e2e-jobs.ps1 aynı
# yaklaşımı zaman damgalarıyla yapıyor.
#
# Kullanım:  powershell -ExecutionPolicy Bypass -File .\tools\e2e-artik-supurme.ps1

$ErrorActionPreference = 'Stop'
$Api = 'http://localhost:5000'
$Psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'

# psql üç yoldan aranır (bkz. e2e-jobs.ps1): Windows kurulumu, PATH, docker compose.
# Testin KOŞAMAMASI, başarısız olmasından daha sinsi — özet onu hiç görünmemiş sayar.
if (-not (Test-Path $Psql)) {
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    if ($psqlCmd) { $Psql = $psqlCmd.Source } else { $Psql = $null }
}
$script:ComposeYml = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'
$env:PGPASSWORD = 'PeerLearnDev2026'

$script:Pass = 0
$script:Fail = 0
function OK($m) { $script:Pass++; Write-Host "  [OK] $m" -ForegroundColor Green }
function Fail($m) { $script:Fail++; Write-Host "  [KALDI] $m" -ForegroundColor Red }
function Section($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

<#
  ⛔ KURULUM ADIMI DÜŞERSE TEST DEVAM ETMEZ.

  İlk koşumda tam olarak şu yaşandı: DB sorgusu yanlış şemaya gittiği için anahtar
  BOŞ döndü, Join-Path boş parçayla depo KÖKÜNÜ üretti, Test-Path o dizin için TRUE
  dedi ve test "aday belgesi KORUNDU" diye YEŞİL yandı — ortada hiç belge yokken.

  Yanlış nedenle geçen bir test, hiç olmayan testten kötüdür: koruduğunu sandığın
  kural sessizce kırılır. Kurulum tutmadıysa iddiaya hiç geçilmiyor.
#>
function Dur($m) {
    Fail $m
    Write-Host ""
    Write-Host "Geçen: $script:Pass   Kalan: $script:Fail" -ForegroundColor White
    exit 1
}

function Sql($query) {
    if (-not $Psql) {
        $out = $query | docker compose -f $script:ComposeYml exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
        return ($out -join '').Trim()
    }
    $file = Join-Path $env:TEMP "dersmate-supurme-$([Guid]::NewGuid().ToString('N')).sql"
    [IO.File]::WriteAllText($file, $query, [Text.UTF8Encoding]::new($false))
    try { (& $Psql -h localhost -U peerlearn -d peerlearn -t -A -f $file) -join '' } finally { Remove-Item $file -Force }
}

function Post($path, $body, $token) {
    $headers = @{}
    if ($token) { $headers['Authorization'] = "Bearer $token" }
    $json = $body | ConvertTo-Json -Depth 6
    Invoke-RestMethod -Uri "$Api$path" -Method Post -Headers $headers `
        -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json))
}

function Put_($path, $body, $token) {
    $json = $body | ConvertTo-Json -Depth 6
    Invoke-RestMethod -Uri "$Api$path" -Method Put -Headers @{ Authorization = "Bearer $token" } `
        -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json))
}

# PowerShell 5.1'de Invoke-RestMethod -Form YOK; multipart gövdesi elle kuruluyor.
function PostDosya($path, $bytes, $dosyaAdi, $contentType, $token) {
    $sinir = [Guid]::NewGuid().ToString('N')
    $enc = [Text.Encoding]::UTF8
    $LF = "`r`n"
    $bas = "--$sinir$LF" +
           "Content-Disposition: form-data; name=`"document`"; filename=`"$dosyaAdi`"$LF" +
           "Content-Type: $contentType$LF$LF"
    $son = "$LF--$sinir--$LF"

    $ms = New-Object System.IO.MemoryStream
    $basB = $enc.GetBytes($bas); $ms.Write($basB, 0, $basB.Length)
    $ms.Write($bytes, 0, $bytes.Length)
    $sonB = $enc.GetBytes($son); $ms.Write($sonB, 0, $sonB.Length)

    Invoke-RestMethod -Uri "$Api$path" -Method Post -Headers @{ Authorization = "Bearer $token" } `
        -ContentType "multipart/form-data; boundary=$sinir" -Body $ms.ToArray()
}

function NewHwid { -join ((1..64) | ForEach-Object { '0123456789abcdef'[(Get-Random -Max 16)] }) }

function NewUser($prefix, $stamp) {
    $hwid = NewHwid
    $email = "$prefix$stamp@test.dev"
    $reg = Post '/api/auth/register' @{ email = $email; password = 'Demo12345'; displayName = "$prefix $stamp"; termsVersion = (& "$PSScriptRoot\yasal-surum.ps1"); ageConfirmed = $true; hwidHash = $hwid } $null
    Post '/api/auth/verify-email' @{ email = $email; code = $reg.verificationToken } $null | Out-Null
    $login = Post '/api/auth/login' @{ email = $email; password = 'Demo12345'; hwidHash = $hwid } $null
    return [pscustomobject]@{ Email = $email; Hwid = $hwid; Token = $login.accessToken; UserId = $login.userId }
}

$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
Write-Host "dersmate — artık süpürme testi" -ForegroundColor White
Write-Host "API: $Api   koşum: $stamp"

# ---------------------------------------------------------------------------
Section 'Hazırlık: aday belgesi yükle'

$aday = NewUser 'supaday' $stamp
$admin = NewUser 'supadmin' $stamp
Sql "UPDATE identity.""Users"" SET ""Role"" = 'Admin' WHERE ""Id"" = '$($admin.UserId)';" | Out-Null
# Rol değişikliği token'a ancak yeni girişte yansır.
$admin.Token = (Post '/api/auth/login' @{ email = $admin.Email; password = 'Demo12345'; hwidHash = $admin.Hwid } $null).accessToken
OK 'aday ve yönetici hazır'

# Belge yüklenebilmesi için ÖNCE beyan gerekiyor (UploadTeacherDocumentHandler).
Put_ '/api/profile/teacher-candidate' @{ university = 'Test Üniversitesi'; faculty = 'Eğitim Fakültesi'; department = 'Matematik Öğretmenliği'; gradeYear = 3; hasPedagogicalCertificate = $false } $aday.Token | Out-Null
OK 'öğretmen adaylığı beyanı kaydedildi'

# En küçük geçerli PNG (1x1). İçeriği önemsiz; önemli olan depoya bir dosya düşmesi.
$png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==')
PostDosya '/api/profile/teacher-candidate/document' $png 'belge.png' 'image/png' $aday.Token | Out-Null

$anahtar = Sql "SELECT ""DocumentStorageKey"" FROM identity.""TeacherCandidateProfiles"" WHERE ""UserId"" = '$($aday.UserId)';"
$anahtar = $anahtar.Trim()
if (-not $anahtar) { Dur 'belge anahtarı DB''de bulunamadı — yükleme tutmamış' }
OK "belge yüklendi (anahtar: $anahtar)"

# ---------------------------------------------------------------------------
Section 'Dosyaları yaşlandır'

# Depo kökü: API''nin çalışma dizinine göreli (appsettings ProofStorage:RootPath).
$kok = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\PeerLearn.Api\proof-storage'
if (-not (Test-Path $kok)) { Dur "depo kökü bulunamadı: $kok" }

$belgeYolu = Join-Path $kok ($anahtar -replace '/', '\')
if (-not (Test-Path -PathType Leaf $belgeYolu)) { Dur "belge dosyası diskte yok: $belgeYolu" }
OK 'belge dosyası diskte'

# B — GERÇEK ARTIK. Hiçbir satır buna işaret etmiyor; süpürücü bunu SİLMELİ.
# Testin ikinci yönü bu: silinmezse süpürücü hiç çalışmamış demektir ve
# "belge duruyor" iddiası da anlamsızlaşır.
$artikYolu = Join-Path $kok "sahte-artik-$stamp.png"
[IO.File]::WriteAllBytes($artikYolu, $png)

# Koruma penceresi 7 gün (OrphanGraceDays). 10 gün geriye alınıyor.
$eski = (Get-Date).ToUniversalTime().AddDays(-10)
(Get-Item $belgeYolu).LastWriteTimeUtc = $eski
(Get-Item $artikYolu).LastWriteTimeUtc = $eski
OK 'iki dosya da 10 gün geriye alındı (koruma penceresi 7 gün)'

# ---------------------------------------------------------------------------
Section 'Süpürücüyü çalıştır'

$sonuc = Post '/api/admin/jobs/storage-cleanup' @{} $admin.Token
Write-Host "  süpürme sonucu: $($sonuc.orphansDeleted) artık silindi"

# ---------------------------------------------------------------------------
Section 'Sonuç'

# A — referanslı belge KALMALI
# -PathType Leaf: dizinin varlığı "dosya duruyor" sayılmasın (bkz. Dur yorumu).
if (Test-Path -PathType Leaf $belgeYolu) {
    OK 'aday belgesi KORUNDU (referans kümesinde)'
} else {
    Fail 'ADAY BELGESİ SİLİNDİ — referans kümesinde eksik (CleanupStorage.ArtiklariSupurAsync)'
}

# Dosya durmakla kalmamalı, uçtan da okunabilmeli.
$profilId = (Sql "SELECT ""Id"" FROM identity.""TeacherCandidateProfiles"" WHERE ""UserId"" = '$($aday.UserId)';").Trim()
try {
    Invoke-WebRequest -Uri "$Api/api/profile/teacher-candidate/$profilId/document" `
        -Headers @{ Authorization = "Bearer $($aday.Token)" } -UseBasicParsing | Out-Null
    OK 'belge uçtan hâlâ indirilebiliyor'
} catch {
    Fail "belge ucu hata verdi: $($_.Exception.Message)"
}

# B — gerçek artık SİLİNMELİ (süpürücünün çalıştığının kanıtı)
if (Test-Path $artikYolu) {
    Fail 'sahte artık SİLİNMEDİ — süpürücü çalışmadı, testin ilk iddiası anlamsız'
    Remove-Item $artikYolu -Force -ErrorAction SilentlyContinue
} else {
    OK 'sahte artık silindi (süpürücü gerçekten çalıştı)'
}

Write-Host ""
Write-Host "Geçen: $script:Pass   Kalan: $script:Fail" -ForegroundColor White
if ($script:Fail -gt 0) { exit 1 }
exit 0
