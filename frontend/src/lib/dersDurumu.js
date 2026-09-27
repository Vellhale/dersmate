/*
  DERS DURUMU — "bu ders benden bir şey bekliyor mu?" ve "Derslerim'in hangi sekmesinde
  durur?" sorularının TEK tanımı.

  ⚠️ İKİ DEPODA BAYT BAYT AYNI DOSYA: web `frontend/src/lib/dersDurumu.js` ↔ mobil
  `src/lib/dersDurumu.js` (format.js ve seviye.js'teki "birebir kopya" kuralı). Biri
  değişirse öteki AYNI GÜN değişir; iki dosyanın `diff`'i boş çıkmalı. Bu yüzden içinde
  platforma özgü hiçbir şey yok (bileşen, depolama, ağ): saf JS, yalnızca ders nesnesine
  (SessionListItemDto) bakıyor.

  Aynı soruyu soran yerler:
    • Derslerim'deki "Senden aksiyon bekleyenler" sekmesi ve hapındaki sayaç (iki istemci),
    • mobilde çekmecedeki Derslerim sayacı (src/lib/bekleyenIsler.js).
  Tanım iki yerde ayrı yazılsaydı biri değişip öteki kaldığında rozet "2" derken ekranda
  tek kart olurdu; kullanıcı bekleyen işi aramaya başlar ve bulamaz. 2026-09-26'ya kadar
  tam olarak bu yaşanıyordu, istemciler arasında: web itirazdaki dersi sayıyor, eğitmenin
  saati geçmiş Booked dersini saymıyordu; mobil tersini yapıyordu. Aynı hesap iki
  istemcide iki ayrı sayı görüyordu.

  Sunucu bayrakları (canApprove, canComplete) istek anındaki saate göre hesaplanıyor.
  Eğitmenin Booked dersi ise liste çekildikten SONRA da bitiş saatini geçebilir: o an
  "Dersi tamamladım" açılıyor (ders kartının iyimser completeReady'si: web SessionCard,
  mobil SessionKarti), yani ders sayfa yenilenmeden aksiyon grubuna geçmeli. `simdi` bu
  yüzden parametre.

  Disputed DAHİL DEĞİL: itirazdaki derste karar yönetimde, kullanıcının basabileceği bir
  düğme yok. Sayaca girseydi "işlem bekliyor" diyen rozet kullanıcıyı yapamayacağı bir işe
  çağırır ve karar gelene kadar sönmezdi. Derslerim onları aksiyon sekmesinde ama AYRI
  başlıkta ("İtirazda, karar yönetimde") gösteriyor — bkz. dersSekmesi.
*/
export function eylemBekliyor(s, simdi = Date.now()) {
  if (s.canApprove || s.canComplete) return true
  return s.iAmTutor && s.status === 'Booked' && new Date(s.scheduledEndUtc).getTime() <= simdi
}

/*
  DERSİN SEKMESİ — Derslerim'in beş sekmesinden hangisinde durduğu.

    'aksiyon'     senden bir şey bekliyor (eylemBekliyor) YA DA itirazda. İtiraz sayaca
                  girmez ama aynı sekmede, kendi başlığı altında durur: açılmış bir itiraz
                  kullanıcının gündemindedir, "Planlanmış" bir ders değildir.
    'planlanmis'  kalan aktif dersler: senden iş beklemeyen Booked / AwaitingApproval.
                  Saati geçmiş olanlar da burada ("Saati geçti, hâlâ açık" başlığıyla).
    'gecmis'      Completed. "Geçmiş dersler" YALNIZCA tamamlananlar (kullanıcı kararı).
    'rezerve'     Cancelled / Expired. Başka sekmede görünmezler.

  'rezerve' sekmesi aslında TÜM dersleri listeler (Rezerve geçmişi). Bu fonksiyonun
  'rezerve' demesi "başka hiçbir sekmede yok, kaydı orada bulursun" demektir; bildirimden
  gelen dersin (mobil ?ders=) hangi sekmeyi açacağı buradan okunuyor.

  'puan' hiçbir dersin sekmesi değil: o sekme puan defteri.
*/
export function dersSekmesi(s, simdi = Date.now()) {
  if (s.status === 'Completed') return 'gecmis'
  if (s.status === 'Cancelled' || s.status === 'Expired') return 'rezerve'
  if (s.status === 'Disputed' || eylemBekliyor(s, simdi)) return 'aksiyon'
  return 'planlanmis'
}

