import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../lib/api'
import { useAsync } from '../state/useAsync'
import { formatDateTime } from '../lib/format'
import { Badge, Button, Card, EmptyState, ErrorBox, Loading, Notice } from '../components/ui'
import { PersonLink } from '../components/PersonLink'

const TABS = [
  { key: 'incoming', label: 'Gelen istekler' },
  { key: 'outgoing', label: 'Gönderdiklerim' },
  { key: 'active', label: 'Aktif eşleşmeler' },
]

export default function Matches() {
  const matches = useAsync(() => api.myMatches(), [])
  const [tab, setTab] = useState('incoming')
  const [notice, setNotice] = useState(null)

  const lists = matches.data ?? { incoming: [], outgoing: [], active: [] }
  const current = lists[tab] ?? []

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-slate-900">Eşleşmeler</h1>
        <p className="mt-1 text-sm text-slate-600">
          İstek kabul edildiğinde sohbet otomatik açılır ve ders rezerve edebilirsin.
        </p>
      </div>

      {notice && (
        <Notice tone="success" onDismiss={() => setNotice(null)}>
          {notice}
        </Notice>
      )}

      {/* Mobilde ızgara: flex + flex-wrap, sekmeler tek satıra sığmayınca 2+1 gibi eğreti bir
          dizilim üretiyordu. grid-cols-3 üçünü de eşit böler ve düzen simetrik kalır. */}
      <div className="grid grid-cols-3 gap-1 rounded-lg bg-slate-100 p-1 sm:flex">
        {TABS.map((item) => (
          <button
            key={item.key}
            onClick={() => setTab(item.key)}
            // Dar ekranda üç sekmeye ~110px düşüyor ve "Gelen istekler" iki satıra kırılıyordu;
            // mobilde bir punto küçültüp yatay boşluğu kısınca tek satırda kalıyor.
            // sm: genişlikle ilgili (dar ekranda üç sekme sığmıyor), lg: dokunmayla ilgili.
            className={`min-h-11 min-w-0 rounded-md px-1.5 py-2 text-xs font-medium leading-tight
                        transition sm:flex-1 sm:px-3 sm:text-sm lg:min-h-0 ${
                          tab === item.key
                            ? 'bg-white text-brand-700 shadow-sm'
                            : 'text-slate-600 hover:text-slate-800'
                        }`}
          >
            {item.label}
            {(lists[item.key]?.length ?? 0) > 0 && (
              <span className="ml-1.5 text-xs text-slate-400">({lists[item.key].length})</span>
            )}
          </button>
        ))}
      </div>

      <ErrorBox error={matches.error} onRetry={matches.reload} />

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
