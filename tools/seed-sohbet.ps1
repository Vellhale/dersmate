# Sohbet listesini gerçek veriyle doldurur: Ayşe ile 15 ayrı kullanıcı arasında
# eşleşme + karşılıklı mesajlaşma.
#
# NEDEN AYRI BETİK: seed-demo.ps1 akışların DOĞRU çalıştığını göstermek için minimum
# veriyi kuruyor (tek eşleşme, tek sohbet). Bu betik ise sohbet EKRANINA bakmak için
# hacim üretiyor — sıralama, okunmamış rozeti, uzun liste kaydırması ancak dolu bir
# listede görünür. İkisini birleştirmek, akış doğrulaması yapan betiği her koşuda
# gereksiz yere şişirirdi.
#
# ÖNCE seed-demo.ps1 çalışmış olmalı (Ayşe hesabı ve katalog oradan geliyor); yine de
# eksikse aşağıdaki portföy adımı Ayşe'nin sunduğu konuları kendisi ekliyor.
#
# TEKRAR KOŞULABİLİR: var olan sohbet yeniden kurulmaz, mesaj kopyalanmaz.

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

function Register($email, $name, $hwid) {
    try {
        $r = Api POST '/api/auth/register' @{ email = $email; password = 'Demo12345'; displayName = $name; termsVersion = '2026-08-27'; ageConfirmed = $true }
        Api POST '/api/auth/verify-email' @{ token = $r.verificationToken } | Out-Null
    } catch { }
    return (Api POST '/api/auth/login' @{ email = $email; password = 'Demo12345'; hwidHash = $hwid })
}

# SQL yolu: yerel psql varsa o, yoksa Docker konteyneri (seed-demo.ps1 ile aynı mantık).
$PSQL = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$YerelPsql = Test-Path $PSQL
$Compose = Join-Path (Split-Path $PSScriptRoot -Parent) 'docker-compose.yml'
$env:PGPASSWORD = 'PeerLearnDev2026'

function Sql($query) {
    if ($YerelPsql) {
        $f = Join-Path $env:TEMP ("pl_" + [Guid]::NewGuid().ToString('N') + ".sql")
        Set-Content -Path $f -Value $query -Encoding UTF8
        $out = & $PSQL -h localhost -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 -f $f 2>&1
        Remove-Item $f -Force -ErrorAction SilentlyContinue
    } else {
        $out = $query | docker compose -f $Compose exec -T db psql -U peerlearn -d peerlearn -t -A -v ON_ERROR_STOP=1 2>&1
    }
    return ($out -join '').Trim()
}

# ---------------------------------------------------------------- AYŞE + KONULAR

$ayse = Register 'ayse@demo.dev' 'Ayşe Yılmaz' ('a' * 64)
$ayseT = $ayse.accessToken
if (-not $ayseT) { throw 'ayse@demo.dev icin token alinamadi' }
Write-Host "ayse@demo.dev hazir"

$topics = Api GET '/api/catalog/topics'

# Konu adları müfredatla değişebiliyor; aday listesiyle ara (bkz. seed-demo.ps1).
function Konu($katalog, [string[]]$adaylar) {
    foreach ($ad in $adaylar) {
        $tam = @($katalog | Where-Object { $_.topic -eq $ad })
        if ($tam.Count -gt 0) { return $tam[0] }
    }
    foreach ($ad in $adaylar) {
        $bas = @($katalog | Where-Object { $_.topic -like ($ad + '*') })
        if ($bas.Count -gt 0) { return $bas[0] }
    }
    throw ("Katalogda konu bulunamadi: " + ($adaylar -join ' / '))
}

$turev    = Konu $topics @('Türev')
$integral = Konu $topics @('İntegral', 'Belirsiz İntegral')

# Eşleşme isteği, KARŞI TARAFIN o konuyu Offer etmesini şart koşuyor
# (CreateMatchRequestHandler). Ayşe bu iki konuyu sunmuyorsa 15 istek de 409 alırdı.
foreach ($k in @($turev, $integral)) {
    try {
        Api POST '/api/portfolio/entries' @{ topicId = $k.topicId; direction = 'Offer'; selfAssessedLevel = 5; note = $null } $ayseT | Out-Null
    } catch { }
}
Write-Host ("ayse portfoyu: " + $turev.topic + " + " + $integral.topic)

