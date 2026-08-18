import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../lib/api'
import { useAsync } from '../state/useAsync'
import { DISPUTE_REASON_LABELS, REPORT_REASON_LABELS, formatDateTime } from '../lib/format'
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

const RESOLUTIONS = [
  {
    value: 'ForStudent',
    label: 'Öğrenci haklı — puan basma',
    hint: 'Eğitmene puan yazılmaz, ders iptal olur. Öğrenciden düşen bir şey zaten yok.',
    variant: 'primary',
  },
  {
    value: 'ForTutor',
    label: 'Eğitmen haklı — puanı bas',
    hint: 'Ders tamamlanmış sayılır ve eğitmene süreye göre puan yazılır.',
    variant: 'success',
  },
  {
    value: 'Dismissed',
    label: 'İtiraz geçersiz',
    hint: 'Ders itiraz öncesindeki durumuna döner.',
    variant: 'secondary',
  },
]

const TABS = [
  // Şikayet kuyruğu ÖNCE: yeni akış buradan geçiyor. İtiraz sekmesi yalnızca eski,
  // hâlâ açık olan itirazlar için duruyor — yeni itiraz açılamıyor.
  { key: 'reports', label: 'Şikayetler', badge: (m) => m?.openReports },
  { key: 'disputes', label: 'Eski itirazlar', badge: (m) => m?.openDisputes },
  { key: 'teachers', label: 'Öğretmen adayları', badge: (m) => m?.pendingTeacherCandidates },
  { key: 'economy', label: 'Ekonomi' },
  { key: 'audit', label: 'Denetim izi' },
]

/** Hakem ve yönetim paneli (Modül 2). */
export default function Admin() {
  const [tab, setTab] = useState('reports')
  const [notice, setNotice] = useState(null)

  /*
    Metrikler sekme değil SAYFA düzeyinde okunuyor: bekleyen iş sayıları sekme
    başlıklarında rozet olarak görünsün. Yalnızca "Ekonomi" sekmesinde yüklenseydi,
    o sekmeye hiç girmeyen bir moderatör öğretmen adaylığı kuyruğunda iş biriktiğini
    fark etmezdi. Panel düşük trafikli; tek ek istek bu görünürlüğe değer.
  */
  const metrics = useAsync(() => api.economyMetrics(), [])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-slate-900">Yönetim</h1>
        <p className="mt-1 text-sm text-slate-600">
          İtiraz hakemliği, öğretmen adaylığı doğrulama, ekonomi izleme ve denetim izi.
          Karar verilene kadar itirazlı derslerde puan basımı bekletilir.
        </p>
      </div>

      {notice && (
        <Notice tone="success" onDismiss={() => setNotice(null)}>
          {notice}
        </Notice>
      )}

      <div className="grid grid-cols-2 gap-1 rounded-lg bg-slate-100 p-1 sm:flex">
        {TABS.map((item) => {
          const count = item.badge?.(metrics.data) ?? 0
          return (
            <button
              key={item.key}
              onClick={() => setTab(item.key)}
              className={`flex min-h-11 min-w-0 items-center justify-center gap-1.5 rounded-md px-1.5
                          py-2 text-xs font-medium leading-tight transition sm:flex-1 sm:px-3
                          sm:text-sm lg:min-h-0 ${
                            tab === item.key
                              ? 'bg-white text-brand-700 shadow-sm'
                              : 'text-slate-600 hover:text-slate-800'
                          }`}
            >
              {item.label}
              {count > 0 && (
                <span className="rounded-full bg-rose-500 px-1.5 py-0.5 text-[10px] font-bold leading-none text-white">
                  {count}
                </span>
              )}
            </button>
          )
        })}
      </div>

      {tab === 'reports' && (
        <ReportQueue
          onNotice={(m) => {
            setNotice(m)
            metrics.reload()
          }}
        />
      )}
      {tab === 'disputes' && (
        <DisputeQueue
          onNotice={(m) => {
            setNotice(m)
            metrics.reload()
          }}
        />
      )}
      {tab === 'teachers' && (
        <TeacherCandidateQueue
          onNotice={(m) => {
            setNotice(m)
            metrics.reload()
          }}
        />
      )}
      {tab === 'economy' && <EconomyPanel metrics={metrics} />}
      {tab === 'audit' && <AuditLogPanel />}
    </div>
  )
}

// ---------------------------------------------------------------------------
// Şikayet kuyruğu (tek yönlü)
// ---------------------------------------------------------------------------

/**
 * Şikayetler yalnızca burada görünür. Şikayet edilen kullanıcı bunu hiçbir ekranda
 * göremez; bu yüzden kararı verirken tek kaynak şikayetçinin anlatısıdır.
 *
 * "Toplam N şikayet" rozeti kasıtlı olarak öne çıkarılıyor: tek bir şikayet bir
 * anlaşmazlık olabilir, aynı kişi hakkında biriken şikayetler örüntüdür — yaptırım
 * kararının ağırlığı oradan gelir.
 */
