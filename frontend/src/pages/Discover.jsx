import { useMemo, useState } from 'react'
import { api } from '../lib/api'
import { useAsync } from '../state/useAsync'
import { useDebounced } from '../hooks/useDebounced'
import { FilterDrawer, FilterPanel, UniversiteFiltrePaneli } from '../components/FilterDrawer'
import { PersonLink } from '../components/PersonLink'
import { SeviyeRozeti } from '../components/SeviyeRozeti'
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
  Pagination,
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

/*
  Üniversite ağı filtresi YKS durumundan AYRI tutuluyor: iki sekme aynı sayfada yaşıyor
  ama farklı soru soruyor (biri konu/ilan, diğeri okuduğu yer). Ortak bir durum nesnesi,
  sekme değiştirince yazılanı silmek ya da bir kipin alanını diğerinin sorgusuna
  sızdırmak zorunda bırakırdı.
*/
const UNIVERSITE_VARSAYILAN = {
  university: '',
  department: '',
  page: 1,
  pageSize: 20,
}

const SEKMELER = [
  { key: 'yks', label: 'YKS' },
  { key: 'universite', label: 'Üniversite' },
]

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
 *
 * Bunların ÜSTÜNDE ayrıca bir sekme var: YKS / Üniversite. Sekme moddan farklı bir şey
 * ayırıyor — yukarıdaki iki mod aynı veriyi (ders ilanları) iki biçimde gösterirken
 * Üniversite sekmesi BAŞKA BİR VERİYE bakıyor: kişinin okuduğu üniversite ve bölüm.
 * Orada ders, konu ve konu seviyesi kavramı yok; sekme sessizce gizlenen bir filtre
 * değil, ayrı bir soru olduğu için açık bir seçim olarak duruyor.
 */
