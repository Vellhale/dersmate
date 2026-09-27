/*
  HAKKIMIZDA METİNLERİ — tek kaynak, iki depoda BAYT BAYT aynı dosya:

    web    frontend/src/lib/hakkimizdaMetni.js   KAYNAK
    mobil  src/lib/hakkimizdaMetni.js            birebir kopya

  Sapma `diff` ile ölçülür ve BOŞ çıkmalı; bu yüzden dosyada platforma özgü tek satır
  yok. Saf JS: ikon bileşeni İÇERMEZ. Her kayıttaki `anahtar`ı sayfa kendi ikonuna
  çeviriyor (web ve mobil ikon setleri ayrı dosyalarda).

  ─── NE ANLATIYOR ──────────────────────────────────────────────────────────────
  dersmate iki şey: akran öğrenme platformu VE öğrencilerin sosyal ağı (Topluluk).
  Eski metin yalnızca birincisini anlatıyordu; menü, Keşfet önerileri, Ders Portföyü,
  arkadaşlıktan sohbete geçiş, Derslerim akışı, Topluluk ve bildirimler hiç geçmiyordu.

  ADIMLAR menüdeki sırayla ve menüdeki adlarla yazılı (Keşfet · Ders Portföyü ·
  Arkadaşlar · Sohbet · Derslerim · Topluluk). Ürün rehberi de aynı adları kullanıyor;
  bir taraf bir bölümü yeniden adlandırırsa diğeri de değişmeli, yoksa iki açıklama
  birbirini yalanlar.

  ─── OLGU DAYANAKLARI (metin bunlara bakılarak yazıldı) ─────────────────────────
  • Öneriler portföyden türüyor, karşılıklı eşleşme üstte (GetMatchSuggestions).
  • Kabulde sohbet açılıyor; mesaj yalnızca kabul edilmiş arkadaşlıkta. Görüntülü
    görüşmeyi platform barındırmıyor, bağlantı sohbette paylaşılıyor.
  • Dersi öğrenci rezerve ediyor; kanıtı anlatan yüklüyor, öğrenci onaylıyor.
  • Değerlendirme yalnızca tamamlanan dersten (CreateReview).
  • Topluluk'ta net oy puana dönüşüyor (CommunityRewardRules) ve ders kazancı gibi
    süresiz (CreditLedgerService → MintCommunityRewardAsync, ExpiresAtUtc = null).
    Kullanım koşulları §3 aynı olguyu anlatıyor (2026-09-25 sürümünün içinde, yayından
    önce düzeltildi); biri değişirse diğeri de.

  ─── BİLİNÇLİ OLARAK YAZILMAYANLAR ──────────────────────────────────────────────
  • SAYI YOK: puan tablosu, 48 saat, net oy eşiği, seviye sayısı. Bunlar sunucu
    sabiti; buraya kopyalanırsa sessizce eskir (seviye.js ilkesi). Sayılar Kullanım
    koşullarında.
  • "Çekmece", "sol üst", "ana ekran" YOK: web'de doğru değiller.
  • Bildirim ayarlarına giden YOL YOK: iki platformda farklı ve değişebiliyor.
  • Ödül ya da başarı iddiası yok.

  ⚠️ GÖNÜLLÜ İLAN: sunucu gönüllü ilandaki derse puan basmıyor. "Onaylanan dersler
  anlatana süresine göre puan kazandırır" bu yüzden "her ders" demiyor. Bugün hiçbir
  istemci gönüllü ilan açtırmıyor; açtırmaya başlarsa puan güvencesine "gönüllü ilanlar
  hariç" eklenmeli.
*/

/** Sayfa başlığının altındaki özet paragraf. */
export const OZET =
  'dersmate, öğrencilerin birbirine ders anlattığı bir akran öğrenme platformu ve aynı ' +
  'yolda yürüyen öğrencilerin sosyal ağıdır. İyi bildiğin konuyu anlatır, eksik olduğun ' +
  'konuda bir akranından ders alırsın; Topluluk’ta soru sorar, deneyimini paylaşırsın.'

