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

<#
  psql UC YOLDAN aranir; ilk bulunan kullanilir. e2e paketlerinin tamami bu uclusu
  tasiyor, bu betik tasimiyordu:

    1. Windows kurulumu   — gelistiricinin makinesinde tipik yol
    2. PATH uzerinde psql — Linux/macOS ve CI kosuculari
    3. docker compose     — makinede psql yok ama compose yigini ayakta

  UCUNCU YOL OLMADAN bu betik, docs/GELISTIRME-ORTAMI.md'nin tarif ettigi DOCKER
  kurulumunda HIC KOSAMIYORDU: ilk satirda "psql bulunamadi" diye oluyordu. Yani
  temizlik "yapilmadi" degil, DENENEMEDI — ve bu, basarisiz olmasindan daha sinsi:
  kimse temizligin kosmadigini fark etmez, veritabani sessizce sisar.
#>
if (-not (Test-Path $psql)) {
    $psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    $bulunan = if ($psqlCmd) { $psqlCmd.Source } else { $null }
    if ($bulunan) { $psql = $bulunan } else { $psql = $null }
}
if (-not (Test-Path $script)) { throw "SQL betigi bulunamadi: $script" }

$compose = Join-Path $root 'docker-compose.yml'
if (-not $psql -and -not (Test-Path $compose)) {
    throw "Ne psql bulundu ne docker-compose.yml — temizlik kosamaz."
}

$env:PGPASSWORD = 'PeerLearnDev2026'

# Docker yolunda sorgu/betik STDIN'den gecer: -f ile dosya yolu vermek konteynerin
# icinde aranir ve bulunamaz.
function PsqlDosya($yol, $ekArgumanlar) {
    if ($psql) {
        & $psql -h localhost -U peerlearn -d peerlearn @ekArgumanlar -f $yol
        return
    }
    Get-Content -Raw -Encoding UTF8 $yol |
        docker compose -f $compose exec -T db psql -U peerlearn -d peerlearn @ekArgumanlar
}

function Say($q) {
    if ($psql) {
        $f = Join-Path $env:TEMP "peerlearn-say.sql"
        [IO.File]::WriteAllText($f, $q, [Text.UTF8Encoding]::new($false))
        return (& $psql -h localhost -U peerlearn -d peerlearn -t -A -f $f) -join ''
    }
    return ($q | docker compose -f $compose exec -T db psql -U peerlearn -d peerlearn -t -A) -join ''
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
PsqlDosya $script @('-v', 'ON_ERROR_STOP=1')
$kod1 = $LASTEXITCODE

$kod2 = 0
if ($kod1 -eq 0) {
    Write-Host "`n[2/2] Katalog artiklari (kategori/ders/konu)..." -ForegroundColor Yellow
    PsqlDosya $katalog @('-v', 'ON_ERROR_STOP=1')
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
