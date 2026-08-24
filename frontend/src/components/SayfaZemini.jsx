/*
  SAYFA ZEMİNİ — düz beyazdan çıkış, kontrastı bozmadan.

  Uygulama gövdesi bg-slate-50 idi (index.css) ve sayfalar beyaz kartlardan oluşan
  düz bir yığın gibi duruyordu. Bu bileşen içeriğin ARKASINA üç katman koyuyor:

    1. Mesh gradyan  — iki yumuşak renk havuzu (marka mavisi + gök mavisi), radial
                        gradyanlarla. Düz bir linear geçişin aksine köşelerden değil
                        İÇERİDEN aydınlanıyor; "flat" hissini kıran şey bu.
    2. Izgara deseni  — çok düşük opaklıkta SVG çizgi ızgarası. Gözle seçilmiyor ama
                        yüzeye doku veriyor; tamamen düz bir alanla arasındaki fark
                        yan yana konunca görülüyor.
    3. Geometrik leke — tek bir büyük, bulanık daire. Derinlik veriyor.

  ─── KONTRAST NEDEN BOZULMUYOR ────────────────────────────────────────────────
  Bu projede erişilebilirlik eşikleri TESTLE korunuyor (e2e/marka.spec.js) ve
  metin kontrastı ölçülen bir sözleşme. O yüzden kural şu: ZEMİN, METNİN ALTINA
  DOĞRUDAN GİRMEZ. Metin her zaman bir kartın (Card / GlassCard) üstünde duruyor
  ve o kartın kendi opak-ya-da-yüksek-opaklıklı zemini var. Zemin katmanı yalnızca
  kartların ARASINDA görünüyor.

  En koyu mesh durağı brand-100 (#CCE9F7) seviyesinde tutuldu; cam kartın altına
  girse bile beyazla karışıp neredeyse beyaz kalıyor. Daha doygun bir zemin (300+)
  cam kartlarda slate-600 gövde metnini AA'nın altına düşürürdü — ölçülerek seçildi.

  pointer-events-none + aria-hidden: dekor. Tıklamayı geçirir, ekran okuyucuya
  görünmez. `fixed` DEĞİL `absolute`: fixed olsaydı modal ve çekmecelerin altında da
  boyanır, sayfa kabuğunun dışına taşardı.

  ─── SARMALAYICI `relative` OLMALI, `isolate` OLMAMALI ──────────────────────────
  Bu bileşenin ilk sürümü çağıranlardan "relative isolate" istiyordu. O talimat
  YANLIŞTI ve çalışan uygulamada ölçülerek bulundu.

  Bu projede modal/perde bileşenleri PORTAL KULLANMIYOR — ui.jsx Modal, CookieBanner,
  FilterDrawer, Avatar büyütme; hepsi `fixed inset-0 z-50` ile bulundukları yerde
  çiziliyor. `isolation: isolate` sarmalayıcıyı bir YIĞIN BAĞLAMINA çevirir ve
  içindeki z-50 o bağlamın içine hapsolur. Dışarıdaki sticky `<header>` ise z-40:
  kök bağlamda karşılaştırma "header z-40" ile "bu kutu z-auto" arasında yapılır ve
  KOYU ÜST BAR MODALIN ÜSTÜNE ÇIKAR. Ölçüldü — /profil'de düzenleme modalı açıkken
  ekranın tepesinde elementFromPoint `<header>` döndürüyordu: perde barı örtmüyor,
  bardaki menü ve çıkış düğmesi hâlâ tıklanabilir kalıyordu.

  İzolasyona gerek de yok: -z-10 ancak araya ZEMİNİ OLAN bir ata girerse kaybolur.
  Zincirde zemini olan kutu yok ve `body`nin bg-slate-50'si tuvale devrolduğu için
  negatif z katmanı onun ÜSTÜNE boyanıyor. Araya bir gün zemin sınıfı girerse çözüm
  `isolate` değil, zemini taşıyan kutuyu ayırmaktır.

  TEK ÇAĞIRAN LAYOUT. Sayfalar kendi zeminlerini ÇİZMEZ: iki zemin üst üste binince
  ızgara opaklığı iki katına çıkıyor (0.06 → 0.12) ve mesh havuzları toplanıyordu.
  Yoğunluk kararı Layout'ta, rotaya göre veriliyor.
  ────────────────────────────────────────────────────────────────────────────────

  DESEN SVG'Sİ data URI DEĞİL, satır içi <svg>: data URI hex renk taşımak zorunda
  ve e2e/kaynak-sabitleri.spec.js src altında palet dışı hex arıyor. Satır içi SVG
  currentColor kullanabiliyor, yani renk paletten sınıfla geliyor.
  ──────────────────────────────────────────────────────────────────────────────
*/

