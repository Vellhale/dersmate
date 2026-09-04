<#
.SYNOPSIS
  dersmate yedegi: PostgreSQL dokumu + proof-storage klasoru.

.DESCRIPTION
  IKISI BIRLIKTE ALINIR ve ayri alinmalari anlamsizdir. Ders kaniti iki parcali bir
  kayit: satir veritabaninda (SessionProofs.StorageKey + SHA-256 hash), dosyanin
  kendisi diskte. Yalnizca birini geri yuklemek, "kanit var" diyen bir satirla var
  olmayan bir dosya birakir -- ya da tersi: sahipsiz dosyalar ve hicbir seyi
  kanitlamayan bir depo.

  Betik BOM ILE kaydedilmistir. Bu projede 27 PowerShell betiginin 11 i yalnizca
  BOM suz olduklari icin PowerShell 5.1 de parse EDILEMIYOR: BOM yoksa 5.1 dosyayi
  CP1254 okuyor ve Turkce karakterler dizgeleri bozuyor. Bu dosyayi duzenlerken
  kodlamayi "UTF-8 with BOM" olarak koru.

.PARAMETER Hedef
  Yedeklerin yazilacagi klasor. Yoksa olusturulur.

.PARAMETER Konteyner
  PostgreSQL Docker konteyneri. Bos verilirse yerel pg_dump kullanilir
  (uretimde veritabani genelde Docker da degildir).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\tools\yedek-al.ps1 -Hedef D:\yedek\dersmate
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Hedef,

    [string]$Konteyner = "peerlearn-db",
    [string]$Sunucu    = "localhost",
    [int]$Port         = 5432,
    [string]$Veritabani = "peerlearn",
    [string]$Kullanici  = "peerlearn",
    [string]$Parola     = "PeerLearnDev2026",
    [string]$KanitYolu  = "proof-storage"
)

$ErrorActionPreference = "Stop"

# Damga elle uretiliyor: dosya adinda ":" olamaz ve siralanabilir olmali.
$damga = Get-Date -Format "yyyy-MM-dd_HHmmss"
$klasor = Join-Path $Hedef $damga

if (-not (Test-Path $Hedef)) {
    New-Item -ItemType Directory -Path $Hedef -Force | Out-Null
}
New-Item -ItemType Directory -Path $klasor -Force | Out-Null

Write-Host "dersmate yedegi -> $klasor"
Write-Host ""

# --- 1. Veritabani -----------------------------------------------------------
$dokum = Join-Path $klasor "veritabani.sql"

# Satir ici ortam degiskeni oneki PowerShell de YOK (PGPASSWORD=x pg_dump calismaz);
# $env: ile ayri satirda atanir. Docker yolunda ise -e ile konteynere gecirilir.
$konteynerVar = $false
if ($Konteyner) {
    $bulunan = docker ps --filter "name=$Konteyner" --format "{{.Names}}" 2>$null
    if ($bulunan -eq $Konteyner) { $konteynerVar = $true }
}

# On kontrol: pg_dump PATH'te mi? Yoksa hata "terim taninmiyor" olur ve sebebi
# soylemez. Docker yolunda gerekmiyor -- dokumu konteynerin icindeki pg_dump aliyor.
if (-not $konteynerVar -and -not (Get-Command pg_dump -ErrorAction SilentlyContinue)) {
    throw ("pg_dump bulunamadi. PostgreSQL istemci araclari kurulu degil ya da PATH'te yok. " +
           "Windows'ta genellikle 'C:\Program Files\PostgreSQL\<surum>in' altindadir; " +
           "o klasoru PATH'e ekleyip yeni bir kabuk acin.")
}

