import { useEffect, useRef, useState } from 'react'
import { Button, ErrorBox, Modal } from './ui'

/**
 * Profil fotoğrafı seçme + kırpma.
 *
 * KIRPMA VE KÜÇÜLTME TARAYICIDA yapılır (canvas), sunucuya hazır 512×512 JPEG gider.
 * Böylece sunucuya görüntü kütüphanesi eklemeye gerek kalmaz ve 4 MB'lık bir telefon
 * fotoğrafı yerine ~60 KB'lık bir kare yüklenir. Sunucu yine de boyut/tür/içerik
 * imzasını bağımsız olarak doğrular — istemciye güvenilmez.
 *
 * Kırpma kasıtlı olarak BASİT: kare çerçeve, kaydır ve yakınlaştır. Serbest oranlı
 * kırpma bir profil fotoğrafı için gereksiz; her yerde kare gösterildiği için
 * kullanıcıyı kareye zorlamak sonucun tutarlı olmasını sağlar.
 */

const OUTPUT_SIZE = 512
const MAX_INPUT_BYTES = 8 * 1024 * 1024

/*
  ÖNİZLEME KUTUSUNUN KENARI — TEK SAYI, İKİ KULLANIM.

  Bu değer hem ekrandaki çerçevenin ölçüsü hem de kırpma matematiğinin ölçeği. Eskiden
  ikisi AYRI yerlerde duruyordu: çerçeve `h-64 w-64` (Tailwind, 256px), matematik ise
  kendi içinde `const previewSize = 256`. İkisi aynı sayıyı tesadüfen söylüyordu; biri
  değişse diğeri sessizce yalan söylerdi. Artık kutu da bu sabitten çiziliyor.
*/
const ONIZLEME = 256

/**
 * Görselin çerçeveyi TAM KAPLAMASI için gereken taban ölçek (CSS `object-fit: cover`
 * ile aynı kural). Yakınlaştırma bunun ÜSTÜNE çarpılır, yani zoom=1 her zaman
 * "tam kaplayan en küçük hâl" demektir ve altına inilemez.
 */
function tabanOlcek(dogal) {
  if (!dogal) return 1
  return Math.max(ONIZLEME / dogal.w, ONIZLEME / dogal.h)
}

/**
 * Sürüklemeyi çerçevenin içinde tutar.
 *
 * Sınır yoktu: kullanıcı fotoğrafı çerçeveden dışarı sürükleyebiliyor, boşta kalan
 * alan kaydedilen JPEG'de BEYAZ olarak çıkıyordu (canvas zemini beyaz — JPEG saydamlık
 * taşımıyor). "Profil fotoğrafında beyazlık oluşuyor" şikâyetinin kaynağı buydu.
 *
 * Taşma payı, ölçeklenmiş görselin çerçeveyi aşan kısmının yarısı: görsel tam
 * kaplıyorken (zoom=1, kare fotoğraf) pay sıfırdır ve fotoğraf hiç oynamaz — doğrusu
 * da bu, oynayacak yer yok.
 */
function sinirla(offset, dogal, zoom) {
  if (!dogal) return offset
  const olcek = tabanOlcek(dogal) * zoom
  const payX = Math.max(0, (dogal.w * olcek - ONIZLEME) / 2)
  const payY = Math.max(0, (dogal.h * olcek - ONIZLEME) / 2)
  return {
    x: Math.min(payX, Math.max(-payX, offset.x)),
    y: Math.min(payY, Math.max(-payY, offset.y)),
  }
}

