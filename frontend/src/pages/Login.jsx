import { useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../state/AuthContext'
import { Button, Card, ErrorBox, Field } from '../components/ui'
import { Logo } from '../components/Logo'
import { CookieSettingsLink } from '../components/CookieBanner'
import { brand, ink } from '../lib/brand'

export default function Login() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)

  async function onSubmit(event) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await login(email, password)
      navigate(location.state?.from ?? '/', { replace: true })
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthShell title="Giriş yap" subtitle="Bilgini paylaş, ihtiyacın olan dersi ücretsiz al.">
      <form onSubmit={onSubmit} className="space-y-4">
        <Field label="E-posta">
          <input
            type="email"
            className="input"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            autoComplete="email"
          />
        </Field>

        <Field label="Şifre">
          <input
            type="password"
            className="input"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            autoComplete="current-password"
          />
        </Field>

        <ErrorBox error={error} />

        {/* Doğrulanmamış hesap çıkmaza girmesin: hata kodundan doğrudan doğrulama sayfasına yol ver. */}
        {error?.code === 'EMAIL_NOT_VERIFIED' && (
          <Link to="/dogrula">
            <Button variant="secondary" type="button" className="w-full">
              E-postamı doğrula
            </Button>
          </Link>
        )}

        <Button type="submit" loading={busy} className="w-full">
          Giriş yap
        </Button>
      </form>

      {/* Parola sıfırlama bağlantısı FORMUN HEMEN ALTINDA, "Kayıt ol"dan da önce:
          buraya gelip giremeyen kullanıcının ilk ihtiyacı yeni hesap açmak değil,
          kendi hesabına dönmek. 2026-08-27'ye kadar üründe hiç sıfırlama yolu yoktu
          ve parolasını unutan hesabını kalıcı kaybediyordu. */}
      <p className="mt-4 text-center text-sm text-slate-600">
        <Link to="/sifre-sifirla" className="font-medium text-brand-600 hover:underline">
          Şifreni mi unuttun?
        </Link>
      </p>

      <p className="mt-2 text-center text-sm text-slate-600">
        Hesabın yok mu?{' '}
        <Link to="/kayit" className="font-medium text-brand-600 hover:underline">
          Kayıt ol
        </Link>
      </p>
      <p className="mt-2 text-center text-sm text-slate-500">
        E-postanı henüz doğrulamadın mı?{' '}
        <Link to="/dogrula" className="font-medium text-brand-600 hover:underline">
          Doğrulama sayfası
        </Link>
      </p>
    </AuthShell>
  )
}

/*
  GİRİŞ / KAYIT ÇERÇEVESİ — iki panel.

  Login, Register ve VerifyEmail bu kabuğu paylaşıyor; tasarım tek yerde değişiyor.

  SOLDAKİ PANEL lg altında TAMAMEN GİZLİ (hidden lg:flex) — süslü bir kolonu telefonda
  forma kaydırma mesafesi olarak ödetmenin anlamı yok. Mobilde marka, formun üstünde
  küçük bir logoya iniyor.

  İLLÜSTRASYON SATIR İÇİ SVG: ayrı bir ağ isteği yok, marka tonlarıyla (currentColor
  değil, brand-* değerleriyle) çiziliyor ve ölçeklenirken bozulmuyor. aria-hidden —
  dekoratif; ekran okuyucuya anlattığı şey zaten yanındaki metinde yazıyor.

  ⚠️ Logo'nun aria-label="dersmate" olan TEK svg olarak kalması önemli: e2e testi
  (marka.spec.js → "logo vurgusu sayfada brand-400 olarak çiziliyor") bu seçiciye bakıyor.
  Bu yüzden illüstrasyonun rol/etiketi yok.
*/

