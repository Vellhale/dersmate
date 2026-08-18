import { useEffect, useState } from 'react'
import { api } from '../lib/api'

/**
 * Profil fotoğrafı. Yoksa baş harflerden bir yer tutucu üretir.
 *
 * Yer tutucu bilinçli olarak RENKLİ ve kişiye özel: kullanıcı kimliğinden türeyen sabit
 * bir renk seçilir, böylece fotoğrafı olmayan kişiler de listede birbirinden ayırt edilir.
 * Tek tip gri bir daire, sohbet ve arama listelerini okunmaz hâle getirirdi.
 */

const COLORS = [
  'bg-brand-600',
  'bg-sky-600',
  'bg-emerald-600',
  'bg-amber-600',
  'bg-rose-600',
  'bg-violet-600',
]

function colorFor(userId = '') {
  let sum = 0
  for (let i = 0; i < userId.length; i++) sum += userId.charCodeAt(i)
  return COLORS[sum % COLORS.length]
}

function initialsOf(name = '') {
  return name
    .split(' ')
    .filter(Boolean)
    .map((w) => w[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()
}

export function Avatar({ userId, name, size = 'md', className = '' }) {
  const [url, setUrl] = useState(null)

  useEffect(() => {
    let cancelled = false
    if (!userId) return

    api.avatarObjectUrl(userId).then((objectUrl) => {
      if (!cancelled) setUrl(objectUrl)
    })

    return () => {
      cancelled = true
      // Object URL BİLEREK serbest bırakılmıyor: önbellek onu paylaşıyor ve burada
      // iptal etmek aynı avatarı gösteren diğer kartları bozardı.
    }
  }, [userId])

  const sizes = {
    sm: 'h-8 w-8 text-xs rounded-lg',
    md: 'h-12 w-12 text-sm rounded-xl',
    lg: 'h-20 w-20 text-2xl rounded-2xl',
  }

  if (url) {
    return (
      <img
        src={url}
        alt={name ?? 'Profil fotoğrafı'}
        className={`${sizes[size]} shrink-0 object-cover ${className}`}
      />
    )
  }

  return (
    <div
      aria-label={name}
      className={`${sizes[size]} ${colorFor(userId)} grid shrink-0 place-items-center font-bold text-white ${className}`}
    >
      {initialsOf(name)}
    </div>
  )
}
