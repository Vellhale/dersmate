/**
 * Marka kilidi. Kaynak: resimler/gemini-svg.svg — buraya satır içi (inline) kopyalandı ki
 * ayrı bir ağ isteği gerektirmesin ve tema/boyut sınıflarıyla doğrudan yönetilebilsin.
 *
 * Boyut YALNIZCA yükseklikten verilir (h-8 gibi); genişlik viewBox oranından türer.
 */

/* Üç marka rengi tek yerde. Kaynak SVG koyu zeminliydi; açık zemine çevirirken BG ile INK
   yer değiştirdi — aksi halde beyaz yazı açık zeminde kaybolurdu.
   BG = brand-50 (tailwind.config.js): giriş ekranı gradyanının başlangıç rengi.

   F1b'de ikisi de düzeltildi:
   • ACCENT #38BDF8 → #0088CC (brand-500). Eskisi beyaz üzerinde 2.14:1 veriyordu — büyük
     metin için bile yetersiz (AA eşiği 3.0). Yenisi 3.89:1 ile geçiyor VE marka rengiyle
     aynı. 400 DEĞİL 500: #0088CC gövde rengi olarak AA'yı kaçırdığı için kimlik 500'de,
     zemin görevi 600'de duruyor (bkz. docs/DEVAM-EDILECEK.md, F1b).
   • BG #EEF2FF indigo-50'ydi: logo, izlediğini söylediği brand-50 ile farklı bir renk
     ailesindeydi. Artık gerçekten brand-50.
   Aynı üç değer favicon.svg'de de birebir geçiyor (LogoMark ile aynı geometri);
   birini değiştirirsen diğerini de değiştir. */
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
*/
const ACCENT_DARK = '#CCE9F7'
const INK = '#0F172A'
const ACCENT = '#0088CC'

