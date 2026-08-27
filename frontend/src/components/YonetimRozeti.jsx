import { KalkanIkonu } from './Ikonlar'

/*
  ─── YÖNETİM ROZETİ ──────────────────────────────────────────────────────────

  Ürün sahibi kararı (2026-08-27): forumda ve Keşfet'te platformla ilgili sorular
  sorulacak ve RESMİ CEVABIN HANGİSİ OLDUĞU ayırt edilebilmeli. Sıradan bir
  kullanıcının "ben yöneticiyim" yazmasıyla gerçek yöneticinin cevabı aynı görünürse,
  kimliğe bürünme bu üründeki en ucuz saldırı olur.

  ⚠️ İŞARET SUNUCUDAN GELİR, İSTEMCİ HESAPLAMAZ. Bu bileşen yalnızca çiziyor; kararı
  veren alan sunucudaki `isStaff` (ForumAuthorDto, OfferCardDto.TutorIsStaff,
  UniversityPeerDto.IsStaff, MatchSuggestionDto.IsStaff). Rol tarayıcıda türetilseydi,
  kullanıcı kendi tarafında değiştirip sahte bir yönetim rozeti üretebilirdi — tam da
  engellenmek istenen şey. Bu bileşene bir "role" prop'u EKLEME; kim yönetimdir sorusu
  istemcide cevaplanmamalı.

  ⚠️ PROFİL UCU ROLÜ HÂLÂ SIZDIRMIYOR. Bayrak yalnızca yazarın/ilan sahibinin
  göründüğü listelerde var. Bunu profil DTO'suna da eklemek cazip görünüyor ama
  gereksiz: rozetin işi resmi cevabı ayırt etmek, kişi listelemek değil.

  METİN "YÖNETİM", "ADMİN" DEĞİL: bayrak Admin VE Moderator rollerini birlikte
  kapsıyor (sunucuda `Role is UserRole.Admin or UserRole.Moderator`). "Admin" yazmak
  moderatörler için yanlış bir unvan iddiası olurdu.

  Rozet DOLU marka zemini alıyor çünkü işi tam olarak dikkat çekmek — sessiz bir
  rozet, resmi cevabı sıradan cevaptan ayırma işini yapmaz. brand-600 üstüne beyaz
  metin, paletteki ölçülmüş AA çifti (bkz. tailwind.config.js notu); marka rengi 500'de
  duruyor ve orada beyaz metin AA eşiğini geçmiyor, bu yüzden zemin 600.
*/
export function YonetimRozeti({ kucuk = false, className = '' }) {
  return (
    <span
      className={`inline-flex shrink-0 items-center gap-1 rounded-full bg-brand-600 font-semibold
                  text-white ${kucuk ? 'px-1.5 py-0.5 text-[10px]' : 'px-2 py-0.5 text-[11px]'}
                  ${className}`}
      title="dersmate ekibinden — resmi hesap"
    >
      <KalkanIkonu className={kucuk ? 'h-3 w-3' : 'h-3.5 w-3.5'} />
      Yönetim
    </span>
  )
}
