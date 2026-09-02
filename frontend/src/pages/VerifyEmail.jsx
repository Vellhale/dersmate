import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../lib/api'
import { Button, ErrorBox, Field, Notice } from '../components/ui'
import { AuthShell } from './Login'

/** Sunucudaki EmailVerificationRules ile aynı olmalı. */
const KOD_UZUNLUK = 6
const KOD_DAKIKA = 15
const YENIDEN_BEKLEME_SN = 60

/*
  ─── BAĞLANTI YERİNE KOD (2026-09-02) ───────────────────────────────────────────

  Bu sayfa eskiden bir JWT yapıştırma kutusuydu: kullanıcı e-postadaki bağlantıya
  tıklıyor, token sorgu dizesinden geliyordu; gelmezse 300 karakterlik bir dizeyi elle
  kopyalaması gerekiyordu (mobilde pratikte imkânsız).

  Artık 6 haneli kod var. Değişimin gerekçesi güvenlik değil TESLİM EDİLEBİLİRLİK:
  Türkiye'deki birçok sağlayıcı tanımadığı alan adından gelen bağlantılı postayı spam'e
  atıyor; sayı bu filtrelere takılmıyor. Ayrıca kullanıcı posta uygulamasına geçip geri
  dönmek zorunda kalmıyor — kodu okuyup buraya yazıyor.

  E-POSTA DA İSTENİYOR ve bu bir zahmet değil zorunluluk: 6 hane kullanıcıya özgü değil,
  aynı anda yüzlerce hesapta aynı kod olabilir. Sunucu "bu kodu kimin için deniyorsun"
  sorusunun cevabını bilmek zorunda.

  ADRES ÖNCEDEN DOLU GELİYOR: kayıt ekranı buraya `?email=` ile yönlendiriyor, yani
  kullanıcı az önce yazdığı adresi ikinci kez yazmıyor. Doğrudan gelen (yer imi, eski
  sekme) kullanıcı için kutu boş ve düzenlenebilir kalıyor.
*/
export default function VerifyEmail() {
  const [searchParams] = useSearchParams()

  const [email, setEmail] = useState(searchParams.get('email') ?? '')
  const [kod, setKod] = useState(searchParams.get('kod') ?? '')

  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState(null)

  const [resendBusy, setResendBusy] = useState(false)
  const [resendDone, setResendDone] = useState(false)

  /*
    ─── GERİ SAYIM: SUNUCUNUN SESSİZ BEKLEMESİNİ GÖRÜNÜR KILIYOR ────────────────

    ⚠️ BU BİR SÜS DEĞİL, GERÇEK BİR KUSURUN ÇARESİ. Sunucu, aynı adrese dakikada
    birden fazla doğrulama postası göndermiyor (mail bombing koruması) ve bunu
    SESSİZCE yapıyor — hata döndürmüyor, çünkü "biraz bekle" demek o adresin kayıtlı
    olduğunu söylerdi (bu ucun tüm tasarımı varlık sızdırmamak üzerine kurulu).

    Sonuç, kullanıcı açısından şuydu: kayıt olduktan hemen sonra kodu göremeyen kişi
    "Yeni kod gönder"e basıyor, arayüz "gönderdik" diyor ve HİÇBİR ŞEY GÖNDERİLMİYOR.
    Kullanıcı hiç gelmeyecek bir postayı bekliyor. Kayıttan sonraki ilk dakika, tam
    olarak bu düğmeye basılma olasılığının en yüksek olduğu an.

    Çare düğmeyi İSTEMCİDE kilitlemek: sayaç tarayıcıda tutuluyor, sunucuya hiç
    sorulmuyor, yani varlık bilgisi sızmıyor. Kullanıcı da boşa basmıyor — ne kadar
    beklemesi gerektiğini görüyor.

    Kayıttan geliniyorsa (?email= ile) sayaç DOLU başlıyor: kod az önce gönderildi.
    Doğrudan gelen kullanıcıda (yer imi, eski sekme) sıfır — onun için bekleme yok.
  */
  const [bekleme, setBekleme] = useState(searchParams.get('email') ? YENIDEN_BEKLEME_SN : 0)

  useEffect(() => {
    if (bekleme <= 0) return
    const t = setTimeout(() => setBekleme((s) => s - 1), 1000)
    return () => clearTimeout(t)
  }, [bekleme])

  const gonderilebilir = email.trim().length >= 5 && kod.trim().length === KOD_UZUNLUK

  async function onSubmit(event) {
    event.preventDefault()
    if (!gonderilebilir) return
    setBusy(true)
    setError(null)
    try {
      setResult(await api.verifyEmail(email.trim(), kod.trim()))
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
      setBekleme(YENIDEN_BEKLEME_SN)
      // Geliştirmede sunucu kodu yanıtta döndürüyor; kullanıcı e-postaya bakmasın diye
      // doğrudan kutuya yazılıyor. Üretimde bu alan BOŞ gelir ve hiçbir şey değişmez.
      if (r?.verificationToken) setKod(r.verificationToken)
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
            <Field label="E-posta" hint="Kodu gönderdiğimiz adres.">
              <input
                type="email"
                className="input"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                autoComplete="email"
                required
              />
            </Field>

            <Field label="Doğrulama kodu" hint={`E-postandaki ${KOD_UZUNLUK} haneli sayı.`}>
              {/*
                inputMode="numeric" + pattern: mobilde SAYI KLAVYESİ açılıyor. type="number"
                DEĞİL ve bu bilinçli — sayı girdisi baştaki sıfırları kırpıyor, oysa
                "004271" geçerli bir kod ve kırpılırsa eşleşme tutmaz. Ayrıca number
                girdisi kaydırma tekerleğiyle değişiyor.

                autoComplete="one-time-code": iOS ve Android, gelen postadaki kodu
                klavyenin üstünde önerip tek dokunuşla dolduruyor.

                tracking-[0.3em]: haneler arası boşluk, altı haneyi tek bakışta
                doğrulanabilir kılıyor.
              */}
              <input
                type="text"
                inputMode="numeric"
                pattern="[0-9]*"
                autoComplete="one-time-code"
                maxLength={KOD_UZUNLUK}
                className="input text-center text-lg font-semibold tracking-[0.3em]"
                value={kod}
                onChange={(e) => setKod(e.target.value.replace(/\D/g, ''))}
                placeholder="000000"
                required
              />
            </Field>

            <ErrorBox error={error} />

            <Button type="submit" loading={busy} disabled={!gonderilebilir} className="w-full">
              Doğrula
            </Button>
          </form>

          <form onSubmit={onResend} className="space-y-3 border-t border-slate-200/80 pt-5">
            <p className="text-sm text-slate-600">
              <strong>Kod gelmedi mi ya da süresi doldu mu?</strong> Kod {KOD_DAKIKA} dakika
              geçerlidir; e-postanı yaz, yenisini gönderelim.
            </p>

            {/* Yanıt her durumda aynı: e-posta kayıtlı olsun olmasın. Aksi halde bu form,
                bir adresin platformda kayıtlı olup olmadığını herkese söylerdi. */}
            {resendDone && (
              <Notice tone="success">
                Bu adres kayıtlı ve henüz doğrulanmamışsa yeni bir kod gönderdik. Gelen
                kutunu (ve spam klasörünü) kontrol et.
              </Notice>
            )}

            <Button
              type="submit"
              variant="secondary"
              loading={resendBusy}
              disabled={email.trim().length < 5 || bekleme > 0}
              className="w-full"
            >
              {bekleme > 0 ? `Yeni kod gönder (${bekleme} sn)` : 'Yeni kod gönder'}
            </Button>
          </form>
        </div>
      )}
    </AuthShell>
  )
}
