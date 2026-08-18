import { useState } from 'react'
import { api } from '../lib/api'
import { Button, ErrorBox, Field, Modal } from './ui'

/**
 * Ders sonu değerlendirmesi (Yemeksepeti/Trendyol Go tarzı).
 *
 * NE ZAMAN AÇILIR: yalnızca öğrenci dersi ONAYLADIKTAN hemen sonra. Bu bir kolaylık
 * değil, ürün kuralının görünen yüzü — sunucu da aynı kuralı bağımsız olarak uygular
 * (tamamlanmamış derse, dersi almayan kişiye, ikinci kez yorum yazılamaz).
 *
 * ZORUNLU DEĞİL, ATLANABİLİR: değerlendirmeyi zorunlu kılmak, kurtulmak için rastgele
 * yıldız verdirir ve veriyi bozar. "Şimdi değil" bilinçli olarak duruyor.
 */

const QUICK_TAGS = [
  { value: 'KnowsSubject', label: 'Konuya çok hakim' },
  { value: 'PatientAndClear', label: 'Sabırlı ve açıklayıcı' },
  { value: 'StartedOnTime', label: 'Zamanında başladı' },
  { value: 'GreatExamples', label: 'Çözümlü sorular çok iyiydi' },
  { value: 'SharedResources', label: 'Anlaşılır kaynaklar paylaştı' },
  { value: 'WouldBookAgain', label: 'Tekrar ders alırım' },
]

const SCORE_FIELDS = [
  { key: 'score', label: 'Genel deneyim', hint: 'Dersten genel memnuniyetin.' },
  { key: 'teachingScore', label: 'Anlatım becerisi', hint: 'Konuyu anlaşılır anlattı mı?' },
  { key: 'punctualityScore', label: 'Zamanlama', hint: 'Ders vaktinde başladı mı?' },
]

export function ReviewModal({ session, open, onClose, onSubmitted }) {
  const [scores, setScores] = useState({ score: 0, teachingScore: 0, punctualityScore: 0 })
  const [tags, setTags] = useState([])
  const [comment, setComment] = useState('')
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)

  // Üç puanın da verilmesi beklenir: eksik puanı 5 sayıp göndermek, kullanıcının
  // söylemediği bir şeyi ona söyletmek olurdu.
  const ready = SCORE_FIELDS.every((f) => scores[f.key] >= 1)

  function toggleTag(value) {
    setTags((current) =>
      current.includes(value) ? current.filter((t) => t !== value) : [...current, value],
    )
  }

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      await api.createReview(session.sessionId, { ...scores, tags, comment: comment.trim() || null })
      onSubmitted?.()
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
      title="Dersi değerlendir"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Şimdi değil
          </Button>
          <Button loading={busy} disabled={!ready} onClick={submit}>
            Değerlendirmeyi gönder
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        <div className="rounded-xl bg-brand-50 p-4">
          <p className="text-sm text-slate-600">Değerlendirdiğin ders</p>
          <p className="mt-0.5 font-semibold text-slate-900">
            {session?.topicName} · {session?.otherDisplayName}
          </p>
          {session?.isVolunteer && (
            <span className="mt-2 inline-flex rounded-full bg-emerald-100 px-2.5 py-0.5 text-xs font-medium text-emerald-700">
              🤝 Gönüllü ders
            </span>
          )}
        </div>

        {SCORE_FIELDS.map((field) => (
          <StarRow
            key={field.key}
            label={field.label}
            hint={field.hint}
            value={scores[field.key]}
            onChange={(v) => setScores((s) => ({ ...s, [field.key]: v }))}
          />
        ))}

        <div>
          <p className="label">Neler iyiydi?</p>
          <p className="-mt-0.5 mb-2 text-xs text-slate-500">
            İstediğin kadar seçebilirsin — hiçbirini seçmemek de serbest.
          </p>
          <div className="flex flex-wrap gap-2">
            {QUICK_TAGS.map((tag) => {
              const active = tags.includes(tag.value)
              return (
                <button
                  key={tag.value}
                  type="button"
                  aria-pressed={active}
                  onClick={() => toggleTag(tag.value)}
                  className={`min-h-11 rounded-full border px-3 text-sm font-medium transition lg:min-h-0 lg:py-1.5 ${
                    active
                      ? 'border-brand-500 bg-brand-600 text-white'
                      : 'border-slate-300 bg-white text-slate-600 hover:border-brand-300 hover:text-brand-700'
                  }`}
                >
                  {active && <span className="mr-1">✓</span>}
                  {tag.label}
                </button>
              )
            })}
          </div>
        </div>

        <Field label="Eklemek istediğin bir şey var mı? (opsiyonel)">
          <textarea
            className="input h-24 resize-none"
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            maxLength={1000}
            placeholder="Dersin nasıl geçtiğini birkaç cümleyle anlatabilirsin…"
          />
        </Field>

        <ErrorBox error={error} />
      </div>
    </Modal>
  )
}

/**
 * Yıldız satırı. Radio grubu olarak modellendi (checkbox/label yığını değil): ekran
 * okuyucu "5 üzerinden 4" diyebilsin ve klavyeyle ok tuşlarıyla gezilebilsin diye.
 */
function StarRow({ label, hint, value, onChange }) {
  const [hover, setHover] = useState(0)
  const shown = hover || value

  return (
    <div>
      <div className="flex items-baseline justify-between gap-2">
        <p className="label mb-0">{label}</p>
        <span className="text-sm font-medium text-slate-500">
          {value ? `${value}/5` : 'Seçilmedi'}
        </span>
      </div>
      <p className="mb-1.5 text-xs text-slate-500">{hint}</p>

      <div
        role="radiogroup"
        aria-label={label}
        className="flex gap-1"
        onMouseLeave={() => setHover(0)}
      >
        {[1, 2, 3, 4, 5].map((star) => (
          <button
            key={star}
            type="button"
            role="radio"
            aria-checked={value === star}
            aria-label={`${star} yıldız`}
            onMouseEnter={() => setHover(star)}
            onClick={() => onChange(star)}
            className={`grid h-11 w-11 place-items-center rounded-lg text-2xl transition ${
              star <= shown ? 'text-amber-400' : 'text-slate-300'
            } hover:bg-slate-50`}
          >
            ★
          </button>
        ))}
      </div>
    </div>
  )
}
