import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { api } from '../lib/api'
import { useAuth } from './AuthContext'
import {
  CONSENT_VERSION,
  needsConsent,
  readLocalConsent,
  writeLocalConsent,
} from '../lib/consent'

const ConsentContext = createContext(null)

/**
 * Çerez rızası durumu ve kaydı.
 *
 * OKUMA SIRASI ÖNEMLİ: başlangıç değeri SENKRON olarak localStorage'dan okunur.
 * Sunucuyu beklemek, analitik script'inin yükleneceği ilk anı geciktirir ya da daha
 * kötüsü, "henüz bilmiyorum" durumunda yanlışlıkla yüklenmesine yol açardı.
 * Sunucu yanıtı geldiğinde (giriş yapılmışsa) yerel değerin ÜZERİNE yazar — çünkü
 * cihazlar arası taşınan ve ispat değeri olan kayıt odur.
 */
export function ConsentProvider({ children }) {
  const { session, isAuthenticated } = useAuth()
  const [consent, setConsent] = useState(() => readLocalConsent())
  const [settingsOpen, setSettingsOpen] = useState(false)

  // Giriş yapıldığında sunucu kaydıyla uzlaş.
  useEffect(() => {
    if (!isAuthenticated) return

    let cancelled = false

    api
      .myPreferences()
      .then((prefs) => {
        if (cancelled) return

        const serverAnswered =
          prefs.analyticsConsent !== 'NotAsked' || prefs.functionalConsent !== 'NotAsked'

        if (serverAnswered && prefs.consentVersion === CONSENT_VERSION) {
          // Sunucu güncel bir rıza taşıyor: otorite odur, yerele de yazılır.
          const fromServer = {
            analytics: prefs.analyticsConsent === 'Granted',
            functional: prefs.functionalConsent === 'Granted',
            version: prefs.consentVersion,
            updatedAt: prefs.consentUpdatedAtUtc,
          }
          setConsent(fromServer)
          writeLocalConsent(fromServer)
          return
        }

        // Sunucuda güncel kayıt yok ama tarayıcıda var: anonim ziyarette verilen rızayı
        // hesaba TAŞI. Aksi halde kullanıcı giriş yapar yapmaz aynı banner'ı yeniden görürdü.
        const local = readLocalConsent()
        if (local && !needsConsent(local)) {
          api
            .saveCookieConsent(local.analytics, local.functional, CONSENT_VERSION)
            .catch(() => {
              /* Taşıma başarısız olursa yerel tercih geçerli kalır; bir dahaki girişte yeniden denenir. */
            })
        }
      })
      .catch(() => {
        // Tercihler okunamazsa yerel değerle devam edilir — banner akışı bozulmasın.
      })

    return () => {
      cancelled = true
    }
  }, [isAuthenticated, session?.userId])

  const save = useCallback(
    async ({ analytics, functional }) => {
      const next = {
        analytics,
        functional,
        version: CONSENT_VERSION,
        updatedAt: new Date().toISOString(),
      }

      // Önce yerel: kullanıcı "Kaydet"e bastığı anda arayüz kararlı olmalı, ağ beklenmemeli.
      setConsent(next)
      writeLocalConsent(next)
      setSettingsOpen(false)

      if (isAuthenticated) {
        try {
          await api.saveCookieConsent(analytics, functional, CONSENT_VERSION)
        } catch {
          // Sunucuya yazılamadıysa yerel tercih yine geçerli; bir sonraki girişte taşınır.
        }
      }
    },
    [isAuthenticated],
  )

  const value = useMemo(
    () => ({
      consent,
      /** Banner gösterilecek mi? */
      mustAsk: needsConsent(consent),
      /** Modül 4 buna bakacak: GA4 yalnızca true iken yüklenir. */
      analyticsAllowed: Boolean(consent?.analytics),

      /*
        Ürün turu buna bakar (bkz. ProductTour). Bir süre yalnızca TOPLANIP hiçbir yerde
        KULLANILMIYORDU: kullanıcı fonksiyonel çerezleri açıkça reddetse bile davranış
        birebir aynı kalıyordu. Rıza alıp uygulamamak, hiç sormamaktan daha kötü bir
        konumdur — banner'da verilen taahhüt yerine getirilmiş olmaz.
      */
      functionalAllowed: Boolean(consent?.functional),
      settingsOpen,
      openSettings: () => setSettingsOpen(true),
      closeSettings: () => setSettingsOpen(false),
      save,
    }),
    [consent, settingsOpen, save],
  )

  return <ConsentContext.Provider value={value}>{children}</ConsentContext.Provider>
}

export function useConsent() {
  const context = useContext(ConsentContext)
  if (!context) throw new Error('useConsent, ConsentProvider içinde kullanılmalı.')
  return context
}
