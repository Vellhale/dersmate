# Test/bench hesap temizligini calistirir.
#
# NEDEN SARMALAYICI: komutu elle yazmak iki kez basarisiz oldu. Sebep PowerShell'in inline
# env-var onekini (PGPASSWORD=... psql) desteklememesi ve proje yolunda kesme isareti
# olabilmesi. Burada yol da parola da betigin icinde, disaridan tirnaklanacak hicbir sey yok.
#
# Kullanim (proje kokunden):  powershell -ExecutionPolicy Bypass -File .\tools\temizlik-calistir.ps1
#
# Silme tek transaction'dadir ve sonunda dort defter degismezi sinanir; biri tutmazsa
# hicbir sey silinmez (ROLLBACK). Yani yarim kalmis bir temizlik mumkun degildir.

$ErrorActionPreference = 'Stop'

$psql   = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$root   = Split-Path $PSScriptRoot -Parent
$script = Join-Path $PSScriptRoot 'temizlik-test-hesaplari.sql'

if (-not (Test-Path $psql))   { throw "psql bulunamadi: $psql" }
if (-not (Test-Path $script)) { throw "SQL betigi bulunamadi: $script" }

$env:PGPASSWORD = 'PeerLearnDev2026'

function Say($q) {
    $f = Join-Path $env:TEMP "peerlearn-say.sql"
    [IO.File]::WriteAllText($f, $q, [Text.UTF8Encoding]::new($false))
    (& $psql -h localhost -U peerlearn -d peerlearn -t -A -f $f) -join ''
}

<#
  İKİ AŞAMA, SIRASI ÖNEMLİ.

  Betik başta yalnızca hesapları siliyordu ve katalog artıkları (Zqx kategorileri, test
  konuları) ardında ÖKSÜZ kalıyordu — sahibi bir kullanıcı olmadığı için hesap silmesi
  onlara hiç dokunmuyor. 2026-08-17'de bu yüzden "temizlik yapıldı" denen bir kurulumda
  Keşfet filtresi hâlâ 18 çöp kategori gösteriyordu.

  Sıra HESAP → KATALOG olmalı: katalog betiği, bağlı kaydı olan bir konu bulursa hiçbir
  şey silmeden durur. Hesaplar önce gidince o bağlar zaten kalmıyor.
#>
$katalog = Join-Path $PSScriptRoot 'temizlik-test-katalogu.sql'
if (-not (Test-Path $katalog)) { throw "Katalog betigi bulunamadi: $katalog" }

$oncesiKullanici = Say 'SELECT COUNT(*) FROM identity."Users";'
$oncesiKonu      = Say 'SELECT COUNT(*) FROM catalog."Topics";'
Write-Host "Once  : $oncesiKullanici kullanici, $oncesiKonu konu" -ForegroundColor Cyan

Write-Host "`n[1/2] Hesaplar ve bagli kayitlar..." -ForegroundColor Yellow
& $psql -h localhost -U peerlearn -d peerlearn -v ON_ERROR_STOP=1 -f $script
$kod1 = $LASTEXITCODE

$kod2 = 0
if ($kod1 -eq 0) {
    Write-Host "`n[2/2] Katalog artiklari (kategori/ders/konu)..." -ForegroundColor Yellow
    & $psql -h localhost -U peerlearn -d peerlearn -v ON_ERROR_STOP=1 -f $katalog
    $kod2 = $LASTEXITCODE
} else {
    Write-Host "`n[2/2] ATLANDI: hesap asamasi basarisiz oldu." -ForegroundColor Red
}

$sonrasiKullanici = Say 'SELECT COUNT(*) FROM identity."Users";'
$sonrasiKonu      = Say 'SELECT COUNT(*) FROM catalog."Topics";'
Write-Host "`nSonra : $sonrasiKullanici kullanici, $sonrasiKonu konu" -ForegroundColor Cyan

if ($kod1 -eq 0 -and $kod2 -eq 0) {
    if ($sonrasiKullanici -ne $oncesiKullanici -or $sonrasiKonu -ne $oncesiKonu) {
        Write-Host "BASARILI: temizlik uygulandi ve commit edildi." -ForegroundColor Green
    } else {
        Write-Host "DEGISIKLIK YOK: silinecek test verisi zaten yoktu." -ForegroundColor Green
    }
} else {
    Write-Host "BASARISIZ (cikis kodlari: hesap=$kod1 katalog=$kod2). Yukaridaki hatayi bana getir." -ForegroundColor Red
}
