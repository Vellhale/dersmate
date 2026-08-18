import { useMemo, useState } from 'react'
import { api } from '../lib/api'
import { SearchableSelect } from '../components/SearchableSelect'
import { useAsync } from '../state/useAsync'
import { Badge, Button, Card, EmptyState, ErrorBox, Field, Loading, Modal, SectionTitle } from '../components/ui'
import { Link } from 'react-router-dom'
import { AnalyticsEvents, trackEvent } from '../lib/analytics'

/** Fiyat seçeneği kartı — radio yerine tıklanabilir kart: seçenekler açıklama istiyor. */
function PriceOption({ active, onClick, title, description }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={`w-full rounded-lg border p-3 text-left transition ${
        active ? 'border-brand-400 bg-brand-50' : 'border-slate-200/80 hover:bg-slate-50'
      }`}
    >
      <span className="block text-sm font-medium text-slate-800">{title}</span>
      <span className="mt-0.5 block text-xs text-slate-500">{description}</span>
    </button>
  )
}

/**
 * Dinamik portföy (Modül 1.1): iki yönlü profil.
 *  Offer = "Verebileceğim dersler"   → PUAN KAZANDIRIR (ders onaylandığında basılır)
 *  Seek  = "Almak istediğim dersler" → bedelsiz; yalnızca eşleşme için sinyal
 * Çapraz eşleşme algoritması bu iki listeyi karşı tarafınkiyle çakıştırır.
 */
export default function Portfolio() {
  const entries = useAsync(() => api.myPortfolio(), [])
  const [modalDirection, setModalDirection] = useState(null)

  const offers = entries.data?.filter((e) => e.direction === 'Offer') ?? []
  const seeks = entries.data?.filter((e) => e.direction === 'Seek') ?? []

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-slate-900">Ders Portföyü</h1>
        <p className="mt-1 text-sm text-slate-600">
          Anlatabildiğin konular puan kazandırır; almak istediklerin ücretsizdir. İkisini de
          doldurduğunda karşılıklı takas önerileri güçlenir.
        </p>
      </div>

      <ErrorBox error={entries.error} onRetry={entries.reload} />

      {entries.loading ? (
        <Loading />
      ) : (
        <div className="grid gap-6 lg:grid-cols-2">
          <PortfolioColumn
            title="Verebileceğim konular"
            tone="success"
            emptyText="Henüz anlatabileceğin bir konu eklemedin. En iyi olduğun konuyla başla."
            entries={offers}
            onAdd={() => setModalDirection('Offer')}
            onRemoved={entries.reload}
          />
          <PortfolioColumn
            title="Almak istediğim konular"
            tone="brand"
            emptyText="İhtiyacın olan konuları ekle; sana anlatabilecek öğrenciler önerilsin."
            entries={seeks}
            onAdd={() => setModalDirection('Seek')}
            onRemoved={entries.reload}
          />
        </div>
      )}

      <AddEntryModal
        direction={modalDirection}
        onClose={() => setModalDirection(null)}
        onSaved={() => {
          setModalDirection(null)
          entries.reload()
        }}
      />
    </div>
  )
}

function PortfolioColumn({ title, tone, entries, emptyText, onAdd, onRemoved }) {
  const [removingId, setRemovingId] = useState(null)

  async function remove(entryId) {
    setRemovingId(entryId)
    try {
      await api.removePortfolioEntry(entryId)
      onRemoved()
    } finally {
      setRemovingId(null)
    }
  }

  return (
    <section>
      <SectionTitle
        action={
          <Button variant="secondary" onClick={onAdd}>
            + Konu ekle
          </Button>
        }
      >
        {title}
      </SectionTitle>

      {entries.length === 0 ? (
        <EmptyState title="Liste boş" description={emptyText} />
      ) : (
        <div className="space-y-2">
          {entries.map((entry) => (
            <Card key={entry.entryId} className="flex items-start justify-between gap-3 p-4">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-medium text-slate-800">{entry.topicName}</span>
                  <Badge tone={tone}>Seviye {entry.selfAssessedLevel}/5</Badge>
                  {entry.isVolunteer && <Badge tone="success">🤝 Gönüllü</Badge>}
                </div>
                <p className="mt-0.5 text-xs text-slate-500">
                  {entry.categoryName} · {entry.subjectName}
                </p>
                {entry.note && <p className="mt-2 text-sm text-slate-600">{entry.note}</p>}
              </div>

              <Button
                variant="ghost"
                loading={removingId === entry.entryId}
                onClick={() => remove(entry.entryId)}
                title="Listeden çıkar"
              >
                Kaldır
              </Button>
            </Card>
          ))}
        </div>
      )}
    </section>
  )
}

