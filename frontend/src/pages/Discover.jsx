import { useMemo, useState } from 'react'
import { api } from '../lib/api'
import { useAsync } from '../state/useAsync'
import { useDebounced } from '../hooks/useDebounced'
import { FilterDrawer, FilterPanel } from '../components/FilterDrawer'
import { PersonLink } from '../components/PersonLink'
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorBox,
  Field,
  Loading,
  Modal,
  Notice,
  SectionTitle,
} from '../components/ui'

const DEFAULT_FILTERS = {
  categoryId: null,
  sort: 'Relevance',
  minLevel: null,
  minRating: null,
  page: 1,
  pageSize: 20,
}

/**
 * Keşfet — İKİ MOD (Modül 1).
 *
 *  1. Varsayılan: kişiselleştirilmiş çapraz eşleşme önerileri (portföyünden türer).
 *  2. Arama/filtre kullanılır kullanılmaz: katalog geneli filtrelenmiş ilan listesi.
 *
 * Neden ayrı bir sayfa değil: ikisi de "kiminle ders yapayım" sorusunu yanıtlıyor. Ayrı
 * sekme açmak hem gezinme çubuğunu (zaten 7 öğe) şişirir hem de kullanıcıyı "önerilerde mi
 * arasam, aramada mı" ikilemine sokardı. Arama kutusuna dokunmak modu değiştirir; kutuyu
 * temizlemek önerilere döndürür.
 */
export default function Discover() {
  const [term, setTerm] = useState('')
  const [filters, setFilters] = useState(DEFAULT_FILTERS)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [target, setTarget] = useState(null)
  const [notice, setNotice] = useState(null)

  const debouncedTerm = useDebounced(term)

  const filtersTouched =
    filters.categoryId !== null ||
    filters.minLevel !== null ||
    filters.minRating !== null ||
    filters.sort !== DEFAULT_FILTERS.sort

  const searchMode = debouncedTerm.trim().length > 0 || filtersTouched

  const categories = useAsync(() => api.categories(), [])
  const portfolio = useAsync(() => api.myPortfolio(), [])
  const suggestions = useAsync(() => api.suggestions(20), [])

  const results = useAsync(
    () =>
      searchMode
        ? api.searchOffers({ ...filters, search: debouncedTerm.trim() })
        : Promise.resolve(null),
    [
      searchMode,
      debouncedTerm,
      filters.categoryId,
      filters.sort,
      filters.minLevel,
      filters.minRating,
      filters.page,
    ],
  )

  const myOffers = portfolio.data?.filter((entry) => entry.direction === 'Offer') ?? []
  const mySeekCount = portfolio.data?.filter((entry) => entry.direction === 'Seek').length ?? 0

  const activeFilterCount = useMemo(
    () =>
      [filters.categoryId, filters.minLevel, filters.minRating].filter((v) => v !== null).length +
      (filters.sort !== DEFAULT_FILTERS.sort ? 1 : 0),
    [filters],
  )

  function resetAll() {
    setFilters(DEFAULT_FILTERS)
    setTerm('')
    setDrawerOpen(false)
  }

  const filterPanel = (
    <FilterPanel
      value={filters}
      onChange={setFilters}
      onReset={resetAll}
      categories={categories.data ?? []}
      resultCount={searchMode ? (results.data?.totalCount ?? null) : null}
    />
  )

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-slate-900">Keşfet</h1>
        <p className="mt-1 text-sm text-slate-600">
          {searchMode
            ? 'Katalogdaki tüm ders ilanlarında arıyorsun.'
            : 'Almak istediğin konuları anlatabilen öğrenciler. Karşılıklı takas mümkün olanlar üstte.'}
        </p>
      </div>

      {notice && (
        <Notice tone="success" onDismiss={() => setNotice(null)}>
          {notice}
        </Notice>
      )}

      <div className="flex gap-2">
        {/* shadow-sm: arama kutusu sayfanın ana giriş noktası ama .input tek başına
            zeminle aynı düzlemde kalıyordu; kartlarla aynı ince gölge onu da "dokunulur
            bir yüzey" yapıyor. Odak halkası ve yumuşak köşe zaten .input'tan geliyor
            (index.css) — burada yalnızca derinlik ekleniyor, ikinci bir stil dili değil. */}
        <input
          className="input shadow-sm"
          type="search"
          placeholder="Konu, ders ya da eğitmen ara…"
          value={term}
          onChange={(e) => {
            setTerm(e.target.value)
            setFilters((f) => ({ ...f, page: 1 }))
          }}
          aria-label="Ara"
        />
        <Button
          variant="secondary"
          className="shrink-0 lg:hidden"
          onClick={() => setDrawerOpen(true)}
        >
          Filtre{activeFilterCount > 0 ? ` (${activeFilterCount})` : ''}
        </Button>
      </div>

      <div className="grid gap-6 lg:grid-cols-[260px_1fr]">
        {/* Masaüstünde sabit sütun; mobilde çekmece (aşağıda). */}
        <aside className="hidden lg:block">
          <Card>{filterPanel}</Card>
        </aside>

        <div className="min-w-0">
          {searchMode ? (
            <SearchResults
              results={results}
              onRequest={setTarget}
              onClearFilters={resetAll}
              onPage={(page) => setFilters((f) => ({ ...f, page }))}
            />
          ) : (
            <Suggestions
              suggestions={suggestions}
              mySeekCount={mySeekCount}
              portfolioLoading={portfolio.loading}
              onRequest={setTarget}
            />
          )}
        </div>
      </div>

      <FilterDrawer open={drawerOpen} onClose={() => setDrawerOpen(false)}>
        {filterPanel}
      </FilterDrawer>

      <RequestModal
        person={target}
        myOffers={myOffers}
        onClose={() => setTarget(null)}
        onSent={(name) => {
          setTarget(null)
          setNotice(`${name} kişisine eşleşme isteği gönderildi. Kabul edilince sohbet açılacak.`)
          suggestions.reload()
          results.reload()
        }}
      />
    </div>
  )
}

