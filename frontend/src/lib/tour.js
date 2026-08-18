/**
 * Ürün turu adımları (Modül 5).
 *
 * Amaç: yeni gelen öğrencinin ilk üç sorusunu sırayla yanıtlamak — "bu bana neye mal
 * olacak", "aradığımı nasıl bulurum", "karşı taraf işini yapmazsa ne olur".
 *
 * EKONOMİ DEĞİŞTİ, TUR DA DEĞİŞTİ. Önceki metinler kredi takasını anlatıyordu:
 * "kazandığın krediyle ders alırsın", "kayıt olurken 1 kredi hediye edildi",
 * "rezervasyonda kredin bloke edilir". Üçü de artık yanlış — ders almak ücretsiz, bloke
 * edilen bir şey yok ve puan yalnızca ANLATAN tarafa yazılıyor. Yanlış beklenti kuran bir
 * tur, hiç tur olmamasından kötüdür: kullanıcı bakiyesinin düşmesini bekler, düşmeyince
 * ürünü anlamadığını sanır.
 *
 * selector: turun ışık tutacağı öğe. Bulunamazsa adım ORTADA kart olarak gösterilir
 * (bkz. ProductTour). Bu sayede ekran boyutuna göre gizlenen öğeler turu KIRMAZ —
 * mobilde üst gezinme çubuğu hamburgere dönüştüğü için bu gerçek bir durum.
 *
 * Çıpalar Layout'taki NAV tanımından ve unvan rozetinden gelir; buradaki her selector'ın
 * karşılığı orada VARDIR. (Eskiden ilk adım `[data-tour="wallet"]` arıyordu ama cüzdan
 * rozeti kaldırılıp yerine `rank` konmuştu — yani turun ilk adımı sessizce çıpasız
 * kalmıştı.)
 */
export const TOUR_STEPS = [
  {
    id: 'free',
    selector: '[data-tour="rank"]',
    title: 'Ders almak ücretsiz 🌱',
    body:
      'Burada para da, ödediğin bir kredi de yok. Ders ALMAK tamamen ücretsiz. ' +
      'Ders ANLATTIĞINDA puan kazanırsın; biriken puan buradaki unvanını yükseltir ' +
      '(Çırak → Öğretici → Uzman → …). Yani puan harcanan bir bakiye değil, emeğinin karşılığı.',
  },
  {
    id: 'discover',
    selector: '[data-tour="discover"]',
    title: 'Keşfet — ders talep et',
    body:
      'Almak istediğin konuyu anlatabilen öğrencileri burada görürsün. ' +
      '“Karşılıklı takas” etiketi, o kişinin senin anlatabildiğin bir konuyu da aradığı ' +
      'anlamına gelir — ikinizin de kazandığı eşleşme budur.',
  },
  {
    id: 'portfolio',
    selector: '[data-tour="portfolio"]',
    title: 'Ders Portföyü — ne anlatabilirsin',
    body:
      'Anlatabildiğin konuları buraya ekle; Keşfet’te başkalarına böyle görünürsün. ' +
      'Puan kazanmanın tek yolu ders anlatmak olduğu için portföyün boşsa unvanın da yerinde sayar.',
  },
  {
    id: 'sessions',
    selector: '[data-tour="sessions"]',
    title: 'Kanıt ve onay — risksiz akış',
    body:
      'Eşleşme kabul edilince sohbet açılır; ders saatini ve Zoom / Meet / Discord linkini ' +
      'orada konuşursunuz — dersi biz barındırmıyoruz. Ders bitince anlatan taraf ekran ' +
      'görüntüsü yükler, sen onaylarsın ve puan ancak o an yazılır. Bir sorun varsa itiraz ' +
      'edersin, kararı hakem verir.',
  },
]

export const TOUR_STEP_COUNT = TOUR_STEPS.length
