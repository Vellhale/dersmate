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

export function AvatarPicker({ open, onClose, onUploaded, currentUrl }) {
  const [file, setFile] = useState(null)
  const [imageUrl, setImageUrl] = useState(null)
  const [zoom, setZoom] = useState(1)
  const [offset, setOffset] = useState({ x: 0, y: 0 })
  const [error, setError] = useState(null)
  const [busy, setBusy] = useState(false)

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
    setOffset({ x: point.clientX - dragRef.current.x, y: point.clientY - dragRef.current.y })
  }

  function endDrag() {
    dragRef.current = null
  }

  /** Ekranda görünen çerçeveyi birebir canvas'a çizip JPEG üretir. */
  async function crop() {
    const img = imgRef.current
    if (!img) return null

    const canvas = document.createElement('canvas')
    canvas.width = OUTPUT_SIZE
    canvas.height = OUTPUT_SIZE
    const ctx = canvas.getContext('2d')

    // Önizleme kutusu 256px; canvas 512px → ölçek 2. Kullanıcının gördüğü çerçeve
    // ile kaydedilen kare birebir aynı olsun diye tüm dönüşümler bu oranla çarpılır.
    const previewSize = 256
    const scale = OUTPUT_SIZE / previewSize

    const ratio = Math.max(previewSize / img.naturalWidth, previewSize / img.naturalHeight)
    const drawWidth = img.naturalWidth * ratio * zoom
    const drawHeight = img.naturalHeight * ratio * zoom

    ctx.fillStyle = '#ffffff' // JPEG saydamlığı desteklemez; siyah yerine beyaz zemin.
    ctx.fillRect(0, 0, OUTPUT_SIZE, OUTPUT_SIZE)

    ctx.drawImage(
      img,
      ((previewSize - drawWidth) / 2 + offset.x) * scale,
      ((previewSize - drawHeight) / 2 + offset.y) * scale,
      drawWidth * scale,
      drawHeight * scale,
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
              className="relative mx-auto h-64 w-64 cursor-move select-none overflow-hidden rounded-2xl bg-slate-100"
              onMouseDown={startDrag}
              onMouseMove={moveDrag}
              onMouseUp={endDrag}
              onMouseLeave={endDrag}
              onTouchStart={startDrag}
              onTouchMove={moveDrag}
              onTouchEnd={endDrag}
            >
              <img
                ref={imgRef}
                src={imageUrl}
                alt="Kırpma önizlemesi"
                draggable={false}
                className="pointer-events-none absolute left-1/2 top-1/2 max-w-none"
                style={{
                  transform: `translate(calc(-50% + ${offset.x}px), calc(-50% + ${offset.y}px)) scale(${zoom})`,
                  width: 256,
                  objectFit: 'cover',
                }}
              />
              {/* Daire maskesi: profilde yuvarlak gösterilecek alanı önceden gösterir. */}
              <div className="pointer-events-none absolute inset-0 rounded-2xl ring-[64px] ring-white/70" />
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
                onChange={(e) => setZoom(Number(e.target.value))}
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
