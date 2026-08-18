# PeerLearn — tüm test paketlerini sırayla çalıştırır ve tek bir özet basar.
#
# SIRA ÖNEMLİ: hafif ve hızlı olanlar önce. Eşzamanlılık paketi en sonda, çünkü İKİ
# instance ister ve en uzun sürer; öncekiler kırmızıysa ona hiç sıra gelmemeli.
#
# Kullanım:
#   powershell -ExecutionPolicy Bypass -File .\tools\run-all-tests.ps1
#   ... -SkipConcurrency     (ikinci instance yoksa)
#
# Ön koşullar: PostgreSQL (tools/start-postgres.ps1), Redis, API :5000.
# Eşzamanlılık için ayrıca :5001 (bkz. .claude/launch.json "api2").

param([switch]$SkipConcurrency)

$root = Split-Path $PSScriptRoot -Parent
$sonuclar = @()

# Paketler AYRI BİR SÜREÇTE çalıştırılır. Sebebi: e2e betikleri çıktılarını Write-Host ile
# basıyor ve Write-Host PowerShell 5.1'de nesne akışına YAZMAZ — aynı süreçte "& script"
# ile çağırınca çıktı yakalanamıyor, dolayısıyla [KALDI] satırları da sayılamıyordu.
# Bu, başarısız bir paketi YEŞİL raporlama riski demekti. Dış süreçte tüm konsol çıktısı
# yakalanır ve çıkış kodu tek başına da güvenilir bir sinyaldir.
function Kosum($ad, $komut, $argumanlar) {
    Write-Host "`n########################################" -ForegroundColor DarkGray
    Write-Host "# $ad" -ForegroundColor White
    Write-Host "########################################" -ForegroundColor DarkGray

    $sure = [Diagnostics.Stopwatch]::StartNew()
    $ciktilar = & $komut @argumanlar 2>&1 | ForEach-Object { $_.ToString() }
    $cikisKodu = $LASTEXITCODE
    $sure.Stop()

    # BAŞARISIZLIK İŞARETLERİ TEK LİSTEDE: paketler yıllar içinde farklı etiketler kullandı
    # ([KALDI], [FAIL], [HATA]). Liste eksik kaldığında o paketin başarısızlıkları özete HİÇ
    # yansımıyordu — eşzamanlılık paketi tam olarak böyle sessizce yeşil görünüyordu.
    # Yeni bir paket farklı bir etiket kullanacaksa BURAYA da eklenmeli.
    $kontrol = @($ciktilar | Where-Object { $_ -match '\[OK\]' }).Count
    $kalan = @($ciktilar | Where-Object { $_ -match '\[KALDI\]|\[FAIL\]|\[HATA\]' }).Count

    # İki bağımsız sinyal: çıkış kodu VE çıktıdaki başarısız satırlar. Biri kaçarsa diğeri yakalar.
    $basarili = ($cikisKodu -eq 0) -and ($kalan -eq 0)

    $ciktilar | Select-Object -Last 5 | ForEach-Object { Write-Host "  $_" }
    if ($kalan -gt 0) {
        Write-Host "  --- başarısız kontroller ---" -ForegroundColor Red
        $ciktilar | Where-Object { $_ -match '\[KALDI\]|\[FAIL\]|\[HATA\]' } | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    }

    # ATLANAN kapsam başarısızlık değil ama "tam koştu" da değil: özet bunu göstermezse
    # eksik koşan bir paket yeşil görünür ve kapsam sessizce kaybolur.
    $atlanan = @($ciktilar | Where-Object { $_ -match '\[ATLANDI\]' }).Count
    if ($atlanan -gt 0) {
        Write-Host "  --- atlanan kapsam ---" -ForegroundColor Yellow
        $ciktilar | Where-Object { $_ -match '\[ATLANDI\]' } | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    }

    $script:sonuclar += [pscustomobject]@{
        Paket    = $ad
        Durum    = if (-not $basarili) { 'KALDI' } elseif ($atlanan -gt 0) { 'EKSİK' } else { 'GEÇTİ' }
        Kontrol  = $kontrol
        Basarisiz = $kalan
        Atlanan  = $atlanan
        Saniye   = [math]::Round($sure.Elapsed.TotalSeconds, 1)
    }
}

function Betik($ad, $dosya) {
    Kosum $ad 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "$root\tools\$dosya")
}

Push-Location $root
try {
    Kosum 'Birim testleri' 'dotnet' @('test', '--nologo')
    Betik 'Ana akış (smoke)' 'e2e-smoke.ps1'
    Betik 'Modül 1 — arama ve filtreleme' 'e2e-discovery.ps1'
    Betik 'Modül 2 — hakem paneli' 'e2e-moderation.ps1'
    Betik 'Modül 3+5 — tercihler' 'e2e-preferences.ps1'
    Betik 'Sosyal profil / gönüllü ders' 'e2e-social.ps1'
    Betik 'Kusur regresyonları' 'e2e-fixes.ps1'
    Betik 'Ölçek ve veri' 'e2e-scale.ps1'
    # İtiraz paketi emekli: itiraz açma yeteneği kaldırıldı (tek yönlü şikayete geçildi).
    # Yerine yeni akışın testi + o paketten kurtarılan ban/değişmez bölümleri.
    Betik 'Şikayet (tek yönlü)' 'e2e-report.ps1'
    Betik 'Ban ve değişmezler' 'e2e-ban.ps1'
    Betik 'Yönetim puan düzeltmesi' 'e2e-admin-credits.ps1'
    Betik 'Arka plan işleri' 'e2e-jobs.ps1'

    # Suistimal frenleri sona yakın: eşzamanlılık paketi gibi İKİ instance ister (B adımı)
    # ve bir eğitmene bağlı ~35 hesap üretir. Tek instance'ta kendini [ATLANDI] ile
    # bildirir, yani özet "eksik koştu" der — sessizce yeşile dönmez.
    Betik 'Suistimal frenleri (MintGuard)' 'e2e-mintguard.ps1'

    if (-not $SkipConcurrency) {
        Betik 'Eşzamanlılık (iki instance)' 'e2e-concurrency.ps1'
    }
}
finally {
    Pop-Location
}

Write-Host "`n================ ÖZET ================" -ForegroundColor White
$sonuclar | Format-Table -AutoSize

$kalanPaket = @($sonuclar | Where-Object { $_.Durum -eq 'KALDI' })
$eksikPaket = @($sonuclar | Where-Object { $_.Durum -eq 'EKSİK' })
$toplamKontrol = ($sonuclar | Measure-Object -Property Kontrol -Sum).Sum
$toplamAtlanan = ($sonuclar | Measure-Object -Property Atlanan -Sum).Sum

if ($kalanPaket.Count -gt 0) {
    Write-Host "$($kalanPaket.Count) PAKET BAŞARISIZ" -ForegroundColor Red
    exit 1
}

# EKSİK, "geçti" DEĞİLDİR: paket koştu ve kırılmadı ama bir kısmı hiç çalışmadı.
# Bunu yeşil raporlamak, sınanmamış kodu sınanmış sanmaktır.
if ($eksikPaket.Count -gt 0) {
    Write-Host "PAKETLER KIRILMADI ama $toplamAtlanan KAPSAM ATLANDI — $toplamKontrol kontrol koştu." -ForegroundColor Yellow
    Write-Host "Eksik koşan: $($eksikPaket.Paket -join ', ')" -ForegroundColor Yellow
} else {
    Write-Host "TÜM PAKETLER GEÇTİ — $toplamKontrol kontrol" -ForegroundColor Green
}
