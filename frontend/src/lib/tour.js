/**
 * Rehber adımları (Modül 5).
 *
 * Amaç: yeni gelen öğrenciyi ürünün akışında bir kez baştan sona gezdirmek — "bu bana
 * neye mal olacak", "aradığımı nasıl bulurum", "anlaştıktan sonra ne oluyor", "karşı
 * taraf işini yapmazsa ne olur".
 *
 * ─── BİÇİM: KISA CÜMLE + MADDELER (2026-08-24) ───────────────────────────────────
 * Her adım tek bir yoğun paragraftı; kart altı satır metinle doluyor ve kullanıcı
 * okumadan "Devam"a basıyordu. Sahibin isteği "daha sade ama daha detaylı" idi ve
 * ikisi çelişmiyor: sadeleşen BİÇİM, detaylanan KAPSAM.
 *
 *   • `body` tek cümle — adımın tek cümlelik özeti. Kullanıcı yalnızca bunu okusa bile
 *     adımı anlamış olmalı.
 *   • `points` 2–3 kısa madde — ayrıntı burada. Göz taramayla ilerliyor, paragrafta
 *     ilerlemiyordu.
 *
 * Adım sayısı 4'ten 6'ya çıktı. Eskiden dördüncü adım sohbeti, kanıtı, onayı ve itirazı
 * TEK paragrafta anlatıyordu — turun en yoğun ve en atlanan yeriydi. Şimdi eşleşme,
 * sohbet ve kanıt ayrı adımlar; her biri kendi menü öğesinin üstünde duruyor.
 *
 * BAŞLIKLARDA EMOJİ YOK. İlk adım "Ders almak ücretsiz 🌱" idi. Aynı gerekçe rozetlerde
 * de uygulandı (bkz. SubjectBadges): emoji her platformda başka çiziliyor, aynı ekran
 * her cihazda başka görünüyor. Vurgu için BÜYÜK HARF de kullanılmıyor — "ders ALMAK"
 * bağırma gibi okunuyordu; ayrımı cümlenin kendisi taşıyor.
 * ─────────────────────────────────────────────────────────────────────────────────
 *
 * EKONOMİ DEĞİŞTİ, REHBER DE DEĞİŞTİ. Önceki metinler kredi takasını anlatıyordu:
 * "kazandığın krediyle ders alırsın", "kayıt olurken 1 kredi hediye edildi",
 * "rezervasyonda kredin bloke edilir". Üçü de artık yanlış — ders almak ücretsiz, bloke
 * edilen bir şey yok ve puan yalnızca ANLATAN tarafa yazılıyor. Yanlış beklenti kuran
 * bir rehber, hiç rehber olmamasından kötüdür.
 *
 * SAYI YAZILMIYOR. Ne puan eşikleri (UserLevel.cs) ne blok başına basılan puan
 * (SessionRules.MintPerBlock) buraya yazıldı: kural değişince rehberi güncellemeyi
 * kimse hatırlamaz ve rehber sessizce yalan söylemeye başlar. Anlatılan tek şey
 * mekanizma. "10 basamaklı" bir istisna: o, ölçeğin kendisi, eşiği değil.
 *
 * selector: rehberin ışık tutacağı öğe. Bulunamazsa adım ORTADA kart olarak gösterilir
 * (bkz. ProductTour). Bu sayede ekran boyutuna göre gizlenen öğeler rehberi KIRMAZ —
 * mobilde gezinme hamburgere dönüştüğü için bu gerçek bir durum.
 *
 * Çıpalar Layout'taki NAV tanımından ve seviye rozetinden gelir; buradaki her
 * selector'ın karşılığı orada VARDIR. (Eskiden ilk adım `[data-tour="wallet"]`
 * arıyordu ama cüzdan rozeti kaldırılıp yerine `rank` konmuştu — yani ilk adım sessizce
 * çıpasız kalmıştı.) Rozetin içeriği unvandan seviyeye dönerken `data-tour="rank"` adı
 * BİLEREK korundu: çıpa adını değiştirmek aynı sessiz kırılmayı bir kez daha üretirdi.
 */
export const TOUR_STEPS = [
  {
    id: 'free',
    selector: '[data-tour="rank"]',
    title: 'Ders almak ücretsiz',
    body: 'Burada para yok, harcadığın bir kredi de yok. Puanı ders anlatarak kazanırsın.',
    points: [
      'Ders almak her zaman ücretsiz — bakiyenden bir şey düşmez.',
      'Puan, anlattığın dersin onaylandığı anda yazılır.',
      'Biriken puan seviyeni yükseltir; ölçek 10 basamaklı.',
    ],
  },
  {
    id: 'discover',
    selector: '[data-tour="discover"]',
    title: 'Keşfet — ders bul',
    body: 'Almak istediğin konuyu anlatabilen öğrencileri burada bulursun.',
    points: [
      'Konu, ders ya da eğitmen adıyla ara.',
      'Filtreyle sıralamayı ve eğitmen puanını daralt.',
      '“Karşılıklı takas” etiketi, o kişinin de senden bir konu aradığını gösterir.',
    ],
  },
  {
    id: 'portfolio',
    selector: '[data-tour="portfolio"]',
    title: 'Ders Portföyü — ne anlatabilirsin',
    body: 'Anlatabildiğin konuları ekle; Keşfet’te başkalarına böyle görünürsün.',
    points: [
      'Portföyün boşken kimse senden ders isteyemez.',
      'Her konu için kendi seviyeni işaretlersin.',
      'Puan kazanmanın tek yolu ders anlatmak — başlangıcı burası.',
    ],
  },
  {
    id: 'matches',
    selector: '[data-tour="matches"]',
    title: 'Eşleşmeler — istek gönder ve al',
    body: 'Gönderdiğin ve sana gelen ders istekleri bu sayfada toplanır.',
    points: [
      'Gelen bir isteği kabul ya da reddedersin.',
      'Kabul edilen istekte sohbet kendiliğinden açılır.',
      'Eşleşmeyi istediğin an sonlandırabilirsin.',
    ],
  },
  {
    id: 'chat',
    selector: '[data-tour="chat"]',
    title: 'Sohbet — saati ve linki kararlaştır',
    body: 'Ders saatini ve görüşme linkini karşı tarafla burada konuşursun.',
    points: [
      'Zoom, Google Meet ya da Discord — dersi biz barındırmıyoruz.',
      'Linki sohbete yapıştırman yeterli.',
      'Anlaştıktan sonra dersi Derslerim’den rezerve edersiniz.',
    ],
  },
  {
    id: 'sessions',
    selector: '[data-tour="sessions"]',
    title: 'Derslerim — kanıt ve onay',
    body: 'Ders bittikten sonra puanın yazılması için tek bir adım kalır: onay.',
    points: [
      'Anlatan taraf dersin ekran görüntüsünü yükler.',
      'Alan taraf onaylar; puan tam o anda yazılır.',
      'Bir sorun varsa itiraz edersin, kararı hakem verir.',
    ],
  },
]

export const TOUR_STEP_COUNT = TOUR_STEPS.length