function ReportQueue({ onNotice }) {
  const reports = useAsync(() => api.reports(true), [])
  const [busyId, setBusyId] = useState(null)
  const [error, setError] = useState(null)

  async function kapat(reportId, actionTaken) {
    setBusyId(reportId)
    setError(null)
    try {
      await api.resolveReport(reportId, actionTaken, null)
      onNotice(actionTaken ? 'Şikayet kapatıldı: yaptırım uygulandı.' : 'Şikayet kapatıldı: işlem gerekmedi.')
      reports.reload()
    } catch (err) {
      setError(err)
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div className="space-y-3">
      <ErrorBox error={reports.error} onRetry={reports.reload} />
      <ErrorBox error={error} />
      <SectionTitle>Açık şikayetler ({reports.data?.length ?? 0})</SectionTitle>

      {reports.loading ? (
        <Loading />
      ) : (reports.data?.length ?? 0) === 0 ? (
        <EmptyState title="Kuyruk boş" description="Bekleyen şikayet yok." />
      ) : (
        reports.data.map((r) => (
          <Card key={r.reportId}>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge tone="danger">{REPORT_REASON_LABELS[r.reason] ?? r.reason}</Badge>
                  {r.reportedUserTotalReports > 1 && (
                    <Badge tone="warning">
                      Bu kişi hakkında {r.reportedUserTotalReports} şikayet
                    </Badge>
                  )}
                  <span className="text-xs text-slate-500">{formatDateTime(r.createdAtUtc)}</span>
                </div>

                <p className="mt-2 text-xs text-slate-500">
                  <Link to={`/profil/${r.reportedUserId}`} className="font-medium text-slate-700 underline">
                    {r.reportedDisplayName}
                  </Link>{' '}
                  hakkında · şikayet eden: {r.reporterDisplayName}
                  {r.topicName ? ` · ${r.topicName}` : ''}
                </p>

                <p className="mt-2 whitespace-pre-wrap text-sm text-slate-700">{r.description}</p>
              </div>

              <div className="flex shrink-0 flex-col gap-2">
                <Button
                  variant="danger"
                  loading={busyId === r.reportId}
                  onClick={() => kapat(r.reportId, true)}
                >
                  Yaptırım uyguladım
                </Button>
                <Button
                  variant="secondary"
                  loading={busyId === r.reportId}
                  onClick={() => kapat(r.reportId, false)}
                >
                  İşlem gerekmedi
                </Button>
              </div>
            </div>
          </Card>
        ))
      )}
    </div>
  )
}

// ---------------------------------------------------------------------------
// Eski itiraz kuyruğu (salt okunur akış — yeni itiraz açılamıyor)
// ---------------------------------------------------------------------------

function DisputeQueue({ onNotice }) {
  const disputes = useAsync(() => api.disputes(), [])
  const [selectedId, setSelectedId] = useState(null)

  return (
    <div className="space-y-3">
      <ErrorBox error={disputes.error} onRetry={disputes.reload} />
      <SectionTitle>Açık itirazlar ({disputes.data?.length ?? 0})</SectionTitle>

      {disputes.loading ? (
        <Loading />
      ) : (disputes.data?.length ?? 0) === 0 ? (
        <EmptyState title="Kuyruk boş" description="Bekleyen itiraz yok." />
      ) : (
        disputes.data.map((dispute) => (
          <Card key={dispute.disputeId}>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge tone="danger">
                    {DISPUTE_REASON_LABELS[dispute.reason] ?? dispute.reason}
                  </Badge>
                  <Badge tone="neutral">{dispute.status}</Badge>
                  <span className="text-xs text-slate-500">
                    {formatDateTime(dispute.createdAtUtc)}
                  </span>
                </div>
                <p className="mt-2 whitespace-pre-wrap text-sm text-slate-700">
                  {dispute.description}
                </p>
              </div>

              <Button onClick={() => setSelectedId(dispute.disputeId)}>İncele ve karar ver</Button>
            </div>
          </Card>
        ))
      )}

      <ReviewModal
        disputeId={selectedId}
        onClose={() => setSelectedId(null)}
        onResolved={(message) => {
          setSelectedId(null)
          onNotice(message)
          disputes.reload()
        }}
      />
    </div>
  )
}

/**
 * Kanıt kartı: görsel + üstveri. "Sahte kanıt" itirazına karar veren yöneticinin
 * görseli GÖRMESİ şart — hash ve dosya adı tek başına karar için yeterli değil.
 */
function ProofCard({ proof, sessionId }) {
  const [imageUrl, setImageUrl] = useState(null)
  const [error, setError] = useState(null)

  useEffect(() => {
    let revoked = false
    let url = null

    api
      .adminProofContentUrl(sessionId, proof.proofId)
      .then((objectUrl) => {
        if (revoked) {
          URL.revokeObjectURL(objectUrl)
          return
        }
        url = objectUrl
        setImageUrl(objectUrl)
      })
      .catch(setError)

    return () => {
      revoked = true
      if (url) URL.revokeObjectURL(url) // Bellek sızıntısını önle.
    }
  }, [sessionId, proof.proofId])

  return (
    <div className="rounded-lg border border-slate-200/80 p-3 text-xs text-slate-600">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-medium text-slate-700">{formatDateTime(proof.uploadedAtUtc)}</span>
        {proof.isDuplicateHash && <Badge tone="danger">Tekrar kullanılmış görsel</Badge>}
      </div>

      <div className="mt-2">
        {error ? (
          <ErrorBox error={error} />
        ) : imageUrl ? (
          <a href={imageUrl} target="_blank" rel="noopener noreferrer">
            <img
              src={imageUrl}
              alt="Ders kanıtı"
              className="max-h-72 w-full rounded border border-slate-200/80 object-contain"
            />
          </a>
        ) : (
          <Loading label="Görsel indiriliyor…" />
        )}
      </div>

      <p className="mt-2 break-all font-mono text-slate-400">SHA-256: {proof.sha256Hash}</p>
    </div>
  )
}

/** Taraf kartı: kimlik + hakemin işine yarayan geçmiş sinyalleri. */
function PartyCard({ title, party, onBan, banning }) {
  return (
    <div className="rounded-lg border border-slate-200/80 p-3">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="text-xs uppercase tracking-wide text-slate-400">{title}</p>
          <p className="truncate font-medium text-slate-800">{party.displayName}</p>
          <p className="truncate text-xs text-slate-500">{party.email}</p>
        </div>
        {party.status !== 'Active' && <Badge tone="danger">{party.status}</Badge>}
      </div>

      <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-600">
        <span>
          Puan: <strong>{party.averageRating}</strong> ({party.ratingCount})
        </span>
        <span>Üyelik: {formatDateTime(party.joinedAtUtc)}</span>
        {/* Aleyhine sonuçlanmış geçmiş itiraz: tekrar eden davranışın tek göstergesi. */}
        <span className={party.pastDisputesAgainst > 0 ? 'font-medium text-rose-600' : ''}>
          Aleyhine sonuçlanan itiraz: {party.pastDisputesAgainst}
        </span>
      </div>

      {party.status === 'Active' && (
        <Button
          variant="danger"
          className="mt-3 w-full"
          loading={banning}
          onClick={() => onBan(party)}
        >
          Kalıcı banla (+ cihazları)
        </Button>
      )}
    </div>
  )
}

function ReviewModal({ disputeId, onClose, onResolved }) {
  const detail = useAsync(
    () => (disputeId ? api.disputeDetail(disputeId) : Promise.resolve(null)),
    [disputeId],
  )

  const [resolution, setResolution] = useState('ForStudent')
  const [note, setNote] = useState('')
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)
  const [banTarget, setBanTarget] = useState(null)
  const [banning, setBanning] = useState(false)

  // Modal her açılışta sıfırlanır: önceki itirazın kararı/notu yeni itiraza taşınmasın.
  useEffect(() => {
    setResolution('ForStudent')
    setNote('')
    setError(null)
    setBanTarget(null)
  }, [disputeId])

  const d = detail.data

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      await api.resolveDispute(d.disputeId, resolution, note.trim() || null)
      onResolved('İtiraz karara bağlandı ve puan sonucu uygulandı.')
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  async function ban(party) {
    setBanning(true)
    setError(null)
    try {
      const result = await api.banUser(party.userId, `İtiraz incelemesi: ${d.disputeId}`)
      setBanTarget(null)
      detail.reload()
      setError(null)
      alert(`${party.displayName} banlandı. Engellenen cihaz: ${result.devicesBanned}`)
    } catch (err) {
      setError(err)
    } finally {
      setBanning(false)
    }
  }

  const selectedResolution = RESOLUTIONS.find((r) => r.value === resolution)

  return (
    <Modal open={Boolean(disputeId)} onClose={onClose} title="İtirazı karara bağla">
      {detail.loading ? (
        <Loading label="İtiraz detayı yükleniyor…" />
      ) : detail.error ? (
        <ErrorBox error={detail.error} onRetry={detail.reload} />
      ) : d ? (
        <div className="space-y-4">
          {/* Ders künyesi: Session ID, süre, ücret — spec'in istediği inceleme bilgileri. */}
          <div className="rounded-lg bg-slate-50 p-3 text-sm">
            <div className="flex flex-wrap items-center gap-2">
              <Badge tone="danger">{DISPUTE_REASON_LABELS[d.reason] ?? d.reason}</Badge>
              <Badge tone="neutral">{d.sessionStatus}</Badge>
              {d.mintPending ? (
                <Badge tone="warning">{d.creditCost} puan basılacak</Badge>
              ) : (
                <Badge tone="neutral">Escrow kapalı</Badge>
              )}
            </div>
            <p className="mt-2 font-medium text-slate-800">
              {d.topicName} · {d.subjectName}
            </p>
            <p className="text-xs text-slate-500">
              {formatDateTime(d.scheduledStartUtc)} — {d.durationMinutes} dk
            </p>
            <p className="mt-1 font-mono text-xs text-slate-400">
              Session ID: {d.sessionId} · Kod: {d.verificationCode}
            </p>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <PartyCard title="Eğitmen" party={d.tutor} onBan={setBanTarget} banning={banning} />
            <PartyCard title="Öğrenci" party={d.student} onBan={setBanTarget} banning={banning} />
          </div>

          {/* İKİ TARAFIN BEYANI. Eğitmen yanıtı yoksa bu da hakem için bir veridir. */}
          <div className="space-y-2">
            <div className="rounded-lg border border-rose-200 bg-rose-50 p-3">
              <p className="text-xs font-medium uppercase tracking-wide text-rose-700">
                Öğrencinin iddiası · {formatDateTime(d.createdAtUtc)}
              </p>
              <p className="mt-1 whitespace-pre-wrap text-sm text-slate-700">{d.description}</p>
            </div>

            <div className="rounded-lg border border-slate-200/80 bg-white p-3">
              <p className="text-xs font-medium uppercase tracking-wide text-slate-500">
                Eğitmenin savunması
                {d.tutorStatementAtUtc && ` · ${formatDateTime(d.tutorStatementAtUtc)}`}
              </p>
              {d.tutorStatement ? (
                <p className="mt-1 whitespace-pre-wrap text-sm text-slate-700">{d.tutorStatement}</p>
              ) : (
                <p className="mt-1 text-sm italic text-slate-500">
                  Eğitmen henüz savunma yazmadı.
                </p>
              )}
            </div>
          </div>

          <div>
            <p className="label">Yüklenen kanıtlar</p>
            {(d.proofs?.length ?? 0) === 0 ? (
              <p className="text-sm text-slate-500">
                Bu derse hiç kanıt yüklenmemiş — “ders yapılmadı” iddiasını güçlendirir.
              </p>
            ) : (
              <div className="space-y-3">
                {d.proofs.map((proof) => (
                  <ProofCard key={proof.proofId} proof={proof} sessionId={d.sessionId} />
                ))}
              </div>
            )}
          </div>

          <Field label="Karar">
            <div className="space-y-2">
              {RESOLUTIONS.map((option) => (
                <label
                  key={option.value}
                  className={`flex cursor-pointer items-start gap-3 rounded-lg border p-3 transition ${
                    resolution === option.value
                      ? 'border-brand-400 bg-brand-50'
                      : 'border-slate-200/80 hover:bg-slate-50'
                  }`}
                >
                  <input
                    type="radio"
                    name="resolution"
                    value={option.value}
                    checked={resolution === option.value}
                    onChange={(e) => setResolution(e.target.value)}
                    className="mt-1 accent-brand-600"
                  />
                  <span>
                    <span className="block text-sm font-medium text-slate-800">{option.label}</span>
                    <span className="block text-xs text-slate-500">{option.hint}</span>
                  </span>
                </label>
              ))}
            </div>
          </Field>

          <Field label="Karar notu (opsiyonel)">
            <textarea
              className="input h-20 resize-none"
              value={note}
              onChange={(e) => setNote(e.target.value)}
              maxLength={2000}
            />
          </Field>

          <ErrorBox error={error} />

          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose}>
              Vazgeç
            </Button>
            <Button
              variant={selectedResolution?.variant ?? 'primary'}
              loading={busy}
              onClick={submit}
            >
              Kararı uygula
            </Button>
          </div>

          {/* Ban geri alınamaz: tek tıkla değil, açık onayla. */}
          <Modal
            open={Boolean(banTarget)}
            onClose={() => setBanTarget(null)}
            title="Kalıcı ban onayı"
            footer={
              <>
                <Button variant="secondary" onClick={() => setBanTarget(null)}>
                  Vazgeç
                </Button>
                <Button variant="danger" loading={banning} onClick={() => ban(banTarget)}>
                  Banla
                </Button>
              </>
            }
          >
            <p className="text-sm text-slate-700">
              <strong>{banTarget?.displayName}</strong> kalıcı olarak banlanacak ve bu hesabın
              bilinen tüm cihaz kimlikleri (HWID) engellenecek. Bu işlem geri alınamaz.
            </p>
          </Modal>
        </div>
      ) : null}
    </Modal>
  )
}

