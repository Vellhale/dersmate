import { api } from '../lib/api'
import { useAsync } from '../state/useAsync'
import { Avatar } from './Avatar'
import { PersonLink } from './PersonLink'
import { CamKart } from './SayfaZemini'
import { SeviyeRozeti } from './SeviyeRozeti'
import { ErrorBox } from './ui'

/**
 * Profilin arkadaş bölümü.
 *
 * ─── ÜÇ FARKLI ŞEY GÖSTERİYOR, HANGİSİ OLDUĞUNU SUNUCU SÖYLÜYOR ──────────────
 *   • kendi profilin      → arkadaş sayısı + TAM LİSTE
 *   • başkasının profili  → arkadaş sayısı + ORTAK ARKADAŞLAR
 *
 * "Tam listeyi yalnızca sahibi görür" kuralı BURADA DEĞİL sunucuda: uç, başkasının
 * profilinde `friends` alanını zaten boş döndürüyor. İstemcide bir `if` ile gizlemek,
 * veriyi ağdan geçirip ekranda saklamak olurdu — tarayıcı konsolunu açan herkes görürdü.
 *
 * ─── BÖLÜM HER PROFİLDE AYNI ŞEKİLDE ÇİZİLİYOR ──────────────────────────────
 * Engelli bir kişinin profilinde bölüm GİZLENMİYOR ya da özel bir metne çevrilmiyor.
 * Sebebi doğrudan: yokluk üzerinden çıkarım yapılabilir. "Bu profilde arkadaşlar bölümü
 * yok" demek, "bu kişiyle aranızda bir engel var" demenin dolaylı yolu olurdu. Aynı
 * gerekçe engelli profilin 404'e düşürülmemesi kararında da yazılı (DEVAM-EDILECEK.md).
 * Tekdüzelik sızdırmaz; istisna sızdırır.
 *
 * ─── SAYI İLE LİSTE AYNI KAYNAKTAN ──────────────────────────────────────────
 * Başlıktaki sayı sunucudan geliyor ve sunucuda listeyle AYNI süzgeçten (engel + aktif
 * kullanıcı) geçiyor. İkisi ayrışsaydı — "12 arkadaş" yazıp 11 kart göstermek — kullanıcı
 * eksik olanın kim olduğunu merak ederdi; ortak arkadaşlarda ise bu doğrudan bir kâhin
 * olurdu: "seninle bu kişi arasında gizlenmiş biri var".
 */
export function ArkadaslarBolumu({ userId, kendiProfilim, ad }) {
  const veri = useAsync(() => api.userFriends(userId), [userId])
  const d = veri.data

  /*
    Yükleme sırasında BÖLÜM ÇİZİLMİYOR (iskelet de yok). Profil kartı ayrı bir istekle
    geliyor ve bu bölüm ondan sonra düşüyor; araya boş bir kutu koymak sayfayı iki kez
    zıplatırdı. Bölüm hazır olduğunda tek seferde beliriyor.
  */
  if (veri.loading) return null
  if (veri.error) {
    return (
      <CamKart>
        <ErrorBox error={veri.error} onRetry={veri.reload} />
      </CamKart>
    )
  }
  if (!d) return null

  const sayi = d.friendCount ?? 0
  const kisiler = kendiProfilim ? (d.friends ?? []) : (d.mutualFriends ?? [])
  const ortak = d.mutualCount ?? 0

  return (
    <CamKart>
      <div className="mb-3 flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <p className="text-sm font-medium text-slate-700">Arkadaşlar</p>
        {/*
          Sayı sağda ve VURGULU: bölümün taşıdığı tek herkese açık sinyal bu. Sıfırken de
          yazılıyor — "0 arkadaş" bir bilgi, gizlenmesi ise sayının bazen görünüp bazen
          görünmemesi demek olurdu.
        */}
        <p className="text-sm text-slate-600">
          <span className="font-semibold text-slate-800">{sayi}</span> arkadaş
        </p>
      </div>

      {/* Ortak arkadaş satırı YALNIZCA VARSA yazılıyor. Yokken de yazılıp altına ayrıca
          bir boş durum metni konduğunda ("… ile ortak arkadaşınız yok." + "Ortak
          arkadaşınız olduğunda burada görünür.") aynı şey iki kez söyleniyordu. */}
      {!kendiProfilim && ortak > 0 && (
        <p className="mb-3 text-sm text-slate-600">{ortak} ortak arkadaşınız var.</p>
      )}

      {kisiler.length === 0 ? (
        <p className="text-sm text-slate-600">
          {kendiProfilim
            ? sayi === 0
              ? 'Henüz arkadaşın yok. Keşfet’teki “Arkadaş Ekle” bölümünden adını bildiğin birini bulabilirsin.'
              : 'Arkadaşların gösterilemedi.'
            : `${ad} ile ortak arkadaşınız yok.`}
        </p>
      ) : (
        /*
          Kaydırılabilir liste — ReviewsSection'daki kalıbın aynısı. max-h-96 ≈ 4-5 satır;
          uzun bir arkadaş listesi profil sayfasını tek başına metrelerce uzatmasın.
          tabIndex={0} klavye kullanıcısı için ŞART: kaydırılabilir bir kutuya odak
          verilemezse içine sekmeyle girilemez. Maske alt kenarı yumuşatıp "devamı var"
          diyor — kesik bir satır, listenin bittiğini sanmaya yol açıyordu.
        */
        <ul
          tabIndex={0}
          aria-label={kendiProfilim ? 'Arkadaş listesi' : 'Ortak arkadaşlar'}
          className="kaydirma-ince -mx-1 max-h-96 divide-y divide-slate-100 overflow-y-auto px-1 focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-200"
          style={{
            maskImage: 'linear-gradient(to bottom, black calc(100% - 20px), transparent)',
            WebkitMaskImage: 'linear-gradient(to bottom, black calc(100% - 20px), transparent)',
          }}
        >
          {kisiler.map((k) => (
            /* min-h-11 lg:min-h-0 — 44px dokunma hedefi. PersonLink kendi yüksekliğini
               taşımıyor (bileşenin kendi notu söylüyor), o yüzden satır taşıyor. */
            <li key={k.userId} className="flex min-h-11 items-center gap-3 py-2 lg:min-h-0">
              <Avatar userId={k.userId} name={k.displayName} size="sm" className="shrink-0" />
              <PersonLink userId={k.userId} className="min-w-0 flex-1 truncate text-sm font-medium text-brand-700">
                {k.displayName}
              </PersonLink>
              <SeviyeRozeti kaynak={{ level: k.level }} boyut="sm" ton="acik" />
            </li>
          ))}
        </ul>
      )}

      {/* Tavan aşıldığında sessiz kalmak, listeyi eksiksiz sanmaya yol açar. */}
      {!kendiProfilim && ortak > kisiler.length && kisiler.length > 0 && (
        <p className="mt-2 text-xs text-slate-600">
          ve {ortak - kisiler.length} kişi daha
        </p>
      )}
      {kendiProfilim && sayi > kisiler.length && kisiler.length > 0 && (
        <p className="mt-2 text-xs text-slate-600">
          ve {sayi - kisiler.length} kişi daha
        </p>
      )}
    </CamKart>
  )
}
