import { useState } from 'react'
import { api } from '../lib/api'
import { KonuSecici } from '../components/KonuSecici'
import { useAsync } from '../state/useAsync'
import { Badge, Button, Card, EmptyState, ErrorBox, Field, Loading, Modal, SectionTitle } from '../components/ui'
import { AnalyticsEvents, trackEvent } from '../lib/analytics'

/**
 * Dinamik portföy (Modül 1.1): iki yönlü profil.
 *  Offer = "Verebileceğim dersler"   → PUAN KAZANDIRIR (ders onaylandığında basılır)
 *  Seek  = "Almak istediğim dersler" → bedelsiz; yalnızca eşleşme için sinyal
 * Çapraz eşleşme algoritması bu iki listeyi karşı tarafınkiyle çakıştırır.
 */
export default function Portfolio() {
  const entries = useAsync(() => api.myPortfolio(), [])
  const [modalDirection, setModalDirection] = useState(null)

  /*
    KATALOG BURADA, MODALDA DEĞİL. Konu seçici her açıldığında yeniden istek atsaydı
    kullanıcı iki konu eklerken aynı 767 satırı iki kez indirirdi. Sayfa açılışında bir
    kez alınıyor ve iki yöne (Offer/Seek) de aynı liste veriliyor.
  */
  const konular = useAsync(() => api.topics(), [])

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
        konular={konular.data}
        konularYukleniyor={konular.loading}
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
            /* Hover'da marka tonlu kenarlık + bir kademe koyu gölge: kartın içinde
               basılabilir bir eylem ("Kaldır") var, ama kart gövdesi tepkisiz kalınca
               liste ölü duruyordu. Geçiş Card'ın kendi sınıflarına EK olarak veriliyor —
               ui.jsx yüzey dilinin tek sahibi, ona dokunulmuyor. */
            <Card
              key={entry.entryId}
              className="flex items-start justify-between gap-3 p-4 transition hover:border-brand-200 hover:shadow-md"
            >
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-medium text-slate-800">{entry.topicName}</span>
                  <Badge tone={tone}>Seviye {entry.selfAssessedLevel}/5</Badge>
                </div>
                <p className="mt-0.5 text-xs text-slate-500">
                  {entry.categoryName} · {entry.subjectName}
                </p>
                {entry.note && <p className="mt-2 text-sm text-slate-600">{entry.note}</p>}
              </div>

              {/* "Kaldır" yıkıcı bir eylem; hover rengi anlamını söylüyor. rose,
                  Tailwind paletinde slate'ten SONRA üretildiği için ghost varyantının
                  hover:bg-slate-100 değerini güvenle ezer — sıra tersine dönseydi bu
                  sınıflar sessizce etkisiz kalırdı. */}
              <Button
                variant="ghost"
                className="hover:bg-rose-50 hover:text-rose-600"
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

/*
  İKİ AŞAMALI EKLEME: önce konu seçilir (KonuSecici), sonra detay formu doldurulur.

  Tek ekranda tutulabilirdi ama olmadı: hiyerarşik seçici dört basamak ve kendi
  kırıntı yolunu taşıyor; altına seviye kaydırıcısı ve not alanı da konsaydı kullanıcı,
  daha ders bile seçmemişken "Bu konudaki seviyen" sorusunu görüyor olurdu. Form,
  cevaplanacak sorusu oluşmadan görünmemeli.
*/
function AddEntryModal({ direction, konular, konularYukleniyor, onClose, onSaved }) {
  const [secilenKonu, setSecilenKonu] = useState(null)
  const [level, setLevel] = useState(3)
  const [note, setNote] = useState('')
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)

  const isOffer = direction === 'Offer'
  const acik = Boolean(direction)

  function kapat() {
    setSecilenKonu(null)
    setLevel(3)
    setNote('')
    setError(null)
    onClose()
  }

  async function onSubmit(event) {
    event.preventDefault()
    if (busy || !secilenKonu) return
    setBusy(true)
    setError(null)
    try {
      await api.addPortfolioEntry({
        topicId: secilenKonu.topicId,
        direction,
        selfAssessedLevel: Number(level),
        note: note.trim() || null,
      })

      /*
        lesson_created — "yeni ders ilanı açıldı". Yalnızca Offer sayılır: Seek girişi bir
        ilan değil, talep beyanıdır ve ikisini aynı olayda toplamak metriği anlamsız kılardı.
        Olay API BAŞARILI döndükten sonra atılır (tıklamada değil): başarısız denemeler
        ilan sayısını şişirmemeli. topicId GÖNDERİLMEZ — bkz. trackEvent kişisel veri notu.
      */
      if (direction === 'Offer') {
        trackEvent(AnalyticsEvents.LessonCreated, { self_assessed_level: Number(level) })
      }

      setSecilenKonu(null)
      setNote('')
      setLevel(3)
      onSaved()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  // 1. AŞAMA — konu seçimi
  if (acik && !secilenKonu) {
    return (
      <KonuSecici
        open
        konular={konular}
        yukleniyor={konularYukleniyor}
        baslik={isOffer ? 'Verebileceğim konu ekle' : 'Almak istediğim konu ekle'}
        onClose={kapat}
        onSelect={setSecilenKonu}
      />
    )
  }

  // 2. AŞAMA — detaylar
  return (
    <Modal
      open={acik}
      onClose={kapat}
      title={isOffer ? 'Verebileceğim konu ekle' : 'Almak istediğim konu ekle'}
    >
      <form onSubmit={onSubmit} className="space-y-4" id="portfolio-form">
        {/* Seçilen konu ÖZET OLARAK duruyor ve yanında "Değiştir" var: kullanıcı hangi
            konuyu seçtiğini formu doldururken de görebilmeli, yanlış seçtiyse baştan
            başlamak zorunda kalmamalı. */}
        <div className="flex items-start justify-between gap-3 rounded-lg border border-brand-200 bg-brand-50 p-3">
          <div className="min-w-0">
            <p className="truncate text-sm font-semibold text-brand-900">{secilenKonu?.topic}</p>
            <p className="mt-0.5 text-xs text-brand-800">
              {secilenKonu?.rootCategory} · {secilenKonu?.category} · {secilenKonu?.subject}
            </p>
          </div>
          <button
            type="button"
            onClick={() => setSecilenKonu(null)}
            className="shrink-0 rounded px-2 py-1 text-xs font-medium text-brand-700 underline transition hover:bg-white/60 hover:no-underline active:bg-white"
          >
            Değiştir
          </button>
        </div>

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
          İPUCU YÖNE GÖRE DEĞİŞİYOR. Tek bir örnek metin ("Sadece temel seviye
          anlatabilirim") iki listede birden kullanılıyordu ve ALMAK isteyen tarafta
          anlamsızdı: ders alacak kişi ne anlatacağını değil, neye ihtiyacı olduğunu
          yazar. Not alanının işi karşı tarafa bir beklenti iletmek; beklenti de yöne
          göre değişiyor.
        */}
        <Field
          label="Not (opsiyonel)"
          hint={
            isOffer
              ? 'Örn: Temelden başlayıp soru çözümüyle ilerliyorum.'
              : 'Örn: Bu konunun formüllerinde zorlanıyorum, bol soru çözümü istiyorum.'
          }
        >
          <input
            className="input"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            maxLength={500}
            placeholder={
              isOffer
                ? 'Temelden başlayıp soru çözümüyle ilerliyorum.'
                : 'Bu konunun formüllerinde zorlanıyorum, bol soru çözümü istiyorum.'
            }
          />
        </Field>

        <ErrorBox error={error} />
      </form>

      <div className="mt-4 flex justify-end gap-2">
        <Button variant="secondary" onClick={kapat}>
          Vazgeç
        </Button>
        <Button type="submit" form="portfolio-form" loading={busy} disabled={busy || !secilenKonu}>
          Ekle
        </Button>
      </div>
    </Modal>
  )
}