// ---------------------------------------------------------------------------
// Öğretmen adaylığı doğrulama
// ---------------------------------------------------------------------------

const CANDIDATE_FILTERS = [
  { key: 'Pending', label: 'Bekleyen' },
  { key: 'Verified', label: 'Doğrulanmış' },
  { key: 'Rejected', label: 'Reddedilmiş' },
  { key: 'All', label: 'Tümü' },
]

const CANDIDATE_STATUS = {
  Pending: { label: 'Karar bekliyor', tone: 'warning' },
  Verified: { label: 'Doğrulandı', tone: 'success' },
  Rejected: { label: 'Reddedildi', tone: 'danger' },
}

/**
 * Kararlar. "Reddet" kırmızı ama YIKICI DEĞİL: beyan silinmez, kullanıcı bilgilerini
 * düzeltip yeniden gönderebilir. Geri alınamaz tek işlem ban'dır ve o bu ekranda yok.
 */
const CANDIDATE_DECISIONS = {
  Verify: {
    label: 'Doğrula',
    variant: 'success',
    title: 'Beyanı doğrula',
    hint: 'Profilde “Doğrulandı” rozeti görünür. Gerekçeye hangi belgeyi gördüğünü yaz — sistemde belge kaydı yok, bu not tek dayanak.',
    notePlaceholder: 'Örn: Öğrenci belgesi e-posta ile gönderildi, 2026 bahar dönemi.',
  },
  Reject: {
    label: 'Reddet',
    variant: 'danger',
    title: 'Beyanı reddet',
    hint: '🌱 rozeti geri alınır ve gönüllü ders açamaz. Beyan silinmez; kullanıcı düzeltip yeniden gönderebilir. Gerekçeyi KULLANICI GÖRÜR.',
    notePlaceholder: 'Örn: Belge gönderilmedi. Öğrenci belgeni ilettiğinde yeniden değerlendirilecek.',
  },
  Revert: {
    label: 'Kararı geri al',
    variant: 'secondary',
    title: 'Kararı geri al',
    hint: 'Beyan yeniden kuyruğa döner, kullanıcı bilgilerini düzenleyebilir hâle gelir ve reddedilmişse 🌱 rozeti iade edilir. Doğrulanmış bir beyanı güncellemenin tek yolu budur.',
    notePlaceholder: 'Örn: Bölüm değişikliği bildirildi, yeniden inceleme gerekiyor.',
  },
}

