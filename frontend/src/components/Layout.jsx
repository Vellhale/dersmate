import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../state/AuthContext'
import { useConsent } from '../state/ConsentContext'
import { RAIL_KEY } from '../lib/consent'
import { WalletProvider, useWallet } from '../state/WalletContext'
import { InboxProvider, useInbox } from '../state/InboxContext'
import { Logo } from './Logo'
import { CookieSettingsLink } from './CookieBanner'
import { ProductTour, RestartTourLink } from './ProductTour'
import { Avatar } from './Avatar'
import { SeviyeRozeti } from './SeviyeRozeti'
import { SayfaZemini } from './SayfaZemini'
import {
  MenuIkonu,
  AramaIkonu,
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
  yaslanıyor. Logo `zemin="gece"` varyantıyla çiziliyor: beyaz "ders" + brand-400
  "mate". brand-100 denendi ve BEYAZDAN farkı yalnızca 1.27:1 çıktı — iki hece aynı
  renk gibi okunuyordu (ölçüm tablosu Logo.jsx'te).

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
  { to: '/kesfet', label: 'Keşfet', tour: 'discover', Ikon: AramaIkonu },
  { to: '/portfolio', label: 'Ders Portföyü', tour: 'portfolio', Ikon: KitapIkonu },
  // matches / chat çıpaları rehber altı adıma çıkarken eklendi (2026-08-24): eşleşme ve
  // sohbet eskiden tek bir "kanıt ve onay" paragrafının içinde geçiyordu, kendi adımları
  // yoktu. Adım ekleyip çıpa eklememek, adımı sessizce ekranın ortasına düşürürdü.
  { to: '/eslesmeler', label: 'Eşleşmeler', tour: 'matches', Ikon: KisilerIkonu },
  { to: '/sohbet', label: 'Sohbet', tour: 'chat', Ikon: MesajIkonu },
  { to: '/dersler', label: 'Derslerim', tour: 'sessions', Ikon: KepIkonu },
]

/*
  Sosyal hesaplar tek sabitte. Satırlar yalnız ikon + kullanıcı adı gösterir —
  "Instagram sayfamız" gibi açıklama metni bilinçli olarak yok (tasarım isteri).

  KULLANICI ADI ARTIK PLATFORM BAŞINA: TikTok hesabı `dersmate`, Instagram ve X ise
  `dersmate_`. Eskiden üçü de tek sabitten (`dersmate_`) yazdırılıyordu ve TikTok
  bağlantısı var olmayan bir hesaba gidiyordu. Ad, href ile aynı satırda duruyor ki
  ikisi bir daha ayrışmasın: linki değiştiren, altındaki metni de görür.
*/
/*
  ZEMİN YOĞUNLUĞU ROTAYA GÖRE — ve zemin TEK YERDEN, buradan çiziliyor.

  İlk uygulamada Profile ve Hakkımızda kendi SayfaZemini'lerini de çiziyordu; Layout
  zaten her sayfaya bir zemin verdiği için iki ızgara üst üste biniyordu. 32px hücreler
  tam örtüştüğü için çizgi opaklığı 0.06'dan ~0.12'ye çıkıyor, mesh havuzları da
  toplanıyordu. Zemin artık yalnızca burada.

  Rota listesi Layout'ta çünkü zemin kabuğun bir parçası: sayfa kendi arkasını
  boyayamaz (mx-auto max-w-6xl bir şerit, zemin ise kabuğun tamamını kaplamalı).
  Bunun bedeli küçük bir bağlılık — kabuk, iki rotanın adını biliyor. Alternatifi
  (context + her sayfada bir efekt) yalnızca dekoratif bir tercih için taşınacak
  yükten fazlaydı.

  "Zengin" olanlar VİTRİN sayfaları: kullanıcı orada veri taramıyor, bakıyor. Veri
  yoğun ekranlarda (Derslerim, Keşfet) zemin geri çekilir — orada dekor, okumanın
  önüne geçmemeli.
*/
const ZENGIN_ZEMIN_ROTALARI = ['/profil', '/hakkimizda']

const SOSYAL = [
  { ad: 'Instagram', kullanici: 'dersmate_', href: 'https://instagram.com/dersmate_', Ikon: InstagramIkonu },
  { ad: 'TikTok', kullanici: 'dersmate', href: 'https://tiktok.com/@dersmate', Ikon: TiktokIkonu },
  { ad: 'X', kullanici: 'dersmate_', href: 'https://x.com/dersmate_', Ikon: XIkonu },
]

/*
  Ray tercihinin anahtarı ARTIK BURADA TANIMLI DEĞİL, consent.js'ten geliyor.

  Sebebi bu projede ısırmış bir hata: anahtar burada duruyordu ve cihaza yazılıyordu ama
  çerez aydınlatma metninde hiçbir kategoride görünmüyordu — yani fonksiyonel çerezleri
  REDDETMİŞ kullanıcının cihazına da yazılıyordu. Anahtarı rızanın listesiyle aynı yerde
  tutmak, ikisinin bir daha ayrışmamasını sağlıyor.

  (Adlandırma peerlearn.* biçiminde — bkz. api.js, hwid.js. F4: bu ad kullanıcıya
  görünmez, altyapı kimliğidir.)
*/

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
  const location = useLocation()
  const [menuOpen, setMenuOpen] = useState(false)

  /*
    RAY TERCİHİ FONKSİYONEL ÇEREZ RIZASINA TABİ.

    Tercih kalıcı olmalı — her sayfa yenilemesinde daraltmayı yeniden yapmak, tercihi hiç
    hatırlamamakla aynı şey olurdu. Ama "kalıcı" demek cihaza yazmak demek ve kullanıcı
    fonksiyonel çerezleri reddetmişse yazacak bir şeyimiz yok.

    Reddedildiğinde ray her açılışta GENİŞ başlar ve daraltma yalnızca o sayfa ömrü
    boyunca hatırlanır. Banner'ın "kapatırsan bunlar her açılışta sıfırlanır" cümlesinin
    karşılığı tam olarak bu. Daha önce yazılmış değeri silmek buranın işi değil —
    ConsentProvider'daki temizlik kapısı yapıyor (clearFunctionalStorage).

    Okuma da rızaya bakıyor: reddedilmişken eski bir değer cihazda kalmış olabilir
    (temizlik kapısı henüz çalışmamış olabilir) ve onu okumak, reddedilen tercihi geri
    getirirdi.
  */
  const { functionalAllowed } = useConsent()

  const [rayDar, setRayDar] = useState(() => {
    if (!functionalAllowed) return false
    try {
      return localStorage.getItem(RAIL_KEY) === '1'
    } catch {
      // Depolama erişilemez (gizli sekme kısıtları). Bu okuma RENDER SIRASINDA çalışıyor;
      // sarmalanmazsa fırlayan hata tüm kabuğu düşürürdü. Varsayılan: geniş ray.
      return false
    }
  })

  const rayiDegistir = () => {
    setRayDar((v) => {
      const yeni = !v
      if (functionalAllowed) {
        try {
          localStorage.setItem(RAIL_KEY, yeni ? '1' : '0')
        } catch {
          // Depolama kapalı (gizli sekme): tercih bu oturum boyunca yine de geçerli.
        }
      }
      return yeni
    })
  }

  // Seviye rozeti her sayfada görünür ve verisini cüzdan ucundan alır: `level`,
  // `nextLevelAt` ve `totalEarnedCredits` aynı yanıtta geliyor, ayrı bir istek yok.
  const { wallet } = useWallet()

  /*
    Okunmamış mesaj rozeti de her sayfada. Ders saati ve toplantı linki YALNIZCA sohbetten
    kararlaştırılıyor; kullanıcı başka sayfadayken gelen mesajı fark etmezse randevulaşma
    sessizce kopuyor.
  */
  const { unreadTotal } = useInbox()

  // startsWith: /profil hem kendi profilini hem /profil/:userId'yi kapsıyor.
  const zeminYogunlugu = ZENGIN_ZEMIN_ROTALARI.some((r) => location.pathname.startsWith(r))
    ? 'zengin'
    : 'hafif'

  const items = (
    session?.isAdmin ? [...NAV, { to: '/admin', label: 'Yönetim', Ikon: KalkanIkonu }] : NAV
  ).map((item) => (item.to === '/sohbet' ? { ...item, badge: unreadTotal } : item))

  /*
    ÇEKMECE AÇIKKEN: Esc kapatır, arkadaki sayfa kaymaz (2026-08-24).

    Çekmece `<header>`in içinde duruyor ve barın 64px'lik yüksekliğinden TAŞARAK
    altındaki içeriğin üstüne biniyor — yani görsel olarak bir katman, ama akışta
    değil. Bu yüzden altındaki sayfa hâlâ kaydırılabiliyordu: kullanıcı menü açıkken
    parmağını sürükleyince menü yerinde duruyor, arkadaki liste kayıyordu; menüyü
    kapattığında bambaşka bir yerdeydi.

    Kilit ile perde (aşağıda) BİRLİKTE anlamlı: perde dokunuşu yakalar, kilit de
    perdeye düşen hareketin gövdeye sızmasını keser.

    Önceki `overflow` değeri geri yazılıyor, sabit '' değil — aynı anda başka bir kip
    (örn. filtre çekmecesi) kilit koymuş olabilir ve onu sessizce açmamalıyız.
  */
  useEffect(() => {
    if (!menuOpen) return

    const oncekiOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    const esc = (e) => {
      if (e.key === 'Escape') setMenuOpen(false)
    }
    document.addEventListener('keydown', esc)

    return () => {
      document.body.style.overflow = oncekiOverflow
      document.removeEventListener('keydown', esc)
    }
  }, [menuOpen])

  const cikisYap = () => {
    logout()
    navigate('/giris')
  }

  return (
    <div className="min-h-[100dvh]">
      <header className="sticky top-0 z-40 h-16 bg-slate-900">
        {/*
          ÜÇ BÖLGELİ BAR (2026-08-24): solda hamburger, SAĞDA seviye rozeti, ORTADA logo.

          Logo eskiden hamburgerin hemen sağındaydı ve barın soluna yığılmış bir kümenin
          parçası gibi görünüyordu — marka, gezinme düğmelerinin arasına sıkışmıştı.
          Şimdi barın kendi merkezinde duruyor.

          MERKEZLEME `absolute` İLE, flex ile DEĞİL: sağ küme masaüstünde avatar + isim +
          çıkış taşıyor, solda tek düğme var. Üç eşit sütunlu bir ızgarada logo, sağ
          kümenin genişliği her değiştiğinde (isim uzunluğu kullanıcıdan kullanıcıya
          değişiyor) yerinden oynardı. Mutlak merkez, barın geometrisine bakar; içeriğe
          değil, yani her kullanıcıda aynı yerde durur.

          pointer-events: sarmalayıcı tıklamayı GEÇİRİR (none), yalnızca logonun kendisi
          yakalar (auto). Aksi halde ekranı boydan boya kaplayan görünmez bir katman
          hamburger ile çıkış düğmesini tıklanamaz yapardı.

          DİKEY HİZA (2026-08-24): sarmalayıcı `inset-x-0` idi, yani yalnızca YATAY
          eksende geriliyordu; dikey konumu tarayıcının mutlak konumlu flex çocuğu nereye
          koyduğuna kalmıştı. `inset-0 + items-center` bunu belirsizlikten çıkarıyor:
          logo, hamburger ve rozetle aynı dikey merkeze oturuyor.

          ÇOK DAR EKRANDA MERKEZ TERK EDİLİYOR (2026-08-24, max-[359px]).

          Mutlak merkez barın geometrisine bakar, komşularına değil — genişlik yettiği
          sürece doğru davranış bu. 320px'te yetmiyor: tarayıcıda ölçüldü, kelime
          markasının sağ kenarı 208'de bitiyor, seviye rozeti 218'de başlıyordu. Aradaki
          10px, "dersmate" ile mavi rozeti tek bir küme gibi okutuyordu — bitişik
          değillerdi ama bitişik görünüyorlardı.

          `left-14 right-24`, sarmalayıcıyı iki kümenin arasında kalan boş banda
          daraltır (56px hamburger tarafı, 96px rozet + çıkış tarafı) ve logo O BANDIN
          merkezine oturur: 320px'te iki yana da ~30px. Logo barın matematiksel
          merkezinden birkaç piksel kayar; sıkışıklığın yanında görünmeyen bir bedel.

          Yalnızca 359px ve altında: 360px'ten itibaren boşluk zaten ~30px'e çıkıyor ve
          mutlak merkez kuralı olduğu gibi geçerli kalıyor. Eşik ölçümle seçildi, yuvarlak
          bir sayı olduğu için değil.
        */}
        <div className="relative flex h-full items-center justify-between px-3 sm:px-6">
          <div className="pointer-events-none absolute inset-0 flex items-center justify-center max-[359px]:left-14 max-[359px]:right-24">
            <NavLink
              to="/"
              className="pointer-events-auto flex h-11 items-center"
              aria-label="Ana sayfa"
            >
              {/* Boyut `boyut` belirtecinden geliyor, className yüksekliğinden değil:
                  kelime markası artık HTML metni ve punto ile işaretin oranı bileşenin
                  içinde sabit tutuluyor (bkz. Logo.jsx). */}
              <Logo zemin="gece" boyut="md" />
            </NavLink>
          </div>

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
            {/*
              lg:-ml-2 — HAMBURGER, RAYDAKİ İKON SÜTUNUYLA AYNI DİKEY EKSENE OTURUYOR.

              Tarayıcıda ölçüldü (1280px): raydaki büyüteç ikonunun merkezi x=38'de
              (nav p-3 = 12px + bağlantı px-3.5 = 14px + ikonun yarısı 12px; dar rayda
              justify-center de aynı 38'i veriyor — iki durumda da sabit). Üst barın
              sm:px-6 dolgusuyla hamburgerin merkezi ise x=46'daydı: 8px sağda, ve bu
              kayma dar/geniş her ray durumunda gözle görülüyordu.

              -ml-2 (8px) düğmeyi 16'ya çeker → merkez 16+22 = 38. Yalnızca lg'de:
              mobilde ray yok, hizalanacak bir sütun da yok — orada üst barın kendi
              dolgusu doğru referans.

              Ray dolguları (p-3 / px-3.5) değişirse bu değer de değişmeli; formül
              yukarıda, ezber değil.
            */}
            <button
              className="hidden h-11 w-11 shrink-0 place-items-center rounded-lg text-white hover:bg-white/10 lg:-ml-2 lg:grid"
              onClick={rayiDegistir}
              aria-label={rayDar ? 'Menüyü genişlet' : 'Menüyü daralt'}
              aria-expanded={!rayDar}
            >
              <MenuIkonu />
            </button>

          </div>

          <div className="flex items-center gap-2 sm:gap-4">
            {/* UNVAN yerine SEVİYE. "Çırak / Öğretici / Uzman" merdiveninin kaç basamak
                olduğu kullanıcıya hiç görünmüyordu; numaralı seviye bunu tek bakışta
                söylüyor. Seviye SUNUCUDAN geliyor (cüzdan ucundaki `level` alanı,
                krediden türer — Domain/Community/UserLevel.cs); arayüz eşik taşımıyor.
                data-tour="rank" çıpası KORUNDU: ürün turunun ilk adımı bu seçiciye
                bağlı (lib/tour.js) ve rozet yer değiştirdi, kaybolmadı. */}
            <NavLink
              to="/profil"
              data-tour="rank"
              className="-my-2 flex min-h-11 shrink-0 items-center py-2"
            >
              <SeviyeRozeti kaynak={wallet} />
            </NavLink>

            {/* Avatar profile giden kısayol: sosyal bir üründe kendi profiline ulaşmanın
                en beklenen yolu fotoğrafına tıklamaktır. lg altında kimlik satırı
                çekmecenin tepesinde — barda avatara yer yok. */}
            <NavLink to="/profil" className="hidden shrink-0 lg:block" aria-label="Profilim" title="Profilim">
              <Avatar userId={session?.userId} name={session?.displayName} size="sm" />
            </NavLink>

            {/* max-w + truncate: logo artık barın MUTLAK merkezinde duruyor ve uzun bir
                görünen ad sağ kümeyi büyütüp logonun üstüne binebilirdi. Ad kırpılır,
                logo yerinden oynamaz. */}
            <span className="hidden max-w-[12rem] truncate text-sm font-medium text-white lg:block">
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

        {/*
          PERDE — çekmecenin dışına dokununca kapansın diye (2026-08-24).

          Çekmeceyi kapatmanın tek yolu hamburgere geri dönmekti; menü ekranın yarısını
          kaplarken kullanıcının ilk refleksi ise açıkta kalan yere dokunmak oluyor ve
          orada hiçbir şey olmuyordu. Karartma ayrıca "arkadaki sayfa şu an devre dışı"
          diyor — menü kapalıymış gibi görünen tıklanabilir bir alan bırakmıyor.

          top-16: perde barın ALTINDAN başlar. Barı da karartsaydı hamburger ile çıkış
          düğmesi perdenin altında kalır, menüyü hamburgerle kapatmak imkânsızlaşırdı.

          z-30: `<header>` z-40'ta, yani çekmece perdenin ÜSTÜNDE kalır; perde yalnızca
          onun dışındaki her şeyi örter.

          aria-hidden: perde saf dekor, ekran okuyucuya söyleyecek bir şeyi yok —
          kapatma eylemi zaten hamburgerin `aria-expanded`'ı ve menü öğeleriyle
          erişilebilir durumda.
        */}
        {menuOpen && (
          <div
            className="fixed inset-x-0 bottom-0 top-16 z-30 bg-slate-900/40 lg:hidden"
            onClick={() => setMenuOpen(false)}
            aria-hidden="true"
          />
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

        {/*
          SAYFA ZEMİNİ KABUKTA, SAYFALARDA DEĞİL (2026-08-25).

          Zemin tek yerde duruyor çünkü her sayfa kendi kopyasını çizseydi: (a) sayfa
          geçişlerinde zemin sökülüp yeniden kurulur, mesh lekeleri gözle görülür biçimde
          "sıçrardı"; (b) desen id'si sayfa başına benzersiz tutulmak zorunda kalırdı —
          SVG pattern id'leri BELGE genelinde tekil, çakışan iki id'de ilk desen
          diğerinin yerine uygulanır.

          Sarmalayıcı `<main>` değil onun ÜST kutusu: `<main>` mx-auto max-w-6xl, yani
          geniş ekranda ortada bir şerit. Zemin oraya konsaydı ızgara ve lekeler o
          şeritte kesilir, iki yanda çıplak slate-50 kalırdı — zemin, kabuğun içeriğe
          ayırdığı alanın TAMAMINI kaplamalı. Altbilgi de bu kutunun içinde; zeminin
          alt sönümlemesi (SayfaZemini'nin from-slate-50 geçişi) böylece gövde zeminine
          bağlanıyor.

          ⚠ `isolate` BİLEREK YOK — SayfaZemini'nin belgelediği "relative isolate"
          kalıbından tek sapma, ve sebebi ölçülebilir:

          Bu projede modal/perde bileşenleri PORTAL KULLANMIYOR (ui.jsx Modal,
          CookieBanner, Avatar büyütme, FilterDrawer: hepsi `fixed inset-0 z-50` ile
          bulundukları yerde çiziliyor). `isolation: isolate` bu kutuyu bir yığın
          bağlamına çevirir ve içindeki z-50 O BAĞLAMIN İÇİNE hapsolur; dışarıda kalan
          `<header>` ise z-40. Kök bağlamda karşılaştırma "header z-40" ile "bu kutu
          z-auto" arasında yapılır, yani KOYU ÜST BAR MODALIN ÜSTÜNE ÇIKAR: 64px'lik
          bar, ortalanmış modalın başlık satırını ve Kapat düğmesini örter.

          İzolasyona burada gerek de yok: -z-10, ancak araya ZEMİNİ OLAN bir ata girerse
          kaybolur. Zincirdeki hiçbir kutunun zemini yok (min-h-[100dvh] → lg:flex → bu
          kutu) ve `body`nin bg-slate-50'si tuvale devrolduğu için negatif z katmanı onun
          ÜSTÜNE boyanıyor. Buraya bir gün zemin sınıfı eklenirse zemin görünmez olur —
          o zaman çözüm `isolate` değil, zemini taşıyan kutuyu ayırmaktır.
        */}
        <div className="relative min-w-0 flex-1">
          <SayfaZemini yogunluk={zeminYogunlugu} desenId="dm-kabuk" />

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

      {SOSYAL.map(({ ad, kullanici, href, Ikon }) => (
        <a
          key={ad}
          href={href}
          target="_blank"
          rel="noreferrer noopener"
          aria-label={`${ad}: ${kullanici}`}
          title={dar ? `${ad}: ${kullanici}` : undefined}
          className={`flex min-h-11 items-center rounded-xl text-sm text-slate-400 transition hover:bg-white/5 hover:text-slate-200 ${
            dar ? 'justify-center' : 'gap-3.5 px-3.5'
          }`}
        >
          <Ikon className="h-[18px] w-[18px]" />
          {!dar && <span className="whitespace-nowrap">{kullanici}</span>}
        </a>
      ))}
    </div>
  )
}
