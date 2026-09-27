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
 * TEK paragrafta anlatıyordu — turun en yoğun ve en atlanan yeriydi. Şimdi arkadaşlık,
 * sohbet ve kanıt ayrı adımlar; her biri kendi menü öğesinin üstünde duruyor.
 *
 * ─── 6 → 8 ADIM (2026-09-26) ─────────────────────────────────────────────────────
 * Topluluk ve Profil adımları eklendi (ürün kararı: rehber ürünün SON hâlini anlatsın).
 * Topluluk'un çıpası menüde zaten vardı, adımı yoktu; Profil adımı sağ üstteki ayarlar
 * (dişli) menüsünü ve fotoğrafın altındaki "Arkadaşlarım"ı anlatıyor.
 *
 *   • YENİ ADIMLAR SONA EKLENDİ, araya DEĞİL. Sunucu kaldığın adımı SAYI olarak tutuyor
 *     (onboardingLastStep); ilk altı adımın indeksi aynı kaldığı için yarıda bırakan
 *     kullanıcı kaldığı adımdan devam ediyor. Araya eklenen bir adım herkesi bir adım
 *     kaydırırdı.
 *   • Rehber SÜRÜMÜ tutulmuyor: eski rehberi bitirmiş kullanıcıya yeni adımlar
 *     kendiliğinden gösterilmez, "Rehberi tekrar izle" ile görür. Tamamlanma bir sürüm
 *     değil bir bayrak (UserPreference.OnboardingCompleted).
 *   • Mobil rehber AYRI bir dizi (mobil src/lib/tur.js: 9 adım, Topluluk ve menü başta,
 *     bildirimler sonda). Ortak adımların metni iki platformda aynı; SIRA ve SAYI farklı.
 *     İlerleme satırı hesapta TEK olduğu için aynı indeks iki platformda farklı adımı
 *     gösterir — bilinen, düşük etkili sınır (en kötüde birkaç adım erken ya da geç
 *     devam edilir).
 *   • Web'de push yok: mobildeki bildirim adımı ve bildirim maddeleri BURADA YOK.
 *   • Metinler ÇIPAYA DAYANMIYOR ("burada bulursun" değil "menüdeki “Keşfet”te
 *     bulursun"): lg altında ray gizli ve kart ortada çıkıyor; neyi anlattığını cümle
 *     kendisi söylemek zorunda.
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
 * selector: rehberin ışık tutacağı öğe — tek seçici ya da TERCİH SIRASIYLA seçici dizisi.
 * GÖRÜNÜR bir karşılığı yoksa adım ORTADA kart olarak gösterilir (ProductTour →
 * hedefBul). Bu sayede ekran boyutuna göre gizlenen öğeler rehberi KIRMAZ — lg altında
 * ray gizli ve gezinme hamburgerde.
 *
 * ⚠️ Bu cümle 2026-09-26'ya kadar YANLIŞTI. Tek querySelector gizli raydaki öğeyi de
 * buluyordu (`display: none` öğe DOM'da duruyor) ve onun sıfır dikdörtgenini "çıpa
 * var" diye ölçüyordu: 375 ve 800px'te 2–6. adımlarda sol üst köşede 16px'lik bir
 * halka, 800px'te bir de köşeye yapışmış kart çıkıyordu (ölçüldü). hedefBul artık
 * yalnızca kutusu olan öğeyi kabul ediyor (getClientRects().length > 0).
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
    body: 'Almak istediğin konuyu anlatabilen öğrencileri menüdeki “Keşfet”te bulursun.',
    points: [
      'YKS sekmesi, almak istediğin konulara göre sana öneri getirir; yazarak katalogda da ararsın.',
      'Üniversite ve Arkadaş Ekle sekmeleriyle okulundan ya da tanıdığın birini bulursun.',
      '“Karşılıklı takas” etiketi, o kişinin de senden bir konu aradığını gösterir.',
    ],
  },
  {
    id: 'portfolio',
    selector: '[data-tour="portfolio"]',
    title: 'Ders Portföyü — ne anlatabilirsin',
    body: 'Anlatabildiğin konuları ekle; Keşfet’te başkalarına böyle görünürsün.',
    points: [
      'Almak istediğin konuları da buraya eklersin; öneriler onlardan üretilir.',
      'Portföyün boşken kimse senden ders isteyemez.',
      // "Tek yolu" DEĞİL (2026-09-26): Topluluk'ta oy toplayan katkılar da puan getiriyor
      // (Kullanım koşulları §3; sunucuda CommunityRewardRules).
      'Puanın asıl kaynağı ders anlatmak — başlangıcı burası.',
    ],
  },
  {
    id: 'matches',
    selector: '[data-tour="matches"]',
    title: 'Arkadaşlar — istek gönder ve al',
    body: 'Gönderdiğin ve sana gelen ders istekleri menüdeki “Arkadaşlar”da toplanır.',
    points: [
      'Gelen bir isteği kabul ya da reddedersin.',
      'Kabul edilen istekte sohbet kendiliğinden açılır.',
      'Arkadaşlığı istediğin an sonlandırabilirsin.',
    ],
  },
  {
    id: 'chat',
    selector: '[data-tour="chat"]',
    title: 'Sohbet — saati ve linki kararlaştır',
    body: 'Ders saatini ve görüşme linkini karşı tarafla menüdeki “Sohbet”te konuşursun.',
    points: [
      'Zoom, Google Meet ya da Discord — dersi biz barındırmıyoruz.',
      'Linki sohbete yapıştırman yeterli.',
      // Rezervasyonu yapan ÖĞRENCİ olur (BookSession: çağıran, StudentUserId). Eski metin
      // "rezerve edersiniz" diyordu ve iki taraf da rezerve edebilir gibi okunuyordu.
      'Anlaştığınızda dersi alan taraf Derslerim’den rezerve eder.',
    ],
  },
  {
    id: 'sessions',
    selector: '[data-tour="sessions"]',
    title: 'Derslerim — rezervasyon, kanıt ve onay',
    // Sekme adları Sessions.jsx → SEKMELER ile aynı: rehberin söylediği ad ekranda yazan
    // adla aynı olmalı. Sekmeler yeniden adlandırılırsa bu adım aynı değişiklikte değişir.
    body: 'Her dersin burada; planlanmış, geçmiş dersler, puan ve rezerve geçmişi ayrı sekmelerde.',
    points: [
      '“Senden aksiyon bekleyenler”, kanıt yüklemen ya da onaylaman gereken dersleri toplar.',
      'Anlatan taraf dersin ekran görüntüsünü yükler, alan taraf onaylar; puan o anda yazılır.',
      'Onay gelmezse ders kendiliğinden onaylanır; sorun varsa itiraz edersin, kararı yönetim verir.',
    ],
  },
  {
    id: 'community',
    selector: '[data-tour="community"]',
    title: 'Topluluk — öğrencilerin sosyal ağı',
    body: 'Menüdeki “Topluluk”, dersmate’in sosyal ağı: soru sorar, deneyimini paylaşır, başkalarına yanıt verirsin.',
    points: [
      'Gönderini bir etiketle paylaş; yalnızca metin, dosya yükleme kapalı.',
      'Gönderileri oyla, altına yorum yaz.',
      'Kurallara aykırı bir içerik görürsen şikayet et.',
    ],
  },
  {
    id: 'profil',
    /* Tercih sırası: lg'de üst bardaki avatar (`profil`); lg altında avatar gizli olduğu
       için seviye rozeti (`rank`). İkisi de profile götürüyor. Avatar çıpası yoksa ya da
       gizliyse hedefBul rozete düşer, ikisi de yoksa kart ortada çıkar. */
    selector: ['[data-tour="profil"]', '[data-tour="rank"]'],
    title: 'Profilin ve ayarların',
    body: 'Üst çubuktaki seviye rozetin ya da fotoğrafın seni profiline götürür.',
    points: [
      'Profilini ve fotoğrafını sağ üstteki ayarlar (dişli) menüsünden düzenlersin.',
      'Arkadaşlarına fotoğrafının altındaki “Arkadaşlarım”dan ulaşırsın.',
      'Bu rehberi ayarlar menüsünden ya da sayfanın altından yeniden izleyebilirsin.',
    ],
  },

  /* ─── 8 → 13 ADIM (2026-09-27): AYRINTI YENİ ADIMLARA, MOBİLLE BİREBİR ────────────
     Kullanıcı kararı: ürün karmaşık, rehber daha ayrıntılı anlatsın. Beş adım eklendi
     ve İLK ALTI ADIMIN metnine DOKUNULMADI — mobil src/lib/tur.js ile bayt pariteti
     korundu. Bu beşinin title/body/points'i de mobille BİREBİR AYNI; biri değişirse
     öteki AYNI GÜN değişir (mobilde ek olarak `yer` alanı var, burada `selector`).

     YİNE SONA EKLENDİ, araya DEĞİL: `onboardingLastStep` sunucuda tek sayı ve araya
     eklenen adım turu yarıda bırakan herkesi kaydırırdı. Mobil araya ekleyebildi
     (mağazada henüz uygulama yok); web CANLIDA, o serbestlik burada yok.
     Sonuç: anlatım sırası mobildekinden farklı — bilinçli, indeks korunması uğruna.

     ⚠️ Mobildeki `menu` adımının "hamburger rozeti üçünün toplamı" maddesi BURAYA
     TAŞINMADI ve taşınmamalı: webde hamburgerde sayı değil nokta var, yalnızca
     okunmamış mesaj sayıyor ve düğme geniş ekranda çekmeceyi bile açmıyor.

     ⚠️ `rules` adımının üçüncü maddesi bilerek SAYFA ADI VERMİYOR: mobilde kurallar
     bir alt sayfada, burada Topluluk sayfasının yan sütunundaki iki kartta. Tarafsız
     ifade sayesinde madde iki platformda birebir kalabiliyor.
     ───────────────────────────────────────────────────────────────────────────── */
  {
    id: 'requests',
    selector: '[data-tour="matches"]',
    title: 'İstek gönderdikten sonra',
    body: 'Gönderdiğin bir isteğin üç farklı sonu var ve ikisi sessiz.',
    points: [
      'Kabul edilirse sohbet açılır ve kişi “Arkadaşlar”da görünür.',
      'Reddedilirse sana bildirilmez; istek listeden sessizce kalkar.',
      'Yanıtsız kalan istek bir süre sonra kendiliğinden düşer — aynı kişiye yeniden gönderebilirsin.',
    ],
  },
  {
    id: 'proof',
    selector: '[data-tour="sessions"]',
    title: 'Kanıt ve doğrulama kodu',
    body: 'Ders bitince anlatan taraf bir ekran görüntüsü yükler; o görüntüde dersin doğrulama kodu ve sistem saati görünmelidir.',
    points: [
      'Kod dersin kendisine ait ve Derslerim’de iki tarafta da yazılı; ders başlarken görüşme ekranına yazın.',
      'Tamamlama, dersin planlanan bitişinden önce açılmaz.',
      'Onay kendiliğinden geldiğinde itiraz yolu da kapanır — kanıta beklemeden bak.',
    ],
  },
  {
    id: 'history',
    selector: '[data-tour="sessions"]',
    title: 'Hangi ders hangi sekmede',
    body: '“Geçmiş dersler” yalnızca tamamlananları gösterir; iptal edilen ve süresi geçip kapanan dersleri bütün rezervasyonların durduğu “Rezerve geçmişi”nde bulursun.',
    points: [
      '“Senden aksiyon bekleyenler” kanıt yüklemen ya da onaylaman gereken dersleri toplar; itirazdakiler ayrı başlıkta.',
      'Anlatan taraf hiç tamamlamazsa rezervasyon bir süre sonra düşer ve kimse puan almaz.',
      'Onayı verdiğin anda değerlendirme ekranı açılır — tek şansın o an: sonradan yazılamaz, yazdığın da değiştirilemez ve adınla anlatanın profilinde görünür.',
    ],
  },
  {
    id: 'rules',
    selector: '[data-tour="community"]',
    title: 'Toplulukta neler geçerli',
    body: 'Paylaştığın gönderi ve yorum geri alınamaz — silme ya da düzenleme yok.',
    points: [
      'Yazdıklarının topladığı net oy belli bir düzeye ulaştıkça puan yazılır; puanın ikinci kaynağı burası.',
      'Kendi gönderine oy veremezsin.',
      'Dışarıya bağlantı paylaşımı gönderilerde belli bir seviyeden sonra açılıyor; kurallar ve alınan önlemler ayrıca yazılı.',
    ],
  },
  {
    id: 'safety',
    selector: '[data-tour="discover"]',
    title: 'Engelleme ve şikayet',
    body: 'Engellediğin kişi seni aramada bulamaz, sana yazamaz ve yeni ders rezerve edemez.',
    points: [
      'Zaten arkadaşsanız engellemek kişiyi arkadaş listenden düşürmez; kaydı da kaldırmak istersen arkadaşlığı ayrıca sonlandır (açık ders varken sonlandırma çalışmaz).',
      'Engeli Keşfet’in “Arkadaş Ekle” sekmesindeki Engellediklerim listesinden ya da o kişinin profilinden kaldırırsın.',
      'Kurallara aykırı bir içeriği gördüğün yerden şikayet edersin; kararı yönetim verir.',
    ],
  },
]

export const TOUR_STEP_COUNT = TOUR_STEPS.length
