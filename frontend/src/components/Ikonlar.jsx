/*
  Gezinme kabuğunun ikon seti — tamamı satır içi SVG, tek çizgi ağırlığı, 24'lük ızgara.

  NEDEN KÜTÜPHANE DEĞİL: beş menü ikonu + üç sosyal ikon için lucide-react eklemek,
  bağımlılık ağacına ~30 KB ve bir sürüm takip yükü koyar. Çizgiler currentColor
  kullanır, yani renk ve kalınlık kullanıldığı yerden yönetilir (aktif menü öğesi
  kalın çizgiyle vurgulanıyor — bkz. Layout.jsx).

  Sosyal ikonlar platformların resmî logo geometrisinin sadeleştirilmiş çizgi
  yorumlarıdır; marka kılavuzu birebir logo isterse assets'ten gerçek SVG'ye geçilir.
*/

function Cizgi({ children, strokeWidth = 2, className = 'h-6 w-6' }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={strokeWidth}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={`shrink-0 ${className}`}
      aria-hidden="true"
    >
      {children}
    </svg>
  )
}

export function MenuIkonu(props) {
  return (
    <Cizgi {...props}>
      <path d="M4 6h16" /><path d="M4 12h16" /><path d="M4 18h16" />
    </Cizgi>
  )
}

/*
  KEŞFET İKONU — pusula değil BÜYÜTEÇ (2026-08-24).

  Pusula "yön bul" der; Keşfet sayfasının yaptığı iş ise aramak: kullanıcı konu yazıyor,
  filtre açıyor, eğitmen listesini süzüyor. Büyüteç bu işin evrensel işareti ve ekrandaki
  arama kutusuyla aynı şeyi söylüyor — pusula, sayfayı hiç görmemiş birine yanlış söz
  veriyordu.

  Geometri Lucide'ın `search` ikonuyla aynı (r=8 halka + 45° sap). Lucide PAKET OLARAK
  EKLENMEDİ: bu dosyanın başındaki gerekçe hâlâ geçerli — sekiz ikon için bir bağımlılık
  ağacı ve sürüm takibi taşımak gereksiz. Alınan şey çizim, kütüphane değil.
*/
export function AramaIkonu(props) {
  return (
    <Cizgi {...props}>
      <circle cx="11" cy="11" r="8" /><path d="m21 21-4.3-4.3" />
    </Cizgi>
  )
}

export function KitapIkonu(props) {
  return (
    <Cizgi {...props}>
      <path d="M2 4h6a4 4 0 0 1 4 4v12a3 3 0 0 0-3-3H2z" />
      <path d="M22 4h-6a4 4 0 0 0-4 4v12a3 3 0 0 1 3-3h7z" />
    </Cizgi>
  )
}

export function KisilerIkonu(props) {
  return (
    <Cizgi {...props}>
      <circle cx="9" cy="7" r="4" /><path d="M2 21v-2a4 4 0 0 1 4-4h6a4 4 0 0 1 4 4v2" />
      <path d="M16 3.1a4 4 0 0 1 0 7.8" /><path d="M22 21v-2a4 4 0 0 0-3-3.9" />
    </Cizgi>
  )
}

export function MesajIkonu(props) {
  return (
    <Cizgi {...props}>
      <path d="M21 11.5a8.4 8.4 0 0 1-8.5 8.4 8.6 8.6 0 0 1-3.9-.9L3 21l1.9-5.6a8.4 8.4 0 1 1 16.1-3.9z" />
    </Cizgi>
  )
}

export function KepIkonu(props) {
  return (
    <Cizgi {...props}>
      <path d="M22 10 12 5 2 10l10 5z" /><path d="M6 12v5c0 1.7 2.7 3 6 3s6-1.3 6-3v-5" />
    </Cizgi>
  )
}

export function KalkanIkonu(props) {
  return (
    <Cizgi {...props}>
      <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
    </Cizgi>
  )
}

export function CikisIkonu(props) {
  return (
    <Cizgi {...props}>
      <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
      <path d="M16 17l5-5-5-5" /><path d="M21 12H9" />
    </Cizgi>
  )
}

export function BilgiIkonu(props) {
  return (
    <Cizgi {...props}>
      <circle cx="12" cy="12" r="10" /><path d="M12 16v-4" /><path d="M12 8h.01" />
    </Cizgi>
  )
}