function AddEntryModal({ direction, onClose, onSaved }) {
  const topics = useAsync(() => (direction ? api.topics() : Promise.resolve([])), [direction])
  const [topicId, setTopicId] = useState('')
  const [level, setLevel] = useState(3)
  const [note, setNote] = useState('')
  const [isVolunteer, setIsVolunteer] = useState(false)
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)

  // Gönüllü seçeneği yalnızca öğretmen adaylarına gösterilir; sunucu da aynı kuralı
  // bağımsız olarak uygular (403). Arayüz burada yalnızca gereksiz denemeyi önlüyor.
  const me = useAsync(() => (direction === 'Offer' ? api.myProfile() : Promise.resolve(null)), [direction])
  const canOfferVolunteer = Boolean(me.data?.teacherCandidate)

  // Konuları ders bazında grupla: uzun düz liste yerine okunabilir <optgroup>.
  const grouped = useMemo(() => {
    const map = new Map()
    for (const topic of topics.data ?? []) {
      const key = `${topic.rootCategory} · ${topic.subject}`
      if (!map.has(key)) map.set(key, [])
      map.get(key).push(topic)
    }
    return [...map.entries()]
  }, [topics.data])

  /*
    SearchableSelect düz bir liste ister; grup başlığı satırın kendi alanında taşınır.
    Sıra korunuyor: aynı dersin konuları ardışık kalmalı, yoksa grup başlıkları tekrar eder.
  */
  const konuSecenekleri = useMemo(
    () =>
      grouped.flatMap(([grup, items]) =>
        items.map((t) => ({ value: t.topicId, label: t.topic, group: grup })),
      ),
    [grouped],
  )


  async function onSubmit(event) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.addPortfolioEntry({
        topicId,
        direction,
        selfAssessedLevel: Number(level),
        note: note.trim() || null,
        isVolunteer: direction === 'Offer' && isVolunteer,
      })

      /*
        lesson_created — "yeni ders ilanı açıldı". Yalnızca Offer sayılır: Seek girişi bir
        ilan değil, talep beyanıdır ve ikisini aynı olayda toplamak metriği anlamsız kılardı.
        Olay API BAŞARILI döndükten sonra atılır (tıklamada değil): başarısız denemeler
        ilan sayısını şişirmemeli. topicId GÖNDERİLMEZ — bkz. trackEvent kişisel veri notu.
      */
      if (direction === 'Offer') {
        trackEvent(AnalyticsEvents.LessonCreated, {
          self_assessed_level: Number(level),
          volunteer: isVolunteer,
        })
      }

      setTopicId('')
      setNote('')
      setLevel(3)
      setIsVolunteer(false)
      onSaved()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  const isOffer = direction === 'Offer'

  return (
    <Modal
      open={Boolean(direction)}
      onClose={onClose}
      title={isOffer ? 'Verebileceğim konu ekle' : 'Almak istediğim konu ekle'}
    >
      <form onSubmit={onSubmit} className="space-y-4" id="portfolio-form">
        <Field label="Konu">
          {topics.loading ? (
            <Loading label="Katalog yükleniyor…" />
          ) : (
            /*
              Düz <select> DEĞİL: katalog 767 konu. Kullanıcı ya yazarak süzer ("türev")
              ya da listeyi gezer; ikisi de açık. Gerekçenin tamamı SearchableSelect'te.
            */
            <SearchableSelect
              id="portfoy-konu"
              options={konuSecenekleri}
              value={topicId}
              onChange={setTopicId}
              placeholder="Konu ara ya da listeden seç…"
              emptyLabel="Bu aramayla eşleşen konu yok"
            />
          )}
        </Field>

        <Field
          label={isOffer ? 'Bu konudaki seviyen' : 'Mevcut seviyen'}
          hint={
            isOffer
              ? 'Öz değerlendirme. Eşleşme sıralamasında dikkate alınır.'
              : 'Anlatacak kişiye nereden başlayacağını gösterir.'
          }
        >
          <input
            type="range"
            min={1}
            max={5}
            value={level}
            onChange={(e) => setLevel(e.target.value)}
            className="h-11 w-full accent-brand-600 lg:h-auto"
          />
          <div className="mt-1 text-center text-sm font-medium text-slate-700">{level} / 5</div>
        </Field>

        {/*
          Seçim yalnızca "anlatabilirim" tarafında anlamlı ve artık ÖĞRENCİYE DEĞİL,
          EĞİTMENE dair: ders her hâlükârda ücretsiz. Karar, anlatanın bu ders için puan
          isteyip istemediğidir.
        */}
        {isOffer && (
          <Field label="Puan kazanımı">
            {canOfferVolunteer ? (
              <div className="space-y-2">
                <PriceOption
                  active={!isVolunteer}
                  onClick={() => setIsVolunteer(false)}
                  title="Puan kazan"
                  description="30 dk = 50 puan, 60 dk = 100 puan. Ders onaylandığında puan sana yazılır."
                />
                <PriceOption
                  active={isVolunteer}
                  onClick={() => setIsVolunteer(true)}
                  title="🤝 Gönüllü ders"
                  description="Puan kazanmazsın; anlattığın ders ve süre sayaçların yine artar."
                />
              </div>
            ) : (
              <div className="rounded-lg border border-slate-200/80 bg-slate-50 p-3 text-sm text-slate-600">
                Bu ders onaylandığında sana puan yazılır (30 dk = 50, 60 dk = 100).{' '}
                <Link to="/profil" className="font-medium text-brand-600 underline">
                  Öğretmen adaylığı
                </Link>{' '}
                bilgilerini doldurursan gönüllü ders de açabilirsin.
              </div>
            )}
          </Field>
        )}

        <Field label="Not (opsiyonel)" hint="Örn: 'Sadece temel seviye anlatabilirim.'">
          <input
            className="input"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            maxLength={500}
          />
        </Field>

        <ErrorBox error={error} />
      </form>

      <div className="mt-4 flex justify-end gap-2">
        <Button variant="secondary" onClick={onClose}>
          Vazgeç
        </Button>
        <Button type="submit" form="portfolio-form" loading={busy} disabled={!topicId}>
          Ekle
        </Button>
      </div>
    </Modal>
  )
}
