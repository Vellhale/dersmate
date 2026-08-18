import { useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../state/AuthContext'
import { WalletProvider, useWallet } from '../state/WalletContext'
import { InboxProvider, useInbox } from '../state/InboxContext'
import { Badge } from './ui'
import { Logo } from './Logo'
import { CookieSettingsLink } from './CookieBanner'
import { ProductTour, RestartTourLink } from './ProductTour'
import { Avatar } from './Avatar'

/*
  Panel ve Cüzdan sekmeleri kaldırıldı. Panel bir özet ekranıydı ve kullanıcıyı asıl
  işinden bir tık uzağa koyuyordu; Cüzdan ise harcanmayan bir bakiyeyi yönetiyordu —
  puan artık profilde unvan olarak görünüyor.
*/
/*
  tour: ürün turunun ışık tutacağı öğeler (bkz. lib/tour.js). Çıpa BURADA duruyor çünkü
  turun anlattığı şey gezinmenin kendisi — "şuraya git" derken ekranda o sekmeyi
  göstermezse adım havada kalır. Çıpası olmayan adım ortada kart olarak çıkar; bu bir
  yedek, hedef değil.
*/
const NAV = [
  { to: '/kesfet', label: 'Keşfet', tour: 'discover' },
  { to: '/portfolio', label: 'Ders Portföyü', tour: 'portfolio' },
  { to: '/eslesmeler', label: 'Eşleşmeler' },
  { to: '/sohbet', label: 'Sohbet' },
  { to: '/dersler', label: 'Derslerim', tour: 'sessions' },
]

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

  // Unvan rozeti her sayfada görünür: kullanıcının biriktirdiği tek ölçü budur.
  const { wallet } = useWallet()

  /*
    Okunmamış mesaj rozeti de her sayfada. Ders saati ve toplantı linki YALNIZCA sohbetten
    kararlaştırılıyor; kullanıcı Panel'de dururken gelen mesajı fark etmezse randevulaşma
    sessizce kopuyor.
  */
  const { unreadTotal } = useInbox()

  const items = (session?.isAdmin ? [...NAV, { to: '/admin', label: 'Yönetim' }] : NAV).map((item) =>
    item.to === '/sohbet' ? { ...item, badge: unreadTotal } : item,
  )

  return (
    <div className="min-h-screen">
      <header className="sticky top-0 z-40 border-b border-slate-200/80 bg-white/90 backdrop-blur">
        <div className="mx-auto flex max-w-6xl items-center gap-2 px-4 py-3 sm:gap-4">
          {/* 44x44: parmak hedefi alt sınırı. Önceki p-2 hâli 30x40 idi ve ıskalanıyordu. */}
          <button
            className="relative -ml-2 grid h-11 w-11 shrink-0 place-items-center rounded-lg text-lg text-slate-600 hover:bg-slate-100 lg:hidden"
            onClick={() => setMenuOpen((v) => !v)}
            aria-label={unreadTotal > 0 ? `Menü — ${unreadTotal} okunmamış mesaj` : 'Menü'}
            aria-expanded={menuOpen}
          >
            ☰
            {/* Mobilde gezinme kapalı: rozet menünün İÇİNDE kalırsa hiç görünmez.
                Hamburger üstündeki nokta "menüde bakılacak bir şey var" der. */}
            {unreadTotal > 0 && (
              <span className="absolute right-1.5 top-1.5 h-2 w-2 rounded-full bg-rose-500" />
            )}
          </button>

          {/* Logo kilidi marka adını zaten içeriyor — yanına ayrıca metin konmaz. */}
          {/* -my-2/py-2: logonun görünen boyu değişmeden dokunma alanı 48px'e çıkıyor. */}
          <NavLink
            to="/"
            className="-my-2 flex shrink-0 items-center py-2"
            aria-label="Ana sayfa"
          >
            <Logo className="h-8 w-auto sm:h-10" />
          </NavLink>

          <nav className="hidden flex-1 items-center gap-1 lg:flex">
            {items.map((item) => (
              <NavItem key={item.to} {...item} />
            ))}
          </nav>

          <div className="ml-auto flex items-center gap-3 lg:ml-0">
            {/* Bakiye rozeti yerine UNVAN. Harcanmayan bir sayıyı her sayfada göstermenin
                anlamı yok; unvan ise kullanıcının biriktirdiği şeyi tek bakışta söylüyor
                ve profile götürüyor. Puan da yanında duruyor ki unvanın neye dayandığı
                görünsün. */}
            <NavLink
              to="/profil"
              data-tour="rank"
              className="-my-2 flex min-h-11 shrink-0 items-center gap-1.5 py-2 sm:gap-2"
              title={`${wallet?.rankTitle ?? 'Unvan'} — ${wallet?.totalEarnedCredits ?? 0} puan`}
            >
              <Badge tone="brand" className="whitespace-nowrap">
                <span aria-hidden="true">{wallet?.rankEmoji ?? '🌱'}</span>{' '}
                <span className="hidden sm:inline">{wallet?.rankTitle ?? '—'}</span>
                <span className="sm:hidden">{wallet?.totalEarnedCredits ?? 0}</span>
              </Badge>
            </NavLink>

            {/* lg altında gizli: isim ve çıkış zaten hamburger menüsünde 44px'lik satırlar
                olarak var. Burada bırakılınca tablette 47x16'lık, parmakla ıskalanan ikinci
                bir "Çıkış yap" oluyordu — aynı işlevin iki kopyası, biri kullanılamaz. */}
            {/* Avatar profile giden kısayol: sosyal bir üründe kendi profiline ulaşmanın
                en beklenen yolu fotoğrafına tıklamaktır. */}
            <NavLink
              to="/profil"
              className="hidden shrink-0 lg:block"
              aria-label="Profilim"
              title="Profilim"
            >
              <Avatar userId={session?.userId} name={session?.displayName} size="sm" />
            </NavLink>

            <div className="hidden text-right lg:block">
              <div className="text-sm font-medium text-slate-700">{session?.displayName}</div>
              <button
                onClick={() => {
                  logout()
                  navigate('/giris')
                }}
                className="text-xs text-slate-500 underline hover:text-slate-700"
              >
                Çıkış yap
              </button>
            </div>
          </div>
        </div>

        {menuOpen && (
          <nav className="flex max-h-[70dvh] flex-col overflow-y-auto border-t border-slate-200 px-4 py-2 lg:hidden">
            {items.map((item) => (
              // Menüdeki satırlar parmakla seçilir: masaüstündeki kompakt py-2 yerine 44px.
              <NavItem
                key={item.to}
                {...item}
                className="min-h-11 items-center py-3"
                onClick={() => setMenuOpen(false)}
              />
            ))}
            <div className="mt-1 border-t border-slate-100 pt-1">
              {/* Mobilde profil bağlantısı burada: üst barda avatara yer yok. */}
              <NavLink
                to="/profil"
                onClick={() => setMenuOpen(false)}
                className="flex min-h-11 items-center gap-2 px-3 py-2"
              >
                <Avatar userId={session?.userId} name={session?.displayName} size="sm" />
                <span className="text-sm font-medium text-slate-700">
                  {session?.displayName}
                </span>
              </NavLink>
              <button
                onClick={() => {
                  logout()
                  navigate('/giris')
                }}
                className="min-h-11 w-full px-3 py-3 text-left text-sm text-slate-500"
              >
                Çıkış yap
              </button>
            </div>
          </nav>
        )}
      </header>

      <main className="mx-auto max-w-6xl px-4 py-6">
        <Outlet />
      </main>

      {/* Rıza her zaman geri alınabilir olmalı: tercihi değiştirmenin yolu, vermenin
          yolu kadar erişilebilir olmadan rıza "özgür iradeyle verilmiş" sayılmaz. */}
      <footer className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-4 px-4 pb-8 pt-2">
        <CookieSettingsLink />
        <RestartTourLink />
      </footer>

      {/* Tur yalnızca giriş yapmış kullanıcıya gösterilir; bu yüzden Layout içinde. */}
      <ProductTour />
    </div>
  )
}

function NavItem({ to, label, end, onClick, className = '', badge = 0, tour }) {
  return (
    <NavLink
      to={to}
      end={end}
      onClick={onClick}
      data-tour={tour}
      className={({ isActive }) =>
        `flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition ${
          isActive ? 'bg-brand-50 text-brand-700' : 'text-slate-600 hover:bg-slate-100'
        } ${className}`
      }
    >
      {label}
      {badge > 0 && (
        <span
          className="rounded-full bg-rose-500 px-1.5 py-0.5 text-[10px] font-bold leading-none text-white"
          aria-label={`${badge} okunmamış mesaj`}
        >
          {badge > 99 ? '99+' : badge}
        </span>
      )}
    </NavLink>
  )
}
