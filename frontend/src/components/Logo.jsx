/**
 * Marka kilidi: iki nokta (SVG) + kelime markası (HTML metin).
 *
 * ─────────────────────────────────────────────────────────────────────────────
 * NEDEN KELİME MARKASI ARTIK SVG DEĞİL (2026-08-24, ikinci tur).
 *
 * Logo üst barda "sönük / parlak / bulanık" görünüyordu ve sebebi tarayıcıda ölçüldü:
 * SVG 228×80 birimlik kutuyu 36px yüksekliğe sığdırıyor, yani ölçek 0.45 ve 34 birimlik
 * yazı ekranda 15.3px'e düşüyordu. SVG <text>, HTML metninin aldığı font hinting'ini ve
 * alt piksel yerleşimini ALMIYOR: 15px civarında gövde çizgileri piksel ızgarasına
 * oturmuyor ve harfler yıkanmış görünüyor. Aynı punto HTML metni olarak keskin çiziliyor.
 *
 * Bu yüzden kelime markası artık gerçek metin. Kazanımlar:
 *   • net kenarlar (işletim sisteminin metin rasterizasyonu devrede)
 *   • gerçek yazı tipi ağırlıkları ve tracking
 *   • kırpılma riski yok — viewBox'a sığdırma derdi ortadan kalktı
 *
 * İki nokta SVG kaldı: hem geometri orada daha doğru, hem de e2e/marka.spec.js logonun
 * `circle` dolgularını paletle karşılaştırıyor (sözleşme).
 *
 * BOYUT ARTIK `className` YÜKSEKLİĞİNDEN DEĞİL, `boyut` BELİRTECİNDEN geliyor: metin
 * ile işaretin oranı sabit kalmalı ve CSS, bir kapsayıcının yüksekliğinden punto
 * türetemiyor. Dört belirteç var, hepsi aynı orana sadık.
 * ─────────────────────────────────────────────────────────────────────────────
 *
 * ÜÇ ZEMİN, ÜÇ VURGU TONU — ve üçü de ölçüldü (WCAG 2.1, tarayıcıda hesaplandı):
 *
 *                     slate-900 üstünde   brand-600 üstünde   BEYAZDAN farkı
 *   brand-100            14.08:1              3.86:1              1.27:1
 *   brand-300             8.50:1              2.33:1              2.10:1
 *   brand-400             6.57:1              1.80:1              2.72:1
 *   brand-500             4.59:1              1.26:1              3.89:1
 *
 * SON SÜTUN "SÖNÜK" ŞİKÂYETİNİN CEVABI. Üst bar eskiden brand-100 kullanıyordu:
 * slate-900 üstünde 14:1 ile fazlasıyla okunur, AMA beyazdan farkı yalnızca 1.27:1.
 * Yani "ders" beyaz, "mate" de neredeyse beyaz — marka ayrımı gözle seçilemiyor ve
 * logo tek renkli, soluk bir kütle gibi duruyordu. Sorun kontrast eksikliği değil,
 * KİMLİK eksikliğiydi.
 *
 * Üst bar artık brand-400: zeminle 6.57:1 (AA'nın çok üstünde) ve beyazdan 2.72:1 —
 * "mate" gerçekten mavi okunuyor. Koyu tema aslı (resimler/gemini-svg.svg) da zaten
 * bu tonu kullanıyordu.
 *
 * Giriş ekranının sol paneli AYRI bir koyu zemin (brand-600 gradyanı) ve orada brand-400
 * yalnızca 1.80:1 veriyor — okunamaz. O yüzden orası brand-100'de kalıyor (3.86:1).
 * Tek "koyu" varyant iki farklı koyu zemine hizmet edemez; bu yüzden zemin bir bayrak
 * değil, üç değerli bir belirteç.
 */

/* Palet senkronu: e2e/kaynak-sabitleri.spec.js bu iki sabitin tailwind.config.js'teki
   brand-500 ve brand-50 ile birebir aynı olmasını şart koşuyor. Logo.jsx palete import
   edemiyor (SVG dolguları satır içi hex olmak zorunda), bu yüzden senkron testle
   zorlanıyor — palet değişir, logo eski tonda kalır ve kimse fark etmez. */
