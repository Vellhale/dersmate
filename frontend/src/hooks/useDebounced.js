import { useEffect, useState } from 'react'

/**
 * Değeri gecikmeli yansıtır (debounce).
 *
 * Arama kutusunda ŞART: her tuş vuruşunda istek atmak, 20 bin kullanıcılık portföy
 * tablosunda hem sunucuyu hem de kullanıcının bağlantısını gereksiz yorar. Ayrıca
 * yarış oluşur — "mat" için dönen yanıt "matematik" için dönenden sonra gelip listeyi
 * yanlış sonuçla doldurabilir.
 */
export function useDebounced(value, delay = 350) {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay)
    // Değer yeniden değişirse önceki zamanlayıcı iptal: yalnızca DURAKLAMA anında tetiklenir.
    return () => clearTimeout(timer)
  }, [value, delay])

  return debounced
}
