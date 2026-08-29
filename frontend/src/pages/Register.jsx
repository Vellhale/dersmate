import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../lib/api'
import { SOZLESME_SURUMU } from '../lib/yasalMetinler'
import { Button, ErrorBox, Field, Notice } from '../components/ui'
import { AuthShell } from './Login'

export default function Register() {
  const navigate = useNavigate()

  const [form, setForm] = useState({ email: '', password: '', displayName: '' })
  const [kosullarKabul, setKosullarKabul] = useState(false)
  const [yasBeyani, setYasBeyani] = useState(false)
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
      /*
        ONAY SUNUCUYA GİDİYOR. `kosullarKabul` bir düğme durumu değil, kaydedilecek bir
        beyan: hangi metin sürümünü gördüğümüzü bildiriyoruz ve sunucu kendi yürürlükteki
        sürümüyle karşılaştırıp KENDİ değerini yazıyor (bkz. LegalDocuments.cs).

        Sürüm formun state'inde tutulmuyor, gönderim anında sabitten okunuyor: kullanıcı
        formu açıkken metin güncellenirse, göndermeye çalıştığı sürüm hâlâ gördüğü sürüm
        olur ve sunucu bunu reddedip "sayfayı yenile" der. Doğru davranış bu.
      */
      setResult(
        await api.register({
          ...form,
          termsVersion: kosullarKabul ? SOZLESME_SURUMU : null,
          ageConfirmed: yasBeyani,
        }),
      )
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

        {/*
          ONAY VE YAŞ BEYANI — 2026-08-27'de eklendi.

          Önceden kayıt formunda tek bir onay kutusu YOKTU: kullanıcı hiçbir metni kabul
          etmeden hesap açıyordu ve "şu kişi şunu kabul etmişti" denebilecek hiçbir kayıt
          oluşmuyordu. Metinler de yoktu (bkz. /kosullar, /gizlilik).

          İKİ AYRI KUTU, tek kutu değil: biri sözleşmeyi kabul etmek, diğeri yaş beyanı.
          Tek kutuda birleştirilseydi kullanıcı ikisini de okumadan tek hareketle geçerdi
          ve hangisine onay verdiği ayrıştırılamazdı.

          BAĞLANTILAR YENİ SEKMEDE (target=_blank): metni okumak için formu terk eden
          kullanıcı geri döndüğünde doldurduğu alanları kaybederdi.

          ONAY SUNUCUDA KAYITLI (2026-08-29): kabul edilen sözleşme sürümü ve iki ayrı
          zaman damgası kullanıcı satırına yazılıyor. Kutunun tek başına bir değeri yoktu
          — onayın değeri kanıtındadır.

          Aşağıdaki `disabled` yalnızca yönlendirme; asıl kapı sunucuda (RegisterHandler).
          İstemcideki kontrol, uca doğrudan istek atan biri için hiçbir şey ifade etmez.
        */}
        <div className="space-y-3 rounded-xl border border-slate-200 bg-slate-50 p-3">
          <label className="flex cursor-pointer items-start gap-2.5">
            <input
              type="checkbox"
              checked={kosullarKabul}
              onChange={(e) => setKosullarKabul(e.target.checked)}
              className="mt-0.5 h-4 w-4 shrink-0 accent-brand-600"
            />
            <span className="text-sm leading-relaxed text-slate-700">
              <Link
                to="/kosullar"
                target="_blank"
                rel="noopener"
                className="font-medium text-brand-700 hover:underline"
              >
                Kullanım koşullarını
              </Link>{' '}
              ve{' '}
              <Link
                to="/gizlilik"
                target="_blank"
                rel="noopener"
                className="font-medium text-brand-700 hover:underline"
              >
                gizlilik metnini
              </Link>{' '}
              okudum, kabul ediyorum.
            </span>
          </label>

          <label className="flex cursor-pointer items-start gap-2.5">
            <input
              type="checkbox"
              checked={yasBeyani}
              onChange={(e) => setYasBeyani(e.target.checked)}
              className="mt-0.5 h-4 w-4 shrink-0 accent-brand-600"
            />
            <span className="text-sm leading-relaxed text-slate-700">
              18 yaşından büyüğüm ya da hesabımı velimin bilgisi ve onayıyla açıyorum.
            </span>
          </label>
        </div>

        <ErrorBox error={error} />

        <Button
          type="submit"
          loading={busy}
          disabled={!kosullarKabul || !yasBeyani}
          className="w-full"
        >
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
