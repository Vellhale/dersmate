import { Link } from 'react-router-dom'
import { useConsent } from '../state/ConsentContext'
import { KunyeSatiri } from './Kunye'
import { rehberiYenidenBaslat } from './ProductTour'

/*
  ALT BİLGİ — TEK BİLEŞEN, İKİ YÜZEY (2026-09-26).

  Kabuğun (Layout) ve giriş/kayıt kabuğunun (AuthShell) alt bilgisi eskiden iki ayrı
  yerde elle yazılıyordu. Metinler aynıydı ama iki şey ayrışmıştı:
    • SIRA: eylemler (Çerez tercihleri, Rehberi tekrar izle) öndeydi, yasal sayfalar
      sonda. Şimdi yasal üçlü önde, tercih eylemleri arkada; arada ince bir çizgi.
    • DOKUNMA HEDEFİ: iki düğme lg altında 44px kalıbını taşıyordu, üç yasal bağlantı
      taşımıyordu (16px yüksekliğinde "Gizlilik"). Kalıp artık tek sabitte:
      ALT_BAGLANTI. "Çerez tercihleri" ve "Rehberi tekrar izle" düğmeleri de burada
      yazılıyor; ayrı bileşenleri (CookieSettingsLink, RestartTourLink) kalktı, yoksa
      aynı sınıf dizesi üç dosyada yaşardı.

  ⛔ ÇEREZ TERCİHLERİ VE HESAP SİLME ALT BİLGİDEN ÇIKMAZ — profildeki ayarlar menüsü
  aynı işleri yapsa bile:
    • Rıza her zaman geri alınabilir olmalı: tercihi değiştirmenin yolu, vermenin yolu
      kadar erişilebilir olmadan rıza "özgür iradeyle verilmiş" sayılmaz. Çerez
      penceresinin kendisi de "sayfanın altındaki “Çerez tercihleri” bağlantısından
      değiştirebilirsin" diyor.
    • Hesap silme sayfası mağaza kaydındaki silme adresi: sitede bulunabilir olmalı,
      yalnızca adresi yazılarak ulaşılan gizli bir sayfa değil. ÇIKIŞ YAPMIŞ kullanıcıya
      da görünmeli — hesabına giremeyen birinin de bulabileceği yer AuthShell'in alt
      bilgisi.
  Yasal metinler de iki yüzeyde: oturum açtıktan sonra bunlara ulaşmanın başka yolu
  yok. Metni okumak, kabul ettikten sonra da mümkün olmalı.

  "Rehberi tekrar izle" yalnızca `oturumlu` iken: tur Layout'ta kuruluyor, giriş
  ekranında dinleyen yok.

  ─── DÜZEN ────────────────────────────────────────────────────────────────────
  Künye AYRI SATIRDA, bağlantı şeridinin İÇİNDE değil: bu bir gezinme bağlantısı değil,
  kimlik beyanı (aynı sırada "Hesap silme"nin komşusu gibi okunuyordu) ve şerit dar
  ekranda sarıldığında künye iki bağlantının ortasında kalıyordu.
    • Kabuk: dar ekranda bağlantılar üstte, künye altta; lg'de künye solda, bağlantılar
      sağda (klasik site alt bilgisi). DOM sırası lg'deki görsel sırayla aynı (künye
      önce); lg altında `flex-col-reverse` görsel sırayı çeviriyor, odak sırası
      künyeden başlıyor. Kabul edilen fark: lg altı dokunmayla kullanılıyor.
    • `yigin` (AuthShell'in dar form sütunu): her kırılımda sütun, bağlantılar üstte.
      Burada lg'de de sütun olduğu için DOM sırası görsel sırayla aynı tutuldu
      (bağlantılar önce). lg altında ortalı, lg'de sola hizalı — üstündeki başlık ve
      para transferi notuyla aynı.

  ─── 44px HEDEFLER ÜST ÜSTE BİNMESİN ───────────────────────────────────────────
  ALT_BAGLANTI hedefi 44px'e büyütüp -my-2 ile satırı 28px'te tutuyor; yani hedef
  satırın 8px üstüne ve altına taşıyor. Satırlar arası boşluk 16px'ten azsa iki satırın
  hedefleri üst üste biner ve örtüşen şeritteki dokunuş öbür bağlantıya gider (44px
  yalnızca kâğıt üstünde kalır). Bu yüzden lg altında satır arası `gap-y-4`, şeritle
  künye arası `gap-4`: 28 + 16 = 44, hedefler kenar kenara. lg'de hedefler büyütülmediği
  için aralık daralıyor.

  ─── YÜKSEKLİK İKİ SAYFANIN HESABINDA ──────────────────────────────────────────
  Ekrana sabitlenen sayfalar kabuğun alt bilgisini çıkararak boy hesaplıyor:
  Matches.jsx (lg: 100dvh − 12.25rem) ve Chat.jsx (PANEL_YUKSEKLIGI). Ölçüm
  (2026-09-26, Layout içinde): lg'de 81px ve 1024–1440px arasında sabit (tek satır);
  390px'te 173px (iki satır bağlantı + künye). Buraya satır, dolgu ya da bağlantı
  eklenirse o iki sayfada sayfa kayması yeniden ölçülmeli.
*/

/** Alt bilgi bağlantılarının ve düğmelerinin tek sınıf dizesi. */
export const ALT_BAGLANTI =
  '-my-2 inline-flex min-h-11 items-center py-2 text-xs text-slate-500 underline ' +
  'hover:text-slate-700 lg:my-0 lg:min-h-0 lg:py-0'

export function AltBilgi({ oturumlu = false, yigin = false, className = '' }) {
  const { openSettings } = useConsent()

  const kunye = <KunyeSatiri className={yigin ? 'text-center lg:text-left' : ''} />

  const baglantilar = (
    <nav aria-label="Alt bilgi">
      <ul
        className={`flex flex-wrap items-center gap-x-4 gap-y-4 lg:gap-y-1 ${
          yigin ? 'justify-center lg:justify-start' : ''
        }`}
      >
        <li>
          <Link to="/kosullar" className={ALT_BAGLANTI}>
            Kullanım koşulları
          </Link>
        </li>
        <li>
          <Link to="/gizlilik" className={ALT_BAGLANTI}>
            Gizlilik
          </Link>
        </li>
        <li>
          <Link to="/hesap-silme" className={ALT_BAGLANTI}>
            Hesap silme
          </Link>
        </li>
        {/* Yasal sayfalar ile tercih eylemleri arasındaki çizgi. Dar ekranda şerit
            sarıldığında satır başında asılı kalırdı; orada yok. */}
        <li aria-hidden="true" className="hidden h-3 w-px bg-slate-300 sm:block" />
        <li>
          <button type="button" onClick={openSettings} className={ALT_BAGLANTI}>
            Çerez tercihleri
          </button>
        </li>
        {oturumlu && (
          <li>
            <button type="button" onClick={rehberiYenidenBaslat} className={ALT_BAGLANTI}>
              Rehberi tekrar izle
            </button>
          </li>
        )}
      </ul>
    </nav>
  )

  return (
    <div
      className={`flex gap-4 border-t border-slate-200/70 pt-4 ${
        yigin
          ? 'flex-col lg:gap-2'
          : 'flex-col-reverse lg:flex-row lg:items-center lg:justify-between lg:gap-x-6'
      } ${className}`}
    >
      {yigin ? (
        <>
          {baglantilar}
          {kunye}
        </>
      ) : (
        <>
          {kunye}
          {baglantilar}
        </>
      )}
    </div>
  )
}
