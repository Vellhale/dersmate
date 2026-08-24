import { useState } from 'react'
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
  const { session } = useAuth()
  const targetId = userId ?? session?.userId

  const [dialog, setDialog] = useState(null)
  const [notice, setNotice] = useState(null)
  // Alt bileşenleri yeniden kurmak için: avatar/profil değişince taze veri okunsun.
  const [version, setVersion] = useState(0)

  const isSelf = targetId === session?.userId

  return (
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
