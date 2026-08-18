import { useCallback, useEffect, useState } from 'react'

/**
 * Basit veri çekme kancası: { data, error, loading, reload }.
 *
 * reload({ silent: true }) — arka plan tazelemesi: elde veri varken `loading` bayrağı
 * KALDIRILMAZ. Bu şart: sohbet listesi her gelen mesajda tazeleniyor ve `loading`
 * yükseltilseydi tüm ekran spinner'a dönüp yazma kutusunun odağını ve kaydırma
 * konumunu kaybettirirdi.
 */
export function useAsync(loader, deps = []) {
  const [state, setState] = useState({ data: null, error: null, loading: true })

  const run = useCallback(
    (options = {}) => {
      let cancelled = false

      setState((prev) => ({
        ...prev,
        // Sessiz tazelemede yalnızca ilk yüklemede spinner göster.
        loading: options.silent ? prev.data === null : true,
        error: null,
      }))

      loader()
        .then((data) => {
          if (!cancelled) setState({ data, error: null, loading: false })
        })
        .catch((error) => {
          if (!cancelled) setState((prev) => ({ data: prev.data, error, loading: false }))
        })

      return () => {
        cancelled = true
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    deps,
  )

  useEffect(() => run(), [run])

  return { ...state, reload: run }
}