# DOKUM PowerShell BORUSUNDAN GECMEZ -- pg_dump dosyayi kendisi yazar (-f).
#
# Borudan gecirmek (pg_dump | Out-File) dokumu iki kez donusturur: PowerShell, yerel
# komutun cikti baytlarini [Console]::OutputEncoding ile METNE cevirir, Out-File onu
# yeniden kodlar. Konsol kod sayfasi UTF-8 ise sonuc dogru cikar; Turkce bir Windows'ta
# sik gorulen CP857/CP1254 ise TUM Turkce karakterler bozulur. Olculdu:
#
#   kod sayfasi 65001 -> "Yarim Aci Formulle" dogru  (asil metin: Yarım Açı Formülle)
#   kod sayfasi 857   -> "Yar─▒m A├ğ─▒ Form├╝lle"
#   kod sayfasi 1254  -> "YarÄ±m AÃ§Ä± FormÃ¼lle"
#
# Bu, yedeklerin en tehlikeli bozulma bicimi: dosya olusur, boyut kontrolunu gecer,
# geri yukleme HATASIZ tamamlanir ve veritabanindaki her Turkce isim bozuk gelir.
# -f ile pg_dump baytlari dogrudan diske yazar; arada donusum yoktur.
if ($konteynerVar) {
    Write-Host "[1/2] Veritabani dokumu (docker: $Konteyner)..."
    # Konteyner icine yazip disari kopyalaniyor: `docker exec ... > dosya` da ayni
    # boru sorununu yasardi.
    $gecici = "/tmp/dersmate-dokum.sql"
    docker exec -e PGPASSWORD=$Parola $Konteyner `
        pg_dump -U $Kullanici -d $Veritabani --clean --if-exists -f $gecici
    if ($LASTEXITCODE -ne 0) { throw "pg_dump basarisiz (cikis kodu $LASTEXITCODE). Yedek EKSIK, kullanmayin." }
    docker cp "${Konteyner}:${gecici}" $dokum
    if ($LASTEXITCODE -ne 0) { throw "docker cp basarisiz; dokum konteynerden alinamadi." }
    docker exec $Konteyner rm -f $gecici | Out-Null
}
else {
    Write-Host "[1/2] Veritabani dokumu (yerel pg_dump: $Sunucu`:$Port)..."
    $env:PGPASSWORD = $Parola
    pg_dump -h $Sunucu -p $Port -U $Kullanici -d $Veritabani --clean --if-exists -f $dokum
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
}

if ($LASTEXITCODE -ne 0) {
    throw "pg_dump basarisiz (cikis kodu $LASTEXITCODE). Yedek EKSIK, kullanmayin."
}

$dokumBoyut = (Get-Item $dokum).Length
if ($dokumBoyut -lt 1024) {
    # Bos bir dokum, basarili gorunen en tehlikeli sonuctur: dosya var, icinde veri yok.
    throw "Dokum sadece $dokumBoyut bayt. Baglanti ya da yetki sorunu olmali; yedek gecersiz."
}
# Bozulma kontrolu: gecerli UTF-8 olmayan bayt dizisi, cozumlemede U+FFFD birakir.
# Boyut kontrolu bunu YAKALAMAZ -- bozuk dokum de dolu ve "saglikli" gorunur.
$icerik = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($dokum))
if ($icerik.Contains([char]0xFFFD)) {
    throw "Dokum gecerli UTF-8 degil (U+FFFD bulundu). Kodlama bozulmus; yedek gecersiz."
}
Write-Host ("      tamam - {0:N0} bayt" -f $dokumBoyut)

# --- 2. Kanit dosyalari ------------------------------------------------------
Write-Host "[2/2] Kanit dosyalari ($KanitYolu)..."

if (Test-Path $KanitYolu) {
    $kanitHedef = Join-Path $klasor "proof-storage"
    Copy-Item -Path $KanitYolu -Destination $kanitHedef -Recurse -Force
    $sayi = (Get-ChildItem -Path $kanitHedef -Recurse -File -ErrorAction SilentlyContinue |
             Measure-Object).Count
    Write-Host "      tamam - $sayi dosya"
}
else {
    # Uyari, hata degil: yeni bir kurulumda henuz hic kanit yuklenmemis olabilir.
    Write-Host "      UYARI: '$KanitYolu' bulunamadi." -ForegroundColor Yellow
    Write-Host "      Uretimde ProofStorage__RootPath MUTLAK bir yol olmali; goreli birakilirsa"
    Write-Host "      servis baska bir calisma dizininden basladiginda bos bir klasor acilir."
}

Write-Host ""
Write-Host "Yedek hazir: $klasor"
Write-Host ""
Write-Host "Geri yukleme (dokumun kendisi DROP iceriyor, mevcut semayi siler):"
Write-Host "  psql -h $Sunucu -p $Port -U $Kullanici -d $Veritabani -f `"$dokum`""
Write-Host ""
Write-Host "Denenmemis yedek, yedek degildir: geri yuklemeyi en az bir kez bos bir"
Write-Host "veritabaninda deneyin."