function TeacherCandidateQueue({ onNotice }) {
  const [filter, setFilter] = useState('Pending')
  const [page, setPage] = useState(1)
  const list = useAsync(() => api.teacherCandidates(filter, page, 25), [filter, page])
  const [target, setTarget] = useState(null) // { row, decision }

  const data = list.data

  return (
    <div className="space-y-3">
      <ErrorBox error={list.error} onRetry={list.reload} />

      <div className="flex flex-wrap gap-1.5">
        {CANDIDATE_FILTERS.map((item) => (
          <button
            key={item.key}
            onClick={() => {
              setFilter(item.key)
              setPage(1)
            }}
            className={`min-h-11 rounded-full border px-3 text-sm transition lg:min-h-0 lg:py-1.5 ${
              filter === item.key
                ? 'border-brand-400 bg-brand-50 font-medium text-brand-700'
                : 'border-slate-200/80 text-slate-600 hover:bg-slate-50'
            }`}
          >
            {item.label}
          </button>
        ))}
      </div>

      <SectionTitle>
        {CANDIDATE_FILTERS.find((f) => f.key === filter)?.label} ({data?.totalCount ?? 0})
      </SectionTitle>

      {/* Dürüstlük notu operatöre de gösteriliyor: bu ekran belge doğrulamaz, karar kaydeder. */}
      <p className="text-xs text-slate-500">
        Sistemde öğrenci belgesi yükleme kanalı yok. Doğrulama, sistem dışı bir kanıta (ör.
        e-posta ile gelen öğrenci belgesi) dayanır; gerekçe alanı o kanıtın kayda geçtiği
        tek yerdir ve denetim izine yazılır.
      </p>

      {list.loading ? (
        <Loading />
      ) : (data?.items?.length ?? 0) === 0 ? (
        <EmptyState
          title="Kayıt yok"
          description={
            filter === 'Pending'
              ? 'Karar bekleyen öğretmen adaylığı beyanı yok.'
              : 'Bu filtreye uyan beyan yok.'
          }
        />
      ) : (
        <>
          <div className="space-y-2">
            {data.items.map((row) => (
              <CandidateCard key={row.profileId} row={row} onDecide={setTarget} />
            ))}
          </div>

          {data.totalPages > 1 && (
            <div className="flex items-center justify-between">
              <Button variant="secondary" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
                ← Önceki
              </Button>
              <span className="text-sm text-slate-500">
                {data.page} / {data.totalPages}
              </span>
              <Button
                variant="secondary"
                disabled={!data.hasNextPage}
                onClick={() => setPage((p) => p + 1)}
              >
                Sonraki →
              </Button>
            </div>
          )}
        </>
      )}

      <CandidateDecisionModal
        target={target}
        onClose={() => setTarget(null)}
        onDone={(message) => {
          setTarget(null)
          onNotice(message)
          list.reload()
        }}
      />
    </div>
  )
}