export default function Discover() {
  const [sekme, setSekme] = useState('yks')
  const [term, setTerm] = useState('')
  const [filters, setFilters] = useState(DEFAULT_FILTERS)
  const [uniFiltre, setUniFiltre] = useState(UNIVERSITE_VARSAYILAN)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [target, setTarget] = useState(null)
  const [sohbetHedefi, setSohbetHedefi] = useState(null)
  const [notice, setNotice] = useState(null)

  const universiteKipi = sekme === 'universite'

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

  /*
    İki metin alanı gecikmeli (debounce) sorgulanıyor, sayfa numarası ANINDA: harf başına
    istek atmak hem gereksiz yük hem de yarış demek ("Bo" yanıtı "Boğaziçi"ninkinden sonra
    gelip listeyi yanlış doldurabilir). Sayfa düğmesi ise tek bir kasıtlı tıklama, onu
    geciktirmek arayüzü tembel gösterirdi.

    Sekme YKS'deyken Promise.resolve(null): searchMode kalıbının aynısı — görünmeyen bir
    sekmenin sorgusu koşmasın.
  */
  const gecikmeliUniversite = useDebounced(uniFiltre.university)
  const gecikmeliBolum = useDebounced(uniFiltre.department)

  const uniSonuclar = useAsync(
    () =>
      universiteKipi
        ? api.searchUniversityPeers({
            ...uniFiltre,
            university: gecikmeliUniversite,
            department: gecikmeliBolum,
          })
        : Promise.resolve(null),
    [universiteKipi, gecikmeliUniversite, gecikmeliBolum, uniFiltre.page],
  )

  const myOffers = portfolio.data?.filter((entry) => entry.direction === 'Offer') ?? []
  const mySeekCount = portfolio.data?.filter((entry) => entry.direction === 'Seek').length ?? 0

  const activeFilterCount = useMemo(
    () =>
      [filters.categoryId, filters.minLevel, filters.minRating].filter((v) => v !== null).length +
      (filters.sort !== DEFAULT_FILTERS.sort ? 1 : 0),
    [filters],
  )

  // Boşluk temizleniyor: yalnızca boşluk yazılmış bir alan sorguyu değiştirmiyor, o
  // yüzden sayaçta da "aktif filtre" gibi görünmemeli.
  const uniAktifFiltreSayisi = [uniFiltre.university, uniFiltre.department].filter(
    (v) => (v ?? '').trim() !== '',
  ).length

  function resetAll() {
    setFilters(DEFAULT_FILTERS)
    setTerm('')
    setDrawerOpen(false)
  }

  function universiteFiltreleriniTemizle() {
    setUniFiltre(UNIVERSITE_VARSAYILAN)
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

  /*
    Aktif sekmenin paneli TEK yerde seçiliyor: aynı panel hem masaüstü sütununda hem
    mobil çekmecede basılıyor ve ikisinin ayrışması, "Filtre" düğmesinin yanlış sekmenin
    denetimlerini açması demek olurdu.
  */
  const aktifPanel = universiteKipi ? (
    <UniversiteFiltrePaneli
      value={uniFiltre}
      onChange={setUniFiltre}
      onReset={universiteFiltreleriniTemizle}
      resultCount={uniSonuclar.data?.totalCount ?? null}
    />
  ) : (
    filterPanel
  )

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-slate-900">Keşfet</h1>

        {/* Şerit başlıkla açıklama arasında: açıklama metni seçili sekmeye göre
            değiştiği için seçimin ARDINDAN okunmalı. */}
        <div
          className="mt-3 inline-flex rounded-xl bg-slate-100 p-1"
          role="tablist"
          aria-label="Keşif sekmeleri"
        >
          {SEKMELER.map((item) => (
            <button
              key={item.key}
              type="button"
              role="tab"
              aria-selected={sekme === item.key}
              onClick={() => setSekme(item.key)}
              className={`min-h-11 rounded-lg px-4 py-2 text-sm font-medium transition lg:min-h-0 ${
                sekme === item.key
                  ? 'bg-white text-brand-700 shadow-sm'
                  : 'text-slate-600 hover:text-slate-800'
              }`}
            >
              {item.label}
            </button>
          ))}
        </div>

        <p className="mt-3 text-sm text-slate-600">
          {universiteKipi
            ? 'Aynı üniversiteden ya da okumak istediğin bölümden öğrencileri bul.'
            : searchMode
              ? 'Katalogdaki tüm ders ilanlarında arıyorsun.'
              : 'Almak istediğin konuları anlatabilen öğrenciler. Karşılıklı takas mümkün olanlar üstte.'}
        </p>
      </div>

      {notice && (
        <Notice tone="success" onDismiss={() => setNotice(null)}>
          {notice}
        </Notice>
      )}

      {/*
        Arama kutusu ÜNİVERSİTE SEKMESİNDE YOK: o kutu konu/ders/eğitmen arıyor, üniversite
        ağında ise konu kavramı bulunmuyor. Bırakılsaydı yazılan hiçbir şeyin sonucu
        değiştirmediği bir alan olurdu — sessizce çalışmayan bir denetim. Filtreleme
        oradaki tek yerden, soldaki iki alandan yapılıyor.
      */}
      <div className="flex gap-2">
        {/* shadow-sm: arama kutusu sayfanın ana giriş noktası ama .input tek başına
            zeminle aynı düzlemde kalıyordu; kartlarla aynı ince gölge onu da "dokunulur
            bir yüzey" yapıyor. Odak halkası ve yumuşak köşe zaten .input'tan geliyor
            (index.css) — burada yalnızca derinlik ekleniyor, ikinci bir stil dili değil. */}
        {!universiteKipi && (
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
        )}
        {/* ml-auto: arama kutusu düşünce düğme tek başına kalıyor ve sola yapışırdı;
            filtre denetimi her iki sekmede de sağ kenarda duruyor. */}
        <Button
          variant="secondary"
          className="ml-auto shrink-0 lg:hidden"
          onClick={() => setDrawerOpen(true)}
        >
          {universiteKipi
            ? `Filtre${uniAktifFiltreSayisi > 0 ? ` (${uniAktifFiltreSayisi})` : ''}`
            : `Filtre${activeFilterCount > 0 ? ` (${activeFilterCount})` : ''}`}
        </Button>
      </div>

      <div className="grid gap-6 lg:grid-cols-[260px_1fr]">
        {/* Masaüstünde sabit sütun; mobilde çekmece (aşağıda). */}
        <aside className="hidden lg:block">
          <Card>{aktifPanel}</Card>
        </aside>

        <div className="min-w-0">
          {universiteKipi ? (
            <UniversiteSonuclari
              sonuclar={uniSonuclar}
              onSohbet={setSohbetHedefi}
              onSayfa={(page) => setUniFiltre((f) => ({ ...f, page }))}
            />
          ) : searchMode ? (
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
        {aktifPanel}
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

      {/* key: kip kapanınca bileşen monte kalıyor ve içindeki hata/gönderim durumu bir
          sonraki kişide de görünürdü. Hedef değişince yeniden monte edilmesi durumu
          sıfırlıyor — kip her açılışta temiz. */}
      <SohbetIstegiModali
        key={sohbetHedefi?.userId ?? 'sohbet-istegi-yok'}
        kisi={sohbetHedefi}
        onClose={() => setSohbetHedefi(null)}
        onSent={(name) => {
          setSohbetHedefi(null)
          setNotice(`${name} kişisine sohbet isteği gönderildi. Kabul edilince sohbet açılacak.`)
          uniSonuclar.reload({ silent: true })
        }}
      />
    </div>
  )
}

/**
 * Üniversite ağı sonuç listesi.
 *
 * Sayfalama ui.jsx'teki Pagination ile: bu liste bir katalog taraması değil, kişi listesi
 * ve kullanıcı "sona git" isteyebiliyor. YKS tarafındaki önceki/sonraki ikilisi orada
 * kalmaya devam ediyor — orada sıralama alaka temelli, sayfa numarasının anlamı yok.
 */
function UniversiteSonuclari({ sonuclar, onSohbet, onSayfa }) {
  const veri = sonuclar.data

  return (
    <div className="space-y-4">
      <ErrorBox error={sonuclar.error} onRetry={sonuclar.reload} />

      {sonuclar.loading ? (
        <Loading label="Aranıyor…" />
      ) : (veri?.items?.length ?? 0) === 0 ? (
        <EmptyState
          title="Kimseyi bulamadık"
          description="Üniversite ya da bölüm adını değiştirip tekrar dene."
        />
      ) : (
        <>
          <SectionTitle>{veri.totalCount} öğrenci</SectionTitle>

          <div className="grid gap-4 md:grid-cols-2">
            {veri.items.map((kisi) => (
              <UniversiteKarti key={kisi.userId} kisi={kisi} onSohbet={onSohbet} />
            ))}
          </div>

          <Pagination page={veri.page} totalPages={veri.totalPages} onChange={onSayfa} />
        </>
      )}
    </div>
  )
}

/**
 * Üniversite ağı kişi kartı.
 *
 * KARTTA DERS/KONU YOK ve bu bilinçli: buradaki kayıt bir ilan değil, kişinin okuduğu yer.
 * "Anlatabilir / öğrenmek istiyor" listeleri katalog verisinden gelir, bu uçta yoktur —
 * yer tutucu olarak konmaları boş kutular üretirdi.
 *
 * BÖLÜM VURGULU (font-medium text-slate-800), üniversite normal ağırlıkta: aranan şey
 * çoğunlukla bölüm, üniversite onu konumlandıran bağlam.
 *
 * 1–10 genel seviye rozeti BURADA DA VAR: seviye kişiye ait, konuya değil — üniversite
 * ağında da aynı anlamı taşıyor.
 */
function UniversiteKarti({ kisi, onSohbet }) {
  return (
    /* Hover dili öneri/arama kartlarıyla birebir aynı — gerekçesi Suggestions'ta. */
    <Card className="flex flex-col justify-between transition hover:border-brand-200 hover:shadow-md">
      <div>
        {/* items-start: uzun bir isim iki satıra düştüğünde rozet ilk satırın hizasında
            kalsın, ortalanıp aşağı kaymasın. */}
        <div className="flex items-start justify-between gap-2">
          <PersonLink userId={kisi.userId} className="font-semibold text-brand-700">
            {kisi.displayName}
          </PersonLink>
          <SeviyeRozeti kaynak={{ level: kisi.level }} boyut="sm" ton="acik" />
        </div>

        {kisi.university && <p className="mt-2 text-sm text-slate-600">{kisi.university}</p>}
        {kisi.department && (
          <p className="mt-0.5 text-sm font-medium text-slate-800">{kisi.department}</p>
        )}

        <p className="mt-2 text-xs text-slate-500">
          {kisi.ratingCount > 0
            ? `${Number(kisi.averageRating).toFixed(1)} ★ (${kisi.ratingCount} değerlendirme)`
            : 'Henüz değerlendirilmemiş'}
        </p>
      </div>

      <div className="mt-4">
        <Button className="w-full" onClick={() => onSohbet(kisi)}>
          Sohbet isteği gönder
        </Button>
      </div>
    </Card>
  )
}

/**
 * Üniversite ağı sohbet isteği kipi.
 *
 * RequestModal'dan AYRI bir bileşen: oradaki formun tamamı konu seçimidir (alınacak konu +
 * takas teklifi) ve burada seçilecek konu yok. Aynı bileşene "konusuz kip" dalı eklemek,
 * iki farklı akışı tek gövdede koşullarla yaşatmak olurdu.
 *
 * requestedTopicId null gidiyor — uç konusuz isteği bu şekilde tanıyor (bkz. api.js).
 */
function SohbetIstegiModali({ kisi, onClose, onSent }) {
  const [hata, setHata] = useState(null)
  const [busy, setBusy] = useState(false)

  async function gonder() {
    setBusy(true)
    setHata(null)
    try {
      await api.createMatch({
        responderUserId: kisi.userId,
        requestedTopicId: null,
        offeredTopicId: null,
      })
      onSent(kisi.displayName)
    } catch (err) {
      setHata(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open={Boolean(kisi)} onClose={onClose} title="Sohbet isteği">
      {kisi && (
        <>
          <div className="space-y-3">
            <div>
              <p className="font-semibold text-slate-800">{kisi.displayName}</p>
              {kisi.university && <p className="text-sm text-slate-600">{kisi.university}</p>}
              {kisi.department && (
                <p className="text-sm font-medium text-slate-800">{kisi.department}</p>
              )}
            </div>

            <p className="text-sm text-slate-600">
              Kabul edilirse sohbet açılır ve doğrudan yazışabilirsiniz.
            </p>

            <ErrorBox error={hata} />
          </div>

          <div className="mt-4 flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose}>
              Vazgeç
            </Button>
            <Button onClick={gonder} loading={busy}>
              İsteği gönder
            </Button>
          </div>
        </>
      )}
    </Modal>
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
                {/* Kart hiyerarşisi: kimlik üstte (isim + seviye rozeti + puan), bio
                    altında, konular sonra, aksiyon en altta — göz önce kişiyi tanısın.
                    items-start: uzun isim ikinci satıra düşerse rozet ilk satırın
                    hizasında kalsın (UniversiteKarti ile aynı gerekçe). */}
                <div className="flex items-start justify-between gap-2">
                  <div className="flex flex-wrap items-center gap-2">
                    <PersonLink userId={person.userId} className="font-semibold text-brand-700">
                      {person.displayName}
                    </PersonLink>
                    {/* Gönüllülük rozeti kaldırıldı: gönüllü ders kavramı yok, herkes eşit. */}
                    {person.isCrossMatch && <Badge tone="success">Karşılıklı takas</Badge>}
                  </div>
                  <SeviyeRozeti kaynak={{ level: person.level }} boyut="sm" ton="acik" />
                </div>

                {/* Puan rozet değil küçük satır: kimlik bloğunda tek vurgu rozette
                    kalsın, iki rozet yan yana yarışmasın. */}
                {person.ratingCount > 0 && (
                  <p className="mt-1 text-xs text-slate-500">
                    {Number(person.averageRating).toFixed(1)} ★ ({person.ratingCount})
                  </p>
                )}

                {/* bio null ise satır TAMAMEN düşer — boş çizgi kalmasın. */}
                {person.bio && (
                  <p className="mt-2 line-clamp-2 text-sm text-slate-600">{person.bio}</p>
                )}

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
                  {/* Öneri kartıyla aynı kimlik hiyerarşisi (kimlik → bio → konu):
                      iki mod tek sayfada yaşıyor, kart dili moda göre ayrışmasın.
                      Puan bu yüzden rozetten küçük satıra taşındı; "Yeni" rozeti
                      puanın yokluğunu söylediği için isim satırında kalıyor. */}
                  <div className="flex flex-wrap items-center gap-2">
                    <PersonLink userId={offer.tutorUserId} className="font-semibold text-brand-700">
                      {offer.tutorDisplayName}
                    </PersonLink>
                    {offer.tutorRatingCount === 0 && <Badge tone="neutral">Yeni</Badge>}
                  </div>

                  {offer.tutorRatingCount > 0 && (
                    <p className="mt-1 text-xs text-slate-500">
                      {Number(offer.tutorAverageRating).toFixed(1)} ★ ({offer.tutorRatingCount})
                    </p>
                  )}

                  {/* tutorBio null ise satır TAMAMEN düşer (öneri kartıyla aynı biçim). */}
                  {offer.tutorBio && (
                    <p className="mt-2 line-clamp-2 text-sm text-slate-600">{offer.tutorBio}</p>
                  )}

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