/** Izgara deseni — tek bir <pattern>, sayfa boyunca döşenir. */
function IzgaraDeseni({ id }) {
  return (
    <svg className="absolute inset-0 h-full w-full text-brand-900" aria-hidden="true">
      <defs>
        {/* 32px hücre: daha sık olunca moiré yapıyor, daha seyrek olunca doku kayboluyor. */}
        <pattern id={id} width="32" height="32" patternUnits="userSpaceOnUse">
          <path
            d="M32 0H0V32"
            fill="none"
            stroke="currentColor"
            strokeOpacity="0.06"
            strokeWidth="1"
          />
        </pattern>
      </defs>
      <rect width="100%" height="100%" fill={`url(#${id})`} />
    </svg>
  )
}

/**
 * @param yogunluk  'hafif' (varsayılan, iç sayfalar) | 'zengin' (profil, Hakkımızda)
 *
 * İki yoğunluk var çünkü her sayfa aynı görsel ağırlığı kaldırmıyor: Derslerim gibi
 * veri yoğun ekranlarda zemin geri çekilmeli, profil gibi "vitrin" sayfalarında öne
 * çıkabilir. Ayrım opaklıkta, renkte değil — palet tek.
 */
export function SayfaZemini({ yogunluk = 'hafif', desenId = 'dm-izgara' }) {
  const zengin = yogunluk === 'zengin'

  return (
    <div className="pointer-events-none absolute inset-0 -z-10 overflow-hidden" aria-hidden="true">
      {/*
        MESH GRADYAN — iki radial havuz. Tailwind'in bg-gradient-* yardımcıları yalnızca
        linear/conic veriyor; radial-gradient için style gerekiyor. Renkler yine PALETTEN:
        CSS değişkeni olarak değil, doğrudan brand tonlarının rgb karşılığı olarak değil —
        sınıfla boyanan iki <div> katmanı olarak. Böylece dosyada hex yok.
      */}
      <div
        className={`absolute -left-24 -top-32 h-[28rem] w-[28rem] rounded-full bg-brand-100
                    blur-3xl ${zengin ? 'opacity-70' : 'opacity-40'}`}
      />
      <div
        className={`absolute -right-32 top-24 h-[26rem] w-[26rem] rounded-full bg-sky-100
                    blur-3xl ${zengin ? 'opacity-60' : 'opacity-30'}`}
      />
      {/* Üçüncü havuz yalnızca zengin kipte: hafif kipte üç leke zemini "bulutlu" yapıyor
          ve veri yoğun sayfalarda gözü yoruyor. */}
      {zengin && (
        <div className="absolute -bottom-40 left-1/3 h-[22rem] w-[22rem] rounded-full bg-indigo-100 opacity-40 blur-3xl" />
      )}

      <div className={zengin ? 'opacity-100' : 'opacity-60'}>
        <IzgaraDeseni id={desenId} />
      </div>

      {/* Alta doğru sönümleme: desen ve lekeler sayfa sonunda kesilmiş gibi durmasın. */}
      <div className="absolute inset-x-0 bottom-0 h-40 bg-gradient-to-t from-slate-50 to-transparent" />
    </div>
  )
}

/**
 * Cam kart — zeminle uyumlu, saydam yüzey.
 *
 * ui.jsx'teki Card DEĞİŞTİRİLMEDİ: o, uygulamanın her yerinde kullanılan opak
 * varsayılan ve davranışını değiştirmek yüzlerce yeri aynı anda etkilerdi. Cam
 * görünüm İSTEYEN yüzeyler bunu kullanıyor.
 *
 * OPAKLIK 80 — ölçülerek seçildi. Kontrast hesabı (beyazı zeminle harmanlayıp WCAG
 * 2.1 oranı alınarak, en kötü zemin brand-200 varsayımıyla):
 *
 *     metin        /80      /70      /60
 *     slate-900   16.25    15.52    14.74
 *     slate-700    9.43     9.00     8.55
 *     slate-600    6.90     6.59     6.26   ← gövde metni için taban
 *     slate-500    4.33     4.14     3.93   ← AA'yı (4.5) HİÇBİRİNDE geçmiyor
 *     brand-700    5.69     5.43     5.16
 *
 * ⚠️ CAM YÜZEYDE slate-500 GÖVDE METNİ KULLANMA. Beyaz kartta 4.76:1 ile AA'yı zar
 * zor geçiyordu; camın altındaki hafif ton onu eşiğin ALTINA itiyor. İkincil metin
 * için taban slate-600. slate-500 yalnızca büyük punto (18px+ / 14px bold) ya da
 * dekoratif işaretlerde kalabilir — bunlarda eşik 3.0.
 *
 * Opaklığı /80'in altına çekmek tabloyu tümüyle aşağı kaydırır; düşürülecekse
 * gövde metni tonları da birlikte koyulaştırılmalı.
 *
 * supports-[backdrop-filter] koruması: backdrop-blur desteklenmeyen tarayıcıda
 * bg-white/80 tek başına kalır ve kart yine okunur — cam efekti bir SÜS, okunurluk
 * ona bağlı değil.
 */
export function CamKart({ className = '', children }) {
  return (
    <div
      className={`rounded-2xl border border-white/70 bg-white/80 p-5 shadow-sm
                  shadow-brand-900/5 ring-1 ring-inset ring-white/40
                  supports-[backdrop-filter]:backdrop-blur-xl ${className}`}
    >
      {children}
    </div>
  )
}