export function InstagramIkonu(props) {
  return (
    <Cizgi {...props}>
      <rect x="2" y="2" width="20" height="20" rx="5" /><circle cx="12" cy="12" r="4" />
      <circle cx="17.5" cy="6.5" r="0.5" fill="currentColor" />
    </Cizgi>
  )
}

export function TiktokIkonu(props) {
  return (
    <Cizgi {...props}>
      <path d="M14 3v9.6a3.4 3.4 0 1 1-2.4-3.3" /><path d="M14 3a5.6 5.6 0 0 0 5.6 5.6" />
    </Cizgi>
  )
}

export function XIkonu(props) {
  return (
    <Cizgi {...props}>
      <path d="M4 4l16 16" /><path d="M20 4 4 20" />
    </Cizgi>
  )
}

/*
  ── HAKKIMIZDA SAYFASININ İKONLARI ────────────────────────────────────────────
  Aynı 24'lük ızgara, aynı çizgi ağırlığı. Bu sayfada ikonlar gezinme değil ANLAM
  taşıyor: her kutucuğun ne anlattığını metni okumadan önce söylüyorlar. Bu yüzden
  hepsi tek bir kavramı gösteriyor — süs değil, başlığın görsel karşılığı.
*/

/** Misyon: hedef tahtası. "Bugün ne yapıyoruz" — nişan alınmış, belirli bir şey. */
export function HedefIkonu(props) {
  return (
    <Cizgi {...props}>
      <circle cx="12" cy="12" r="9" /><circle cx="12" cy="12" r="5" /><circle cx="12" cy="12" r="1.5" />
    </Cizgi>
  )
}

/** Vizyon: ufuk çizgisinin üstünde yükselen güneş. "Neye doğru gidiyoruz" — henüz uzak. */
export function UfukIkonu(props) {
  return (
    <Cizgi {...props}>
      <path d="M3 18h18" /><path d="M6.5 18a5.5 5.5 0 0 1 11 0" />
      <path d="M12 4v2.5" /><path d="M4.9 7.4l1.8 1.8" /><path d="M19.1 7.4l-1.8 1.8" />
    </Cizgi>
  )
}

/** Güvence: ücretsizlik. Üzeri çizili etiket — "fiyat yok". */
export function EtiketsizIkonu(props) {
  return (
    <Cizgi {...props}>
      <path d="M20.6 13.4 13.4 20.6a2 2 0 0 1-2.8 0l-7.2-7.2A2 2 0 0 1 2.8 12V4.8A2 2 0 0 1 4.8 2.8H12a2 2 0 0 1 1.4.6l7.2 7.2a2 2 0 0 1 0 2.8Z" />
      <path d="M7.5 7.5h.01" /><path d="M3 21 21 3" />
    </Cizgi>
  )
}

/** Güvence: para dolaşmıyor. Üzeri çizili cüzdan. */
export function CuzdansizIkonu(props) {
  return (
    <Cizgi {...props}>
      {/* Banknot + üzeri çizgi. Cüzdan silueti denendi ve 20px'te bulanıklaştı: kapak,
          dikiş ve toka çizgileri o ölçekte tek bir gri lekeye dönüşüyordu. Dikdörtgen +
          daire aynı anlamı üç çizgide veriyor ve küçük boyutta ayakta kalıyor. */}
      <rect x="2.5" y="6" width="19" height="12" rx="2" />
      <circle cx="12" cy="12" r="2.5" /><path d="M3 21 21 3" />
    </Cizgi>
  )
}

/** Güvence: puan anlatana yazılır. Yükselen çizgi + yıldız. */
export function ArtanIkonu(props) {
  return (
    <Cizgi {...props}>
      <path d="M3 17l5-5 3.5 3.5L20 7" /><path d="M15 7h5v5" />
    </Cizgi>
  )
}

/** Onay: daire içinde tik. Güvence listelerinde madde işareti olarak kullanılıyor. */
export function OnayIkonu(props) {
  return (
    <Cizgi {...props}>
      <circle cx="12" cy="12" r="9" /><path d="m8.5 12 2.5 2.5 4.5-5" />
    </Cizgi>
  )
}

/** Kanıt/güven: kalkan içinde tik. "Her ders kanıtla kapanır" maddesi için. */
export function KanitIkonu(props) {
  return (
    <Cizgi {...props}>
      <path d="M12 3l7 3v5.5c0 4.3-2.9 8.3-7 9.5-4.1-1.2-7-5.2-7-9.5V6z" />
      <path d="m9 12 2 2 4-4" />
    </Cizgi>
  )
}
