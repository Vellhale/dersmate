/*
  TOPLULUK (FORUM) — sayfa ile gönderi kartının ORTAK sözlüğü ve yardımcıları.

  pages/Topluluk.jsx'ten taşındı (2026-09-26): gönderi kartı kendi dosyasına çıkınca
  (components/GonderiKarti.jsx) etiket adı, etiket rengi, zaman biçimi ve oy hesabı iki
  dosyada birden gerekti. Kopyalamak yerine tek yere alındı; bir etiketin adı ya da rengi
  bir dosyada değişip öbüründe eski kalamasın diye.

  Sayfaya özgü olanlar (sıralama ve tarih enum'ları, şikayet sebepleri, kurallar,
  sınırlar) sayfada kaldı: kart onları kullanmıyor.

  Mobil depoda aynı adla bir src/lib/forum.js var ya da olacak; ADLAR aynı tutuluyor
  (ETIKET_ENUM, ETIKET_ANAHTARI, ETIKETLER, ETIKET_ADI, ETIKET_TONU, zamanKisalt,
  yasDakika, oyUygula) ki bir taraftaki düzeltme öbürüne birebir taşınabilsin. İçerik
  birebir DEĞİL: ETIKET_TONU iki platformda bilerek farklı (aşağıdaki nota bak).
*/

/* ─── SUNUCU SÖZLEŞMESİ ────────────────────────────────────────────────────────

   Arayüz Türkçe anahtarlarla çalışıyor ('stres', 'soru'), sunucu enum adlarıyla
   ('ExamStress', 'Question'). Çeviri TEK YERDE, burada: iki tarafın da kendi doğal
   sözlüğünü kullanabilmesi için. Anahtarları sunucununkilerle değiştirmek arayüzün
   geri kalanını (etiket renkleri, adlar, testler) İngilizceye çevirmek demekti.   */

export const ETIKET_ENUM = {
  stres: 'ExamStress',
  soru: 'Question',
  kaynak: 'Resource',
  program: 'StudyPlan',
  motivasyon: 'Motivation',
  tercih: 'Preference',
}

/** Ters yön: sunucudan gelen etiketi arayüz anahtarına çevirir. */
export const ETIKET_ANAHTARI = Object.fromEntries(
  Object.entries(ETIKET_ENUM).map(([anahtar, enumAdi]) => [enumAdi, anahtar]),
)

/** Filtre şeridinin sırası; 'hepsi' bir etiket değil, filtrenin "tümü" seçeneği. */
export const ETIKETLER = [
  { key: 'hepsi', label: 'Tümü' },
  { key: 'stres', label: 'Sınav Stresi' },
  { key: 'soru', label: 'Soru Sor' },
  { key: 'kaynak', label: 'Kaynak' },
  { key: 'program', label: 'Ders Programı' },
  { key: 'motivasyon', label: 'Motivasyon' },
  { key: 'tercih', label: 'Tercih' },
]

export const ETIKET_ADI = Object.fromEntries(ETIKETLER.map((e) => [e.key, e.label]))

/*
  Etiket renkleri. Hepsi 100/700-800 çiftleri — ui.jsx'teki Badge tonlarıyla aynı
  aile, yani forum kendi renk dilini kurmuyor, var olanı kullanıyor. Çiftler AA
  eşiğini geçiyor (en düşüğü violet-700/violet-100 ≈ 6.6:1).

  Marka mavisi SORU etiketine verildi: bu üründe soru sormak ana eylem ve marka rengi
  ana eylemi işaretliyor. Diğerleri marka dışı tonlar — altısı da mavi olsaydı etiket
  bir ayrım aracı olmaktan çıkardı.

  indigo BİLEREK YOK: e2e/marka.spec.js eski indigo tonlarını arayüzde arıyor.

  ⚠️ MOBİLDEN BİLİNÇLİ FARK. Mobil "A düzeni"nde yeşil, mor ve gök mavisi yok; orada
  altı etiket brand-50 ile slate-100 arasında sırayla dönüyor ve etiketi rengi değil
  ADI ayırt ediyor. Web kendi renk sistemini koruyor (emerald, violet, sky burada
  geçerli). Mobilden web'e port yapılırken bu tablo TAŞINMAZ; tersi de geçerli.
*/
export const ETIKET_TONU = {
  stres: 'bg-amber-100 text-amber-800',
  soru: 'bg-brand-100 text-brand-700',
  kaynak: 'bg-emerald-100 text-emerald-700',
  program: 'bg-violet-100 text-violet-700',
  motivasyon: 'bg-rose-100 text-rose-700',
  tercih: 'bg-sky-100 text-sky-800',
}

