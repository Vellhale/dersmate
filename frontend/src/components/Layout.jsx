import { useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../state/AuthContext'
import { WalletProvider, useWallet } from '../state/WalletContext'
import { InboxProvider, useInbox } from '../state/InboxContext'
import { Logo } from './Logo'
import { CookieSettingsLink } from './CookieBanner'
import { ProductTour, RestartTourLink } from './ProductTour'
import { Avatar } from './Avatar'
import {
  MenuIkonu,
  PusulaIkonu,
  KitapIkonu,
  KisilerIkonu,
  MesajIkonu,
  KepIkonu,
  KalkanIkonu,
  CikisIkonu,
  BilgiIkonu,
  InstagramIkonu,
  TiktokIkonu,
  XIkonu,
} from './Ikonlar'

/*
  KABUK B — "sabit ray" (2026-08-20 tasarım kararı, tuvaldeki Masaüstü B yüzeyi).

  Gezinme artık üst barda yatay sekmeler değil; masaüstünde (lg+) her zaman açık duran
  koyu bir SOL SÜTUN. Sütun içeriği örtmez, İTER. Hamburger masaüstünde menüyü
  kapatmaz, DARALTIR: 272px etiketli hâl ↔ 76px yalnız-ikon hâli. lg altında ray yok;
  aynı içerik üstten açılan çekmecede.

  Zeminler koyu lacivert (slate-900 = #0F172A, markanın INK rengi — Logo.jsx'teki
  sabitle aynı). Vurgu tonu brand-300: koyu zeminde beyazla değil zeminle ölçülür ve
  #0F172A üzerinde 8.19:1 verir (AA 4.5). brand-500 koyu zeminde seçilemiyordu —
  Logo.jsx'in koyu tema tablosu bu ölçümleri zaten kayda geçirmişti; ray aynı karara
  yaslanıyor. Logo onDark varyantıyla çiziliyor (beyaz + brand-100), o da ölçülü.

  ÇIKIŞ TEK YERDE: üst barda metinsiz ikon (44px, aria-label'lı). Eski kabukta çıkış
  hem barda metin hem çekmecede satırdı ve tablette 47x16'lık ıskalanan bir kopya
  üretiyordu; şimdi her boyutta aynı tek düğme.
*/

/*
  tour: ürün turunun ışık tutacağı öğeler (bkz. lib/tour.js). Çıpa BURADA duruyor çünkü
  turun anlattığı şey gezinmenin kendisi — "şuraya git" derken ekranda o öğeyi
  göstermezse adım havada kalır. Çıpası olmayan adım ortada kart olarak çıkar; bu bir
  yedek, hedef değil.
*/
const NAV = [
  { to: '/kesfet', label: 'Keşfet', tour: 'discover', Ikon: PusulaIkonu },
  { to: '/portfolio', label: 'Ders Portföyü', tour: 'portfolio', Ikon: KitapIkonu },
  { to: '/eslesmeler', label: 'Eşleşmeler', Ikon: KisilerIkonu },
  { to: '/sohbet', label: 'Sohbet', Ikon: MesajIkonu },
  { to: '/dersler', label: 'Derslerim', tour: 'sessions', Ikon: KepIkonu },
]

/*
  Sosyal hesaplar tek sabitte: üç platformda da kullanıcı adı aynı (dersmate_) ve
  değişirse tek satır değişsin. Satırlar yalnız ikon + kullanıcı adı gösterir —
  "Instagram sayfamız" gibi açıklama metni bilinçli olarak yok (tasarım isteri).
*/
const SOSYAL = [
  { ad: 'Instagram', href: 'https://instagram.com/dersmate_', Ikon: InstagramIkonu },
  { ad: 'TikTok', href: 'https://tiktok.com/@dersmate_', Ikon: TiktokIkonu },
  { ad: 'X', href: 'https://x.com/dersmate_', Ikon: XIkonu },
]

// localStorage anahtarları peerlearn.* biçiminde (bkz. api.js, hwid.js) — F4: bu ad
// kullanıcıya görünmez, altyapı kimliğidir.
const RAY_KEY = 'peerlearn.raydar'

export default function Layout() {
  /*
    İki sağlayıcı da BURADA: ikisi de hem başlığı hem sayfaları besliyor ve Layout hiç
    unmount olmadığı için oturum boyunca tek kaynak kalıyorlar. Gelen kutusu ayrıca
    SignalR bağlantısını da taşıyor — sohbet sayfasında olsun olmasın canlı kalmalı.
  */
  return (
    <WalletProvider>
      <InboxProvider>
        <LayoutShell />
      </InboxProvider>
    </WalletProvider>
  )
}

function LayoutShell() {
  const { session, logout } = useAuth()
  const navigate = useNavigate()
  const [menuOpen, setMenuOpen] = useState(false)

  // Ray tercihi kalıcı: her sayfa yenilemesinde daraltmayı yeniden yapmak, tercihi
  // hiç hatırlamamakla aynı şey olurdu.
  const [rayDar, setRayDar] = useState(() => localStorage.getItem(RAY_KEY) === '1')
  const rayiDegistir = () => {
    setRayDar((v) => {
      localStorage.setItem(RAY_KEY, v ? '0' : '1')
      return !v
    })
  }

  // Unvan rozeti her sayfada görünür: kullanıcının biriktirdiği tek ölçü budur.
  const { wallet } = useWallet()

  /*
    Okunmamış mesaj rozeti de her sayfada. Ders saati ve toplantı linki YALNIZCA sohbetten
    kararlaştırılıyor; kullanıcı başka sayfadayken gelen mesajı fark etmezse randevulaşma
    sessizce kopuyor.
  */
  const { unreadTotal } = useInbox()

  const items = (
    session?.isAdmin ? [...NAV, { to: '/admin', label: 'Yönetim', Ikon: KalkanIkonu }] : NAV
  ).map((item) => (item.to === '/sohbet' ? { ...item, badge: unreadTotal } : item))

  const cikisYap = () => {
    logout()
    navigate('/giris')
  }

  return (
    <div className="min-h-[100dvh]">
      <header className="sticky top-0 z-40 h-16 bg-slate-900">
        <div className="flex h-full items-center justify-between px-3 sm:px-6">
          <div className="flex items-center gap-1">
            {/* İki hamburger, iki iş: lg altında çekmeceyi açar, lg üstünde rayı daraltır.
                Tek düğmeye iki işlev yüklemek aria-expanded'ı anlamsızlaştırıyordu. */}
            <button
              className="relative grid h-11 w-11 shrink-0 place-items-center rounded-lg text-white hover:bg-white/10 lg:hidden"
              onClick={() => setMenuOpen((v) => !v)}
              aria-label={unreadTotal > 0 ? `Menü — ${unreadTotal} okunmamış mesaj` : 'Menü'}
              aria-expanded={menuOpen}
            >
              <MenuIkonu />
              {/* Mobilde gezinme kapalı: rozet menünün İÇİNDE kalırsa hiç görünmez.
                  Hamburger üstündeki nokta "menüde bakılacak bir şey var" der. */}
              {unreadTotal > 0 && (
                <span className="absolute right-1.5 top-1.5 h-2 w-2 rounded-full bg-rose-500" />
              )}
            </button>
            <button
              className="hidden h-11 w-11 shrink-0 place-items-center rounded-lg text-white hover:bg-white/10 lg:grid"
              onClick={rayiDegistir}
              aria-label={rayDar ? 'Menüyü genişlet' : 'Menüyü daralt'}
              aria-expanded={!rayDar}
            >
              <MenuIkonu />
            </button>

            {/* Logo kilidi marka adını zaten içeriyor — yanına ayrıca metin konmaz.
                onDark: koyu barda ölçülü varyant (beyaz + brand-100), bkz. Logo.jsx. */}
            {/* -my-2/py-2: logonun görünen boyu değişmeden dokunma alanı 48px'e çıkıyor. */}
            <NavLink to="/" className="-my-2 flex shrink-0 items-center py-2 pl-1" aria-label="Ana sayfa">
              <Logo onDark className="h-8 w-auto sm:h-9" />
            </NavLink>
          </div>

          <div className="flex items-center gap-2 sm:gap-4">
            {/* Bakiye rozeti yerine UNVAN. Harcanmayan bir sayıyı her sayfada göstermenin
                anlamı yok; unvan ise kullanıcının biriktirdiği şeyi tek bakışta söylüyor
                ve profile götürüyor. Zemin brand-300: slate-900 metinle 8.19:1. */}
            <NavLink
              to="/profil"
              data-tour="rank"
              className="-my-2 flex min-h-11 shrink-0 items-center py-2"
              title={`${wallet?.rankTitle ?? 'Unvan'} — ${wallet?.totalEarnedCredits ?? 0} puan`}
            >
              <span className="inline-flex items-center gap-1 whitespace-nowrap rounded-full bg-brand-300 px-3 py-1 text-xs font-semibold text-slate-900">
                <span aria-hidden="true">{wallet?.rankEmoji ?? '🌱'}</span>
                <span className="hidden sm:inline">{wallet?.rankTitle ?? '—'}</span>
                <span className="sm:hidden">{wallet?.totalEarnedCredits ?? 0}</span>
              </span>
            </NavLink>

            {/* Avatar profile giden kısayol: sosyal bir üründe kendi profiline ulaşmanın
                en beklenen yolu fotoğrafına tıklamaktır. lg altında kimlik satırı
                çekmecenin tepesinde — barda avatara yer yok. */}
            <NavLink to="/profil" className="hidden shrink-0 lg:block" aria-label="Profilim" title="Profilim">
              <Avatar userId={session?.userId} name={session?.displayName} size="sm" />
            </NavLink>

            <span className="hidden text-sm font-medium text-white lg:block">
              {session?.displayName}
            </span>

            <span className="hidden h-6 w-px bg-white/20 lg:block" aria-hidden="true" />

            {/* Metinsiz çıkış: tasarım isteri. Erişilebilir ad aria-label'da. */}
            <button
              onClick={cikisYap}
              className="grid h-11 w-11 shrink-0 place-items-center rounded-lg text-slate-300 hover:bg-white/10 hover:text-white"
              aria-label="Çıkış yap"
              title="Çıkış yap"
            >
              <CikisIkonu className="h-[22px] w-[22px]" />
            </button>
          </div>
        </div>

        {/* lg altı çekmece: rayla aynı içerik, aynı koyu zemin. dvh: mobil adres çubuğu
            vh'ye dahil değil — vh kullanılsa alt satırlar çubuğun arkasında kalırdı. */}
        {menuOpen && (
          <nav className="flex max-h-[75dvh] flex-col overflow-y-auto border-t border-white/10 bg-slate-900 px-3 py-2 lg:hidden">
            {/* Kimlik satırı çekmecenin tepesinde: barda avatar ve isme yer yok. */}
            <NavLink
              to="/profil"
              onClick={() => setMenuOpen(false)}
              className="mb-1 flex min-h-12 items-center gap-3 border-b border-white/10 px-3 pb-3 pt-1"
            >
              <Avatar userId={session?.userId} name={session?.displayName} size="sm" />
              <span className="text-sm font-medium text-white">{session?.displayName}</span>
            </NavLink>
            {items.map((item) => (
              <RayOgesi key={item.to} {...item} onClick={() => setMenuOpen(false)} />
            ))}
            <AltKume onTikla={() => setMenuOpen(false)} />
          </nav>
        )}
      </header>

      <div className="lg:flex">
        {/* Sabit ray (lg+). sticky + h-calc: bar 64px, ray kalan yüksekliğin tamamı.
            transition-[width]: daraltma animasyonun kendisi, sonradan süs değil. */}
        <aside
          className={`sticky top-16 hidden h-[calc(100dvh-4rem)] shrink-0 flex-col justify-between overflow-y-auto bg-slate-900 transition-[width] duration-300 lg:flex ${
            rayDar ? 'w-[76px]' : 'w-[272px]'
          }`}
        >
          <nav className="flex flex-col gap-1 p-3">
            {items.map((item) => (
              <RayOgesi key={item.to} {...item} dar={rayDar} />
            ))}
          </nav>
          <AltKume dar={rayDar} />
        </aside>

        <div className="min-w-0 flex-1">
          <main className="mx-auto max-w-6xl px-4 py-6">
            <Outlet />
          </main>

          {/* Rıza her zaman geri alınabilir olmalı: tercihi değiştirmenin yolu, vermenin
              yolu kadar erişilebilir olmadan rıza "özgür iradeyle verilmiş" sayılmaz. */}
          <footer className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-4 px-4 pb-8 pt-2">
            <CookieSettingsLink />
            <RestartTourLink />
          </footer>
        </div>
      </div>

      {/* Tur yalnızca giriş yapmış kullanıcıya gösterilir; bu yüzden Layout içinde. */}
      <ProductTour />
    </div>
  )
}

/*
  Ray/çekmece öğesi. dar: yalnız-ikon modu — etiket DOM'dan çıkar (görsel gizleme değil;
  272px'lik metin 76px'lik sütunda taşar), erişilebilir ad aria-label'a taşınır ve
  okunmamış rozeti sayı yerine ikon köşesinde nokta olur.
*/
function RayOgesi({ to, label, end, onClick, badge = 0, tour, Ikon, dar = false }) {
  return (
    <NavLink
      to={to}
      end={end}
      onClick={onClick}
      data-tour={tour}
      aria-label={dar ? (badge > 0 ? `${label} — ${badge} okunmamış mesaj` : label) : undefined}
      title={dar ? label : undefined}
      className={({ isActive }) =>
        `flex min-h-12 items-center rounded-xl text-[15px] transition ${
          dar ? 'justify-center' : 'gap-3.5 px-3.5'
        } ${
          isActive
            ? 'bg-brand-300/10 font-bold text-brand-300'
            : 'font-normal text-slate-200 hover:bg-white/5 hover:text-white'
        }`
      }
    >
      {({ isActive }) => (
        <>
          <span className="relative">
            {/* Aktif öğede ikon da kalınlaşır: renk körlüğünde vurgu yalnız renge kalmasın. */}
            <Ikon strokeWidth={isActive ? 2.4 : 2} />
            {dar && badge > 0 && (
              <span className="absolute -right-1 -top-1 h-2.5 w-2.5 rounded-full bg-rose-500" />
            )}
          </span>
          {!dar && <span className="whitespace-nowrap">{label}</span>}
          {!dar && badge > 0 && (
            <span
              className="rounded-full bg-rose-500 px-1.5 py-0.5 text-[10px] font-bold leading-none text-white"
              aria-label={`${badge} okunmamış mesaj`}
            >
              {badge > 99 ? '99+' : badge}
            </span>
          )}
        </>
      )}
    </NavLink>
  )
}

/* Rayın/çekmecenin alt kümesi: Hakkımızda + sosyal hesaplar. */
function AltKume({ dar = false, onTikla }) {
  return (
    <div className="flex flex-col gap-0.5 border-t border-white/10 p-3">
      <NavLink
        to="/hakkimizda"
        onClick={onTikla}
        aria-label={dar ? 'Hakkımızda' : undefined}
        title={dar ? 'Hakkımızda' : undefined}
        className={({ isActive }) =>
          `flex min-h-11 items-center rounded-xl text-sm font-semibold transition ${
            dar ? 'justify-center' : 'gap-3.5 px-3.5'
          } ${isActive ? 'text-brand-300' : 'text-brand-300/90 hover:bg-white/5 hover:text-brand-200'}`
        }
      >
        <BilgiIkonu className="h-5 w-5" />
        {!dar && <span className="whitespace-nowrap">Hakkımızda</span>}
      </NavLink>

      {SOSYAL.map(({ ad, href, Ikon }) => (
        <a
          key={ad}
          href={href}
          target="_blank"
          rel="noreferrer noopener"
          aria-label={`${ad}: dersmate_`}
          title={dar ? `${ad}: dersmate_` : undefined}
          className={`flex min-h-11 items-center rounded-xl text-sm text-slate-400 transition hover:bg-white/5 hover:text-slate-200 ${
            dar ? 'justify-center' : 'gap-3.5 px-3.5'
          }`}
        >
          <Ikon className="h-[18px] w-[18px]" />
          {!dar && <span className="whitespace-nowrap">dersmate_</span>}
        </a>
      ))}
    </div>
  )
}