/*
  REZERVE GEÇMİŞİ SIRASI — sunucunun geçmiş sırasının istemcideki birebir karşılığı:
  ScheduledStartUtc AZALAN, eşitlikte ders kimliği AZALAN (GetMySessions:
  OrderByDescending(ScheduledStartUtc).ThenByDescending(Id)).

  Kimlik karşılaştırması METİN üzerinden ve bu bilinçli: PostgreSQL uuid'i 16 bayt olarak
  bayt bayt karşılaştırıyor; standart küçük harfli onaltılık yazımın (tireler sabit
  konumda) sözlük sırası aynı sonucu veriyor. Büyük harfli bir kimlik gelirse sıra
  bozulmasın diye küçültülüyor. Sıra yanlış olsa bile kayıt KAYBOLMAZ (birleşim
  tekilleştiriyor), yalnızca sınırdaki bir kayıt bir sayfa geç görünür.

  Geçersiz tarih (NaN) karşılaştırmayı bozmaz: fark sayı değilse kimliğe düşülür.
*/
export function dersSirasi(a, b) {
  const fark = new Date(b.scheduledStartUtc).getTime() - new Date(a.scheduledStartUtc).getTime()
  if (fark !== 0 && !Number.isNaN(fark)) return fark
  const ka = String(a.sessionId).toLowerCase()
  const kb = String(b.sessionId).toLowerCase()
  return ka < kb ? 1 : ka > kb ? -1 : 0
}

/*
  REZERVE GEÇMİŞİ BİRLEŞİMİ — iki rolde, durumundan bağımsız bütün rezervasyonlar, ders
  tarihine göre (yeni → eski) TEK liste. Yeni bir sunucu ucu gerektirmiyor:

    aktifler  GET /sessions → `active`. TAM gelir (en fazla 100; fazlası activeTotal ile
              bildirilir, çağıran kesme uyarısını gösterir).
    gecmis    `past.items` sayfalarının birikintisi (sunucu sırasıyla, sayfa sayfa).
    dahaVar   son yüklenen geçmiş sayfasının hasNextPage'i.

  SINIR KURALI: geçmiş sayfalı geldiği için, henüz yüklenmemiş geçmiş sayfalarının
  tarih aralığına düşen bir aktif ders şimdi gösterilirse, o sayfalar yüklendiğinde
  listenin ORTASINA kayıt eklenir ve kullanıcının okuduğu yer kayar. Bu yüzden daha
  sayfa varken yalnızca yüklenmiş en eski geçmiş kaydından YENİ (ya da ona eşit) aktif
  dersler gösterilir; daha eskileri sayfalar ilerledikçe kendi yerinde belirir (ör. üç
  hafta önceki bir itiraz). Sayfa kalmadıysa aktiflerin tamamı gösterilir.

  TEKİLLEŞTİRME ders kimliğiyle ve ÇAKIŞMADA GEÇMİŞ KAZANIR: durum yalnızca aktiften
  nihaiye gider (Booked → Completed / Cancelled / Expired), tersi yok. Aktif liste ilk
  yüklemeden, geçmiş sayfası sonradan geldiği için ikisinde birden görünen ders geçmişte
  güncel hâliyle durur. Ofset sayfalamasında aynı kayıt iki sayfada da gelebilir
  (arada geçmişe yeni kayıt girerse); o da burada tekilleşir.

  Numaralı sayfa YAPILAMAZ: aktif liste sayfalı geçmişin arasına serpiştiği için n.
  sayfanın sınırı önceki sayfalar yüklenmeden hesaplanamaz. İki istemci de "daha eski"
  ile biriktiriyor.
*/
export function rezervasyonBirlesimi(aktifler, gecmis, dahaVar) {
  const gorulen = new Set()
  const liste = []

  for (const s of gecmis) {
    if (gorulen.has(s.sessionId)) continue
    gorulen.add(s.sessionId)
    liste.push(s)
  }

  // Sınır, yüklenmiş geçmişin sıraya göre EN SONUNDAKİ kaydı (sunucu sırasında zaten
  // sonuncusu; yine de ölçülerek bulunuyor, gelen sıraya güvenilmiyor).
  let sinir = null
  if (dahaVar) {
    for (const s of liste) if (!sinir || dersSirasi(s, sinir) > 0) sinir = s
  }

  for (const s of aktifler) {
    if (gorulen.has(s.sessionId)) continue
    if (sinir && dersSirasi(s, sinir) > 0) continue
    gorulen.add(s.sessionId)
    liste.push(s)
  }

  return liste.sort(dersSirasi)
}