function CandidateCard({ row, onDecide }) {
  const status = CANDIDATE_STATUS[row.reviewStatus] ?? { label: row.reviewStatus, tone: 'neutral' }

  // Beyandaki okul ile profildeki serbest metin farklıysa hakem bunu bilmeli:
  // tek başına suç değil ama bakılması gereken bir sinyal.
  const profilFarkli =
    row.profileUniversity &&
    row.profileUniversity.trim().toLocaleLowerCase('tr') !==
      row.university.trim().toLocaleLowerCase('tr')

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <Link
              to={`/profil/${row.userId}`}
              className="font-medium text-brand-700 underline-offset-2 hover:underline"
            >
              {row.displayName}
            </Link>
            <Badge tone={status.tone}>{status.label}</Badge>
            {row.userStatus !== 'Active' && <Badge tone="danger">{row.userStatus}</Badge>}
            {row.hasPedagogicalCertificate && <Badge tone="neutral">Pedagojik formasyon</Badge>}
          </div>

          <p className="mt-0.5 truncate text-xs text-slate-500">{row.email}</p>

          <p className="mt-2 text-sm text-slate-800">
            {row.university} · {row.faculty} · {row.department}
            {row.gradeYear ? ` · ${row.gradeYear}. sınıf` : ''}
          </p>

          {profilFarkli && (
            <p className="mt-1 text-xs text-amber-700">
              Profilinde farklı okul yazıyor: {row.profileUniversity}
              {row.profileDepartment ? ` · ${row.profileDepartment}` : ''}
            </p>
          )}

          <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-600">
            <span>Beyan: {formatDateTime(row.declaredAtUtc)}</span>
            <span>Üyelik: {formatDateTime(row.joinedAtUtc)}</span>
            {/* Davranışsal sinyal: beyanı fiilen kullanıyor mu? */}
            <span className={row.completedVolunteerSessions > 0 ? 'font-medium text-emerald-700' : ''}>
              Gönüllü ders: {row.completedVolunteerSessions} tamamlandı · {row.volunteerOfferCount} açık ilan
            </span>
            {row.ratingCount > 0 && (
              <span>
                Puan: <strong>{row.averageRating}</strong> ({row.ratingCount})
              </span>
            )}
          </div>

          {row.reviewStatus !== 'Pending' && (
            <div className="mt-2 rounded-lg bg-slate-50 p-2 text-xs text-slate-600">
              <p>
                {status.label} · {formatDateTime(row.reviewedAtUtc)}
                {row.reviewedByDisplayName ? ` · ${row.reviewedByDisplayName}` : ''}
              </p>
              {row.reviewNote && (
                <p className="mt-1 whitespace-pre-wrap text-slate-700">{row.reviewNote}</p>
              )}
            </div>
          )}
        </div>

        <div className="flex shrink-0 flex-wrap gap-2">
          {row.reviewStatus !== 'Verified' && (
            <Button variant="success" onClick={() => onDecide({ row, decision: 'Verify' })}>
              Doğrula
            </Button>
          )}
          {row.reviewStatus !== 'Rejected' && (
            <Button variant="danger" onClick={() => onDecide({ row, decision: 'Reject' })}>
              Reddet
            </Button>
          )}
          {row.reviewStatus !== 'Pending' && (
            <Button variant="secondary" onClick={() => onDecide({ row, decision: 'Revert' })}>
              Kararı geri al
            </Button>
          )}
        </div>
      </div>
    </Card>
  )
}