const ACCENT = '#0088CC' // brand-500 — açık zeminlerde marka tonu
const BG = '#E6F4FB' // brand-50 — LogoMark'ın zemini, favicon ile ortak

const ACCENT_MARKA = '#CCE9F7' // brand-100 — giriş ekranının brand-600 gradyanı üstünde
const ACCENT_GECE = '#33A7DF' // brand-400 — slate-900 üst bar üstünde
const INK = '#0F172A' // slate-900
const BEYAZ = '#FFFFFF'

const ZEMINLER = {
  /** Beyaz / açık gri yüzeyler. */
  acik: { nokta: ACCENT, ikinciNokta: INK, ders: 'text-slate-900', mate: 'text-brand-500' },
  /** Giriş ekranının brand-600 gradyanlı sol paneli. */
  marka: { nokta: ACCENT_MARKA, ikinciNokta: BEYAZ, ders: 'text-white', mate: 'text-brand-100' },
  /** slate-900 üst bar ve koyu ray. */
  gece: { nokta: ACCENT_GECE, ikinciNokta: BEYAZ, ders: 'text-white', mate: 'text-brand-400' },
}

/*
  Belirteçler. İşaret yüksekliği metnin gövde yüksekliğine yakın tutuluyor: noktalar
  harflerden büyük olursa kilit "iki top ve yanında yazı" gibi okunuyor, küçük olursa
  yazının noktalama işareti gibi görünüyor.

  İşaretin genişliği verilmiyor (w-auto): viewBox oranı 40:18 olduğu için tarayıcı
  yükseklikten türetiyor. Genişliği elle vermek, oranı iki yerde tutmak demekti.
*/
const BOYUTLAR = {
  sm: { yazi: 'text-[15px]', isaret: 'h-[7px]', bosluk: 'gap-1.5' },
  md: { yazi: 'text-[17px]', isaret: 'h-2', bosluk: 'gap-2' },
  lg: { yazi: 'text-xl', isaret: 'h-[9px]', bosluk: 'gap-2' },
  xl: { yazi: 'text-2xl', isaret: 'h-[11px]', bosluk: 'gap-2.5' },
}

/**
 * @param boyut  sm | md | lg | xl — metin ve işaret birlikte ölçekleniyor.
 * @param zemin  acik | marka | gece — bkz. yukarıdaki kontrast tablosu.
 */
