import { useEffect, useState } from 'react'
import { useConsent } from '../state/ConsentContext'
import { CONSENT_CATEGORIES, CONSENT_VERSION } from '../lib/consent'
import { Button } from './ui'

/**
 * Çerez izni (Modül 3).
 *
 * ENGELLEYİCİ BİR PENCERE DEĞİL, alttan çıkan bir şerit: analitik script'i rıza gelene
 * kadar zaten yüklenmiyor (bkz. ConsentProvider.analyticsAllowed), dolayısıyla kullanıcıyı
 * siteyi kullanamaz hâle getirmenin teknik bir gerekçesi yok. "Reddet" seçeneği "Kabul et"
 * ile aynı tıklama mesafesinde: rızanın özgür iradeyle verilmiş sayılması için reddetmek,
 * kabul etmekten zor olmamalıdır (KVKK/GDPR'da "dark pattern" sayılan tam da budur).
 */
export function CookieBanner() {
  const { mustAsk, settingsOpen, openSettings, closeSettings, save, consent } = useConsent()

  // Şerit yalnızca hiç sorulmadıysa; ayarlar penceresi ayrıca her zaman açılabilir.
  const showBar = mustAsk && !settingsOpen

  return (
    <>
      {showBar && (
        <div className="fixed inset-x-0 bottom-0 z-50 border-t border-brand-100 bg-white/95 p-4 shadow-[0_-4px_20px_rgba(15,23,42,0.08)] backdrop-blur">
          <div className="mx-auto flex max-w-4xl flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <p className="text-sm text-slate-700">
              Siteyi çalıştırmak için zorunlu çerezleri kullanıyoruz. Analitik ve fonksiyonel
              çerezler ise <strong>yalnızca izin verirsen</strong> çalışır.{' '}
              {/* Dokunma alanı bilerek büyük: rızayı DARALTMANIN yolu, kabul etmenin yolundan
                  zor olmamalı. Yanındaki düğmeler zaten 44px. */}
              <button
                onClick={openSettings}
                className="-my-1 inline-flex min-h-11 items-center py-1 font-medium text-brand-600 underline hover:no-underline lg:my-0 lg:min-h-0 lg:py-0"
              >
                Ayrıntılar ve seçenekler
              </button>
            </p>

            <div className="flex shrink-0 flex-col gap-2 sm:flex-row">
              <Button
                variant="secondary"
                onClick={() => save({ analytics: false, functional: false })}
              >
                Yalnızca zorunlu
              </Button>
              <Button onClick={() => save({ analytics: true, functional: true })}>
                Tümünü kabul et
              </Button>
            </div>
          </div>
        </div>
      )}

      {settingsOpen && (
        <ConsentSettings
          initial={consent}
          onClose={closeSettings}
          onSave={save}
          /* Hiç rıza yokken pencere kapatılırsa şerit geri gelmeli — "X" sessiz bir
             kabul anlamına gelmemeli. */
          allowDismiss={!mustAsk}
        />
      )}
    </>
  )
}

function Toggle({ checked, disabled, onChange, label }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={`relative h-6 w-11 shrink-0 rounded-full transition ${
        checked ? 'bg-brand-600' : 'bg-slate-300'
      } ${disabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer'}`}
    >
      <span
        className={`absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-all ${
          checked ? 'left-[22px]' : 'left-0.5'
        }`}
      />
    </button>
  )
}

function ConsentSettings({ initial, onClose, onSave, allowDismiss }) {
  const [analytics, setAnalytics] = useState(Boolean(initial?.analytics))
  const [functional, setFunctional] = useState(Boolean(initial?.functional))

  // Escape yalnızca kapatılabilir durumdayken çalışır (bkz. allowDismiss).
  useEffect(() => {
    if (!allowDismiss) return
    const onKey = (e) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [allowDismiss, onClose])

  const values = { necessary: true, functional, analytics }
  const setters = { functional: setFunctional, analytics: setAnalytics }

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-slate-900/40 p-4 sm:items-center">
      <div className="flex max-h-[90dvh] w-full max-w-lg flex-col rounded-xl bg-white shadow-xl">
        <div className="flex shrink-0 items-center justify-between border-b border-slate-200/80 px-5 py-3">
          <h3 className="font-semibold text-slate-800">Çerez tercihleri</h3>
          {allowDismiss && (
            <button
              onClick={onClose}
              className="rounded p-1 text-slate-400 transition hover:bg-slate-100 hover:text-slate-600"
              aria-label="Kapat"
            >
              ✕
            </button>
          )}
        </div>

        <div className="flex-1 space-y-3 overflow-y-auto px-5 py-4">
          {CONSENT_CATEGORIES.map((category) => (
            <div key={category.key} className="rounded-lg border border-slate-200/80 p-3">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="font-medium text-slate-800">{category.title}</p>
                  <p className="mt-1 text-sm text-slate-600">{category.description}</p>
                </div>

                {category.required ? (
                  <span className="shrink-0 rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-medium text-slate-600">
                    Her zaman açık
                  </span>
                ) : (
                  <Toggle
                    checked={values[category.key]}
                    onChange={setters[category.key]}
                    label={category.title}
                  />
                )}
              </div>

              <ul className="mt-2 space-y-0.5 text-xs text-slate-500">
                {category.details.map((detail) => (
                  <li key={detail}>• {detail}</li>
                ))}
              </ul>
            </div>
          ))}

          <p className="text-xs text-slate-400">
            Metin sürümü: {CONSENT_VERSION}. Tercihini istediğin zaman sayfanın altındaki
            “Çerez tercihleri” bağlantısından değiştirebilirsin.
          </p>
        </div>

        <div className="flex shrink-0 flex-col gap-2 border-t border-slate-200/80 px-5 py-3 sm:flex-row sm:justify-end">
          <Button
            variant="secondary"
            onClick={() => onSave({ analytics: false, functional: false })}
          >
            Tümünü reddet
          </Button>
          <Button onClick={() => onSave({ analytics, functional })}>Seçimimi kaydet</Button>
        </div>
      </div>
    </div>
  )
}

/** Tercihi sonradan değiştirmek için sayfa altına konan bağlantı. */
export function CookieSettingsLink({ className = '' }) {
  const { openSettings } = useConsent()

  return (
    <button
      onClick={openSettings}
      className={`-my-2 inline-flex min-h-11 items-center py-2 text-xs text-slate-500 underline
                  hover:text-slate-700 lg:my-0 lg:min-h-0 lg:py-0 ${className}`}
    >
      Çerez tercihleri
    </button>
  )
}