function CandidateDecisionModal({ target, onClose, onDone }) {
  const [note, setNote] = useState('')
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)

  // Her açılışta sıfırlanır: bir önceki kararın gerekçesi başka bir kişiye yapışmasın.
  useEffect(() => {
    setNote('')
    setError(null)
  }, [target])

  const config = target ? CANDIDATE_DECISIONS[target.decision] : null

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      const result = await api.reviewTeacherCandidate(
        target.row.profileId,
        target.decision,
        note.trim(),
      )
      onDone(
        `${target.row.displayName} — ${config.label.toLocaleLowerCase('tr')} işlemi uygulandı.` +
          (result?.badgeRemoved ? ' 🌱 rozeti geri alındı.' : '') +
          (result?.badgeRestored ? ' 🌱 rozeti iade edildi.' : ''),
      )
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={Boolean(target)}
      onClose={onClose}
      title={config?.title ?? ''}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Vazgeç
          </Button>
          <Button
            variant={config?.variant ?? 'primary'}
            loading={busy}
            disabled={note.trim().length < 5}
            onClick={submit}
          >
            {config?.label}
          </Button>
        </>
      }
    >
      {target && (
        <div className="space-y-4">
          <div className="rounded-lg bg-slate-50 p-3 text-sm">
            <p className="font-medium text-slate-800">{target.row.displayName}</p>
            <p className="mt-0.5 text-slate-600">
              {target.row.university} · {target.row.faculty} · {target.row.department}
            </p>
          </div>

          <p className="text-sm text-slate-600">{config.hint}</p>

          <Field label="Gerekçe (zorunlu)" hint="Denetim izine kaydedilir.">
            <textarea
              className="input h-24 resize-none"
              value={note}
              onChange={(e) => setNote(e.target.value)}
              maxLength={500}
              placeholder={config.notePlaceholder}
            />
          </Field>

          <ErrorBox error={error} />
        </div>
      )}
    </Modal>
  )
}

// ---------------------------------------------------------------------------
// Ekonomi izleme
// ---------------------------------------------------------------------------

function Metric({ label, value, hint, tone = 'default' }) {
  const tones = {
    default: 'text-slate-900',
    good: 'text-emerald-600',
    warn: 'text-amber-600',
    bad: 'text-rose-600',
  }
  return (
    <Card>
      <p className="text-sm text-slate-500">{label}</p>
      <p className={`mt-1 text-3xl font-bold ${tones[tone]}`}>{value}</p>
      {hint && <p className="mt-1 text-xs text-slate-500">{hint}</p>}
    </Card>
  )
}

function EconomyPanel({ metrics }) {
  const m = metrics.data

  return (
    <div className="space-y-4">
      <ErrorBox error={metrics.error} onRetry={metrics.reload} />

      {metrics.loading ? (
        <Loading />
      ) : m ? (
        <>
          {/*
            Sıfır toplam denetimi en üstte ve tek bakışta okunur: bu bayrak kırmızıysa
            ekonominin temel değişmezi bozulmuş demektir ve diğer her metrik ikincildir.
          */}
          <div
            className={`rounded-xl border p-4 ${
              m.ledgerBalanced
                ? 'border-emerald-200 bg-emerald-50'
                : 'border-rose-300 bg-rose-50'
            }`}
          >
            <p
              className={`font-semibold ${
                m.ledgerBalanced ? 'text-emerald-800' : 'text-rose-800'
              }`}
            >
              {m.ledgerBalanced ? 'Defter tutuyor' : 'DEFTER TUTMUYOR — acil inceleme'}
            </p>
            {/* Denetimin dayanağı DEĞİŞTİ: eskiden "arz = basılan − yakılan" idi ve sıfır
                toplamlı takasa dayanıyordu. Puan artık karşılıksız basıldığı için tek
                geçerli denetim, cüzdan toplamının defter toplamına eşitliğidir. */}
            <p className="mt-1 text-sm text-slate-700">
              Cüzdanlardaki {m.circulatingCredits} puan, defterdeki tüm hareketlerin
              toplamına eşit olmalı.
            </p>
          </div>

          <SectionTitle>Puan arzı</SectionTitle>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <Metric label="Cüzdanlardaki puan" value={m.circulatingCredits} hint="Kullanılabilir + bloke" />
            <Metric label="Kullanılabilir" value={m.availableCredits} />
            
            <Metric
              label="7 gün içinde yanacak"
              value={m.expiringWithin7Days}
              tone={m.expiringWithin7Days > 0 ? 'warn' : 'default'}
              hint="Yalnızca vadeli eski lotlar; ders kazancı yanmaz"
            />
          </div>

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <Metric label="Toplam basılan" value={m.totalMinted} hint="Ders kazancı + hoş geldin" />
            <Metric label="Toplam yakılan" value={m.totalExpired} hint="Vadesi dolanlar (eski)" />
            <Metric label="Cüzdan sayısı" value={m.walletCount} />
          </div>

          <SectionTitle>Ders ve moderasyon</SectionTitle>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <Metric label="Aktif rezervasyon" value={m.activeSessions} />
            <Metric label="Onay bekleyen" value={m.awaitingApproval} />
            <Metric
              label="İtirazlı ders"
              value={m.disputedSessions}
              tone={m.disputedSessions > 0 ? 'warn' : 'default'}
            />
            <Metric
              label="Açık itiraz"
              value={m.openDisputes}
              tone={m.openDisputes > 0 ? 'warn' : 'default'}
            />
            <Metric
              label="Bekleyen öğretmen adayı"
              value={m.pendingTeacherCandidates}
              tone={m.pendingTeacherCandidates > 0 ? 'warn' : 'default'}
              hint="Doğrulama kuyruğu"
            />
            <Metric label="Banlı kullanıcı" value={m.bannedUsers} />
            <Metric label="Aktif HWID banı" value={m.activeHwidBans} />
            {/* Arka plan işi sessizce takıldığında bunu gösteren tek yer burası:
                kullanıcı şikâyeti gelene kadar başka hiçbir ekran fark ettirmez. */}
            <Metric
              label="Takılı süpürme kaydı"
              value={m.stuckSweepRecords}
              tone={m.stuckSweepRecords > 0 ? 'warn' : 'default'}
              hint="Otomatik onay/iade bu kayıtlarda işlemiyor — sunucu log'una bak"
            />
          </div>

          {/*
            SESSİZ tazeleme şart. Normal reload `loading` bayrağını kaldırıyor, panel
            <Loading /> ile değişiyor ve alttaki form YENİDEN MONTE oluyor — başarı
            bildirimi hiç görünmeden kayboluyordu. Yönetici işlemin geçtiğini göremeyince
            tekrar denemeye yönelir; idempotens koruması OLMADIĞI için bu doğrudan çifte
            düzeltme demek.
          */}
          <CreditAdjustmentPanel onDone={() => metrics.reload({ silent: true })} />
        </>
      ) : null}
    </div>
  )
}