export function Logo({ boyut = 'md', zemin = 'acik', className = '', title = 'dersmate' }) {
  const z = ZEMINLER[zemin] ?? ZEMINLER.acik
  const b = BOYUTLAR[boyut] ?? BOYUTLAR.md

  return (
    <span className={`inline-flex items-center ${b.bosluk} ${className}`}>
      {/*
        İki düğüm: akran eşleşmesi. Aralarındaki 4 birimlik boşluk BOYA DEĞİL, gerçek
        boşluk. Eski çizimde daireler üst üste biniyor ve aradaki ayrım zemin rengiyle
        çizilmiş bir yayla yapılıyordu; o yay koyu temada `stroke="none"` olduğu için
        daireler tek bulanık kütleye dönüşüyordu. Ayrıca ikisi de opacity 0.95 taşıyordu.

        Erişilebilir ad BURADA: kelime markası `aria-hidden`, yani ekran okuyucu
        "dersmate" ifadesini bir kez duyuyor.
      */}
      {/*
        viewBox'TA 1 BİRİMLİK PAY VAR (2026-08-25). Eski kutu 40×18'di ve daireler
        kenarlara SIFIR payla oturuyordu: cy=9, r=9 → üst/alt kenar tam 0 ve 18'de,
        ikinci dairenin sağ kenarı tam 40'ta. Kenar yumuşatması (antialiasing) son
        piksel şeridini kutunun DIŞINA taşırıyor ve tarayıcı onu kırpıyordu — koyu
        zeminde beyaz dairenin kenarında görünen "kesik" tam buydu. Şimdi her kenarda
        1 birim nefes payı var; daireler kutuya değmiyor.

        BAĞ YAYI GERİ GELDİ — ama eski hatasıyla DEĞİL. İlk çizimde daireler üst üste
        biniyor ve ayrım, zemin rengiyle çizilmiş bir yayla yapılıyordu; koyu temada o
        yay görünmez olup daireleri tek kütleye dönüştürüyordu (bu yüzden kaldırılmıştı).
        Yeni yay GERÇEK renkte (vurgu tonu — her zeminde zaten ölçülü) ve dairelerin
        ARKASINA çiziliyor: uçları noktaların altında kayboluyor, görünen kısım iki
        düğümü birbirine bağlayan köprü. Daireler ayrık kaldığı için eski bulanıklaşma
        geri gelmiyor; işaret favicon'daki "bağlı iki akran" fikrini geri kazanıyor.
      */}
      <svg
        viewBox="0 0 42 20"
        role="img"
        aria-label={title}
        className={`${b.isaret} w-auto shrink-0`}
      >
        <path
          d="M 10 10 Q 21 0, 32 10"
          stroke={z.nokta}
          strokeWidth="2.5"
          fill="none"
          strokeLinecap="round"
        />
        <circle cx="10" cy="10" r="9" fill={z.nokta} />
        <circle cx="32" cy="10" r="9" fill={z.ikinciNokta} />
      </svg>

      {/*
        Kelime markası. `tracking-tight` sıkı ama yapışık değil; `leading-none` kilidi
        dikeyde noktalarla aynı eksene oturtuyor. Yazı tipi ayrıca TANIMLANMADI: gövde
        yazı tipiyle aynı yığın kullanılıyor, çünkü logo arayüzün içinde yaşıyor ve
        farklı bir yazı tipi burada yamalı görünürdü.

        font-bold → font-semibold (2026-08-25): 700 ağırlık bu puntolarda gövdeleri
        şişiriyor ve marka "kaba" okunuyordu (sahibin geri bildirimi). 600, aynı
        okunurluğu daha ince gövdeyle veriyor; iki hece arasındaki renk ayrımı da
        kalın siyah kütleye boğulmadan seçiliyor.
      */}
      <span aria-hidden="true" className={`font-semibold leading-none tracking-tight ${b.yazi}`}>
        <span className={z.ders}>ders</span>
        <span className={z.mate}>mate</span>
      </span>
    </span>
  )
}

/**
 * Yazısız kare rozet — dar alanlar için (favicon ile aynı geometri).
 *
 * ⚠️ ÇALIŞMA ZAMANI HATASI DÜZELTİLDİ (2026-08-24): ilk daire `fill={vurgu}` diyordu ama
 * `vurgu` yalnızca `Logo` fonksiyonunun İÇİNDE tanımlı bir yerel değişkendi — burada
 * tanımsızdı ve bileşen render edilse `ReferenceError` atardı. Hata bugüne kadar
 * görünmedi çünkü `LogoMark` hiçbir yerden çağrılmıyor; yani derleme de test de bunu
 * yakalayamazdı.
 *
 * GEOMETRİSİ BİLEREK ESKİ HÂLİNDE: favicon.svg ile birebir aynı olmak zorunda
 * (e2e/kaynak-sabitleri.spec.js ikisinin renklerini karşılaştırıyor) ve favicon,
 * kelime markasının yeniden çizilmesinden etkilenmiyor.
 */
export function LogoMark({ className = 'h-8 w-8', title = 'dersmate' }) {
  return (
    <svg viewBox="0 0 64 64" role="img" aria-label={title} className={className}>
      <rect width="64" height="64" fill={BG} rx="14" />
      <circle cx="23" cy="34" r="11" fill={ACCENT} opacity="0.95" />
      <circle cx="41" cy="34" r="11" fill={INK} opacity="0.95" />
      <path d="M 23 34 Q 32 22, 41 34" stroke={BG} strokeWidth="3.5" fill="none" strokeLinecap="round" />
    </svg>
  )
}