export function AvatarPicker({ open, onClose, onUploaded, currentUrl }) {
  const [file, setFile] = useState(null)
  const [imageUrl, setImageUrl] = useState(null)
  const [zoom, setZoom] = useState(1)
  const [offset, setOffset] = useState({ x: 0, y: 0 })
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)

  /*
    Görselin DOĞAL ölçüsü state'te, ref'te değil: önizlemenin boyutu buradan hesaplanıyor
    ve hesap değişince yeniden çizim gerekiyor. `imgRef.current.naturalWidth` okumak
    render sırasında güvenilir değil — görsel henüz yüklenmemiş olabilir ve ilk kare
    sıfırla hesaplanırdı.
  */
  const [dogal, setDogal] = useState(null)

  const imgRef = useRef(null)
  const dragRef = useRef(null)

  // Object URL'yi serbest bırak: aksi halde her seçimde bellek sızar.
  useEffect(() => {
    if (!file) {
      setImageUrl(null)
      return
    }
    const url = URL.createObjectURL(file)
    setImageUrl(url)
    setZoom(1)
    setOffset({ x: 0, y: 0 })
    setDogal(null) // Yeni dosya: eski ölçüyle bir kare çizmesin.
    return () => URL.revokeObjectURL(url)
  }, [file])

  function pick(event) {
    const selected = event.target.files?.[0]
    setError(null)

    if (!selected) return

    if (!selected.type.startsWith('image/')) {
      setError({ message: 'Yalnızca görsel dosyası seçebilirsin.' })
      return
    }
    if (selected.size > MAX_INPUT_BYTES) {
      setError({ message: 'Fotoğraf 8 MB’tan küçük olmalı.' })
      return
    }

    setFile(selected)
  }

  function startDrag(event) {
    const point = event.touches?.[0] ?? event
    dragRef.current = { x: point.clientX - offset.x, y: point.clientY - offset.y }
  }

  function moveDrag(event) {
    if (!dragRef.current) return
    const point = event.touches?.[0] ?? event
    setOffset(
      sinirla(
        { x: point.clientX - dragRef.current.x, y: point.clientY - dragRef.current.y },
        dogal,
        zoom,
      ),
    )
  }

  function endDrag() {
    dragRef.current = null
  }

  /**
   * Ekranda görünen çerçeveyi birebir canvas'a çizip JPEG üretir.
   *
   * "Birebir" iddiası ARTIK DOĞRU. Eskiden değildi: canvas burada `cover` oranıyla
   * çiziyordu ama önizleme `width: 256` + otomatik yükseklikle, yani `contain` gibi
   * davranıyordu. 800×400'lük bir fotoğrafta tarayıcıda ölçüldü — önizleme çerçevede
   * 256×128 yer kaplıyor (üstte ve altta 128px boşluk), canvas ise aynı fotoğrafı
   * çerçeveyi tam kaplayacak şekilde İKİ KATI ölçekte çiziyordu. Kullanıcı gördüğü
   * yere göre konumlandırıyor, kaydedilen kare bambaşka bir yerden kırpılıyordu.
   *
   * Artık iki taraf da `tabanOlcek` fonksiyonunu kullanıyor; ölçek tek yerde tanımlı.
   */
  async function crop() {
    const img = imgRef.current
    if (!img || !dogal) return null

    const canvas = document.createElement('canvas')
    canvas.width = OUTPUT_SIZE
    canvas.height = OUTPUT_SIZE
    const ctx = canvas.getContext('2d')

    // Önizleme kutusu ONIZLEME px; canvas OUTPUT_SIZE px. Kullanıcının gördüğü çerçeve
    // ile kaydedilen kare aynı olsun diye tüm dönüşümler bu oranla çarpılır.
    const scale = OUTPUT_SIZE / ONIZLEME
    const olcek = tabanOlcek(dogal) * zoom
    const cizimGen = dogal.w * olcek
    const cizimYuk = dogal.h * olcek

    // Zemin: sürükleme sınırlandığı için normalde hiç görünmemeli. Yine de duruyor —
    // JPEG saydamlık taşımıyor ve bir kenar durumu kalırsa siyah yerine beyaz çıksın.
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, OUTPUT_SIZE, OUTPUT_SIZE)

    // Sınırlama burada BİR KEZ DAHA uygulanıyor: state doğru olsa bile bu fonksiyon
    // kaydedilen kareyi üreten son nokta ve beyaz kenar buradan geçmemeli.
    const g = sinirla(offset, dogal, zoom)

    ctx.drawImage(
      img,
      ((ONIZLEME - cizimGen) / 2 + g.x) * scale,
      ((ONIZLEME - cizimYuk) / 2 + g.y) * scale,
      cizimGen * scale,
      cizimYuk * scale,
    )

    return new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.9))
  }

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      const blob = await crop()
      if (!blob) throw new Error('Görsel işlenemedi.')

      const form = new FormData()
      form.append('avatar', blob, 'avatar.jpg')

      const { api } = await import('../lib/api')
      await api.uploadAvatar(form)

      onUploaded?.()
      setFile(null)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Profil fotoğrafı"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Vazgeç
          </Button>
          <Button loading={busy} disabled={!file} onClick={submit}>
            Kaydet
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {imageUrl ? (
          <>
            <div
              style={{ width: ONIZLEME, height: ONIZLEME }}
              className="relative mx-auto cursor-move select-none overflow-hidden rounded-2xl bg-slate-100"
              onMouseDown={startDrag}
              onMouseMove={moveDrag}
              onMouseUp={endDrag}
              onMouseLeave={endDrag}
              onTouchStart={startDrag}
              onTouchMove={moveDrag}
              onTouchEnd={endDrag}
            >
              {/*
                ÖLÇÜ CANVAS'LA AYNI FORMÜLDEN geliyor: genişlik VE yükseklik açıkça
                veriliyor, ikisi de `tabanOlcek` ile çarpılmış doğal ölçü. Eskiden
                yalnızca `width: 256` vardı ve yükseklik otomatikti — yani görsel
                çerçeveyi kaplamıyor, orantısına göre içine sığıyordu.

                `objectFit: 'cover'` KALDIRILDI: iki boyut da açıkça verildiğinde
                object-fit'in yapacak bir işi yok. Eski kodda duruyordu ve "kaplıyor"
                izlenimi veriyordu ama yükseklik `auto` olduğu için hiçbir etkisi yoktu —
                asıl hatayı gizleyen şey buydu.

                naturalWidth/Height onLoad'da state'e yazılıyor; hem bu ölçü hem de
                sürükleme sınırı oradan besleniyor.
              */}
              <img
                ref={imgRef}
                src={imageUrl}
                alt="Kırpma önizlemesi"
                draggable={false}
                onLoad={(e) =>
                  setDogal({ w: e.currentTarget.naturalWidth, h: e.currentTarget.naturalHeight })
                }
                className="pointer-events-none absolute left-1/2 top-1/2 max-w-none"
                style={{
                  transform: `translate(calc(-50% + ${offset.x}px), calc(-50% + ${offset.y}px)) scale(${zoom})`,
                  width: dogal ? dogal.w * tabanOlcek(dogal) : ONIZLEME,
                  height: dogal ? dogal.h * tabanOlcek(dogal) : undefined,
                }}
              />

              {/*
                DAİRE KILAVUZU — profilde yuvarlak gösterilen alanı işaret eder.

                Öncesinde burada `ring-[64px] ring-white/70` taşıyan bir katman vardı ve
                yorumu "daire maskesi" diyordu. Tarayıcıda bakıldı: HİÇBİR ŞEY
                çizmiyordu. Ring, öğenin dışına çizilir; katman `inset-0` ile kutuyla
                aynı boyutta olduğu için 64px'lik halka tamamen kutunun dışında kalıyor
                ve `overflow-hidden` onu kırpıyordu. Kullanıcı yıllardır olmayan bir
                kılavuza göre konumlandırıyordu.

                Kırpılan alan KARE (512×512 JPEG) — daire yalnızca gösterim. Bu yüzden
                köşeler KARARTILMIYOR: karartmak "buralar kesilecek" derdi, oysa
                kesilmiyor; sohbet ve liste avatarlarında köşeler görünüyor. İnce bir
                çizgi, söz vermeden bilgi veriyor.
              */}
              <div
                className="pointer-events-none absolute inset-0 rounded-full border border-white/70
                           shadow-[0_0_0_1px_rgba(15,23,42,0.15)_inset]"
                aria-hidden="true"
              />
            </div>

            <div>
              <label className="label" htmlFor="avatar-zoom">
                Yakınlaştır
              </label>
              <input
                id="avatar-zoom"
                type="range"
                min="1"
                max="3"
                step="0.05"
                value={zoom}
                /*
                  Yakınlaştırma AZALIRKEN kayma da yeniden sınırlanmalı: pay ölçekle
                  birlikte küçülüyor, eski kayma yeni payın dışında kalabilir ve
                  fotoğraf çerçeveden çıkıp beyaz kenar açardı. Sınır iki yerde birden
                  uygulanıyor — sürüklerken ve yakınlaştırma değişirken — çünkü ikisi
                  de payı bozabilen bağımsız iki eylem.
                */
                onChange={(e) => {
                  const yeni = Number(e.target.value)
                  setZoom(yeni)
                  setOffset((o) => sinirla(o, dogal, yeni))
                }}
                className="h-11 w-full accent-brand-600 lg:h-auto"
              />
              <p className="text-xs text-slate-500">
                Fotoğrafı sürükleyerek konumlandırabilirsin.
              </p>
            </div>

            <Button variant="secondary" className="w-full" onClick={() => setFile(null)}>
              Başka fotoğraf seç
            </Button>
          </>
        ) : (
          <label className="flex cursor-pointer flex-col items-center gap-2 rounded-xl border-2 border-dashed border-slate-300 p-8 text-center transition hover:border-brand-300 hover:bg-brand-50/40">
            {currentUrl ? (
              <img src={currentUrl} alt="Mevcut" className="h-20 w-20 rounded-2xl object-cover" />
            ) : (
              <span className="text-3xl">📷</span>
            )}
            <span className="text-sm font-medium text-slate-700">Fotoğraf seç</span>
            <span className="text-xs text-slate-500">PNG, JPEG veya WebP · en fazla 8 MB</span>
            <input type="file" accept="image/*" className="hidden" onChange={pick} />
          </label>
        )}

        <ErrorBox error={error} />
      </div>
    </Modal>
  )
}
