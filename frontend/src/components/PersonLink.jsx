import { Link } from 'react-router-dom'

/**
 * Kişi adını profiline bağlar.
 *
 * NEDEN AYRI BİLEŞEN: sosyal profil (rozet vitrini, puan dağılımı, etiket histogramı,
 * doğrulanmış yorumlar) "bu kişiyle ders yapar mıyım" sorusunu yanıtlamak için yazıldı
 * ve o karar Keşfet, Eşleşmeler, Sohbet ve Derslerim'de veriliyor. Bağlantı bu ekranların
 * hepsine ayrı ayrı elle serpilseydi biri unutulur, biri farklı stille yazılırdı; tek yer
 * olunca "ad görünen her yerde profile gidilir" kuralı kendiliğinden korunuyor.
 *
 * userId yoksa (silinmiş kullanıcı, eksik alan) sessizce düz metne düşer — kırık bir
 * bağlantı göstermek, bağlantı göstermemekten kötüdür.
 */
export function PersonLink({ userId, children, className = '', title = 'Profili gör' }) {
  if (!userId) {
    return <span className={className}>{children}</span>
  }

  return (
    <Link to={`/profil/${userId}`} title={title} className={`underline-offset-2 hover:underline ${className}`}>
      {children}
    </Link>
  )
}
