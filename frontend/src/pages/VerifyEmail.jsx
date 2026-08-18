import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../lib/api'
import { Button, ErrorBox, Field, Notice } from '../components/ui'
import { AuthShell } from './Login'

export default function VerifyEmail() {
  const [searchParams] = useSearchParams()
  const [token, setToken] = useState(searchParams.get('token') ?? '')
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState(null)

  // Yeniden gönderme: token'ın ömrü 24 saat ve süresi dolduğunda bu sayfa TEK BAŞINA
  // işe yaramıyordu — elde token yoksa doldurulacak kutu da yoktu, hesap kilitli kalıyordu.
  const [email, setEmail] = useState('')
  const [resendBusy, setResendBusy] = useState(false)
  const [resendDone, setResendDone] = useState(false)
  const [resendToken, setResendToken] = useState('')

  async function onSubmit(event) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      setResult(await api.verifyEmail(token.trim()))
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  async function onResend(event) {
    event.preventDefault()
    setResendBusy(true)
    setError(null)
    try {
      const r = await api.resendVerification(email.trim())
      setResendDone(true)
      // Geliştirme ortamında sunucu token'ı döner; kullanıcı kopyalamakla uğraşmasın diye
      // doğrudan kutuya yazılır. Üretimde bu alan boş gelir ve hiçbir şey değişmez.
      if (r?.verificationToken) setResendToken(r.verificationToken)
    } catch (err) {
      setError(err)
    } finally {
      setResendBusy(false)
    }
  }

  /*
    "Hoş geldin kredin açılır" vaadi kaldırıldı: kredi hâlâ tanımlanıyor ama harcanacak
    bir yeri yok (ders almak ücretsiz) — kullanıcıya bir kazanım gibi sunmak yanlış
    beklenti kurardı. Doğrulamanın gerçek karşılığı hesabın etkinleşmesi.
  */
  return (
    <AuthShell title="E-posta doğrulama" subtitle="Doğrulama, hesabını etkinleştirir.">
      {result ? (
        <div className="space-y-4">
          <Notice tone="success">
            E-postan doğrulandı 🎉 Artık eşleşme isteği gönderebilir ve ders rezerve edebilirsin.
          </Notice>
          <Link to="/giris">
            <Button className="w-full">Giriş yap</Button>
          </Link>
        </div>
      ) : (
        <div className="space-y-6">
          <form onSubmit={onSubmit} className="space-y-4">
            <Field label="Doğrulama token'ı" hint="E-postandaki bağlantıdan gelir.">
              <textarea
                className="input h-28 resize-none font-mono text-xs"
                value={resendToken || token}
                onChange={(e) => {
                  setToken(e.target.value)
                  setResendToken('')
                }}
                required
              />
            </Field>

            <ErrorBox error={error} />

            <Button type="submit" loading={busy} className="w-full">
              Doğrula
            </Button>
          </form>

          <form onSubmit={onResend} className="space-y-3 border-t border-slate-200/80 pt-5">
            <p className="text-sm text-slate-600">
              <strong>Bağlantın gelmedi mi ya da süresi doldu mu?</strong> Doğrulama
              bağlantısı 24 saat geçerlidir; e-postanı yaz, yenisini gönderelim.
            </p>

            <Field label="E-posta">
              <input
                type="email"
                className="input"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
              />
            </Field>

            {/* Yanıt her durumda aynı: e-posta kayıtlı olsun olmasın. Aksi halde bu form,
                bir adresin platformda kayıtlı olup olmadığını herkese söylerdi. */}
            {resendDone && (
              <Notice tone="success">
                Bu adres kayıtlı ve henüz doğrulanmamışsa yeni bir bağlantı gönderdik.
                Gelen kutunu (ve spam klasörünü) kontrol et.
              </Notice>
            )}

            <Button
              type="submit"
              variant="secondary"
              loading={resendBusy}
              disabled={email.trim().length < 5}
              className="w-full"
            >
              Yeniden gönder
            </Button>
          </form>
        </div>
      )}
    </AuthShell>
  )
}
