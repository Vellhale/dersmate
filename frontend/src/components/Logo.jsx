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

/* Bağ yayının rengi = ARKASINDAKİ ZEMİN. Üç varyantın ikisi zaten yukarıda var
   (beyaz yüzey → BEYAZ, slate-900 üst bar → INK); üçüncüsü giriş ekranının
   `from-brand-600` gradyanı, o yüzden palete bağlı ayrı bir sabit gerekiyor.
   e2e/kaynak-sabitleri.spec.js bunu brand-600 ile senkron tutuyor: panelin gradyanı
   bir gün değişirse yay zeminden ayrışır ve logonun ortasında yanlış renkte bir
   çizgi belirir. */
const ZEMIN_MARKA = '#0077B3' // brand-600 — giriş panelinin gradyan başlangıcı

const ZEMINLER = {
  /** Beyaz / açık gri yüzeyler. */
  acik: {
    nokta: ACCENT,
    ikinciNokta: INK,
    yay: BEYAZ,
    ders: 'text-slate-900',
    mate: 'text-brand-500',
  },
  /** Giriş ekranının brand-600 gradyanlı sol paneli. */
  marka: {
    nokta: ACCENT_MARKA,
    ikinciNokta: BEYAZ,
    yay: ZEMIN_MARKA,
    ders: 'text-white',
    mate: 'text-brand-100',
  },
  /** slate-900 üst bar ve koyu ray. */
  gece: {
    nokta: ACCENT_GECE,
    ikinciNokta: BEYAZ,
    yay: INK,
    ders: 'text-white',
    mate: 'text-brand-400',
  },
}

