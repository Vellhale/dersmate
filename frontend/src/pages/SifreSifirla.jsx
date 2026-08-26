import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../lib/api'
import { Button, ErrorBox, Field, Notice } from '../components/ui'
import { AuthShell } from './Login'

/*
  PAROLA SIFIRLAMA — TEK SAYFA, İKİ KİP.

  Adreste `?token=` varsa YENİ PAROLA kipi, yoksa BAĞLANTI İSTE kipi. İki ayrı sayfa
  yazmak (ör. /sifremi-unuttum + /sifre-sifirla) daha "temiz" görünürdü ama kullanıcıyı
  ikiye bölerdi: e-postadaki bağlantıyı mobilde açan kişi token'ı kaybederse (kopyala-
  yapıştır sırasında, ya da bağlantı kısalınca) ikinci sayfada yeniden bağlantı isteme
  yolu olmazdı. Tek sayfada ikisi de var; VerifyEmail.jsx de aynı kalıbı izliyor.

  ─── KULLANICI NUMARALANDIRMASI ─────────────────────────────────────────────────
  "Bağlantı gönderildi" mesajı, e-posta kayıtlı olsun olmasın AYNI. Sunucu da 204
  dönüyor. Mesajın dili bu belirsizliği gizlemiyor, AÇIKÇA söylüyor ("kayıtlıysa"):
  kullanıcı beklerken neden e-posta gelmediğini anlayabilmeli, ama biz de bir adresin
  kayıtlı olup olmadığını doğrulamamalıyız.
  ─────────────────────────────────────────────────────────────────────────────────
*/
export default function SifreSifirla() {
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token')

  return token ? <YeniParola token={token} /> : <BaglantiIste />
}

function BaglantiIste() {
  const [email, setEmail] = useState('')
  const [busy, setBusy] = useState(false)
  const [gonderildi, setGonderildi] = useState(false)
  const [error, setError] = useState(null)

  async function gonder(event) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.forgotPassword(email.trim())
      setGonderildi(true)
    } catch (err) {
      // 429 (hız sınırı) buraya düşer ve gösterilmeli: sessizce "gönderildi" demek,
      // kullanıcıyı gelmeyecek bir e-postayı beklemeye bırakırdı.
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthShell
      title="Şifreni mi unuttun?"
      subtitle="E-postanı yaz, sıfırlama bağlantısı gönderelim."
    >
      <form onSubmit={gonder} className="space-y-4">
        <Field label="E-posta">
          <input
            type="email"
            className="input"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="email"
            required
          />
        </Field>

        {gonderildi && (
          <Notice tone="success">
            Bu adres kayıtlı ve doğrulanmışsa sıfırlama bağlantısını gönderdik. Gelen
            kutunu ve spam klasörünü kontrol et — bağlantı <strong>1 saat</strong>{' '}
            geçerli.
          </Notice>
        )}

        <ErrorBox error={error} />

        <Button type="submit" loading={busy} disabled={email.trim().length < 5} className="w-full">
          Sıfırlama bağlantısı gönder
        </Button>

        <p className="text-center text-sm text-slate-600">
          <Link to="/giris" className="font-medium text-brand-700 hover:underline">
            Girişe dön
          </Link>
        </p>
      </form>
    </AuthShell>
  )
}

function YeniParola({ token }) {
  const navigate = useNavigate()
  const [parola, setParola] = useState('')
  const [tekrar, setTekrar] = useState('')
  const [busy, setBusy] = useState(false)
  const [bitti, setBitti] = useState(false)
  const [error, setError] = useState(null)

  // Sunucudaki kuralla AYNI (ResetPasswordHandler.MinParolaUzunlugu). İstemci kontrolü
  // sunucununkinin yerine geçmiyor, yalnızca kullanıcıyı gidip gelmekten kurtarıyor.
  const yeterliUzunluk = parola.length >= 8
  const esitler = parola === tekrar
  const gonderilebilir = yeterliUzunluk && esitler

  async function gonder(event) {
    event.preventDefault()
    if (!gonderilebilir) return
    setBusy(true)
    setError(null)
    try {
      await api.resetPassword(token, parola)
      setBitti(true)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  if (bitti) {
    return (
      <AuthShell title="Parolan değişti" subtitle="Yeni parolanla giriş yapabilirsin.">
        <div className="space-y-4">
          <Notice tone="success">
            Parolan güncellendi. Sıfırlama bağlantısı artık geçersiz.
          </Notice>
          <Button className="w-full" onClick={() => navigate('/giris')}>
            Giriş yap
          </Button>
        </div>
      </AuthShell>
    )
  }

  return (
    <AuthShell title="Yeni parola belirle" subtitle="Bağlantı yalnızca bir kez kullanılabilir.">
      <form onSubmit={gonder} className="space-y-4">
        <Field label="Yeni parola" hint="En az 8 karakter.">
          <input
            type="password"
            className="input"
            value={parola}
            onChange={(e) => setParola(e.target.value)}
            autoComplete="new-password"
            required
          />
        </Field>

        <Field label="Yeni parola (tekrar)">
          <input
            type="password"
            className="input"
            value={tekrar}
            onChange={(e) => setTekrar(e.target.value)}
            autoComplete="new-password"
            required
          />
        </Field>

        {/* Uyarı yalnızca kullanıcı ikinci kutuya YAZMAYA BAŞLADIKTAN sonra: boş kutuyu
            "eşleşmiyor" diye işaretlemek, henüz hata yapmamış birini uyarmak olurdu. */}
        {tekrar.length > 0 && !esitler && (
          <p className="text-sm text-rose-700">İki parola aynı değil.</p>
        )}

        <ErrorBox error={error} />

        <Button type="submit" loading={busy} disabled={!gonderilebilir} className="w-full">
          Parolayı değiştir
        </Button>

        <p className="text-center text-sm text-slate-600">
          Bağlantının süresi mi doldu?{' '}
          <Link to="/sifre-sifirla" className="font-medium text-brand-700 hover:underline">
            Yenisini iste
          </Link>
        </p>
      </form>
    </AuthShell>
  )
}
