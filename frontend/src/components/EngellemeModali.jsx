import { useState } from 'react'
import { api } from '../lib/api'
import { Button, ErrorBox, Field, Modal } from './ui'

/**
 * Engelleme onayı.
 *
 * ─── NEDEN ONAY SORULUYOR ─────────────────────────────────────────────────────
 * Engelleme yalnızca bir satır yazmıyor: bekleyen istekleri de kapatıyor
 * (BlockUserHandler) ve engel kaldırılsa bile o istekler geri gelmiyor. Yani tek
 * tıkla geri alınabilir bir işlem DEĞİL. Kipteki liste bunu açıkça yazıyor —
 * "emin misin" diye sormak, ne olacağını söylemeden onay almak olurdu.
 *
 * Engeli KALDIRMAK ise onay sormuyor. Asimetri bilinçli: kaldırmak eski hâle
 * dönüyor ve yanlışsa aynı yerden tek tıkla geri alınabiliyor.
 *
 * ─── ŞİKAYET YOLU NEDEN BURADA HATIRLATILIYOR ─────────────────────────────────
 * Engelleme kişisel bir tercih: kimseye bildirilmiyor, denetim izi tutulmuyor,
 * yönetimin haberi olmuyor. Taciz varsa engellemek YETMEZ. İki kavramın
 * karıştırılması, kural ihlallerinin yönetime hiç ulaşmaması demek — kullanıcı
 * "hallettim" sanır, yönetim hiçbir şey görmez.
 *
 * ─── TEK KOPYA ────────────────────────────────────────────────────────────────
 * Keşfet kartları ve profil sayfası aynı bileşeni kullanıyor. İki yere ayrı ayrı
 * yazılsaydı yukarıdaki metin er ya da geç ayrışırdı ve bir yol, sonucu eksik
 * anlatan bir onay ekranı gösterirdi.
 *
 * @param kisi          `{ userId, displayName }` — engellenecek kişi. null ise kip kapalı.
 * @param onEngellendi  Başarıdan sonra çağrılır, tek argüman görünen ad.
 */
export function EngellemeModali({ kisi, onClose, onEngellendi }) {
  const [not, setNot] = useState('')
  const [hata, setHata] = useState(null)
  const [busy, setBusy] = useState(false)

  async function engelle() {
    setBusy(true)
    setHata(null)
    try {
      await api.blockUser(kisi.userId, not.trim() || null)
      onEngellendi(kisi.displayName)
    } catch (err) {
      setHata(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open={Boolean(kisi)} onClose={onClose} title="Kişiyi engelle">
      {kisi && (
        <div className="space-y-3">
          <p className="text-sm text-slate-700">
            <span className="font-semibold text-slate-900">{kisi.displayName}</span> engellenecek.
          </p>

          <ul className="list-disc space-y-1 pl-5 text-sm text-slate-600">
            <li>Birbirinize istek gönderemezsiniz.</li>
            <li>Bekleyen istekleriniz kapanır ve engeli kaldırsan da geri gelmez.</li>
            <li>Birbirinizin arama sonuçlarında görünmezsiniz.</li>
            <li>Karşı tarafa bildirilmez.</li>
          </ul>

          {/* Not SUNUCUDA da yalnızca engelleyene dönüyor (GetMyBlocksHandler);
              karşı taraf ne engellendiğini ne de not yazıldığını görüyor. */}
          <Field
            label="Kendine not (isteğe bağlı)"
            hint="Yalnızca sen görürsün. Neden engellediğini sonra hatırlamak için."
          >
            <input
              className="input"
              value={not}
              maxLength={500}
              onChange={(e) => setNot(e.target.value)}
              placeholder="Örn. tanımıyorum"
            />
          </Field>

          <p className="rounded-lg bg-amber-50 p-3 text-xs text-amber-900">
            Taciz ya da kural ihlali varsa engellemek yetmez — dersin sayfasından şikayet et ki
            yönetimin haberi olsun.
          </p>

          <ErrorBox error={hata} />

          <div className="mt-4 flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose}>
              Vazgeç
            </Button>
            <Button variant="danger" onClick={engelle} loading={busy}>
              Engelle
            </Button>
          </div>
        </div>
      )}
    </Modal>
  )
}
