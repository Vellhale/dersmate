# Yürürlükteki yasal metin sürümünü yazdırır.
#
# NEDEN VAR: kayıt ucu `termsVersion` alanını sunucudaki sabitle BİREBİR karşılaştırıyor
# (Domain/Identity/LegalDocuments.cs) ve tutmazsa kaydı reddediyor. Bu, test ve seed
# betiklerinin tamamını sürüm sabitine bağımlı kılıyor.
#
# ⛔ ÖNCEDEN 24 BETİK TARİHİ ELLE YAZIYORDU. Yasal metin ilk kez güncellendiğinde
# (2026-09-05) hepsi aynı anda kırılacaktı: kayıt VALIDATION_FAILED döner, testler
# hazırlık adımında düşer ve hata mesajı "termsVersion" demediği için sebebi
# betiklerde aranırdı — oysa hiçbiri bozuk değil.
#
# Tek kaynak arayüzdeki sabit; sunucu zaten onunla eşit olmak ZORUNDA (ayrışırlarsa
# hiç kimse kayıt olamaz, bkz. LegalDocuments). Yani buradan okumak, sunucudan
# okumakla aynı değeri verir ve API'nin ayakta olmasını gerektirmez.
#
# Kullanım (betik içinden, ek kurulum gerekmez):
#   termsVersion = (& "$PSScriptRoot\yasal-surum.ps1")

$ErrorActionPreference = 'Stop'

$kaynak = Join-Path (Split-Path $PSScriptRoot -Parent) 'frontend\src\lib\yasalMetinler.js'
if (-not (Test-Path $kaynak)) {
    throw "Yasal sürüm kaynağı bulunamadı: $kaynak"
}

$eslesme = Select-String -Path $kaynak -Pattern "SOZLESME_SURUMU\s*=\s*'([^']+)'"
if (-not $eslesme) {
    # Sessizce boş dönmek, testleri anlaşılmaz bir 400 ile düşürürdü.
    throw "SOZLESME_SURUMU $kaynak içinde bulunamadı — sabitin adı mı değişti?"
}

$eslesme.Matches[0].Groups[1].Value
