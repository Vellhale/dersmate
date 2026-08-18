import { useCallback, useEffect, useLayoutEffect, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { api } from '../lib/api'
import { useConsent } from '../state/ConsentContext'
import { TOUR_STEPS, TOUR_STEP_COUNT } from '../lib/tour'
import { Button } from './ui'

/**
 * İnteraktif ürün turu (Modül 5).
 *
 * NEDEN HAZIR KÜTÜPHANE (Joyride/Driver.js) DEĞİL: dört adımlık bir spot ışığı için yeni
 * bir bağımlılık, bakım yükünü kazanılan koddan daha çok artırıyordu. Karşılığında Türkçe
 * metin, mobil davranış ve 44px dokunma kuralı bizde.
 *
 * DURUM SUNUCUDA tutulur, localStorage'da değil: tur yalnızca giriş yapmış kullanıcıya
 * gösteriliyor, dolayısıyla hesaba yazmak hem cihazlar arası taşınır hem de çerez
 * rızasıyla ilgili bir soru doğurmaz.
 */
/**
 * Turu yeniden başlatma sinyali. Bağlantı ile tur bileşeni kardeş olduğu için araya
 * context koymak yerine tek bir pencere olayı kullanılıyor — tek yönlü ve tek kullanımlık
 * bir tetik için context kurmak fazla ağır olurdu.
 */
const RESTART_EVENT = 'peerlearn:restart-tour'

/*
  "Rehberi geç" OTURUM boyunca susturur. Sunucudaki kayıt "tamamlanmadı" olarak kalır —
  yani tur ileride yeniden önerilebilir — ama aynı oturumda her sayfa yüklemesinde geri
  gelmez. sessionStorage tam olarak bu ömre denk düşer: sekme kapanınca silinir.

  Neden sunucuya yazılmıyor: "şimdi değil" ile "bir daha gösterme" farklı niyetler
  (bkz. UserPreference.OnboardingSuppressed) ve ilki kalıcı bir tercih değil.
*/
const SESSION_SKIP_KEY = 'peerlearn.tour-skipped'

function sessionSkipped() {
  try {
    return sessionStorage.getItem(SESSION_SKIP_KEY) === '1'
  } catch {
    return false // Depolama kapalıysa tur gösterilsin; sessizce yutmak yerine varsayılana dön.
  }
}

function markSessionSkipped() {
  try {
    sessionStorage.setItem(SESSION_SKIP_KEY, '1')
  } catch {
    /* yoksay */
  }
}

export function ProductTour() {
  const navigate = useNavigate()
  const location = useLocation()

  /*
    FONKSİYONEL ÇEREZ RIZASI BURADA UYGULANIR — banner'ın "Ürün turu durumu" diye saydığı
    şey tam olarak bu bileşenin sakladığı veri.

    Ayrım bilinçli:
      • ÖRTÜK kolaylık durumu (oturum içi "geç" işareti, kaldığın adım) rızaya tabidir;
        reddedilmişse hiç yazılmaz — banner'daki "kapatırsan bazı şeyleri tekrar ayarlaman
        gerekebilir" cümlesinin karşılığı budur.
      • AÇIK karar ("bir daha gösterme", "turu tamamladım") her hâlükârda kaydedilir.
        Kullanıcının kendi isteğiyle verdiği kalıcı talimatı unutmak, rızaya saygı değil
        kullanıcıya zarardır: turu kapatamayan biri onu her oturumda yeniden görür.
  */
  const { functionalAllowed } = useConsent()

  const [state, setState] = useState({ loading: true, active: false, step: 0 })
  const [rect, setRect] = useState(null)

  // Açılışta sunucudaki duruma bak. Tamamlamış ya da "bir daha gösterme" demişse hiç başlama.
  useEffect(() => {
    let cancelled = false

    /*
      TUR YALNIZCA PANELDE (/) KENDİLİĞİNDEN BAŞLAR.

      Önce başka sayfadayken de başlatıp kullanıcıyı panele YÖNLENDİRİYORDUM. Test bunun
      sonucunu gösterdi: turu "geç"en kullanıcı herhangi bir menü öğesine tıkladığında tur
      yeniden açılıyor VE kullanıcı gitmek istediği sayfadan panele geri sürükleniyordu.
      Rehber, kullanıcının gitmek istediği yeri elinden alamaz. Artık tur, kullanıcı zaten
      paneldeyse başlar; değilse bir sonraki panel ziyaretini bekler.
    */
    if (location.pathname !== '/' || sessionSkipped()) {
      setState({ loading: false, active: false, step: 0 })
      return
    }

    api
      .myPreferences()
      .then((prefs) => {
        if (cancelled) return
        const shouldStart = !prefs.onboardingCompleted && !prefs.onboardingSuppressed
        setState({
          loading: false,
          active: shouldStart,
          // Yarıda bırakmışsa kaldığı yerden devam.
          step: Math.min(prefs.onboardingLastStep ?? 0, TOUR_STEP_COUNT - 1),
        })
      })
      .catch(() => {
        // Tercihler okunamazsa tur BAŞLATILMAZ: yanlışlıkla her açılışta tur göstermek,
        // hiç göstermemekten daha rahatsız edici.
        if (!cancelled) setState({ loading: false, active: false, step: 0 })
      })

    return () => {
      cancelled = true
    }
    // Yalnızca ilk montajda çalışır; rota değiştikçe turu yeniden başlatmak istemiyoruz.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const current = TOUR_STEPS[state.step]

  // Hedefin konumunu ölç. useLayoutEffect: boyama öncesi ölçülür, spot ışığı ilk karede
  // yanlış yerde belirip zıplamaz.
  useLayoutEffect(() => {
    if (!state.active || !current) return

    const el = document.querySelector(current.selector)
    if (!el) {
      setRect(null) // Çapa yok → ortada kart.
      return
    }

    /*
      KAYDIRMA ve ÖLÇÜM ayrı tutulur. İlk yazımda ikisi tek fonksiyondaydı ve o fonksiyon
      scroll dinleyicisine bağlıydı: scrollIntoView kaydırma olayı üretiyor, olay yeniden
      scrollIntoView çağırıyordu — kendi kendini besleyen bir döngü. Artık kaydırma adım
      başına BİR kez, ölçüm ise her kaydırma/yeniden boyutlandırmada yapılıyor.
    */
    el.scrollIntoView({ block: 'center', behavior: 'smooth' })

    let frame = 0
    const measure = () => {
      cancelAnimationFrame(frame)
      // Kaydırma sürerken ölçüm eskir; bir sonraki karede oku.
      frame = requestAnimationFrame(() => {
        const r = el.getBoundingClientRect()
        setRect({ top: r.top, left: r.left, width: r.width, height: r.height })
      })
    }

    measure()

    window.addEventListener('resize', measure)
    window.addEventListener('scroll', measure, true)
    return () => {
      cancelAnimationFrame(frame)
      window.removeEventListener('resize', measure)
      window.removeEventListener('scroll', measure, true)
    }
  }, [state.active, state.step, current])

  const persist = useCallback(
    (step, completed, suppressed) => {
      // Yalnızca ADIM İLERLEMESİ fonksiyonel rızaya tabi; açık kararlar her zaman yazılır.
      if (!completed && !suppressed && !functionalAllowed) return

      // Ateşle-unut: turun akışı ağ yanıtını beklemez. Kaydedilemezse en fazla tur bir kez
      // daha görünür — akışı bloklamaktan iyidir.
      api.saveOnboarding(step, completed, suppressed).catch(() => {})
    },
    [functionalAllowed],
  )

  const finish = useCallback(
    ({ suppressed = false, completed = false } = {}) => {
      setState((s) => ({ ...s, active: false }))
      persist(state.step, completed, suppressed)

      // Tamamlanmadan kapatıldıysa bu oturumda bir daha açılmasın.
      // Oturum işareti cihazda saklanan bir kolaylıktır: rıza yoksa yazılmaz.
      if (!completed && !suppressed && functionalAllowed) {
        markSessionSkipped()
      }
    },
    [persist, state.step, functionalAllowed],
  )

  const next = useCallback(() => {
    if (state.step >= TOUR_STEP_COUNT - 1) {
      finish({ completed: true })
      return
    }
    const step = state.step + 1
    setState((s) => ({ ...s, step }))
    persist(step, false, false)
  }, [state.step, finish, persist])

  const back = useCallback(() => {
    setState((s) => ({ ...s, step: Math.max(0, s.step - 1) }))
  }, [])

  // Escape = "rehberi geç".
  useEffect(() => {
    if (!state.active) return
    const onKey = (e) => e.key === 'Escape' && finish()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [state.active, finish])

  // Alttaki "Turu tekrar izle" bağlantısı. Elle başlatılan tur, "bir daha gösterme"
  // tercihini de sıfırlar: kullanıcı açıkça yeniden istedi.
  useEffect(() => {
    const onRestart = () => {
      // Kullanıcı AÇIKÇA istedi: oturum susturması kalkar ve çapaların bulunduğu panele
      // gidilir. Kendiliğinden başlarken yönlendirme yapılmıyor (bkz. yukarıdaki not),
      // burada yapılıyor çünkü niyet kullanıcının kendisinden geliyor.
      try {
        sessionStorage.removeItem(SESSION_SKIP_KEY)
      } catch {
        /* yoksay */
      }

      if (location.pathname !== '/') {
        navigate('/')
      }

      setState({ loading: false, active: true, step: 0 })
      persist(0, false, false)
    }
    window.addEventListener(RESTART_EVENT, onRestart)
    return () => window.removeEventListener(RESTART_EVENT, onRestart)
  }, [persist, location.pathname, navigate])

  if (state.loading || !state.active || !current) return null

  const isLast = state.step === TOUR_STEP_COUNT - 1
  const padding = 8

  return (
    <div className="fixed inset-0 z-[60]" role="dialog" aria-modal="true" aria-label="Ürün turu">
      {rect ? (
        /*
          Spot ışığı: karartma ayrı bir katman DEĞİL, hedefin etrafına taşan devasa bir
          box-shadow. Böylece "delik" tek bir öğeyle elde ediliyor; dört ayrı karartma
          paneli hizalamaya çalışmak piksel kaymalarına açık olurdu.
        */
        <div
          className="pointer-events-none absolute rounded-xl ring-2 ring-brand-400 transition-all duration-200"
          style={{
            top: rect.top - padding,
            left: rect.left - padding,
            width: rect.width + padding * 2,
            height: rect.height + padding * 2,
            boxShadow: '0 0 0 9999px rgba(15, 23, 42, 0.6)',
          }}
        />
      ) : (
        <div className="absolute inset-0 bg-slate-900/60" />
      )}

      <TourCard
        rect={rect}
        step={state.step}
        title={current.title}
        body={current.body}
        isLast={isLast}
        onNext={next}
        onBack={back}
        onSkip={() => finish()}
        onNeverShow={() => finish({ suppressed: true })}
      />
    </div>
  )
}

/** Sayfa altındaki "Turu tekrar izle" bağlantısı. */
export function RestartTourLink({ className = '' }) {
  return (
    <button
      onClick={() => window.dispatchEvent(new CustomEvent(RESTART_EVENT))}
      className={`-my-2 inline-flex min-h-11 items-center py-2 text-xs text-slate-500 underline
                  hover:text-slate-700 lg:my-0 lg:min-h-0 lg:py-0 ${className}`}
    >
      Turu tekrar izle
    </button>
  )
}

function TourCard({ rect, step, title, body, isLast, onNext, onBack, onSkip, onNeverShow }) {
  const [style, setStyle] = useState(null)

  useLayoutEffect(() => {
    // Çapa yoksa ya da dar ekrandaysak kart altta sabit: 375px'lik bir ekranda küçük bir
    // öğenin yanına konumlandırmaya çalışmak kartı ekran dışına taşırır.
    if (!rect || window.innerWidth < 640) {
      setStyle(null)
      return
    }

    const cardWidth = 380
    const gap = 16
    const below = rect.top + rect.height + gap
    const fitsBelow = below + 240 < window.innerHeight

    setStyle({
      width: cardWidth,
      top: fitsBelow ? below : Math.max(gap, rect.top - 240 - gap),
      left: Math.min(
        Math.max(gap, rect.left + rect.width / 2 - cardWidth / 2),
        window.innerWidth - cardWidth - gap,
      ),
    })
  }, [rect])

  const positioned = Boolean(style)

  return (
    <div
      className={
        positioned
          ? 'absolute rounded-xl bg-white p-5 shadow-2xl'
          : 'absolute inset-x-4 bottom-4 rounded-xl bg-white p-5 shadow-2xl sm:inset-x-auto sm:left-1/2 sm:top-1/2 sm:w-[380px] sm:-translate-x-1/2 sm:-translate-y-1/2'
      }
      style={style ?? undefined}
    >
      <div className="flex items-center justify-between gap-3">
        <span className="text-xs font-medium text-brand-600">
          Adım {step + 1} / {TOUR_STEP_COUNT}
        </span>
        <div className="flex gap-1" aria-hidden="true">
          {TOUR_STEPS.map((s, i) => (
            <span
              key={s.id}
              className={`h-1.5 w-6 rounded-full ${i <= step ? 'bg-brand-500' : 'bg-slate-200'}`}
            />
          ))}
        </div>
      </div>

      <h3 className="mt-2 text-lg font-semibold text-slate-900">{title}</h3>
      <p className="mt-1 text-sm leading-relaxed text-slate-600">{body}</p>

      <div className="mt-4 flex items-center justify-between gap-2">
        <button
          onClick={onSkip}
          className="-my-2 min-h-11 py-2 text-sm text-slate-500 underline hover:text-slate-700 lg:my-0 lg:min-h-0 lg:py-0"
        >
          Rehberi geç
        </button>

        <div className="flex gap-2">
          {step > 0 && (
            <Button variant="secondary" onClick={onBack}>
              Geri
            </Button>
          )}
          <Button onClick={onNext}>{isLast ? 'Bitir' : 'Devam'}</Button>
        </div>
      </div>

      {/* "Bir daha gösterme", "geç"ten AYRI: geçen kullanıcıya bir dahaki girişte tekrar
          önerilebilir, ama açıkça istemeyene hiç sorulmamalı. */}
      <button
        onClick={onNeverShow}
        className="-my-2 mt-2 min-h-11 py-2 text-xs text-slate-400 underline hover:text-slate-600 lg:my-0 lg:min-h-0 lg:py-0"
      >
        Bir daha gösterme
      </button>
    </div>
  )
}
