import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api } from '../lib/api'
import { useAuth } from '../state/AuthContext'
import { useAsync } from '../state/useAsync'
import { UserProfileView } from '../components/UserProfileView'
import { AvatarPicker } from '../components/AvatarPicker'
import { Button, ErrorBox, Field, Modal, Notice } from '../components/ui'

/**
 * Profil sayfası. Parametresiz (/profil) kendi profilini, /profil/:userId başkasınınkini
 * gösterir — tek bileşen, çünkü görüntülenen içerik aynı; fark yalnızca düzenleme
 * düğmelerinin görünürlüğü. İki ayrı sayfa yazmak aynı kartı iki yerde bakımı gerektirirdi.
 */
export default function Profile() {
  const { userId } = useParams()
  const { session, logout } = useAuth()
  const targetId = userId ?? session?.userId

  const [dialog, setDialog] = useState(null)
  const [notice, setNotice] = useState(null)
  // Alt bileşenleri yeniden kurmak için: avatar/profil değişince taze veri okunsun.
  const [version, setVersion] = useState(0)

  const isSelf = targetId === session?.userId

  /*
    ZEMİN "zengin" KİPTE. Profil bu üründeki vitrin sayfası: veri yoğun değil, tek bir
    kişiyi anlatıyor ve ekranın büyük kısmı kartların ARASINDAKİ boşluk. O boşluk düz
    slate-50 kaldığı sürece sayfa, içeriği ne kadar canlı olursa olsun steril duruyordu.

    Önceki turda UserProfileView'ın içinde duran hafif brand-50 şerit KALDIRILDI (proje
    sahibinin "ufak ve basit mavilik" dediği şey oydu): yalnızca üst 12 rem'i boyuyordu,
    yani sayfanın geri kalanı yine bembeyazdı ve şerit bittiği yerde gözle görülür bir
    kesme çizgisi bırakıyordu. SayfaZemini sayfanın tamamını kaplıyor, mesh havuzlarla
    içeriden aydınlanıyor ve altta kendi kendine sönümleniyor — kesme yok.

    ZEMİNİ BU SAYFA ÇİZMİYOR — Layout çiziyor (bkz. ZENGIN_ZEMIN_ROTALARI). İlk
    uygulamada burada da bir SayfaZemini vardı ve Layout'unkiyle üst üste biniyordu:
    iki ızgara tam örtüşünce çizgi opaklığı iki katına çıkıyordu.

    Sarmalayıcıya `isolate` de KONMUYOR ve bu ölçülmüş bir karar: bu sayfadaki modallar
    (AvatarPicker, profil düzenleme) portal kullanmıyor, `fixed inset-0 z-50` ile
    burada çiziliyor. `isolate` bu kutuyu yığın bağlamına çevirdiğinde z-50 içeride
    hapsoluyor ve dışarıdaki z-40 sticky üst bar modalın ÜSTÜNE çıkıyordu — perde barı
    örtmüyor, bardaki menü ve çıkış tıklanabilir kalıyordu. Ayrıntı SayfaZemini.jsx'te.
  */
  return (
    <div>
      <div className="space-y-6">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h1 className="text-2xl font-bold text-slate-900">
            {isSelf ? 'Profilim' : 'Profil'}
          </h1>

          {isSelf && (
            <div className="flex flex-wrap gap-2">
              {/* hover'da marka tonu: ikincil buton normalde nötr kalır (birincilin
                  vurgusunu çalmasın) ama üzerine gelince "tıklanabilirim" der.
                  ui.jsx'e DOKUNULMADI — görünüm className ile ekleniyor; className,
                  variant sınıflarından sonra geldiği için hover'da kazanır. */}
              <Button
                variant="secondary"
                className="hover:border-brand-300 hover:text-brand-700"
                onClick={() => setDialog('avatar')}
              >
                Fotoğrafı değiştir
              </Button>
              {/* "Öğretmen adaylığı" düğmesi kaldırıldı: adaylık beyanı ve ona bağlı
                  gönüllü ders yetkisi üründen çıktı, herkes aynı yetkiye sahip. */}
              <Button onClick={() => setDialog('edit')}>Profili düzenle</Button>
            </div>
          )}
        </div>

        {notice && (
          <Notice tone="success" onDismiss={() => setNotice(null)}>
            {notice}
          </Notice>
        )}

        <UserProfileView key={`${targetId}-${version}`} userId={targetId} />

        {/*
          HESABI SİL — Google Play, hesap açtıran uygulamalarda silmeyi uygulama içinde
          zorunlu tutuyor ve web sürümü de aynı hesabı yönettiği için burada da olmalı.
          Bulunabilir ama öne çıkmıyor: geri alınamaz bir işlem, düzenleme düğmelerinin
          yanında eşit ağırlıkta durursa yanlışlıkla tıklanır.
        */}
        {isSelf && (
          <div className="border-t border-slate-200 pt-4 text-center">
            <button
              type="button"
              onClick={() => setDialog('sil')}
              className="text-sm text-slate-400 underline underline-offset-2 hover:text-rose-600"
            >
              Hesabımı sil
            </button>
          </div>
        )}
      </div>

      {isSelf && (
        <>
          <AvatarPicker
            open={dialog === 'avatar'}
            onClose={() => setDialog(null)}
            onUploaded={() => {
              // Önbellekteki eski avatar düşürülmeli, yoksa değişiklik görünmez.
              api.forgetAvatar(targetId)
              setDialog(null)
              setVersion((v) => v + 1)
              setNotice('Profil fotoğrafın güncellendi.')
            }}
          />

          <HesabiSilModali
            open={dialog === 'sil'}
            onClose={() => setDialog(null)}
            onDeleted={logout}
          />

          <EditProfileModal
            open={dialog === 'edit'}
            userId={targetId}
            onClose={() => setDialog(null)}
            onSaved={() => {
              setDialog(null)
              setVersion((v) => v + 1)
              setNotice('Profilin güncellendi.')
            }}
          />
        </>
      )}
    </div>
  )
}