# ---------------------------------------------------------------- KİŞİLER + DİYALOGLAR
#
# `kim` alanı: 'o' = karşı taraf yazıyor, 'ayse' = Ayşe yanıtlıyor.
# Son mesajı 'o' olan sohbetler Ayşe için OKUNMAMIŞ kalır; son mesajı 'ayse' olanlarda
# Ayşe okundu işaretlenir. Okunmamış rozetinin gerçekten değiştiğini görmek için ikisi de
# listede olmalı — hepsi okunmamış olsaydı rozet bir şey ayırt etmiyor gibi görünürdü.
# Metinlerde KESME İŞARETİ YOK: tek tırnaklı dizgeyi kapatıp betiği sessizce bozuyor
# (PowerShell 5.1 tuzağı, bkz. CLAUDE.md).

$kisiler = @(
    @{ ad = 'Zeynep Aydın'; eposta = 'zeynep@demo.dev'; konu = 'turev'; mesajlar = @(
        @{ kim = 'o';    metin = 'Merhaba, zincir kuralinda surekli takiliyorum. Musait oldugunuz bir aksam var mi?' }
        @{ kim = 'ayse'; metin = 'Merhaba Zeynep, tabii. Sali aksami 20:00 uygun mu senin icin?' }
        @{ kim = 'o';    metin = 'Cok uygun, tesekkur ederim!' }
    )}
    @{ ad = 'Mert Doğan'; eposta = 'mert@demo.dev'; konu = 'integral'; mesajlar = @(
        @{ kim = 'o';    metin = 'Selam, belirsiz integralde kismi integrasyon konusunu calisiyorum ama sorularda hangisini u sececegimi bilemiyorum.' }
        @{ kim = 'ayse'; metin = 'Klasik sikinti. Bir tercih sirasi var, dersde onu gosterecegim. Once birkac soru cozup gonderir misin?' }
        @{ kim = 'o';    metin = 'Bu aksam cozup atarim.' }
        @{ kim = 'ayse'; metin = 'Harika, cozumleri gorunce nerede zorlandigini daha net anlarim.' }
    )}
    @{ ad = 'Elif Kaya'; eposta = 'elif@demo.dev'; konu = 'turev'; mesajlar = @(
        @{ kim = 'o';    metin = 'Merhaba, maksimum minimum sorularinda ikinci turev testini hic anlamadim.' }
        @{ kim = 'ayse'; metin = 'Merhaba Elif, grafik uzerinden anlatinca cok daha kolay oturuyor. Hafta sonu musait misin?' }
        @{ kim = 'o';    metin = 'Cumartesi ogleden sonra olur mu?' }
    )}
    @{ ad = 'Burak Yıldız'; eposta = 'burak@demo.dev'; konu = 'integral'; mesajlar = @(
        @{ kim = 'o';    metin = 'Hocam merhaba, alan hesabinda hangi egrinin ustte oldugunu bulmakta zorlaniyorum.' }
        @{ kim = 'ayse'; metin = 'Once kesisim noktalarini bulup araligi bolmek gerekiyor. Ders icinde uc ornekle netlestiririz.' }
    )}
    @{ ad = 'Selin Arslan'; eposta = 'selin@demo.dev'; konu = 'turev'; mesajlar = @(
        @{ kim = 'o';    metin = 'Selam! TYT denemelerinde turev sorularini hep bos birakiyorum, bastan anlatabilir misin?' }
        @{ kim = 'ayse'; metin = 'Elbette, sifirdan gidelim. Limit konusu tamam mi peki?' }
        @{ kim = 'o';    metin = 'Limit fena degil aslinda, sadece sureklilik kismi karisik geliyor.' }
        @{ kim = 'ayse'; metin = 'O zaman ilk 15 dakikayi surekliligi toparlamaya ayiralim, sonra turebe gecelim.' }
        @{ kim = 'o';    metin = 'Harika, bu plan cok iyi oldu.' }
    )}
    @{ ad = 'Emre Şahin'; eposta = 'emre@demo.dev'; konu = 'integral'; mesajlar = @(
        @{ kim = 'o';    metin = 'Merhaba, degisken degistirme yontemi icin kaynak onerir misin?' }
        @{ kim = 'ayse'; metin = 'Dersden sonra kendi notlarimi paylasabilirim, konu anlatimi ve 20 soruluk bir set var.' }
        @{ kim = 'o';    metin = 'Cok makbule gecer, tesekkurler.' }
    )}
    @{ ad = 'Deniz Koç'; eposta = 'deniz@demo.dev'; konu = 'turev'; mesajlar = @(
        @{ kim = 'o';    metin = 'Merhaba, bu hafta icin uygun bir saatiniz var mi acaba?' }
    )}
    @{ ad = 'Ceren Aksoy'; eposta = 'ceren@demo.dev'; konu = 'integral'; mesajlar = @(
        @{ kim = 'o';    metin = 'Selam, belirli integralde sinir degistirmeyi unutuyorum surekli, cok hata yapiyorum.' }
        @{ kim = 'ayse'; metin = 'Cok yaygin bir hata. Degisken degistirirken sinirlari da yazma aliskanligi kazandiracagim.' }
        @{ kim = 'o';    metin = 'Umarim, deneme puanimi bu yuzden kaybediyorum.' }
    )}
    @{ ad = 'Kerem Polat'; eposta = 'kerem@demo.dev'; konu = 'turev'; mesajlar = @(
        @{ kim = 'o';    metin = 'Merhaba, ozel tanimli fonksiyonlarin turevinde parcali kisimda ne yapacagimi bilemiyorum.' }
        @{ kim = 'ayse'; metin = 'Kritik nokta soldan ve sagdan turevin esitligi. Bunu birkac ornekle oturtalim.' }
        @{ kim = 'o';    metin = 'Anladim, cumartesi icin bir saat ayarlayalim mi?' }
        @{ kim = 'ayse'; metin = 'Cumartesi 14:00 bende uygun, rezervasyonu acabilirsin.' }
    )}
    @{ ad = 'Melis Erdem'; eposta = 'melis@demo.dev'; konu = 'integral'; mesajlar = @(
        @{ kim = 'o';    metin = 'Merhabalar, hacim hesaplarina da giriyor musunuz derste?' }
        @{ kim = 'ayse'; metin = 'Evet, donel cisimlerin hacmi de dahil. Ama once alan hesabini saglamlastirmak lazim.' }
    )}
    @{ ad = 'Onur Çelik'; eposta = 'onur@demo.dev'; konu = 'turev'; mesajlar = @(
        @{ kim = 'o';    metin = 'Selam, gecen sene AYT de turevden hic net yapamadim. Sifirdan calismak istiyorum.' }
        @{ kim = 'ayse'; metin = 'Merhaba Onur, sifirdan kurmak aslinda daha saglikli oluyor. Haftada iki gun ayirabilir misin?' }
        @{ kim = 'o';    metin = 'Ayirabilirim, sali ve persembe olabilir.' }
    )}
    @{ ad = 'Buse Yalçın'; eposta = 'buse@demo.dev'; konu = 'integral'; mesajlar = @(
        @{ kim = 'o';    metin = 'Merhaba, ders ucreti nasil isliyor tam anlayamadim.' }
        @{ kim = 'ayse'; metin = 'Ders alan hicbir sey odemiyor, puan anlatan tarafa basiliyor. Yani senin icin ucretsiz.' }
        @{ kim = 'o';    metin = 'Cok mantikli, tesekkurler. O zaman hemen bir ders acalim.' }
        @{ kim = 'ayse'; metin = 'Tabii, uygun bir saat sec yeter.' }
    )}
    @{ ad = 'Efe Taş'; eposta = 'efe@demo.dev'; konu = 'turev'; mesajlar = @(
        @{ kim = 'o';    metin = 'Merhaba, turev uygulamalarindan hiz ivme sorulari da anlatiyor musunuz?' }
        @{ kim = 'ayse'; metin = 'Evet, fizikle kesisen kisim tam benim sevdigim yer. Onu ayri bir derse alalim.' }
        @{ kim = 'o';    metin = 'Super, iki ders ayarlayalim o zaman.' }
    )}
    @{ ad = 'Naz Güneş'; eposta = 'naz@demo.dev'; konu = 'integral'; mesajlar = @(
        @{ kim = 'o';    metin = 'Selam, dersler kac dakika suruyor?' }
        @{ kim = 'ayse'; metin = 'Genelde 60 dakika, ama 30 dakikalik kisa tekrar dersleri de acabiliyorum.' }
        @{ kim = 'o';    metin = 'Once 30 dakikalik bir tanesiyle deneyelim o zaman.' }
    )}
    @{ ad = 'Tolga Ateş'; eposta = 'tolga@demo.dev'; konu = 'turev'; kapat = $true; mesajlar = @(
        @{ kim = 'o';    metin = 'Merhaba, gecen donem yardimlariniz icin tekrar tesekkurler. Sinavi kazandim!' }
        @{ kim = 'ayse'; metin = 'Cok sevindim Tolga, tebrik ederim! Basarilar dilerim.' }
    )}
)

