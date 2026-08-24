import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../lib/api'
import { useAsync } from '../state/useAsync'
import { formatDateTime } from '../lib/format'
import { Badge, Button, Card, EmptyState, ErrorBox, Loading, Notice } from '../components/ui'
import { PersonLink } from '../components/PersonLink'

/*
  Sekmelerin İKİ ADI var: dar ekranda `kisa`, sm üstünde `label`.

  Uzun adlar telefonda üç sekmeye bölününce sığmıyordu. 375px'te bir sekmeye ~109px
  düşüyor ve "Aktif eşleşmeler (62)" tek satıra sığmayıp İKİ SATIRA kırılıyordu:
  komşuları tek satırdı, şerit tırtıklı görünüyordu. 320px'te daha kötüsü oluyordu —
  metin kendi sekmesinden taşıp yanındakinin üstüne biniyordu.

  Sayaç kırılmayı tetikleyen şeydi ama sayaç bilgi taşıyor; atılacak olan uzun ad.
  "Gelen / Giden / Aktif" bağlamda tek başına anlaşılıyor: sayfanın başlığı zaten
  "Eşleşmeler" ve altında ne olduğunu anlatan bir satır var.

  Kısa ad yalnızca yer açmıyor, PUNTOYU DA GERİ GETİRİYOR: şerit mobilde text-xs
  (12px) kullanmak zorunda kalmıştı — sırf uzun adlar sığsın diye. Kısa adla text-sm
  (14px) rahat sığıyor, yani okunaklılık kırpmanın bedeli olmaktan çıkıyor.
*/
const TABS = [
  { key: 'incoming', label: 'Gelen istekler', kisa: 'Gelen' },
  { key: 'outgoing', label: 'Gönderdiklerim', kisa: 'Giden' },
  { key: 'active', label: 'Aktif eşleşmeler', kisa: 'Aktif' },
]