/* ─── ZAMAN ────────────────────────────────────────────────────────────────── */

/**
 * Sunucudan gelen UTC damgasını milisaniyeye çevirir.
 *
 * ⚠️ ZAMAN DİLİMİ EKİ YOKSA 'Z' EKLENİYOR. .NET, DateTime'ı Kind=Utc iken sonunda
 * 'Z' ile yazıyor; Kind=Unspecified iken YAZMIYOR ve o durumda tarayıcı metni YEREL
 * saat sanar. Türkiye'de bu üç saatlik bir kayma demek: üç saat önce yazılmış bir
 * gönderi "şimdi" görünür, bir dakika önce yazılan ise gelecekte kalır. Sütun
 * timestamptz olduğu için EF Utc döndürüyor, yani bugün ek gereksiz — ama tek bir
 * DTO'nun Kind'i değiştiğinde hata SESSİZ olur, bu yüzden koruma burada duruyor.
 */
export function damgayaCevir(metin) {
  if (!metin) return null
  const tamDamga = /(?:[zZ]|[+-]\d{2}:?\d{2})$/.test(metin) ? metin : `${metin}Z`
  const ms = Date.parse(tamDamga)
  return Number.isNaN(ms) ? null : ms
}

/** Damganın kaç dakika önce olduğunu verir; okunamayan damga 0 sayılıyor ("şimdi"). */
export function yasDakika(metin) {
  const ms = damgayaCevir(metin)
  if (ms === null) return 0
  // Negatife düşebilir: sunucu saati istemciden birkaç saniye ileriyse. "-1 dk" yerine
  // "şimdi" göstermek doğru, çünkü fark saat farkı değil senkron gürültüsü.
  return Math.max(0, Math.round((Date.now() - ms) / 60000))
}

/** "22 dk" / "3 sa" / "2 g". Forumda mutlak tarih işe yaramıyor: okuyanın sorduğu şey
    "ne zaman yazıldı" değil, "hâlâ taze mi". */
export function zamanKisalt(dakika) {
  // Az önce yazılan gönderi/yorum "0 dk" gösteriyordu; sayı doğruydu ama okunuşu
  // bozuktu — sıfır birimli bir süre, süre değil.
  if (dakika < 1) return 'şimdi'
  if (dakika < 60) return `${dakika} dk`
  const saat = Math.floor(dakika / 60)
  if (saat < 24) return `${saat} sa`
  return `${Math.floor(saat / 24)} g`
}

/* ─── OY ───────────────────────────────────────────────────────────────────── */

/**
 * OY UYGULAMA — sunucudaki üç durumun istemci aynası (VoteForumContentHandler).
 *
 *   oy yok      → oy ekle
 *   aynı yön    → GERİ AL (sunucu satırı siler, sayaç düşer)
 *   ters yön    → çevir (bir taraftan düş, diğerine ekle)
 *
 * Tek fonksiyon çünkü üç durumun sayaç etkisi birbirine bağlı; ayrı ayrı yazılsaydı
 * biri düzeltilirken diğeri unutulur ve optimistik sayı sunucununkinden kalıcı olarak
 * ayrışırdı. Yine de bu yalnızca TAHMİN: yanıt gelince sunucunun sayaçları yazılıyor.
 */
export function oyUygula(icerik, yon) {
  const onceki = icerik.myVote ?? 0
  const yeni = onceki === yon ? 0 : yon

  let arti = icerik.upvoteCount
  let eksi = icerik.downvoteCount

  if (onceki === 1) arti -= 1
  else if (onceki === -1) eksi -= 1

  if (yeni === 1) arti += 1
  else if (yeni === -1) eksi += 1

  return { ...icerik, upvoteCount: arti, downvoteCount: eksi, myVote: yeni }
}