function Suggestions({ suggestions, mySeekCount, portfolioLoading, onRequest }) {
  return (
    <div className="space-y-4">
      <ErrorBox error={suggestions.error} onRetry={suggestions.reload} />

      {!portfolioLoading && mySeekCount === 0 && (
        <Notice tone="info">
          Öneriler, <strong>“Almak istediğim konular”</strong> listenden üretilir. Portföyüne en az
          bir konu ekleyerek başla — ya da yukarıdan arayarak tüm ilanlara bak.
        </Notice>
      )}

      {suggestions.loading ? (
        <Loading />
      ) : (suggestions.data?.length ?? 0) === 0 ? (
        <EmptyState
          title="Şimdilik eşleşme yok"
          description="Almak istediğin konuları genişlet ya da yukarıdaki arama kutusundan katalogda ara."
        />
      ) : (
        <div className="grid gap-4 md:grid-cols-2">
          {suggestions.data.map((person) => (
            /* Öneri ve arama sonucu kartları AYNI hover dilini taşıyor (marka tonlu
               kenarlık + koyulaşan gölge): iki mod tek sayfada yaşıyor, kartların
               tepkisi mod değiştikçe farklılaşsaydı sayfa iki ayrı uygulama gibi
               hissettirirdi. */
            <Card
              key={person.userId}
              className="flex flex-col justify-between transition hover:border-brand-200 hover:shadow-md"
            >
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <PersonLink userId={person.userId} className="font-semibold text-brand-700">
                    {person.displayName}
                  </PersonLink>
                  {/* Gönüllülük rozeti kaldırıldı: gönüllü ders kavramı yok, herkes eşit. */}
                  {person.isCrossMatch && <Badge tone="success">Karşılıklı takas</Badge>}
                  {person.ratingCount > 0 && (
                    <Badge tone="neutral">
                      ★ {Number(person.averageRating).toFixed(1)} ({person.ratingCount})
                    </Badge>
                  )}
                </div>

                <TopicList title="Sana anlatabilir" tone="brand" topics={person.theyCanTeach} />

                {person.theyWantToLearn?.length > 0 && (
                  <TopicList
                    title="Senden öğrenmek istiyor"
                    tone="success"
                    topics={person.theyWantToLearn}
                  />
                )}
              </div>

              <div className="mt-4">
                <Button className="w-full" onClick={() => onRequest(person)}>
                  Eşleşme isteği gönder
                </Button>
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  )
}

function SearchResults({ results, onRequest, onClearFilters, onPage }) {
  const data = results.data

  return (
    <div className="space-y-4">
      <ErrorBox error={results.error} onRetry={results.reload} />

      {results.loading ? (
        <Loading label="Aranıyor…" />
      ) : (data?.items?.length ?? 0) === 0 ? (
        <EmptyState
          title="Sonuç yok"
          description="Aramayı kısaltmayı ya da filtreleri gevşetmeyi dene."
          action={
            <Button variant="secondary" onClick={onClearFilters}>
              Filtreleri temizle
            </Button>
          }
        />
      ) : (
        <>
          <SectionTitle>{data.totalCount} ilan</SectionTitle>

          <div className="grid gap-4 md:grid-cols-2">
            {data.items.map((offer) => (
              /* Öneri kartlarıyla birebir aynı hover — gerekçesi Suggestions'ta. */
              <Card
                key={offer.offerId}
                className="flex flex-col justify-between transition hover:border-brand-200 hover:shadow-md"
              >
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <PersonLink userId={offer.tutorUserId} className="font-semibold text-brand-700">
                      {offer.tutorDisplayName}
                    </PersonLink>
                    {offer.tutorRatingCount > 0 ? (
                      <Badge tone="neutral">
                        ★ {Number(offer.tutorAverageRating).toFixed(1)} ({offer.tutorRatingCount})
                      </Badge>
                    ) : (
                      <Badge tone="neutral">Yeni</Badge>
                    )}
                  </div>

                  <div className="mt-3">
                    <Badge tone="brand">{offer.topicName}</Badge>
                    {/* Süre rozeti artık koşulsuz: gönüllülük ayrımı kalktı, her ilan
                        aynı iki süre seçeneğiyle açılıyor. */}
                    <Badge tone="neutral" className="ml-1.5">30 / 60 dk</Badge>
                    <p className="mt-1.5 text-xs text-slate-500">
                      {offer.categoryName} · {offer.subjectName} · seviye{' '}
                      {offer.selfAssessedLevel}/5
                    </p>
                  </div>

                  {offer.note && (
                    <p className="mt-2 line-clamp-3 text-sm text-slate-600">{offer.note}</p>
                  )}
                </div>

                <div className="mt-4">
                  <Button
                    className="w-full"
                    onClick={() =>
                      /*
                        Arama sonucunda konu ZATEN belli, o yüzden istek penceresine
                        tek elemanlı bir "anlatabilir" listesiyle giriliyor. Karşı tarafın
                        ne öğrenmek istediğini bu uç dönmediği için takas teklifi kutusu
                        boş kalır — kullanıcı kredisiyle alır.
                      */
                      onRequest({
                        userId: offer.tutorUserId,
                        displayName: offer.tutorDisplayName,
                        theyCanTeach: [
                          {
                            topicId: offer.topicId,
                            topicName: offer.topicName,
                            subjectName: offer.subjectName,
                          },
                        ],
                        theyWantToLearn: [],
                      })
                    }
                  >
                    Eşleşme isteği gönder
                  </Button>
                </div>
              </Card>
            ))}
          </div>

          {data.totalPages > 1 && (
            <div className="flex items-center justify-between gap-3">
              <Button
                variant="secondary"
                disabled={data.page <= 1}
                onClick={() => onPage(data.page - 1)}
              >
                ← Önceki
              </Button>
              <span className="text-sm text-slate-500">
                {data.page} / {data.totalPages}
              </span>
              <Button
                variant="secondary"
                disabled={!data.hasNextPage}
                onClick={() => onPage(data.page + 1)}
              >
                Sonraki →
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  )
}

function TopicList({ title, tone, topics }) {
  if (!topics?.length) return null
  return (
    <div className="mt-3">
      <p className="text-xs font-medium uppercase tracking-wide text-slate-500">{title}</p>
      <div className="mt-1.5 flex flex-wrap gap-1.5">
        {/* Tek ton: konu başına gönüllülük işareti kalktı, hepsi aynı ağırlıkta. */}
        {topics.map((topic) => (
          <Badge key={topic.topicId} tone={tone}>
            {topic.topicName}
            <span className="ml-1 opacity-70">· {topic.subjectName}</span>
          </Badge>
        ))}
      </div>
    </div>
  )
}

function RequestModal({ person, myOffers, onClose, onSent }) {
  const [requestedTopicId, setRequestedTopicId] = useState('')
  const [offeredTopicId, setOfferedTopicId] = useState('')
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)

  // Karşı tarafın öğrenmek istedikleri ile benim verebildiklerimin kesişimi = geçerli takas teklifi.
  const wantedTopicIds = new Set((person?.theyWantToLearn ?? []).map((topic) => topic.topicId))
  const tradeableOffers = myOffers.filter((offer) => wantedTopicIds.has(offer.topicId))

  async function submit(event) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.createMatch({
        responderUserId: person.userId,
        requestedTopicId,
        offeredTopicId: offeredTopicId || null,
      })
      setRequestedTopicId('')
      setOfferedTopicId('')
      onSent(person.displayName)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open={Boolean(person)} onClose={onClose} title="Eşleşme isteği">
      {person && (
        <>
          <form onSubmit={submit} id="match-form" className="space-y-4">
            <Field label={`${person.displayName} kişisinden almak istediğin konu`}>
              <select
                className="input"
                value={requestedTopicId}
                onChange={(e) => setRequestedTopicId(e.target.value)}
                required
              >
                <option value="">Seç…</option>
                {person.theyCanTeach.map((topic) => (
                  <option key={topic.topicId} value={topic.topicId}>
                    {topic.topicName} ({topic.subjectName})
                  </option>
                ))}
              </select>
            </Field>

            <Field
              label="Karşılığında anlatmayı önerdiğin konu (opsiyonel)"
              hint={
                tradeableOffers.length > 0
                  ? 'Takas teklifi isteğin kabul edilme ihtimalini artırır.'
                  : 'Karşı tarafın aradığı konulardan birini verebiliyorsan burada görünür. Boş bırakman da sorun değil — ders almak ücretsiz.'
              }
            >
              <select
                className="input"
                value={offeredTopicId}
                onChange={(e) => setOfferedTopicId(e.target.value)}
                disabled={tradeableOffers.length === 0}
              >
                <option value="">Teklif yok — yalnızca ders almak istiyorum</option>
                {tradeableOffers.map((offer) => (
                  <option key={offer.entryId} value={offer.topicId}>
                    {offer.topicName} ({offer.subjectName})
                  </option>
                ))}
              </select>
            </Field>

            <ErrorBox error={error} />
          </form>

          <div className="mt-4 flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose}>
              Vazgeç
            </Button>
            <Button type="submit" form="match-form" loading={busy} disabled={!requestedTopicId}>
              İsteği gönder
            </Button>
          </div>
        </>
      )}
    </Modal>
  )
}
