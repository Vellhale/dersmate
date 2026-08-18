import { useEffect } from 'react'
import { useLocation } from 'react-router-dom'
import { useConsent } from './ConsentContext'
import { disableAnalytics, enableAnalytics, trackPageView } from '../lib/analytics'

/**
 * GA4'ün yükleme/kaldırma kararının TEK yeri (Modül 4).
 *
 * Rıza durumunu izler: izin varsa script yüklenir, izin geri çekilirse kapatılır ve
 * bırakılmış _ga çerezleri temizlenir. Bileşenler bu kararı tekrar sorgulamaz —
 * kontrolü tek noktada tutmak, "bir yerde unutuldu" hatasını yapısal olarak imkânsızlaştırır.
 *
 * Ayrıca SPA sayfa görüntülemelerini bildirir: GA4'ün otomatik page_view'i yalnızca ilk
 * yüklemede çalışır, rota değişimlerini görmez (bu yüzden config'te send_page_view=false).
 */
export function AnalyticsGate() {
  const { analyticsAllowed } = useConsent()
  const location = useLocation()

  /*
    İzin yoksa HER render'da kapatma uygulanır — yalnızca "izin geri çekildiği an" değil.

    Önce ikinci davranışı yazmıştım (bir ref ile true→false geçişini izleyerek) ve testte
    şu hatayı verdi: kullanıcı izni geri çekip sayfayı YENİLEDİĞİNDE, açılışta izin zaten
    "yok" olduğu için geçiş yaşanmıyor, dolayısıyla ga-disable bayrağı kurulmuyor ve daha
    önce yazılmış _ga çerezleri sonsuza kadar tarayıcıda kalıyordu. Yani "izni geri çektim"
    demek fiilen çerezleri silmiyordu.

    Kapatma idempotenttir: olmayan script'i kaldırmak ve olmayan çerezi silmek maliyetsizdir.
  */
  useEffect(() => {
    if (analyticsAllowed) {
      enableAnalytics()
    } else {
      disableAnalytics()
    }
  }, [analyticsAllowed])

  useEffect(() => {
    if (!analyticsAllowed) return
    trackPageView(location.pathname)
  }, [analyticsAllowed, location.pathname])

  return null
}
