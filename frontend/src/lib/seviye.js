/*
  SEVİYE SİSTEMİ — unvanın (Çırak / Öğretici / Uzman) yerine geçen tek ölçü.

  NEDEN DEĞİŞTİ: unvan adları hem bir sıralama hem de bir karakter iddiası taşıyordu.
  "Çırak"tan "Uzman"a giden merdivenin basamak sayısı kullanıcıya hiç görünmüyordu;
  kimse kaç unvan olduğunu, nerede durduğunu bilmiyordu. Numaralı seviye bunu tek
  bakışta söylüyor: 10 üzerinden kaçtasın.

  ⚠️ BU DOSYA GEÇİCİ BİR YERDE DURUYOR — ve bilerek.

  Seviye ARTIK SUNUCUDAN GELMİYOR: backend hâlâ rankTitle/rankEmoji/nextRankAt
  gönderiyor ve zorlaşan XP algoritması henüz yazılmadı. Arayüz o gelene kadar
  herkese 1. Seviye gösteriyor.

  Sahte bir ilerleme UYDURMUYORUZ. Mevcut totalEarnedCredits'ten seviye türetmek
  teknik olarak kolaydı ama iki kez yanlış olurdu: (1) kullanıcı bir seviye görür,
  gerçek XP algoritması gelince seviyesi DÜŞEBİLİR — kazanılmış bir şeyin geri
  alınması en kötü ürün hatasıdır; (2) ekranda duran sayı, arkasında hiçbir kural
  olmadığı hâlde kural varmış gibi görünür.

  BACKEND XP GELDİĞİNDE yapılacak tek şey: `seviyeHesapla`'nın gövdesini sunucudan
  gelen alanı okuyacak şekilde değiştirmek. Çağıran hiçbir bileşen değişmez —
  hepsi zaten bu fonksiyondan geçiyor.
*/

/** Sistemdeki en yüksek seviye. Rozet "3 / 10" gibi bir bağlam göstermek isterse buradan okur. */
export const EN_YUKSEK_SEVIYE = 10

/** Backend XP gönderene kadar herkesin seviyesi. Tek yerde dursun ki kaldırması kolay olsun. */
const GECICI_SEVIYE = 1

/**
 * Kullanıcının seviyesi (1..EN_YUKSEK_SEVIYE).
 *
 * @param {object|null|undefined} kaynak - cüzdan ya da profil nesnesi. Bugün OKUNMUYOR;
 *   imzada duruyor çünkü XP alanı geldiğinde çağıranların hiçbiri değişmesin.
 * @returns {number}
 */
export function seviyeHesapla(kaynak) {
  // Sunucu bir gün seviyeyi doğrudan gönderirse (level / xpLevel), ona saygı duy.
  // Bugün hiçbir uç bu alanı döndürmüyor; kod buraya düşmüyor ama sözleşme burada duruyor.
  const sunucudan = kaynak?.level ?? kaynak?.seviye
  if (Number.isInteger(sunucudan) && sunucudan >= 1) {
    return Math.min(sunucudan, EN_YUKSEK_SEVIYE)
  }

  return GECICI_SEVIYE
}

/**
 * Rozette yazan metin: "1. Seviye".
 *
 * Türkçe sıra sayısı noktayla yazılır (1. Seviye), İngilizcedeki gibi "Seviye 1" değil.
 * Tek yerden üretiliyor ki başlıkta, profilde ve tooltip'te aynı biçim kalsın.
 */
export function seviyeEtiketi(seviye) {
  return `${seviye}. Seviye`
}