/**
 * Yönetim eliyle puan tanımlama/düzeltme.
 *
 * NEDEN EKONOMİ SEKMESİNDE: yapılan iş defteri değiştirmek ve sonucunun görüleceği yer
 * hemen üstteki arz metrikleri. Kullanıcı listesine koysaydım, düzeltmenin ekonomiye
 * etkisi başka bir ekranda kalırdı.
 */
function yeniAnahtar() {
  // crypto.randomUUID yalnızca güvenli bağlamda (https ya da localhost) tanımlı. Yedek
  // yol olmasaydı, güvensiz bir bağlamda panel anahtar üretemediği için düzeltme hiç
  // yapılamazdı — tekillik uğruna işlevi kaybetmek kötü bir takas.
  if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID()
  return `k-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`
}

function CreditAdjustmentPanel({ onDone }) {
  const [userId, setUserId] = useState('')
  const [amount, setAmount] = useState('')
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)
  const [result, setResult] = useState(null)

  /*
    TEKİLLİK ANAHTARI — ne zaman yenilenir, ne zaman KORUNUR.

    Anahtar burada, gönderim anında DEĞİL, form doldurulmadan önce üretiliyor ve
    yalnızca işlem BAŞARIYLA bittiğinde yenileniyor. Yani:
      • ağ hatası / zaman aşımı sonrası tekrar gönderim → AYNI anahtar → sunucu ikinci
        kez uygulamaz, ilk sonucu döndürür,
      • yeni bir düzeltme → yeni anahtar.

    Anahtar submit içinde üretilseydi her deneme yeni anahtar alırdı ve koruma tam da
    işe yaraması gereken yerde — "hata gördüm, tekrar denedim" anında — hiçbir şey
    yapmazdı. Korumanın tamamı bu satırın nerede durduğuna bağlı.
  */
  const [anahtar, setAnahtar] = useState(yeniAnahtar)

  const tutar = Number(amount)
  const gecerli =
    userId.trim().length === 36 &&
    Number.isInteger(tutar) &&
    tutar !== 0 &&
    Math.abs(tutar) <= 10000 &&
    reason.trim().length >= 10

  // Aynı anahtar farklı bir yükle gönderildi: sunucu uygulamadı. Bu, kullanıcıya ayrıca
  // anlatılması gereken TEK hata — diğerleri "olmadı, tekrar dene" ile özetlenebilir.
  const anahtarCakismasi = error?.code === 'IDEMPOTENCY_KEY_REUSED'

  async function submit(event) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    setResult(null)
    try {
      const r = await api.adjustCredits(userId.trim(), tutar, reason.trim(), anahtar)
      setResult(r)
      setAmount('')
      setReason('')
      setAnahtar(yeniAnahtar())
      onDone?.()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <SectionTitle>Puan düzeltmesi</SectionTitle>
      <Card>
        <p className="mb-4 text-sm text-slate-600">
          Pozitif tutar <strong>ekler</strong>, negatif <strong>düşer</strong>. Gerekçe denetim
          izine yazılır. Bu işlem <strong>unvanı değiştirmez</strong> — unvan yalnızca ders
          anlatarak yükselir.
        </p>

        <form onSubmit={submit} className="space-y-4">
          <Field label="Kullanıcı kimliği (UUID)" hint="Profil bağlantısındaki kimlik.">
            <input
              className="input font-mono text-xs"
              value={userId}
              onChange={(e) => setUserId(e.target.value)}
              placeholder="00000000-0000-0000-0000-000000000000"
            />
          </Field>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Tutar" hint="Örn: 100 ya da −50. Tek işlemde en fazla 10.000.">
              <input
                type="number"
                className="input"
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
                step="1"
              />
            </Field>

            <Field label="Gerekçe" hint="En az 10 karakter. Kullanıcıya gösterilmez.">
              <input
                className="input"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                maxLength={400}
                placeholder="Kayıp basımın telafisi (ders #...)"
              />
            </Field>
          </div>

          {!anahtarCakismasi && <ErrorBox error={error} />}

          {/* Anahtar çakışması bir "hata" gibi gösterilmemeli: defterde HİÇBİR ŞEY
              değişmedi ve yöneticinin atması gereken adım belli. Kırmızı bir kutu
              burada yanlış bilgi verirdi — sanki işlem yarıda kalmış gibi. */}
          {anahtarCakismasi && (
            <Notice tone="warning">
              <p className="font-medium">Bu anahtar başka bir düzeltme için kullanılmış.</p>
              <p className="mt-1">
                Hiçbir değişiklik uygulanmadı. Muhtemelen önceki denemeniz aslında
                başarılı olmuştu ve sonra tutarı ya da gerekçeyi değiştirdiniz.{' '}
                <strong>Önce denetim izine bakın:</strong> aradığınız düzeltme zaten
                yazılmışsa ikinci kez göndermeyin.
              </p>
              <button
                type="button"
                className="mt-2 text-xs underline hover:no-underline"
                onClick={() => {
                  setAnahtar(yeniAnahtar())
                  setError(null)
                }}
              >
                Denetim izini kontrol ettim, yeni anahtarla gönder
              </button>
            </Notice>
          )}

          {result && (
            <Notice tone={result.replayed ? 'info' : 'success'}>
              {result.replayed ? (
                <>
                  Bu düzeltme <strong>zaten uygulanmıştı</strong>; ikinci kez yazılmadı.
                  Kullanılabilir bakiye: <strong>{result.newAvailableBalance}</strong>
                </>
              ) : (
                <>
                  {result.amount > 0 ? '+' : ''}
                  {result.amount} puan uygulandı. Yeni kullanılabilir bakiye:{' '}
                  <strong>{result.newAvailableBalance}</strong>
                </>
              )}
            </Notice>
          )}

          {/* Buton, sunucunun uyguladığı kuralların AYNISIYLA kilitleniyor; sunucu yine
              bağımsız doğruluyor (400). Buradaki amaç boşuna istek attırmamak. */}
          <Button type="submit" variant="danger" loading={busy} disabled={!gecerli}>
            Düzeltmeyi uygula
          </Button>
        </form>
      </Card>
    </>
  )
}