/*
  ─────────────────────────────────────────────────────────────────────────────
  2026-08-24 YENİDEN ÇİZİM. Üç somut kusur vardı; üçü de ölçülerek düzeltildi.

  1. SOLUK GÖRÜNÜYORDU. İki daire de `opacity="0.95"` taşıyordu ve aralarındaki
     "köprü", zemin rengiyle (BG) çizilmiş 3 birimlik bir yaydı. Yani işaret, üst üste
     binen iki yarı saydam dairenin arasına zemin rengi sürülerek elde ediliyordu:
     h-9 boyutunda (36px) bu, kenarları yıkanmış bir leke gibi okunuyordu. Daha kötüsü,
     köprü koyu zeminde `stroke="none"` oluyordu — yani üst barda daireler ayrılmıyor,
     tek bir bulanık kütle hâline geliyordu.

     Şimdi: tam opak iki nokta, aralarında GERÇEK boşluk. Zemin rengiyle boyanmış bir
     kesik yok, dolayısıyla işaret her zeminde aynı görünüyor.

  2. KENDİ KUTUSUNDA ORTALI DEĞİLDİ. Tarayıcıda ölçüldü: mürekkep 24 → 221.3 arasını
     kaplıyordu (merkez 122.6), viewBox ise 0 → 280 (merkez 140). Yani logonun görsel
     merkezi kutusunun 17.4 birim solundaydı. Üst barda logo mutlak merkeze konduğu için
     (bkz. Layout.jsx) bu fark doğrudan ekrana yansıyordu: h-11'de ~9.5px sola kayık.

     Şimdi: kilit kutunun içinde ortalandı — iki yanda eşit boşluk var (ölçüm aşağıda).

  3. İKİ AYRI VARYANT VARDI. `vurgulu` bayrağı puntoyu 26 → 34 çıkarıyordu ve iki
     varyantın metin genişliği farklı olduğu için ikisi aynı viewBox'ta AYNI ANDA
     ortalanamıyordu. Bayrak kaldırıldı: tek geometri var, boyut yalnızca yükseklik
     sınıfından geliyor (h-8 / h-9 / h-11). Aynı logo her yerde aynı oranlarda.

  ⚠️ RENKLER VE `circle` ÖĞELERİ SÖZLEŞMEDİR. e2e/marka.spec.js logonun daire
  dolgularını paletle karşılaştırıyor (yalnız brand-500 ve brand-100 serbest) ve
  e2e/kaynak-sabitleri.spec.js buradaki ACCENT/BG sabitlerinin palete eşit olmasını
  şart koşuyor. Geometri ve tipografi serbest; renk ve `circle` kullanımı değil.
  ─────────────────────────────────────────────────────────────────────────────

  ÖLÇÜLER (viewBox 0 0 228 80):
    işaret : r=9 daireler, merkezler x=25 ve x=47 → mürekkep 16 … 56
    yazı   : x=68, taban çizgisi 52, punto 34 → tarayıcıda ölçüldü, genişlik 143.6 → 68 … 211.6
    boşluk : solda 16, sağda 16.4 → mürekkebin merkezi 113.8, kutunun merkezi 114

  KUTU NEDEN MÜREKKEBE TAM KESİLMEDİ (228, oysa 212 yeterdi): sağdaki ~16 birim,
  metin genişliğinin platforma göre değişmesine karşı PAY. Yazı tipi yığını sistem
  fontuna düşüyor ve aynı punto her işletim sisteminde aynı genişlikte çizilmiyor;
  kutuyu mürekkebe yapıştırmak, bir platformda son harfin kırpılması demekti.
  Pay iki yana eşit dağıtıldı — güvenlik payı korunurken ortalama bozulmuyor.
*/
export function Logo({ className = 'h-8 w-auto', title = 'dersmate', onDark = false }) {
  const mürekkep = onDark ? '#FFFFFF' : INK
  const vurgu = onDark ? ACCENT_DARK : ACCENT

  return (
    <svg viewBox="0 0 228 80" role="img" aria-label={title} className={className}>
      {/*
        İki düğüm: akran eşleşmesi. Aralarındaki 4 birimlik boşluk BOYA DEĞİL, gerçek
        boşluk — bu yüzden açık ve koyu zeminde birebir aynı görünüyor. Eski çizimde
        buradaki ayrım zemin renkli bir yayla yapılıyordu ve koyu temada kayboluyordu.
      */}
      <circle cx="25" cy="40" r="9" fill={vurgu} />
      <circle cx="47" cy="40" r="9" fill={mürekkep} />

      {/*
        Tipografi: tek ağırlık (700), sıkı ama nefes alan harf aralığı. 800 denendi ve
        küçük boyutta harfler birbirine yapışıyordu — üst barda logo 36px yüksekliğinde
        çiziliyor, yani gerçek punto ~15px. O ölçekte ağırlık değil KONTRAST okunurluk
        veriyor: "ders" mürekkep, "mate" marka tonu.
      */}
      <text
        x="68"
        y="52"
        fontFamily="system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
        fontSize="34"
        fontWeight="700"
        letterSpacing="-1"
      >
        <tspan fill={mürekkep}>ders</tspan>
        <tspan fill={vurgu}>mate</tspan>
      </text>
    </svg>
  )
}

/**
 * Yazısız kare rozet — dar alanlar için (favicon ile aynı geometri).
 *
 * ⚠️ ÇALIŞMA ZAMANI HATASI DÜZELTİLDİ (2026-08-24): ilk daire `fill={vurgu}` diyordu ama
 * `vurgu` yalnızca `Logo` fonksiyonunun İÇİNDE tanımlı bir yerel değişken — burada
 * tanımsızdı ve bileşen render edilse `ReferenceError` atardı. Hata bugüne kadar
 * görünmedi çünkü `LogoMark` hiçbir yerden çağrılmıyor; yani derleme de test de bunu
 * yakalayamazdı. Doğrusu açık zeminin vurgu rengi: ACCENT.
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