# ---------------------------------------------------------------- SOHBETLERİ KUR

# Ayşe'nin mevcut sohbetleri: aynı kişiyle ikinci bir sohbet kurulmaya çalışılmasın.
$mevcut = @{}
foreach ($s in (Api GET '/api/conversations' $null $ayseT)) {
    $mevcut[[string]$s.otherUserId] = $s.conversationId
}

$yeniSayisi = 0
$sira = 0

foreach ($kisi in $kisiler) {
    $sira++

    # HWID 64 karakter olmalı ve kullanıcı başına AYRI olmalı (ban kaydı kullanıcıya değil
    # cihaza yazılıyor; aynı HWID paylaşan hesaplar birbirini etkiler).
    $hwid = ('{0:d2}' -f $sira) * 32
    $u = Register $kisi.eposta $kisi.ad $hwid
    $uid = [string]$u.userId

    if ($mevcut.ContainsKey($uid)) {
        Write-Host ("{0,2}. {1} - sohbet zaten var, atlandi" -f $sira, $kisi.ad)
        continue
    }

    $konuId = if ($kisi.konu -eq 'turev') { $turev.topicId } else { $integral.topicId }

    # Yarım kalmış bir koşumdan bekleyen istek kalmış olabilir; varsa onu kullan.
    $ayseMatches = Api GET '/api/matches' $null $ayseT
    $gelen = @($ayseMatches.incoming | Where-Object { [string]$_.otherUserId -eq $uid })

    if ($gelen.Count -gt 0) {
        $matchId = $gelen[0].matchId
    } else {
        $matchId = Api POST '/api/matches' @{ responderUserId = $ayse.userId; requestedTopicId = $konuId; offeredTopicId = $null } $u.accessToken
    }

    $resp = Api POST "/api/matches/$matchId/respond" @{ accept = $true } $ayseT
    $convId = $resp.conversationId

    foreach ($m in $kisi.mesajlar) {
        $token = if ($m.kim -eq 'ayse') { $ayseT } else { $u.accessToken }
        Api POST "/api/conversations/$convId/messages" @{ content = $m.metin } $token | Out-Null
    }

    # Son sözü Ayşe söylediyse, önceki mesajları okumuş sayılır.
    if ($kisi.mesajlar[-1].kim -eq 'ayse') {
        Api POST "/api/conversations/$convId/read" $null $ayseT | Out-Null
    }

    <#
      ZAMANLARI GEÇMİŞE YAY.

      Tüm mesajlar API üzerinden saniyeler içinde yazılıyor; hepsinin zamanı "şimdi"
      olurdu ve liste rastgele sıralanmış gibi görünürdü — oysa ekranın asıl davranışı
      son mesaja göre sıralamak. Sohbet başına ~95 dakika geriye kaydırıp, sohbet
      içindeki mesajları da 6 dakika arayla diziyoruz.
    #>
    $taban = 25 + (($sira - 1) * 95)
    Sql @"
WITH s AS (
  SELECT "Id", row_number() OVER (ORDER BY "CreatedAtUtc", "Id") AS n, count(*) OVER () AS t
  FROM comms."Messages" WHERE "ConversationId" = '$convId'
)
UPDATE comms."Messages" m
SET "CreatedAtUtc" = now() - (interval '1 minute' * ($taban + (s.t - s.n) * 6))
FROM s WHERE m."Id" = s."Id";

UPDATE comms."Conversations" c
SET "LastMessageAtUtc" = (SELECT max("CreatedAtUtc") FROM comms."Messages" WHERE "ConversationId" = c."Id")
WHERE c."Id" = '$convId';
"@ | Out-Null

    # Kapatılmış eşleşme: sohbet salt okunur olur. Listede bu durumun da bir örneği olsun.
    if ($kisi.kapat) {
        Api POST "/api/matches/$matchId/close" $null $ayseT | Out-Null
        Write-Host ("{0,2}. {1} - sohbet kuruldu ve eslesme KAPATILDI (salt okunur)" -f $sira, $kisi.ad)
    } else {
        Write-Host ("{0,2}. {1} - sohbet kuruldu ({2} mesaj)" -f $sira, $kisi.ad, $kisi.mesajlar.Count)
    }

    $yeniSayisi++
}

$toplam = @(Api GET '/api/conversations' $null $ayseT).Count
$okunmamis = @(Api GET '/api/conversations' $null $ayseT | Where-Object { $_.unreadCount -gt 0 }).Count

Write-Host ""
Write-Host "TAMAMLANDI" -ForegroundColor Green
Write-Host ("  yeni kurulan sohbet : {0}" -f $yeniSayisi) -ForegroundColor Green
Write-Host ("  ayse toplam sohbet  : {0}" -f $toplam) -ForegroundColor Green
Write-Host ("  okunmamisi olan     : {0}" -f $okunmamis) -ForegroundColor Green
Write-Host ""
Write-Host "Giris: ayse@demo.dev / Demo12345  ->  http://localhost:5173" -ForegroundColor Cyan
Write-Host "Karsi taraflarin sifresi da Demo12345 (ornek: zeynep@demo.dev)" -ForegroundColor Cyan