// ---------------------------------------------------------------------------
// Denetim izi
// ---------------------------------------------------------------------------

const ACTION_LABELS = {
  DisputeResolved: 'İtiraz karara bağlandı',
  DisputeReviewStarted: 'İnceleme başlatıldı',
  UserBanned: 'Kullanıcı banlandı',
  UserUnbanned: 'Ban kaldırıldı',
  HwidBanned: 'Cihaz banlandı',
  HwidUnbanned: 'Cihaz banı kaldırıldı',
  RoleChanged: 'Rol değiştirildi',
  JobTriggered: 'İş elle tetiklendi',
  TeacherCandidateVerified: 'Öğretmen adaylığı doğrulandı',
  TeacherCandidateRejected: 'Öğretmen adaylığı reddedildi',
  TeacherCandidateReviewReverted: 'Adaylık kararı geri alındı',
  UserSanctioned: 'Yaptırım uygulandı (uyarı/askı)',
}

function AuditLogPanel() {
  const [page, setPage] = useState(1)
  const log = useAsync(() => api.auditLog(page, 25), [page])
  const data = log.data

  return (
    <div className="space-y-3">
      <ErrorBox error={log.error} onRetry={log.reload} />
      <SectionTitle>Denetim izi ({data?.totalCount ?? 0})</SectionTitle>

      {log.loading ? (
        <Loading />
      ) : (data?.items?.length ?? 0) === 0 ? (
        <EmptyState
          title="Kayıt yok"
          description="Yönetim işlemleri burada iz bırakır: itiraz kararları, banlar, rol değişiklikleri."
        />
      ) : (
        <>
          <div className="space-y-2">
            {data.items.map((row) => (
              <Card key={row.id} className="p-4">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge tone="brand">{ACTION_LABELS[row.action] ?? row.action}</Badge>
                      <Badge tone="neutral">{row.actorRole}</Badge>
                    </div>
                    <p className="mt-1 text-sm text-slate-700">{row.summary}</p>
                    <p className="mt-1 text-xs text-slate-500">
                      {row.actorDisplayName} · {formatDateTime(row.createdAtUtc)}
                    </p>
                  </div>
                </div>
              </Card>
            ))}
          </div>

          {data.totalPages > 1 && (
            <div className="flex items-center justify-between">
              <Button
                variant="secondary"
                disabled={page <= 1}
                onClick={() => setPage((p) => p - 1)}
              >
                ← Önceki
              </Button>
              <span className="text-sm text-slate-500">
                {data.page} / {data.totalPages}
              </span>
              <Button
                variant="secondary"
                disabled={!data.hasNextPage}
                onClick={() => setPage((p) => p + 1)}
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