/*
  Misyon / vizyon / topluluk. "Öğretmen yok, akran var" ile vizyondaki "hem öğrenen hem
  anlatan" artık çelişmiyor (eski vizyon "hem öğrenci hem öğretmen" diyordu).
*/
export const DEGERLER = [
  {
    anahtar: 'misyon',
    baslik: 'Misyonumuz',
    metin:
      'Bir konuyu gerçekten öğrenmenin en kısa yolu onu birine anlatmaktır. dersmate, ' +
      'öğrencilerin bildiklerini anlatarak pekiştirdiği, eksiklerini bir akranından ' +
      'kapattığı ve sorularını toplulukla paylaştığı bir alan açıyor; aradaki mesafeyi, ' +
      'ücreti ve aracıyı kaldırıyoruz.',
  },
  {
    anahtar: 'vizyon',
    baslik: 'Vizyonumuz',
    metin:
      'Hiçbir öğrencinin, sorusunu soracak birini bulamadığı için bir konuyu eksik ' +
      'bırakmadığı bir öğrenme ağı. Bugün YKS konularıyla başlıyoruz; hedefimiz, her ' +
      'öğrencinin hem öğrenen hem anlatan olduğu, sınavdan üniversiteye birbirine destek ' +
      'olan bir topluluk.',
  },
  {
    anahtar: 'topluluk',
    baslik: 'Topluluğumuz',
    metin:
      'Öğretmen yok, akran var. Dayanışma dersle bitmiyor: Topluluk’ta sınav stresinden ' +
      'tercih kararına her şeyi konuşur, kimseyle arkadaş olmadan da soru sorar, işe ' +
      'yarayanı oyunla öne çıkarırsın. Kurallar herkese açık; şikayet edilen içeriği ' +
      'moderasyon ekibi inceler.',
  },
]

/** "Nasıl işliyor" kartının üst satırları. */
export const NASIL = {
  etiket: 'Nasıl işliyor',
  baslik: 'Anlat, öğren, ilerle',
  giris: 'Menüdeki her bölüm bir adım:',
}

/* Menüdeki sırayla. `anahtar` ikonun seçimi için; sayfalar menüdeki ikonun aynısını
   kullanır, kullanıcı simgeyi menüden tanır. */
export const ADIMLAR = [
  {
    anahtar: 'kesfet',
    baslik: 'Keşfet',
    metin:
      'Aradığın konuyu anlatan kişileri bul. Karşılıklı ders verebileceğin kişiler ' +
      'listenin başında çıkar.',
  },
  {
    anahtar: 'portfoy',
    baslik: 'Ders Portföyü',
    metin:
      'Anlatabileceğin ve öğrenmek istediğin konuları ekle; öneriler bunlara göre ' +
      'şekillenir.',
  },
  {
    anahtar: 'arkadaslar',
    baslik: 'Arkadaşlar',
    metin: 'İstek gönder; kabul edilince aranızda sohbet açılır.',
  },
  {
    anahtar: 'sohbet',
    baslik: 'Sohbet',
    metin: 'Dersin gününü konuşun, görüntülü görüşme bağlantısını buradan paylaşın.',
  },
  {
    anahtar: 'derslerim',
    baslik: 'Derslerim',
    metin:
      'Dersi öğrenci rezerve eder. Ders bitince anlatan kanıt yükler, öğrenci onaylar.',
  },
  {
    anahtar: 'topluluk',
    baslik: 'Topluluk',
    metin: 'Soru sor, kaynak öner, başkalarının sorusunu yanıtla.',
  },
]

/* Push yalnızca mobil uygulamada var; cümle bunu söylüyor, web'de de doğru okunuyor.
   Türler sunucudaki bildirim kanallarıyla aynı: mesaj, istek (kabul dahil), ders onayı,
   ders planı (hatırlatmalar). */
export const BILDIRIM_NOTU =
  'Mobil uygulamada istersen mesaj, arkadaşlık isteği, ders onayı ve ders hatırlatmaları ' +
  'için bildirim alırsın; her türü ayrı ayrı kapatabilirsin.'

/*
  Üç güvence. "Doğrulanmış dersler" yalnızca burada geçiyor (eskiden vaat şeridinde de
  vardı). "Karşılıklı takas" fikri Keşfet adımına taşındı.

  ⚠️ TUTULAMAYACAK SÖZ VERME: bir güvence ancak kodda karşılığı varsa yazılır. Eski bir
  istek "ders aldıkça kredi harcarsın" diyordu; sistem öğrenciden hiçbir şey düşmüyor ve o
  cümle kullanıcıyı var olmayan bir mekanizmaya göre karar vermeye iterdi.
*/
export const GUVENCELER = [
  {
    anahtar: 'puan',
    baslik: 'Puanla ilerleme',
    metin:
      'Onaylanan dersler anlatana süresine göre puan kazandırır; Topluluk’ta oy toplayan ' +
      'katkıların da puan getirir. Puan harcanmaz, seviyeni belirler.',
  },
  {
    anahtar: 'para',
    baslik: 'Para transferi yok',
    metin: 'Kimse kimseye ödeme yapmaz. Platformda para dolaşmaz.',
  },
  {
    anahtar: 'kanit',
    baslik: 'Doğrulanmış dersler',
    metin: 'Her ders kanıtla kapanır; değerlendirmeler yalnızca gerçek derslerden gelir.',
  },
]

/** Sayfanın tezi, tek cümlede. */
export const KAPANIS = 'Bir konuyu anlatabiliyorsan, onu gerçekten öğrenmişsindir.'
