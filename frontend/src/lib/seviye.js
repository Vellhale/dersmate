/*
  SEVİYE SİSTEMİ — unvanın (Çırak / Öğretici / Uzman …) yerine geçen tek ölçü.

  NEDEN DEĞİŞTİ: unvan adları hem bir sıralama hem de bir karakter iddiası taşıyordu.
  "Çırak"tan "Üstat"a giden merdivenin basamak sayısı kullanıcıya hiç görünmüyordu;
  kimse kaç unvan olduğunu, nerede durduğunu bilmiyordu. Numaralı seviye bunu tek
  bakışta söylüyor: 10 üzerinden kaçtasın.

  MEKANİZMA AYNI KALDI: seviye, ders anlatarak biriktirilen krediden türüyor. Değişen
  yalnızca basamak sayısı ve etiketleme.

  ⚠️ HESAP BURADA DEĞİL, SUNUCUDA. Bu dosya eşik tablosu TAŞIMAZ ve taşımamalı.
  Sunucu `level` ile `nextLevelAt` alanlarını hazır gönderiyor
  (Domain/Community/UserLevel.cs). Eşikleri buraya kopyalamak, bu projede daha önce
  fiyat formülünde yaşanan sapmanın aynısını üretirdi: sunucu değişir, arayüz eski
  sayıyı göstermeye devam eder ve kimse fark etmez.

  Buradaki fonksiyonların tek işi OKUMAK ve BİÇİMLENDİRMEK.
*/

/**
 * Sistemdeki en yüksek seviye — rozetin "3 / 10" bağlamı için.
 *
 * Sunucudaki UserLevelRules.MaxLevel ile aynı olmak zorunda. Tek başına bir kural
 * DEĞİL, yalnızca bir gösterim sabiti: hiçbir seviye hesabı buna bakarak yapılmıyor,
 * sunucu zaten aralık dışına çıkmayan bir değer gönderiyor.
 */
export const EN_YUKSEK_SEVIYE = 10

/**
 * Kullanıcının seviyesi (1..EN_YUKSEK_SEVIYE).
 *
 * @param {object|null|undefined} kaynak - cüzdan (`/api/wallet`) ya da profil
 *   (`/api/users/{id}`) nesnesi. İkisi de `level` alanını aynı hesaptan üretiyor.
 * @returns {number}
 *
 * VERİ HENÜZ GELMEDİYSE 1 DÖNER. Yükleme sırasında `kaynak` null olur ve rozet bir
 * an için 1 gösterir. Boş bırakmak ya da iskelet çizmek düşünüldü; rozet üst barda
 * sabit genişlikte durduğu için değişen bir rakam, kayan bir düzenden daha az
 * rahatsız edici. Gelen veri yanlışsa da en alt basamağa düşer — kullanıcıya hak
 * etmediği bir seviye göstermektense eksik göstermek yeğdir.
 */
export function seviyeHesapla(kaynak) {
  const sunucudan = kaynak?.level
  if (Number.isInteger(sunucudan) && sunucudan >= 1) {
    return Math.min(sunucudan, EN_YUKSEK_SEVIYE)
  }

  return 1
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

/**
 * Bir sonraki seviyeye kalan puan; en üst seviyede `null`.
 *
 * @param {object|null|undefined} kaynak - `level`, `nextLevelAt` ve
 *   `totalEarnedCredits` taşıyan cüzdan/profil nesnesi.
 * @returns {number|null}
 *
 * EŞİK SUNUCUDAN, FARK BURADA: çıkarma işlemi bir kural değil, bir gösterim.
 * `nextLevelAt` null ise en üst seviyedeyiz ve "kalan" diye bir şey yok — 0 dönmek
 * yanlış olurdu, çünkü 0 "bir sonraki seviyeye ulaşmak üzeresin" demek.
 */
export function sonrakiSeviyeyeKalan(kaynak) {
  const hedef = kaynak?.nextLevelAt
  if (!Number.isInteger(hedef)) return null

  const puan = Number.isInteger(kaynak?.totalEarnedCredits) ? kaynak.totalEarnedCredits : 0
  return Math.max(0, hedef - puan)
}

/**
 * Rozetin ve sayaç satırının altına yazılan tek cümlelik ilerleme metni.
 *
 * Tek yerden üretiliyor: aynı cümle üst barın tooltip'inde, profil sayacında ve
 * ürün turunda görünüyor; üç ayrı yerde yazılsaydı biri güncellenmeden kalırdı.
 */
export function seviyeIlerlemeMetni(kaynak) {
  const puan = Number.isInteger(kaynak?.totalEarnedCredits) ? kaynak.totalEarnedCredits : 0
  const kalan = sonrakiSeviyeyeKalan(kaynak)

  return kalan === null
    ? `${puan} puan · en üst seviye`
    : `${puan} puan · sonraki seviyeye ${kalan}`
}