/*
  HESABI SİL — geri alınamaz olduğu için iki kapı var: ne olacağını AÇIKÇA yazan bir metin
  ve parolanın yeniden girilmesi. Parola sunucuda da doğrulanıyor; buradaki alan güvenliği
  tek başına taşımıyor, onay niyetini kanıtlıyor (oturumu açık kalmış bir tarayıcıdan
  tek tıkla hesap silinememeli).

  METİN NEYİN KALDIĞINI DA SÖYLÜYOR. "Her şey silinecek" demek yanlış olurdu: ders
  geçmişi, verilen puanlar ve değerlendirmeler karşı tarafa ait ve duruyor — orada
  "Silinmiş kullanıcı" olarak görünüyorsun. Kullanıcıya olmayan bir şey vaat etmek,
  silme hakkını yanlış anlatmak olur.
*/
function HesabiSilModali({ open, onClose, onDeleted }) {
  const [sifre, setSifre] = useState('')
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (open) {
      setSifre('')
      setError(null)
    }
  }, [open])

  async function sil(e) {
    e.preventDefault()
    // Geri alınamaz işlemde çift gönderim koruması: ikinci istek 404 döner ve kullanıcı
    // hesabı silindiği hâlde hata görürdü.
    if (busy) return
    if (!sifre) {
      setError({ message: 'Devam etmek için parolanı yaz.' })
      return
    }

    setBusy(true)
    setError(null)
    try {
      await api.deleteAccount(sifre)
      // Oturumu düşürmek yeterli: RequireAuth giriş ekranına kendisi yönlendiriyor.
      onDeleted()
    } catch (err) {
      setError(err)
      setBusy(false)
    }
  }

  return (
    <Modal open={open} onClose={busy ? () => {} : onClose} title="Hesabımı sil">
      <form onSubmit={sil} className="space-y-4">
        <Notice tone="warning">
          Bu işlem geri alınamaz. Hesabına bir daha giriş yapamazsın.
        </Notice>

        <div>
          <p className="text-sm font-semibold text-slate-900">Silinecekler</p>
          <ul className="mt-1.5 list-disc space-y-1 pl-5 text-sm text-slate-600">
            <li>Adın, e-postan, telefonun ve profil fotoğrafın</li>
            <li>Biyografin, üniversite ve bölüm bilgin</li>
            <li>Açtığın ders ilanları</li>
            <li>Veri tercihlerin ve cihaz kaydın</li>
          </ul>
        </div>

        <div>
          <p className="text-sm font-semibold text-slate-900">Kalacaklar</p>
          <p className="mt-1.5 text-sm leading-relaxed text-slate-600">
            Yaptığın dersler, kazandırdığın puanlar ve yazdığın değerlendirmeler karşı
            tarafın geçmişine ait olduğu için siliniyor değil — orada adın yerine
            &ldquo;Silinmiş kullanıcı&rdquo; görünecek.
          </p>
        </div>

        <Field label="Parolan" hint="Onay için parolanı yeniden yaz.">
          <input
            type="password"
            autoComplete="current-password"
            value={sifre}
            onChange={(e) => setSifre(e.target.value)}
            className="w-full rounded-lg border border-slate-300 px-3 py-2 text-base
                       focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-100"
          />
        </Field>

        <ErrorBox error={error} />

        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose} disabled={busy}>
            Vazgeç
          </Button>
          <Button type="submit" variant="danger" loading={busy}>
            Hesabımı kalıcı olarak sil
          </Button>
        </div>
      </form>
    </Modal>
  )
}

