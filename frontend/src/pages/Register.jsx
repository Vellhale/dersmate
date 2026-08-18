import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../lib/api'
import { Button, ErrorBox, Field, Notice } from '../components/ui'
import { AuthShell } from './Login'

export default function Register() {
  const navigate = useNavigate()

  const [form, setForm] = useState({ email: '', password: '', displayName: '' })
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState(null)

  function update(key, value) {
    setForm((prev) => ({ ...prev, [key]: value }))
  }

  async function onSubmit(event) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      setResult(await api.register(form))
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  if (result) {
    return (
      <AuthShell title="Kayıt alındı" subtitle="Son adım: e-postanı doğrula.">
        <div className="space-y-4">
          <Notice tone="info">
            Doğrulama bağlantısı <strong>{form.email}</strong> adresine gönderildi. Doğrulamayı
            tamamlayınca hesabın <strong>etkinleşir</strong> ve eşleşme isteği gönderebilirsin.
          </Notice>

          {result.verificationToken ? (
            <>
              <p className="text-sm text-slate-600">
                Geliştirme ortamında token doğrudan burada gösterilir (gerçek kurulumda yalnızca
                e-postaya gider):
              </p>
              <Button
                className="w-full"
                onClick={() => navigate(`/dogrula?token=${encodeURIComponent(result.verificationToken)}`)}
              >
                E-postamı şimdi doğrula
              </Button>
            </>
          ) : (
            <p className="text-sm text-slate-600">
              E-postandaki doğrulama token'ını{' '}
              <Link to="/dogrula" className="font-medium text-brand-600 hover:underline">
                doğrulama sayfasına
              </Link>{' '}
              yapıştır.
            </p>
          )}

          <Link to="/giris" className="block text-center text-sm text-slate-600 hover:underline">
            Giriş sayfasına dön
          </Link>
        </div>
      </AuthShell>
    )
  }

  return (
    <AuthShell title="Kayıt ol" subtitle="İyi olduğun dersi anlat, ihtiyacın olanı ücretsiz al.">
      <form onSubmit={onSubmit} className="space-y-4">
        <Field label="Ad Soyad">
          <input
            className="input"
            value={form.displayName}
            onChange={(e) => update('displayName', e.target.value)}
            required
            maxLength={100}
          />
        </Field>

        <Field label="E-posta">
          <input
            type="email"
            className="input"
            value={form.email}
            onChange={(e) => update('email', e.target.value)}
            required
            autoComplete="email"
          />
        </Field>

        <Field label="Şifre" hint="En az 8 karakter.">
          <input
            type="password"
            className="input"
            value={form.password}
            onChange={(e) => update('password', e.target.value)}
            required
            minLength={8}
            autoComplete="new-password"
          />
        </Field>

        <ErrorBox error={error} />

        <Button type="submit" loading={busy} className="w-full">
          Hesap oluştur
        </Button>
      </form>

      <p className="mt-4 text-center text-sm text-slate-600">
        Zaten hesabın var mı?{' '}
        <Link to="/giris" className="font-medium text-brand-600 hover:underline">
          Giriş yap
        </Link>
      </p>
    </AuthShell>
  )
}