export default function Matches() {
  const matches = useAsync(() => api.myMatches(), [])
  const [tab, setTab] = useState('incoming')
  const [notice, setNotice] = useState(null)

  const lists = matches.data ?? { incoming: [], outgoing: [], active: [] }
  const current = lists[tab] ?? []

  return (
    /*
      ─────────────────────────────────────────────────────────────────────────
      EKRANA SABİT KABUK (2026-08-24) — Sessions.jsx'teki kanıtlanmış düzenin aynısı.

      Uzun bir aktif eşleşme listesi SAYFANIN TAMAMINI kaydırıyordu: başlık ve sekme
      çubuğu ekrandan çıkıyor, sekme değiştirmek için en yukarı dönmek gerekiyordu.
      Artık lg üstünde sayfa sabit; yalnızca sekme içeriği kendi panelinde kayar
      (bir sohbet penceresi gibi), başlık ve sekmeler hep görünür kalır.

      Yükseklik: 100dvh − 10.5rem. Üç parça: 4rem sabit üst bar + 3rem <main> dolgusu
      (py-6) + 3.5rem altbilgi. Bu toplam Sessions.jsx düzeni kurulurken TARAYICIDA
      ÖLÇÜLDÜ — sayıyı değiştirmeden önce oradaki gerekçeyi oku (altbilgi ilk hesapta
      atlanmış ve sayfa tam 56px kaymıştı). vh DEĞİL dvh: mobil adres çubuğu vh'ye
      dahil değil ve alt kenar kırpılırdı.

      lg ALTINDA kilitleme YOK ve bu bilinçli: dar ekranda iç içe kaydırma alanları
      parmakla savaşıyor, hangi yüzeyin sürüklendiği belirsizleşiyor. Orada normal
      sayfa akışı sürüyor — aynı karar Sessions.jsx'te verildi, gerekçesi orada.
      ─────────────────────────────────────────────────────────────────────────
    */
    <div className="flex flex-col gap-6 lg:h-[calc(100dvh-10.5rem)] lg:min-h-[520px] lg:overflow-hidden">
      <div className="shrink-0">
        <h1 className="text-2xl font-bold text-slate-900">Eşleşmeler</h1>
        <p className="mt-1 text-sm text-slate-600">
          İstek kabul edildiğinde sohbet otomatik açılır ve ders rezerve edebilirsin.
        </p>
      </div>

      {notice && (
        <div className="shrink-0">
          <Notice tone="success" onDismiss={() => setNotice(null)}>
            {notice}
          </Notice>
        </div>
      )}

      {/* Mobilde ızgara: flex + flex-wrap, sekmeler tek satıra sığmayınca 2+1 gibi eğreti bir
          dizilim üretiyordu. grid-cols-3 üçünü de eşit böler ve düzen simetrik kalır. */}
      <div className="grid shrink-0 grid-cols-3 gap-1 rounded-lg bg-slate-100 p-1 sm:flex">
        {TABS.map((item) => (
          <button
            key={item.key}
            onClick={() => setTab(item.key)}
            // whitespace-nowrap: artık kırılmasına gerek yok — kısa ad + sayaç en dar
            // ekranda bile tek satıra sığıyor. Yine de açıkça yasaklıyoruz ki uzun bir
            // sayı (dört haneli) şeridi iki satıra düşürüp aynı hatayı geri getirmesin.
            // sm: genişlikle ilgili (dar ekranda uzun ad sığmıyor), lg: dokunmayla ilgili.
            className={`min-h-11 min-w-0 whitespace-nowrap rounded-md px-1.5 py-2 text-sm
                        font-medium leading-tight transition sm:flex-1 sm:px-3 lg:min-h-0 ${
                          tab === item.key
                            ? 'bg-white text-brand-700 shadow-sm'
                            : 'text-slate-600 hover:text-slate-800'
                        }`}
          >
            {/* İki ad da DOM'da; görünmeyen `display:none` ile kapalı. Bu, ekran
                okuyucudan da gizler — yani her boyutta TEK ad duyurulur. `sr-only`
                kullanılsaydı ikisi birden okunurdu; kırılımı medya sorgusuna bırakmanın
                sebebi bu. */}
            <span className="sm:hidden">{item.kisa}</span>
            <span className="hidden sm:inline">{item.label}</span>
            {(lists[item.key]?.length ?? 0) > 0 && (
              <span className="ml-1.5 text-xs text-slate-400">({lists[item.key].length})</span>
            )}
          </button>
        ))}
      </div>

      {/* ErrorBox hatasızken null döner; empty:hidden boş sarmalayıcının gap'te
          fazladan bir boşluk bırakmasını engeller (Sessions.jsx'teki çözümün aynısı). */}
      <div className="shrink-0 empty:hidden">
        <ErrorBox error={matches.error} onRetry={matches.reload} />
      </div>

      {/*
        SEKME İÇERİĞİ KENDİ PANELİNDE KAYAR — sayfa değil.

        min-h-0 ŞART: flex çocuğunun varsayılan min-height'ı `auto`, yani içerik kadar.
        Sıfırlanmazsa panel içeriğiyle birlikte uzar ve overflow-y-auto hiçbir şey
        yapmaz — kaydırma sayfaya taşar. Ekrana sabitlenen her düzenin ilk tuzağı
        (ayrıntı Sessions.jsx'teki Sutun yorumunda).

        key={tab} SEKME DEĞİŞİNCE KAYDIRMAYI SIFIRLAR: üç sekme aynı kayan div'i
        paylaşıyor ve React yalnızca içeriği değiştirseydi scrollTop olduğu yerde
        kalırdı — kullanıcı yeni sekmeye listenin ORTASINDAN başlardı. Remount,
        paneli başa alır.

        Zemin bir ton koyu (bg-slate-100/60): sayfa zemini ile beyaz kartlar arasına
        hiçbir şey konmayınca kartların ve panelin sınırı okunmuyordu. Sessions'taki
        Sutun ile aynı gerekçe, aynı ton.
      */}
      <div
        key={tab}
        className="kaydirma-ince min-h-0 flex-1 overflow-y-auto overscroll-contain rounded-2xl
                   border border-slate-200/70 bg-slate-100/60 p-4"
      >
        {matches.loading ? (
          <Loading />
        ) : current.length === 0 ? (
          <EmptyStateForTab tab={tab} />
        ) : (
          <div className="space-y-3">
            {current.map((match) => (
              <MatchCard
                key={match.matchId}
                match={match}
                tab={tab}
                onChanged={(message) => {
                  setNotice(message)
                  matches.reload()
                }}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

function EmptyStateForTab({ tab }) {
  const navigate = useNavigate()

  if (tab === 'incoming') {
    return (
      <EmptyState
        title="Bekleyen istek yok"
        description="Portföyüne konu ekledikçe sana daha çok istek gelir."
      />
    )
  }

  if (tab === 'outgoing') {
    return (
      <EmptyState
        title="Bekleyen isteğin yok"
        description="Keşfet sayfasından sana uygun öğrencilere istek gönderebilirsin."
        action={<Button onClick={() => navigate('/kesfet')}>Keşfet'e git</Button>}
      />
    )
  }

  return (
    <EmptyState
      title="Aktif eşleşmen yok"
      description="Bir istek kabul edildiğinde burada görünür ve sohbet açılır."
    />
  )
}

function MatchCard({ match, tab, onChanged }) {
  const navigate = useNavigate()
  const [busy, setBusy] = useState(null)
  const [error, setError] = useState(null)
  const [confirmClose, setConfirmClose] = useState(false)

  async function close() {
    setBusy('close')
    setError(null)
    try {
      await api.closeMatch(match.matchId)
      onChanged(`${match.otherDisplayName} ile eşleşme sonlandırıldı. Sohbet geçmişin duruyor.`)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(null)
      setConfirmClose(false)
    }
  }

  async function respond(accept) {
    setBusy(accept ? 'accept' : 'decline')
    setError(null)
    try {
      await api.respondMatch(match.matchId, accept)
      onChanged(
        accept
          ? `${match.otherDisplayName} ile eşleştiniz. Sohbet açıldı — ders saatini kararlaştırın.`
          : 'İstek reddedildi.',
      )
    } catch (err) {
      setError(err)
    } finally {
      setBusy(null)
    }
  }

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <PersonLink userId={match.otherUserId} className="font-semibold text-brand-700">
              {match.otherDisplayName}
            </PersonLink>
            {match.offeredTopicId && <Badge tone="success">Takas teklifi</Badge>}
          </div>

          <p className="mt-1.5 text-sm text-slate-600">
            <span className="text-slate-500">
              {match.iAmInitiator ? 'Almak istediğin:' : 'Senden istediği:'}
            </span>{' '}
            <strong>{match.requestedTopicName}</strong>
          </p>

          {match.offeredTopicName && (
            <p className="text-sm text-slate-600">
              <span className="text-slate-500">
                {match.iAmInitiator ? 'Karşılığında anlatacağın:' : 'Karşılığında anlatacağı:'}
              </span>{' '}
              <strong>{match.offeredTopicName}</strong>
            </p>
          )}

          <p className="mt-1 text-xs text-slate-500">{formatDateTime(match.createdAtUtc)}</p>
        </div>

        <div className="flex shrink-0 flex-wrap gap-2">
          {tab === 'incoming' && (
            <>
              <Button variant="success" loading={busy === 'accept'} onClick={() => respond(true)}>
                Kabul et
              </Button>
              <Button variant="secondary" loading={busy === 'decline'} onClick={() => respond(false)}>
                Reddet
              </Button>
            </>
          )}

          {tab === 'outgoing' && <Badge tone="warning">Yanıt bekleniyor</Badge>}

          {tab === 'active' && (
            <>
              {match.conversationId && (
                <Button onClick={() => navigate(`/sohbet/${match.conversationId}`)}>Sohbet</Button>
              )}
              <Button variant="secondary" onClick={() => navigate('/dersler')}>
                Ders rezerve et
              </Button>
              <Button variant="secondary" onClick={() => setConfirmClose(true)}>
                Sonlandır
              </Button>
            </>
          )}
        </div>
      </div>

      {/* Onay adımı: sonlandırma tek taraflı ve geri alınamaz — tek tıkla olmamalı. */}
      {confirmClose && (
        <div className="mt-3 rounded-lg border border-amber-200 bg-amber-50 p-3">
          <p className="text-sm text-amber-900">
            <strong>{match.otherDisplayName}</strong> ile eşleşme sonlandırılsın mı? Sohbet geçmişin
            durur ama yeni mesaj yazamazsın ve bu eşleşmeden ders rezerve edilemez. Geri alınamaz.
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            <Button variant="danger" loading={busy === 'close'} onClick={close}>
              Evet, sonlandır
            </Button>
            <Button variant="secondary" onClick={() => setConfirmClose(false)}>
              Vazgeç
            </Button>
          </div>
        </div>
      )}

      {error && (
        <div className="mt-3">
          <ErrorBox error={error} />
        </div>
      )}
    </Card>
  )
}