/** Sol paneldeki "bilgi takası" görseli. İki düğüm, aralarında karşılıklı akış. */
function ExchangeArt({ onDark = false }) {
  return (
    // h-full + max-h: kapsayıcı (flex-1) ne kadar yer verirse ona sığar, oranı korur.
    <svg
      viewBox="0 0 320 240"
      preserveAspectRatio="xMidYMid meet"
      className="h-full max-h-[210px] w-full max-w-[280px]"
      aria-hidden="true"
    >
      <defs>
        <linearGradient id="akis" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stopColor={brand[400]} />
          <stop offset="100%" stopColor={brand[50]} />
        </linearGradient>
        <linearGradient id="akisTers" x1="1" y1="0" x2="0" y2="0">
          <stop offset="0%" stopColor={brand[300]} />
          <stop offset="100%" stopColor={brand[50]} />
        </linearGradient>
      </defs>

      {/* Karşılıklı iki yay: takasın yönü tek taraflı değil. */}
      <path d="M 96 96 C 130 62, 190 62, 224 96" stroke="url(#akis)" strokeWidth="4"
            fill="none" strokeLinecap="round" />
      <path d="M 224 144 C 190 178, 130 178, 96 144" stroke="url(#akisTers)" strokeWidth="4"
            fill="none" strokeLinecap="round" />

      {/* Öğreten / öğrenen düğümleri */}
      <circle cx="96" cy="120" r="34" fill={brand[400]} opacity="0.95" />
      <circle cx="224" cy="120" r="34" fill={onDark ? brand[50] : ink} opacity="0.95" />

      {/* Düğüm içi işaretler: kitap ve ampul — anlatan ve öğrenen. */}
      <text x="96" y="132" textAnchor="middle" fontSize="30">📘</text>
      <text x="224" y="132" textAnchor="middle" fontSize="30">💡</text>

      {/* Çevredeki küçük noktalar: topluluk. */}
      <circle cx="40" cy="52" r="5" fill={brand[300]} opacity="0.7" />
      <circle cx="286" cy="60" r="4" fill={brand[300]} opacity="0.6" />
      <circle cx="58" cy="196" r="4" fill={brand[300]} opacity="0.6" />
      <circle cx="272" cy="192" r="6" fill={brand[300]} opacity="0.7" />
    </svg>
  )
}

const VAATLER = [
  ['🎓', 'Ders almak ücretsiz', 'Öğrenirken hiçbir şey ödemezsin, puan da harcamazsın.'],
  // Unvan adları yerine SEVİYE: mekanizma aynı (puan biriktikçe yükselirsin), değişen
  // yalnızca ölçeğin adı ve basamak sayısı. Eşik sayısı buraya YAZILMADI — giriş
  // ekranındaki bir rakam, sunucudaki tablo değiştiğinde sessizce yalan olur.
  ['⏱️', 'Anlattıkça kazan', 'Verdiğin her ders puana dönüşür; puan harcanmaz, seviyeni yükseltir.'],
  ['🛡️', 'Doğrulanmış dersler', 'Her ders kanıtla kapanır; değerlendirmeler yalnızca gerçek derslerden gelir.'],
]

