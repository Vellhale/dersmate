# ─────────────────────────────────────────────────────────────────────────────
# Uretim ayar dosyasi (.env.production) sablonu uretir ve GUCLU SIRLARI kendisi
# olusturur.
#
#   powershell -File .\tools\uretim-ayarlari-uret.ps1 -AlanAdi dersmate.com
#
# NEDEN SIRLARI BETIK URETIYOR: elle uydurulan parolalar tahmin edilebilir cikiyor
# ("Dersmate2026!"). JWT anahtari icin bu ozellikle onemli — o anahtari bilen herkes
# istedigi kullanici adina token uretebilir, yani tum yetkilendirmeyi atlar.
#
# .NET'in Guid'i ya da Get-Random SIR URETMEZ (kriptografik degil). Burada
# RandomNumberGenerator kullaniliyor.
#
# ⛔ CIKAN DOSYA DEPOYA GIRMEZ. .gitignore'da; kaybolursa yeniden uretilir ama
#    JWT anahtari degisirse ACIK TUM OTURUMLAR DUSER (token'lar dogrulanamaz).
# ─────────────────────────────────────────────────────────────────────────────

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AlanAdi,

    # Varsayilan: alan adinin onunde "api.". Ayri bir alt alan adi kullanmiyorsaniz
    # (ornegin ayni alan adinda /api yolu) burayi degistirin.
    [string]$ApiAlanAdi,

    [string]$Cikti = '.env.production'
)

$ErrorActionPreference = 'Stop'

if (-not $ApiAlanAdi) { $ApiAlanAdi = "api.$AlanAdi" }

function Yeni-Sir {
    param([int]$Bayt = 48)

    # base64url: dosyada ve ortam degiskeninde tirnak/kacis sorunu cikarmayan alfabe.
    # Standart base64'teki '+' ve '/' karakterleri bazi shell ve YAML baglamlarinda
    # kacis gerektiriyor; '=' dolgusu da bazi araclarda kesiliyor.
    #
    # ⚠️ RNGCryptoServiceProvider kullaniliyor, RandomNumberGenerator::Fill DEGIL:
    # Fill yalnizca .NET Core 2.1+ ile var ve PowerShell 5.1 .NET Framework 4.x
    # uzerinde kosuyor — orada "does not contain a method named 'Fill'" ile duser.
    # PowerShell 7'de ikisi de calisir; betik 5.1'de de kosabilmeli.
    #
    # Get-Random ya da [guid]::NewGuid() BURADA KULLANILAMAZ: ikisi de kriptografik
    # degil. JWT anahtarini tahmin edilebilir bir kaynaktan uretmek, tum
    # yetkilendirmeyi tahmin edilebilir yapar.
    $tampon = New-Object byte[] $Bayt
    $uretec = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
    try {
        $uretec.GetBytes($tampon)
    }
    finally {
        $uretec.Dispose()
    }

    [Convert]::ToBase64String($tampon).Replace('+', '-').Replace('/', '_').TrimEnd('=')
}

if (Test-Path $Cikti) {
    # UZERINE YAZMIYORUZ. Mevcut dosyanin uzerine yazmak, calisan bir kurulumun JWT
    # anahtarini degistirip tum oturumlari dusurur ve veritabani parolasini
    # uygulamanin bildiginden farkli hale getirir.
    throw "$Cikti zaten var. Yeniden uretmek istiyorsaniz once yedekleyip silin — " +
          "icindeki JWT anahtari degisirse acik tum oturumlar duser."
}

$jwt = Yeni-Sir -Bayt 48
$dbParola = Yeni-Sir -Bayt 24

$icerik = @"
# dersmate uretim ayarlari — $(Get-Date -Format 'yyyy-MM-dd HH:mm') tarihinde uretildi.
#
# ⛔ BU DOSYA DEPOYA GIRMEZ ve yedeklenirken sifrelenmelidir.
# Kullanimi: docker compose -f docker-compose.prod.yml up -d --build

# ─── Alan adlari ────────────────────────────────────────────────────────────
# PUBLIC_WEB_URL hem CORS kaynagi hem de dogrulama e-postasindaki baglantinin koku.
# Sonunda / OLMAMALI: kod bunun uzerine yol ekliyor ve cift slash olusuyor.
PUBLIC_WEB_URL=https://$AlanAdi
API_URL=https://$ApiAlanAdi

# ─── Veritabani ─────────────────────────────────────────────────────────────
POSTGRES_DB=dersmate
POSTGRES_USER=dersmate
POSTGRES_PASSWORD=$dbParola

# ─── JWT ────────────────────────────────────────────────────────────────────
# ⚠️ DEGISTIRILIRSE ACIK TUM OTURUMLAR DUSER. Sizinti supheniz varsa bu istenen
# davranistir; aksi halde dokunmayin.
JWT_KEY=$jwt

# ─── E-posta (SMTP) ─────────────────────────────────────────────────────────
# ⛔ ELLE DOLDURULACAK. Bunlar olmadan hic kimse hesabini DOGRULAYAMAZ: dogrulama
# token'i yalnizca e-postayla gidiyor. Saglayicinizdan aldiginiz degerleri yazin.
SMTP_HOST=
SMTP_PORT=587
SMTP_USERNAME=
SMTP_PASSWORD=
SMTP_FROM=noreply@$AlanAdi

# ─── Hiz siniri (IP basina, dakikada) ───────────────────────────────────────
# Acikca veriliyor: verilmezse appsettings.json'daki gelistirme degerleri (2000/20000)
# gecerli olur ve giris ucu dakikada 2000 parola denemesine acik kalir.
RATE_AUTH_PER_MINUTE=10
RATE_GLOBAL_PER_MINUTE=300
"@

Set-Content -Path $Cikti -Value $icerik -Encoding UTF8

Write-Host ""
Write-Host "  $Cikti olusturuldu." -ForegroundColor Green
Write-Host ""
Write-Host "  Uretilen sirlar (dosyanin icinde):" -ForegroundColor Cyan
Write-Host "    JWT_KEY            64 karakter, kriptografik rastgele"
Write-Host "    POSTGRES_PASSWORD  32 karakter, kriptografik rastgele"
Write-Host ""
Write-Host "  SIRADAKI ADIM — SMTP alanlari BOS:" -ForegroundColor Yellow
Write-Host "    SMTP_HOST / SMTP_USERNAME / SMTP_PASSWORD elle doldurulmali."
Write-Host "    Bos kalirsa uygulama ACILIR ama hic kimse hesabini dogrulayamaz."
Write-Host ""
Write-Host "  Kurulumun tamami: docs/SUNUCUYA-KURULUM.md" -ForegroundColor Cyan
Write-Host ""
