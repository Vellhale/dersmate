/**
 * Marka kilidi. Kaynak: resimler/gemini-svg.svg — buraya satır içi (inline) kopyalandı ki
 * ayrı bir ağ isteği gerektirmesin ve tema/boyut sınıflarıyla doğrudan yönetilebilsin.
 *
 * Boyut YALNIZCA yükseklikten verilir (h-8 gibi); genişlik viewBox oranından (280:80 = 3.5)
 * türer. Kaynak dosyadaki width/height="100%" bilerek kaldırıldı — kapsayıcıyı taşırıyordu.
 */

/* Üç marka rengi tek yerde. Kaynak SVG koyu zeminliydi; açık zemine çevirirken BG ile INK
   yer değiştirdi — aksi halde beyaz yazı açık zeminde kaybolurdu. Temayı yeniden çevirmek
   istersen bu iki değeri takas etmen yeterli, geri kalan her şey bunlardan türüyor.
   BG = brand-50 (tailwind.config.js): giriş ekranı gradyanının başlangıç rengi.

   F1b'de ikisi de düzeltildi:
   • ACCENT #38BDF8 → #0088CC (brand-500). Eskisi beyaz üzerinde 2.14:1 veriyordu — büyük
     metin için bile yetersiz (AA eşiği 3.0). Yenisi 3.89:1 ile geçiyor VE marka rengiyle
     aynı. 400 DEĞİL 500: #0088CC gövde rengi olarak AA'yı kaçırdığı için kimlik 500'de,
     zemin görevi 600'de duruyor (bkz. docs/DEVAM-EDILECEK.md, F1b).
   • BG #EEF2FF indigo-50'ydi: logo, izlediğini söylediği brand-50 ile farklı bir renk
     ailesindeydi. Artık gerçekten brand-50.
   Aynı üç değer favicon.svg'de de birebir geçiyor (aynı geometri); birini değiştirirsen
   diğerini de değiştir. */
const BG = '#E6F4FB'

/*
  KOYU ZEMİN VURGUSU — açık zemindekiyle AYNI OLAMAZ, ölçüldü.

  Giriş ekranının sol paneli brand-600'den (#0077B3) başlayan bir gradyan ve logo tam o
  köşede duruyor. ACCENT (#0088CC, brand-500) orada zeminin bir basamak komşusu:
  ölçülen kontrast 1.26:1 — "mate" hecesi gözle seçilemiyordu.

  Skalanın koyu zemindeki karşılıkları (zemin #0077B3, WCAG 2.1):
    brand-500  1.26:1   brand-400  1.80:1   brand-300  2.33:1
    brand-200  3.01:1   brand-100  3.86:1   brand-50   4.36:1

  brand-100 seçildi: 3.0 eşiğini rahat geçiyor (200 tam sınırda ve gradyan boyunca zemin
  değiştiği için güvenli değil), ama beyaza da kaçmıyor — "ders" hâlâ saf beyaz, "mate"
  hâlâ mavi tonda, yani logo iki temada da AYNI logo.

  F1b notu bunu zaten söylüyordu: "Aynı rengi iki temada kullanmak zorunlu değil; zorunlu
  olan aynı skaladan gelmesi." (docs/DEVAM-EDILECEK.md)
*/
const ACCENT_DARK = '#CCE9F7'
const INK = '#0F172A'
const ACCENT = '#0088CC'

/*
  onDark: koyu zeminde kullanım. INK (#0F172A) koyu bir lacivert — koyu panelin üstünde
  "ders" hecesi ve düğüm dairesi kayboluyordu. Karartılmış zeminde mürekkep beyaza döner;
  ACCENT ve geometri aynı kalır, yani logo iki zeminde de AYNI logodur.
  BG (köprü çizgisi) de zeminle uyumlu olmalı — koyu tarafta saydam bırakılıyor.
*/
export function Logo({ className = 'h-8 w-auto', title = 'dersmate', onDark = false }) {
  const mürekkep = onDark ? '#FFFFFF' : INK
  const vurgu = onDark ? ACCENT_DARK : ACCENT
  return (
    <svg viewBox="0 0 280 80" role="img" aria-label={title} className={className}>
      {/* Zemin dikdörtgeni bilerek yok: logo saydam, arkasındaki yüzey ne ise o görünür.
          viewBox aynı kaldığı için hizalama ve boşluklar değişmedi. Diğer öğelere (daireler,
          köprü, yazı) dokunulmadı — köprü hâlâ BG rengiyle çizilen bir çizgidir. */}

      {/* İki kullanıcı düğümü + aralarındaki köprü: akran eşleşmesi.
          Köprü zemin rengiyle çizilir — boya değil, dairelerin arasını kesen boşluktur. */}
      <g transform="translate(18, 16)">
        <circle cx="16" cy="24" r="10" fill={vurgu} opacity="0.95" />
        <circle cx="34" cy="24" r="10" fill={mürekkep} opacity="0.95" />
        <path d="M 16 24 Q 25 14, 34 24" stroke={onDark ? 'none' : BG} strokeWidth="3" fill="none" strokeLinecap="round" />
      </g>

      <text
        x="75"
        y="49"
        fontFamily="system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
        fontSize="26"
        fontWeight="700"
        letterSpacing="-0.5"
      >
        <tspan fill={mürekkep}>ders</tspan>
        <tspan fill={vurgu}>mate</tspan>
      </text>
    </svg>
  )
}

/** Yazısız kare rozet — dar alanlar için (favicon ile aynı geometri). */
export function LogoMark({ className = 'h-8 w-8', title = 'dersmate' }) {
  return (
    <svg viewBox="0 0 64 64" role="img" aria-label={title} className={className}>
      <rect width="64" height="64" fill={BG} rx="14" />
      <circle cx="23" cy="34" r="11" fill={vurgu} opacity="0.95" />
      <circle cx="41" cy="34" r="11" fill={INK} opacity="0.95" />
      <path d="M 23 34 Q 32 22, 41 34" stroke={BG} strokeWidth="3.5" fill="none" strokeLinecap="round" />
    </svg>
  )
}