function EditProfileModal({ open, userId, onClose, onSaved }) {
  const profile = useAsync(() => (open ? api.userProfile(userId) : Promise.resolve(null)), [open, userId])
  const [form, setForm] = useState(null)
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)

  // Form yalnızca veri geldiğinde bir kez doldurulur; sonrası kullanıcının.
  const values = form ?? {
    displayName: profile.data?.displayName ?? '',
    bio: profile.data?.bio ?? '',
    university: profile.data?.university ?? '',
    department: profile.data?.department ?? '',
  }

  const set = (patch) => setForm({ ...values, ...patch })

  async function submit(event) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.updateProfile({
        displayName: values.displayName.trim(),
        bio: values.bio.trim() || null,
        university: values.university.trim() || null,
        department: values.department.trim() || null,
      })
      setForm(null)
      onSaved()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Profili düzenle"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Vazgeç
          </Button>
          <Button
            type="submit"
            form="profile-form"
            loading={busy}
            disabled={values.displayName.trim().length < 2}
          >
            Kaydet
          </Button>
        </>
      }
    >
      <form id="profile-form" onSubmit={submit} className="space-y-4">
        <Field label="Görünen ad">
          <input
            className="input"
            value={values.displayName}
            onChange={(e) => set({ displayName: e.target.value })}
            maxLength={100}
          />
        </Field>

        <Field label="Hakkında" hint="Kendini birkaç cümleyle anlat — resmi olmasına gerek yok.">
          <textarea
            className="input h-28 resize-none"
            value={values.bio}
            onChange={(e) => set({ bio: e.target.value })}
            maxLength={1000}
            placeholder="Merhaba! Matematikte iyiyim, kimyada desteğe ihtiyacım var…"
          />
        </Field>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Üniversite / Lise">
            <input
              className="input"
              value={values.university}
              onChange={(e) => set({ university: e.target.value })}
              maxLength={150}
            />
          </Field>

          <Field label="Bölüm / Alan">
            <input
              className="input"
              value={values.department}
              onChange={(e) => set({ department: e.target.value })}
              maxLength={150}
            />
          </Field>
        </div>

        <ErrorBox error={error} />
      </form>
    </Modal>
  )
}
