# PeerLearn frontend — geliştirme çalışma alanı kurulumu
#
# NEDEN GEREKLİ:
# Proje Google Drive sanal diskinde (G:) duruyor. npm, node_modules'ü oraya kuramıyor
# (EBADF / ENOTEMPTY — Drive'ın sanal dosya sistemi binlerce küçük dosyanın hızlı
# oluşturulup silinmesini kaldırmıyor).
#
# ÇÖZÜM:
# Kaynak kod Drive'da KALIR (ekiple paylaşılan tek doğru kopya); yalnızca node_modules ve
# Vite çıktısı yerel diske taşınır. Yerel çalışma alanındaki "src" klasörü, Drive'daki
# gerçek kaynağa bir junction'dır — yani Drive'da düzenlediğin dosya anında burada görünür.
#
# KULLANIM:  powershell -ExecutionPolicy Bypass -File .\setup-dev.ps1
#            (ardından yazdırılan komutla `npm run dev`)

$ErrorActionPreference = 'Stop'

$source = $PSScriptRoot
$dev = Join-Path $env:LOCALAPPDATA 'PeerLearnBuild\frontend-dev'

Write-Host "Kaynak : $source"
Write-Host "Çalışma: $dev`n"

New-Item -ItemType Directory -Force -Path $dev | Out-Null

# src ve public -> Drive'daki gerçek kaynağa junction (dosyalar kopyalanmaz, aynasıdır).
# public de junction olmalı: favicon/statik varlıklar da Drive'da tek kopya kalsın, yoksa
# çalışma alanındaki kopya sessizce eskir.
# e2e de junction: Playwright testleri Drive'daki kaynakta yazılıyor ama node_modules
# (ve dolayısıyla koşum) yerel çalışma alanında. Kopya olsaydı testler burada sessizce
# eskir, "geçti" diyen paket aslında eski sürümü koşardı.
foreach ($dir in @('src', 'public', 'e2e')) {
    $target = Join-Path $source $dir
    if (-not (Test-Path $target)) { continue }

    $link = Join-Path $dev $dir
    if (-not (Test-Path $link)) {
        cmd /c mklink /J "$link" "$target" | Out-Null
        Write-Host "$dir junction olusturuldu."
    } else {
        Write-Host "$dir junction zaten var."
    }
}

# Yapılandırma dosyaları nadiren değişir; Drive kopyası her zaman doğrudur.
$configFiles = @(
    'package.json', 'package-lock.json', 'vite.config.js',
    'tailwind.config.js', 'postcss.config.js', 'index.html', '.env.development',
    'playwright.config.js'
)
foreach ($file in $configFiles) {
    $path = Join-Path $source $file
    if (Test-Path $path) { Copy-Item $path -Destination $dev -Force }
}
Write-Host "Yapilandirma dosyalari kopyalandi."

Push-Location $dev
try {
    Write-Host "`nnpm install calisiyor (yerel disk)..."
    npm install --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw "npm install basarisiz (cikis kodu $LASTEXITCODE)." }

    # Lock dosyasını Drive'a geri yaz: ekip aynı sürümleri kursun.
    $lock = Join-Path $dev 'package-lock.json'
    if (Test-Path $lock) { Copy-Item $lock -Destination $source -Force }
}
finally {
    Pop-Location
}

Write-Host "`nHAZIR. Gelistirme sunucusunu baslatmak icin:" -ForegroundColor Green
Write-Host "  cd `"$dev`"" -ForegroundColor Cyan
Write-Host "  npm run dev" -ForegroundColor Cyan
Write-Host "`nKodu Drive'daki frontend\src icinde duzenle; degisiklikler aninda yansir."