export function AuthShell({ title, subtitle, children }) {
  return (
    /*
      TEK EKRAN, SAYFA KAYDIRMASI YOK.

      Eskiden `min-h-screen` idi: içerik ekrandan biraz taşınca sayfa kayıyor, iki panel
      birbirine göre oynuyor ve tasarım bütünlüğü bozuluyordu.

      lg ve üstünde `h-dvh overflow-hidden` — yükseklik ekranın tamamı, taşma kesiliyor.
      Kaydırma gerekiyorsa PANELİN KENDİ İÇİNDE oluyor, sayfada değil.

      lg ALTINDA overflow-hidden YOK ve bu bilinçli: sol panel zaten gizli, geriye yalnızca
      form kalıyor. Küçük ekranda (ör. 375×667, klavye açıkken daha da az) sert bir
      `h-dvh overflow-hidden` formun altını KESERDİ — "kaydırma yok" uğruna gönder
      düğmesine ulaşılamaz hâle gelirdi.

      `dvh`, `vh` değil: mobil adres çubuğu vh'ye dahil edilmediği için alt kısım kırpılıyor.
    */
    <div className="min-h-dvh bg-slate-50 lg:grid lg:h-dvh lg:grid-cols-2 lg:overflow-hidden">
      {/* ---------- SOL: marka / vizyon ---------- */}
      <aside
        className="relative hidden overflow-hidden bg-gradient-to-br from-brand-600
                   via-brand-700 to-slate-900 px-10 py-6 text-white
                   lg:flex lg:h-full lg:flex-col lg:justify-between
                   xl:px-12 xl:py-8"
      >
        {/*
          Zemindeki yumuşak ışık lekeleri. Salt düz gradyan, bu ölçekte bantlanıyor.

          Bunlar panelin SINIRLARININ DIŞINA taşıyor (-top-24, -bottom-32) ve taşmaları
          `overflow-hidden` ile kırpılıyor — istenen etki bu. Panelde bir ara
          `lg:overflow-y-auto` vardı ve o kırpmayı KAYDIRMAYA çeviriyordu: alttaki leke
          128px dışarı taştığı için iki panelin arasında ince bir kaydırma çubuğu
          beliriyordu. Ölçüldü: panel 730px, içerik 858px — aradaki fark tam da lekenin
          taşma miktarıydı, metin fazlalığı değil.

          Kaydırmaya gerek yok: asıl içerik (başlık + görsel + maddeler) `flex-1` sayesinde
          panele sığacak şekilde esniyor.
        */}
        <div aria-hidden="true"
             className="pointer-events-none absolute -left-24 -top-24 h-72 w-72 rounded-full
                        bg-brand-400/20 blur-3xl" />
        <div aria-hidden="true"
             className="pointer-events-none absolute -bottom-32 -right-16 h-80 w-80 rounded-full
                        bg-brand-300/10 blur-3xl" />

        <div className="relative">
          <Logo zemin="marka" boyut="lg" />
          <p className="mt-6 max-w-md text-2xl font-bold leading-tight xl:mt-8 xl:text-3xl">
            Bildiğini anlat,
            <br />
            öğrenmek istediğini ücretsiz al.
          </p>
          <p className="mt-3 max-w-md text-sm leading-relaxed text-brand-100">
            dersmate bir akran eğitimi platformu. Para değil zaman ve bilgi takas edilir:
            iyi olduğun dersi anlatırsın, ihtiyacın olan dersi karşılıksız alırsın.
          </p>
        </div>

        {/*
          İLLÜSTRASYON SIKIŞABİLİR. 1280×730 gibi yaygın bir laptop ekranında panel içeriği
          858px'e çıkıyor ve iki panelin arasında iç kaydırma çubuğu beliriyordu — sayfa
          kaymıyordu ama tasarım bütünlüğü bozuluyordu.

          `min-h-0` + `flex-1` ile görsel, kalan boşluk kadarını alıyor: kısa ekranda
          küçülüyor, uzun ekranda eski boyutuna dönüyor. Metin ve maddeler sabit kalıyor
          çünkü onlar okunması gereken içerik; kısılacak ilk şey dekoratif olan.
        */}
        <div className="relative my-4 flex min-h-0 flex-1 items-center justify-center xl:my-6">
          <ExchangeArt onDark />
        </div>

        <ul className="relative space-y-3">
          {VAATLER.map(([ikon, baslik, aciklama]) => (
            <li key={baslik} className="flex gap-3">
              <span aria-hidden="true" className="text-lg leading-none">{ikon}</span>
              <span>
                <span className="block text-sm font-semibold">{baslik}</span>
                <span className="block text-xs leading-relaxed text-brand-100/80">{aciklama}</span>
              </span>
            </li>
          ))}
        </ul>
      </aside>

      {/* ---------- SAĞ: form ---------- */}
      {/* Sağ panel kendi içinde kayar: form uzasa bile SAYFA kaymaz, iki panel hizada kalır. */}
      <main className="flex items-center justify-center px-4 py-10 sm:px-8 lg:h-full lg:overflow-y-auto">
        <div className="w-full max-w-md">
          {/* Mobilde markanın tek göründüğü yer; lg'de sol panel zaten taşıyor. */}
          <div className="mb-6 text-center lg:hidden">
            <Logo boyut="xl" />
          </div>

          <div className="mb-6 text-center lg:text-left">
            <h1 className="text-2xl font-bold tracking-tight text-slate-900">{title}</h1>
            {subtitle && <p className="mt-1.5 text-sm text-slate-600">{subtitle}</p>}
          </div>

          {/*
            Card yerine doğrudan kutu: giriş formu sayfanın TEK içeriği, onu ayrıca
            çerçevelemek görsel gürültü. rounded-2xl + shadow-lg, projedeki genel yüzey
            dilinin (rounded-xl + shadow-md) bir tık yükseltilmiş hâli — burası bir liste
            öğesi değil, sayfanın kendisi.
          */}
          <div className="rounded-2xl border border-slate-200/70 bg-white p-6 shadow-lg sm:p-7">
            {children}
          </div>

          <p className="mt-6 text-center text-xs leading-relaxed text-slate-500 lg:text-left">
            dersmate'te para transferi yoktur. Ders almak ücretsizdir; ders anlattığında puan
            kazanırsın ve bu puan harcanmaz — birikip seviyeni yükseltir.
          </p>

          <div className="mt-3 text-center lg:text-left">
            <CookieSettingsLink />
          </div>
        </div>
      </main>
    </div>
  )
}