/*
  Belirteçler. İşaret artık metnin GÖVDE (x) yüksekliğine değil, BÜYÜK HARF
  yüksekliğine yakın: kabaca punto × 0.7.

  ⛔ 12 PİKSELİN ALTINA İNME — ölçüldü, tahmin değil.

  Eski belirteçler 7/8/9/11px'ti ve bu, işaretin içindeki bağ yayı DAİRELERİN ARKASINA
  saklandığı sürece sorun değildi; görünen tek şey iki dolu daireydi ve daire her
  boyutta daire kalır. Yeni kurguda yay işaretin İÇİNDEN geçiyor, yani çizimin
  okunurluğu artık yayın kaç piksele düştüğüne bağlı.

  Tarayıcıda rasterize edilip 1×/2×/3× cihaz piksel oranlarında ölçüldü (yay kalınlığı
  viewBox'ta 2.6/20, yani işaret yüksekliğinin %13'ü):

      işaret   DPR 1                        DPR 2 / 3
       8px     yay 1px → kopuk lekeler      okunur
      10px     hâlâ bulanık                 okunur
      11px     sınırda                      okunur
      12px     OKUNUR                       net
      14px+    net                          net

  DPR 1 belirleyici olan: masaüstü monitörlerin çoğu hâlâ 1× ve logo orada da doğru
  görünmek zorunda. 2× ekranda bakıp "iyi" demek, kullanıcıların yarısını ölçmemektir.

  İşaretin genişliği verilmiyor (w-auto): viewBox oranı 37.6:20 olduğu için tarayıcı
  yükseklikten türetiyor. Genişliği elle vermek, oranı iki yerde tutmak demekti.
*/
const BOYUTLAR = {
  sm: { yazi: 'text-[15px]', isaret: 'h-3', bosluk: 'gap-1.5' }, // 12px — taban
  md: { yazi: 'text-[17px]', isaret: 'h-3', bosluk: 'gap-2' }, // 12px
  lg: { yazi: 'text-xl', isaret: 'h-[14px]', bosluk: 'gap-2' }, // 20px punto
  xl: { yazi: 'text-2xl', isaret: 'h-[17px]', bosluk: 'gap-2.5' }, // 24px punto
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
        İki düğüm: akran eşleşmesi. Erişilebilir ad BURADA — kelime markası
        `aria-hidden`, yani ekran okuyucu "dersmate" ifadesini bir kez duyuyor.

        ─────────────────────────────────────────────────────────────────────────
        ÜÇÜNCÜ (VE SON) KURGU — 2026-09-07. Önceki iki denemenin ikisi de aynı
        şeyi ıskaladı: yayın rengi.

          1. deneme  daireler ÜST ÜSTE, ayrım zemin rengiyle çizilmiş yayla.
                     Doğru fikir, ama yay tek bir renge (açık zemin) sabitlenmişti;
                     koyu temada görünmez kalıp iki daireyi tek bulanık kütleye
                     çeviriyordu. Bu yüzden kaldırıldı.
          2. deneme  daireler AYRIK, aralarına VURGU renginde bir köprü yayı,
                     üstelik dairelerin ARKASINA çizilmiş. Yay ile birinci daire
                     aynı renkte olduğu için ikisi kaynaşıyor, yayın sağ ucu
                     dairelerin arasından bir kuyruk gibi taşıyordu. Kullanılan
                     boyutlarda (işaret 7–11px) bu bir çizim değil, leke.

        Şimdi ikisinin doğru yanları birleşti: daireler yeniden üst üste biniyor
        (kilit, iki ayrı top değil) ve ayrım yine zemin rengiyle yapılıyor — AMA
        yay artık VARYANTA GÖRE zemin rengi (`z.yay`) ve dairelerin ÜSTÜNE, yani
        son çocuk olarak çiziliyor. 1. denemenin arızası tam olarak bu iki
        eksikti; renk sabitlenmişti ve boyama sırası şansa bırakılmıştı.

        ⛔ `yay`'ı vurgu tonuna çevirme, sırasını da yukarı taşıma. İkisi de
        işaretin okunurluğunu YOK EDİYOR ve ikisi de "sadeleştirme" gibi görünüyor.

        Oranlar favicon'la (LogoMark) aynı aileden: r=9, merkezler arası 17.6
        (d/r≈1.96), yay kalınlığı 2.6 (≈0.29r), yayın tepesi merkez ekseninin 5
        birim üstünde (≈0.56r). viewBox'ta her kenarda 1 birim pay var — sıfır
        payda kenar yumuşatması son piksel şeridini kutunun dışına taşırıyor ve
        tarayıcı onu kırpıyordu (koyu zeminde beyaz dairede görünen "kesik" buydu).
      */}
      <svg
        viewBox="0 0 37.6 20"
        role="img"
        aria-label={title}
        className={`${b.isaret} w-auto shrink-0`}
      >
        <circle cx="10" cy="10" r="9" fill={z.nokta} />
        <circle cx="27.6" cy="10" r="9" fill={z.ikinciNokta} />
        <path
          d="M 10 10 Q 18.8 0, 27.6 10"
          stroke={z.yay}
          strokeWidth="2.6"
          fill="none"
          strokeLinecap="round"
        />
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
 * GEOMETRİSİ favicon.svg İLE BİREBİR AYNI OLMAK ZORUNDA — ikisi aynı anda
 * güncellenir (e2e/kaynak-sabitleri.spec.js renklerini karşılaştırıyor).
 *
 * 2026-09-07'de ikisi birden düzeltildi:
 *   • Oranlar yukarıdaki işaretle hizalandı (≈1.96r ara, ≈0.29r kalınlık,
 *     ≈0.56r tepe). Rozet ile kelime markası farklı oranlardaydı; yan yana
 *     geldiklerinde iki ayrı logo gibi duruyorlardı.
 *   • cy 34 → 32. İşaret 64'lük kutuda 2 birim AŞAĞIDAYDI: üstte 23, altta 19
 *     birim boşluk. Sebebi görünmüyordu ama rozet dikeyde ortalanmamış duruyordu.
 *   • `opacity="0.95"` kaldırıldı. Amacı belirsizdi, etkisi kesin: koyu düğüm
 *     zemine karışıp soluyordu.
 */
export function LogoMark({ className = 'h-8 w-8', title = 'dersmate' }) {
  return (
    <svg viewBox="0 0 64 64" role="img" aria-label={title} className={className}>
      <rect width="64" height="64" fill={BG} rx="14" />
      <circle cx="21.2" cy="32" r="11" fill={ACCENT} />
      <circle cx="42.8" cy="32" r="11" fill={INK} />
      <path d="M 21.2 32 Q 32 19.8, 42.8 32" stroke={BG} strokeWidth="3.2" fill="none" strokeLinecap="round" />
    </svg>
  )
}
