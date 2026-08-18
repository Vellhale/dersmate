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
            <Button variant="secondary" onClick={() => setDialog('avatar')}>
              Fotoğrafı değiştir
            </Button>
            <Button variant="secondary" onClick={() => setDialog('teacher')}>
              Öğretmen adaylığı
            </Button>
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

          <TeacherCandidateModal
            open={dialog === 'teacher'}
            userId={targetId}
            onClose={() => setDialog(null)}
            onSaved={() => {
              setDialog(null)
              setVersion((v) => v + 1)
              setNotice('Öğretmen adaylığı bilgilerin kaydedildi. Artık gönüllü ders açabilirsin.')
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

function TeacherCandidateModal({ open, userId, onClose, onSaved }) {
  const profile = useAsync(() => (open ? api.userProfile(userId) : Promise.resolve(null)), [open, userId])
  const existing = profile.data?.teacherCandidate
  const [form, setForm] = useState(null)
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)

  const values = form ?? {
    university: existing?.university ?? '',
    faculty: existing?.faculty ?? '',
    department: existing?.department ?? '',
    gradeYear: existing?.gradeYear ?? '',
    hasPedagogicalCertificate: existing?.hasPedagogicalCertificate ?? false,
  }

  const set = (patch) => setForm({ ...values, ...patch })

  /*
    BELGE ZORUNLU. Beyan tek başına gönüllü ders yetkisi ve 🌱 rozeti açıyor; moderatörün
    elinde serbest metinden başka bir dayanak olmadığı sürece "Üniversite: asdasd" ile
    gerçek bir beyan ayırt edilemiyordu.

    Daha önce belge yüklenmişse yeniden istenmiyor — kullanıcı yalnızca bir alanı
    düzeltmek için 10 MB'lık PDF'i tekrar yüklemek zorunda kalmamalı.
  */
  const [belge, setBelge] = useState(null)
  const belgeVar = Boolean(existing?.hasDocument) || Boolean(belge)
  const ready =
    ['university', 'faculty', 'department'].every((k) => values[k].trim().length > 0) && belgeVar

  async function submit(event) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.declareTeacherCandidate({
        university: values.university.trim(),
        faculty: values.faculty.trim(),
        department: values.department.trim(),
        gradeYear: values.gradeYear === '' ? null : Number(values.gradeYear),
        hasPedagogicalCertificate: values.hasPedagogicalCertificate,
      })

      /*
        SIRA: önce beyan, sonra belge. Yükleme ucu var olan bir beyan satırı arıyor —
        ters sırada ilk kayıtta "önce beyanı doldur" hatası alınırdı.
      */
      if (belge) await api.uploadTeacherDocument(belge)

      setForm(null)
      setBelge(null)
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
      title="Öğretmen adaylığı"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Vazgeç
          </Button>
          <Button type="submit" form="teacher-form" loading={busy} disabled={!ready}>
            Kaydet
          </Button>
        </>
      }
    >
      <form id="teacher-form" onSubmit={submit} className="space-y-4">
        <div className="rounded-lg bg-emerald-50 p-3 text-sm text-emerald-900">
          <p className="font-medium">🌱 Gönüllü ders anlatmak için</p>
          <p className="mt-1">
            Eğitim fakültesi öğrencisiysen ya da pedagojik formasyon alıyorsan, tecrübe
            kazanmak için <strong>gönüllü</strong> ders ilanları açabilirsin: puan
            kazanmazsın, ama anlattığın ders ve süre sayaçların işler.
          </p>
        </div>

        {/* Dürüstlük: sistem okulu teyit edemiyor, bunu kullanıcıdan saklamıyoruz. */}
        <p className="text-xs text-slate-500">
          Bu bilgiler <strong>beyana dayalıdır</strong> ve profilinde “Beyan” olarak görünür.
          Yönetim öğrenci belgesiyle doğruladığında “Doğrulandı” rozetine dönüşür.
        </p>

        {existing?.isVerified && (
          <Notice tone="info">
            Beyanın doğrulanmış durumda; değiştirmek için yönetime başvurman gerekiyor.
          </Notice>
        )}

        {/* Ret bir yol kapatmaz: alanlar açık kalır, kaydetmek beyanı yeniden incelemeye sokar. */}
        {existing?.reviewStatus === 'Rejected' && (
          <Notice tone="warning">
            <p className="font-medium">Beyanın doğrulanmadı.</p>
            {existing.reviewNote && <p className="mt-1">{existing.reviewNote}</p>}
            <p className="mt-1">
              Bilgilerini güncelleyip kaydettiğinde beyanın yeniden incelemeye alınır ve
              gönüllü ders açabilir hâle gelirsin.
            </p>
          </Notice>
        )}

        <Field
          label="Öğrenci belgesi"
          hint="PDF ya da fotoğraf, en fazla 10 MB. e-Devlet'ten indirdiğin belge işini görür."
        >
          <input
            type="file"
            accept="application/pdf,image/png,image/jpeg,image/webp"
            className="input file:mr-3 file:rounded-md file:border-0 file:bg-slate-100
                       file:px-3 file:py-1.5 file:text-sm file:font-medium file:text-slate-700"
            onChange={(e) => setBelge(e.target.files?.[0] ?? null)}
          />
          {existing?.hasDocument && !belge && (
            <p className="mt-1.5 text-xs text-emerald-700">
              Belge yüklü ✓ — değiştirmek istemiyorsan bu alanı boş bırak.
            </p>
          )}
          {belge && (
            <p className="mt-1.5 text-xs text-slate-500">
              Seçildi: {belge.name} ({Math.round(belge.size / 1024)} KB)
            </p>
          )}
          {!belgeVar && (
            <p className="mt-1.5 text-xs text-amber-700">
              Beyanı kaydedebilmek için belge yüklemen gerekiyor.
            </p>
          )}
        </Field>

        <Field label="Üniversite">
          <input
            className="input"
            value={values.university}
            onChange={(e) => set({ university: e.target.value })}
            maxLength={150}
            disabled={existing?.isVerified}
          />
        </Field>

        <Field label="Fakülte">
          <input
            className="input"
            value={values.faculty}
            onChange={(e) => set({ faculty: e.target.value })}
            maxLength={150}
            placeholder="Eğitim Fakültesi"
            disabled={existing?.isVerified}
          />
        </Field>

        <div className="grid gap-4 sm:grid-cols-[1fr_120px]">
          <Field label="Bölüm">
            <input
              className="input"
              value={values.department}
              onChange={(e) => set({ department: e.target.value })}
              maxLength={150}
              placeholder="Matematik Öğretmenliği"
              disabled={existing?.isVerified}
            />
          </Field>

          <Field label="Sınıf">
            <select
              className="input"
              value={values.gradeYear}
              onChange={(e) => set({ gradeYear: e.target.value })}
              disabled={existing?.isVerified}
            >
              <option value="">—</option>
              {[1, 2, 3, 4, 5, 6].map((y) => (
                <option key={y} value={y}>
                  {y}
                </option>
              ))}
            </select>
          </Field>
        </div>

        <label className="flex cursor-pointer items-start gap-3 rounded-lg border border-slate-200/80 p-3">
          <input
            type="checkbox"
            className="mt-1 accent-brand-600"
            checked={values.hasPedagogicalCertificate}
            onChange={(e) => set({ hasPedagogicalCertificate: e.target.checked })}
            disabled={existing?.isVerified}
          />
          <span className="text-sm text-slate-700">
            Pedagojik formasyon programındayım
            <span className="block text-xs text-slate-500">
              Eğitim fakültesi dışından geliyorsan işaretle.
            </span>
          </span>
        </label>

        <ErrorBox error={error} />
      </form>
    </Modal>
  )
}
