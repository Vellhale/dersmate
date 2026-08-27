import { useCallback, useEffect, useRef, useState } from 'react'
import { useAuth } from '../state/AuthContext'
import { Avatar } from '../components/Avatar'
import { CamKart } from '../components/SayfaZemini'
import { SeviyeRozeti } from '../components/SeviyeRozeti'
import { YonetimRozeti } from '../components/YonetimRozeti'
import { Button, ErrorBox, Field, Loading, Modal, Notice, Pagination } from '../components/ui'
import { api } from '../lib/api'
import {
  AlevIkonu,
  ArtanIkonu,
  BayrakIkonu,
  BilgiIkonu,
  KalkanIkonu,
  MesajIkonu,
  OyOkuIkonu,
  SaatIkonu,
  UyariIkonu,
} from '../components/Ikonlar'

/*
  ══════════════════════════════════════════════════════════════════════════════
  TOPLULUK — akran forumu.

  Arayüz 2026-08-25'te sabit veriyle yazıldı, 2026-08-27'de sunucuya bağlandı.
  Yerel çalışan her şey (oy, sıralama, filtre, gönderi ve yorum yazma) yerinde
  kaldı; değişen tek şey verinin nereden okunup nereye yazıldığı.

  ─── SUNUCU NEYİ YAPIYOR, İSTEMCİ NEYİ ────────────────────────────────────────
  SIRALAMA, TARİH PENCERESİ VE ETİKET FİLTRESİ SUNUCUDA. İstemcide yapılsaydı
  sayfalama anlamsız olurdu: ikinci sayfayı verebilmek için tüm gönderileri
  indirmek gerekirdi. Bu yüzden her filtre değişikliği yeni bir istek.

  İSTEMCİDE KALAN TEK HESAP: oyun optimistik gösterimi. Kullanıcı oka bastığı anda
  sayı değişiyor, sunucu yanıtı gelince gerçek sayaçla düzeltiliyor, hata gelirse
  eski hâline dönüyor. Oy vermek 200 ms bekleyen bir işlem gibi hissedilmemeli.

  ⚠️ SIRALAMA OY VERİNCE YENİLENMİYOR — bilerek. "En Çok Oy Alanlar" listesinde bir
  gönderiye oy vermek, o kartı parmağının altından kaydırırdı. Görünen sayı hemen
  değişiyor (geri bildirim orada), yalnızca SIRA sabit kalıyor; liste ancak filtre
  değişince ya da yeni gönderi paylaşılınca yeniden çekiliyor.

  ─── NEDEN REDDIT DÜZENİ ──────────────────────────────────────────────────────
  İstenen referans /liseliler tarzı bir akış. Oradan alınan üç şey var ve üçü de
  kararla alındı:

    1. SOL OY RAYI. Oy, gönderinin İÇERİĞİNDEN önce gelir; forumun sıralaması buna
       bağlı olduğu için kullanıcı "bu gönderi topluluk için ne değerde" bilgisini
       başlığı okumadan görüyor. Alt satıra konsaydı, taranan bir listede oy
       yorumların yanında bir sayı daha olurdu.
    2. BAŞLIK + ÖZET. Gönderi kartı içeriği bitirmez, açmaya davet eder — akış
       taranabilir kalmalı. Özet üç satırda kesiliyor (line-clamp-3).
    3. ETİKET (flair). Bir öğrenci forumunda "soru" ile "motivasyon" bambaşka iki
       okuma kipi; etiket, gönderiyi açmadan hangisine girdiğini söylüyor.

  ALINMAYAN ŞEY: Reddit'in yoğunluğu. Orada bir ekrana 12 gönderi sığar; burada 5.
  Bu ürünün geri kalanı (Keşfet, Derslerim) ferah kartlarla çalışıyor ve forum tek
  başına sıkışık bir liste olsaydı uygulamanın içinde başka bir uygulama gibi dururdu.

  ─── MODERASYON ARAYÜZÜ İKİNCİL DEĞİL, DÜZENİN PARÇASI ────────────────────────
  Aktif bir öğrenci forumunda spam, argo, izinsiz PDF ve trollemenin OLUP OLMAYACAĞI
  sorusu yok; ne zaman olacağı sorusu var. Bu yüzden önlemler sonradan eklenen bir
  panel değil, akışın kendisine yerleştirildi:

    • Her gönderide ve her yorumda "Şikayet et" — tek tık uzakta, ama sessiz.
    • Şikayet formu SEBEP SORUYOR. Tek düğmelik şikayet, moderatöre "biri bundan
      hoşlanmadı"dan başka bir şey söylemez; sebep, gelen yığını sıraya sokan şeydir.
    • Eşiği geçen gönderi AKIŞTA KAPALI gelir (sunucuda ForumRules.AutoReviewThreshold
      = 3). İçerik SİLİNMİYOR, perdeleniyor — "yine de göster" duruyor. Sessiz silme,
      moderasyonu görünmez ve tartışılamaz yapar.
    • Kurallar ve sınırlar sağ sütunda YAZILI ve hepsinin sunucuda karşılığı var
      (ONLEMLER listesindeki her maddenin yanında hangi kural olduğu yazıyor).
    • Gönderi kutusu dosya yükleme SUNMUYOR. Telif ihlalinin bu üründeki en olası
      yolu izinsiz PDF paylaşımı ve en ucuz önlem, o yolu hiç açmamak.
  ══════════════════════════════════════════════════════════════════════════════
*/

/* ─── SUNUCU SÖZLEŞMESİ ────────────────────────────────────────────────────────

   Arayüz Türkçe anahtarlarla çalışıyor ('yeni', 'stres'), sunucu enum adlarıyla
   ('Newest', 'ExamStress'). Çeviri TEK YERDE, burada: iki tarafın da kendi doğal
   sözlüğünü kullanabilmesi için. Anahtarları sunucununkilerle değiştirmek arayüzün
   geri kalanını (etiket renkleri, adlar, testler) İngilizceye çevirmek demekti.   */

const SIRA_ENUM = { yeni: 'Newest', oy: 'Top', tartismali: 'Controversial' }
const ZAMAN_ENUM = { hepsi: 'All', gun: 'Day', hafta: 'Week', ay: 'Month' }
const ETIKET_ENUM = {
  stres: 'ExamStress',
  soru: 'Question',
  kaynak: 'Resource',
  program: 'StudyPlan',
  motivasyon: 'Motivation',
  tercih: 'Preference',
}
/** Ters yön: sunucudan gelen etiketi arayüz anahtarına çevirir. */
const ETIKET_ANAHTARI = Object.fromEntries(
  Object.entries(ETIKET_ENUM).map(([anahtar, enumAdi]) => [enumAdi, anahtar]),
)

/*
  ŞİKAYET SEBEBİ → SUNUCU ENUM'U.

  Dördü (Spam, Copyright, PersonalInfo, OffTopic) forum için ReportReason'a EKLENDİ.
  Öncesinde hepsi `Other`'a düşüyordu ve moderasyon kuyruğundaki sebep sütunu forum
  şikayetleri için hiçbir şey söylemiyordu — tek bir "Diğer" yığını sıraya sokulamaz.
*/
const SEBEP_ENUM = {
  spam: 'Spam',
  dil: 'Abuse',
  telif: 'Copyright',
  kisisel: 'PersonalInfo',
  konudisi: 'OffTopic',
  diger: 'Other',
}

/*
  AÇIKLAMA ALT SINIRI — SUNUCUYLA AYNI SAYI (CreateReportHandler.MinDescriptionLength).

  İstemcide daha gevşek bir sınır, kullanıcıya 12 karakter yazdırıp gönderdikten
  sonra 400 gösterirdi; kontrolün istemcide olmasının tek amacı o gidiş gelişi
  önlemek.

  ⚠️ AÇIKLAMA HER SEBEPTE ZORUNLU — tasarımın ilk hâlinde yalnızca "Diğer" için
  zorunluydu. İki sebeple değişti: (a) sunucu ayrım yapmıyor, (b) yazılı bir cümle
  istemek brigading'i pahalılaştırıyor. Üç şikayet gönderiyi perdeliyor; tek tıkla
  şikayet, o eşiği örgütlü bir susturma aracına çevirirdi.
*/
const EN_AZ_ACIKLAMA = 15

/* ─── SIRALAMA ─────────────────────────────────────────────────────────────── */

const SIRALAMALAR = [
  { key: 'yeni', label: 'En Yeniler', Ikon: SaatIkonu, aciklama: 'Son paylaşılanlar önce.' },
  {
    key: 'oy',
    label: 'En Çok Oy Alanlar',
    Ikon: ArtanIkonu,
    aciklama: 'Topluluğun en çok işe yarar bulduğu gönderiler.',
  },
  {
    key: 'tartismali',
    label: 'Tartışmalı',
    Ikon: AlevIkonu,
    aciklama: 'Oyların ikiye bölündüğü, cevabı net olmayan başlıklar.',
  },
]

/*
  TARİH FİLTRESİ — sıralamadan AYRI bir eksen.

  Sıralama "hangisi önce gelsin", tarih filtresi "hangileri hiç görünmesin" diyor.
  İkisini tek bir listede birleştirmek (Reddit'in eski "top of the week" kalıbı gibi)
  seçenek sayısını 3'ten 12'ye çıkarırdı ve kullanıcı "En Yeniler / Bu ay"ın ne demek
  olduğunu tahmin etmek zorunda kalırdı. Ayrı duruyorlar, birlikte uygulanıyorlar.

  ⚠️ Filtre HER SIRALAMADA açık. Reddit tarih seçicisini yalnızca "top"/"controversial"
  için gösteriyor; burada gizlenmedi çünkü ortadan kaybolan bir denetim, kullanıcının
  "az önce buradaydı" diye aradığı bir şeye dönüşür. "En Yeniler + Bugün" da anlamlı
  bir soru: bugün ne konuşuldu?

  Pencere SUNUCUDA uygulanıyor (ForumRange); buradaki liste yalnızca sunum.
*/
const ZAMAN_ARALIKLARI = [
  { key: 'hepsi', label: 'Tüm zamanlar' },
  { key: 'gun', label: 'Bugün' },
  { key: 'hafta', label: 'Bu hafta' },
  { key: 'ay', label: 'Bu ay' },
]

const ETIKETLER = [
  { key: 'hepsi', label: 'Tümü' },
  { key: 'stres', label: 'Sınav Stresi' },
  { key: 'soru', label: 'Soru Sor' },
  { key: 'kaynak', label: 'Kaynak' },
  { key: 'program', label: 'Ders Programı' },
  { key: 'motivasyon', label: 'Motivasyon' },
  { key: 'tercih', label: 'Tercih' },
]

/*
  Etiket renkleri. Hepsi 100/700-800 çiftleri — ui.jsx'teki Badge tonlarıyla aynı
  aile, yani forum kendi renk dilini kurmuyor, var olanı kullanıyor. Çiftler AA
  eşiğini geçiyor (en düşüğü violet-700/violet-100 ≈ 6.6:1).

  Marka mavisi SORU etiketine verildi: bu üründe soru sormak ana eylem ve marka rengi
  ana eylemi işaretliyor. Diğerleri marka dışı tonlar — altısı da mavi olsaydı etiket
  bir ayrım aracı olmaktan çıkardı.

  indigo BİLEREK YOK: e2e/marka.spec.js eski indigo tonlarını arayüzde arıyor.
*/
const ETIKET_TONU = {
  stres: 'bg-amber-100 text-amber-800',
  soru: 'bg-brand-100 text-brand-700',
  kaynak: 'bg-emerald-100 text-emerald-700',
  program: 'bg-violet-100 text-violet-700',
  motivasyon: 'bg-rose-100 text-rose-700',
  tercih: 'bg-sky-100 text-sky-800',
}

const ETIKET_ADI = Object.fromEntries(ETIKETLER.map((e) => [e.key, e.label]))

/*
  ŞİKAYET SEBEPLERİ — beş tanesi bu ürünün gerçek risklerine birebir karşılık geliyor,
  altıncısı ("Diğer") açık uç.

  Sıra rastgele değil, BEKLENEN SIKLIĞA göre: spam ve dil ihlali her forumda ilk
  ikidir; kullanıcı listenin başında aradığını bulursa formu okumadan geçer.
*/
const SIKAYET_SEBEPLERI = [
  { key: 'spam', baslik: 'Spam veya reklam', aciklama: 'Satış, yönlendirme bağlantısı, tekrar eden gönderi.' },
  { key: 'dil', baslik: 'Hakaret, argo veya taciz', aciklama: 'Kişiye yönelik saldırı ya da aşağılayıcı dil.' },
  {
    key: 'telif',
    baslik: 'Telif ihlali',
    aciklama: 'İzinsiz kitap, PDF, deneme ya da video paylaşımı.',
  },
  {
    key: 'kisisel',
    baslik: 'Kişisel bilgi paylaşımı',
    aciklama: 'Telefon, adres, sosyal hesap — kendisinin ya da başkasının.',
  },
  { key: 'konudisi', baslik: 'Konu dışı veya trolleme', aciklama: 'Tartışmayı bilerek bozan içerik.' },
  { key: 'diger', baslik: 'Diğer', aciklama: 'Yukarıdakilere girmiyorsa kısaca anlat.' },
]

/* Kurallar kullanıcıya GÖRÜNÜR yerde duruyor: yazılmamış kural, uygulandığında keyfî
   görünür ve moderasyona duyulan güveni bitirir. */
/*
  ⚠️ "AYNI SORUYU TEKRAR AÇMA" KURALI KALDIRILDI (2026-08-25, ürün sahibi kararı).
  Tekrar başlık açmakta kısıtlama YOK ve bu bilinçli: bir öğrenci aynı soruyu ikinci kez
  soruyorsa çoğu zaman ilk cevabı anlamamıştır. Onu "zaten sorulmuştu" diye geri
  çevirmek, forumun var oluş sebebine ters. Kuralı geri eklemeden önce bu notu oku.
*/
const KURALLAR = [
  'Argo, hakaret ve kişisel saldırı yok. Fikre karşı çık, kişiye değil.',
  'Telif hakkı olan kitap, PDF ve denemeleri paylaşma — kaynağın adını yaz, dosyasını değil.',
  'Reklam, satış ve yönlendirme bağlantısı yasak.',
  'Kendinin ya da başkasının telefon, adres ve sosyal hesap bilgisini paylaşma.',
]

/*
  Arayüzde görünen sınırlar. Bir kısmı kuralları ihlal etmeyi ZORLAŞTIRIYOR (dosya
  yükleme yok), bir kısmı ihlalin MALİYETİNİ düşürüyor (otomatik incelemeye alma).

  ⚠️ DÖRDÜNÜN DE SUNUCUDA KARŞILIĞI VAR. Bu liste bir zamanlar kodda karşılığı olmayan
  vaatler taşıyordu ve canlıya çıkış denetiminde bulundu: kullanıcıya söz veren bir
  arayüz metni, sözü tutan bir kural olmadan yazılamaz. Karşılıkları:
    • dosya yükleme yok  → formda alan hiç yok (bkz. GonderiModali)
    • günde 3 gönderi    → ForumRules.NewAccountDailyPostLimit / NewAccountDays
    • bağlantı eşiği     → ForumRules.LinkMinLevel (CreateForumPostHandler.BaglantiKapisi)
    • otomatik inceleme  → ForumRules.AutoReviewThreshold (CreateReportHandler)
*/
const ONLEMLER = [
  { baslik: 'Yalnızca metin', metin: 'Dosya yükleme kapalı; izinsiz PDF paylaşımının yolu hiç açılmıyor.' },
  { baslik: 'Yeni hesap sınırı', metin: 'İlk hafta günde en fazla 3 gönderi — spam duvarı.' },
  { baslik: 'Bağlantı eşiği', metin: 'Dışarıya bağlantı paylaşımı 3. seviyeden itibaren açılıyor.' },
  { baslik: 'Otomatik inceleme', metin: 'Kısa sürede 3 şikayet alan gönderi akışta kapatılır.' },
]

/* ─── YARDIMCILAR ──────────────────────────────────────────────────────────── */

/**
 * Sunucudan gelen UTC damgasını milisaniyeye çevirir.
 *
 * ⚠️ ZAMAN DİLİMİ EKİ YOKSA 'Z' EKLENİYOR. .NET, DateTime'ı Kind=Utc iken sonunda
 * 'Z' ile yazıyor; Kind=Unspecified iken YAZMIYOR ve o durumda tarayıcı metni YEREL
 * saat sanar. Türkiye'de bu üç saatlik bir kayma demek: üç saat önce yazılmış bir
 * gönderi "şimdi" görünür, bir dakika önce yazılan ise gelecekte kalır. Sütun
 * timestamptz olduğu için EF Utc döndürüyor, yani bugün ek gereksiz — ama tek bir
 * DTO'nun Kind'i değiştiğinde hata SESSİZ olur, bu yüzden koruma burada duruyor.
 */
function damgayaCevir(metin) {
  if (!metin) return null
  const tamDamga = /(?:[zZ]|[+-]\d{2}:?\d{2})$/.test(metin) ? metin : `${metin}Z`
  const ms = Date.parse(tamDamga)
  return Number.isNaN(ms) ? null : ms
}

/** Damganın kaç dakika önce olduğunu verir; okunamayan damga 0 sayılıyor ("şimdi"). */
function yasDakika(metin) {
  const ms = damgayaCevir(metin)
  if (ms === null) return 0
  // Negatife düşebilir: sunucu saati istemciden birkaç saniye ileriyse. "-1 dk" yerine
  // "şimdi" göstermek doğru, çünkü fark saat farkı değil senkron gürültüsü.
  return Math.max(0, Math.round((Date.now() - ms) / 60000))
}

/** "22 dk" / "3 sa" / "2 g". Forumda mutlak tarih işe yaramıyor: okuyanın sorduğu şey
    "ne zaman yazıldı" değil, "hâlâ taze mi". */
function zamanKisalt(dakika) {
  // Az önce yazılan gönderi/yorum "0 dk" gösteriyordu; sayı doğruydu ama okunuşu
  // bozuktu — sıfır birimli bir süre, süre değil.
  if (dakika < 1) return 'şimdi'
  if (dakika < 60) return `${dakika} dk`
  const saat = Math.floor(dakika / 60)
  if (saat < 24) return `${saat} sa`
  return `${Math.floor(saat / 24)} g`
}

/**
 * OY UYGULAMA — sunucudaki üç durumun istemci aynası (VoteForumContentHandler).
 *
 *   oy yok      → oy ekle
 *   aynı yön    → GERİ AL (sunucu satırı siler, sayaç düşer)
 *   ters yön    → çevir (bir taraftan düş, diğerine ekle)
 *
 * Tek fonksiyon çünkü üç durumun sayaç etkisi birbirine bağlı; ayrı ayrı yazılsaydı
 * biri düzeltilirken diğeri unutulur ve optimistik sayı sunucununkinden kalıcı olarak
 * ayrışırdı. Yine de bu yalnızca TAHMİN: yanıt gelince sunucunun sayaçları yazılıyor.
 */
function oyUygula(icerik, yon) {
  const onceki = icerik.myVote ?? 0
  const yeni = onceki === yon ? 0 : yon

  let arti = icerik.upvoteCount
  let eksi = icerik.downvoteCount

  if (onceki === 1) arti -= 1
  else if (onceki === -1) eksi -= 1

  if (yeni === 1) arti += 1
  else if (yeni === -1) eksi += 1

  return { ...icerik, upvoteCount: arti, downvoteCount: eksi, myVote: yeni }
}

function EtiketPili({ etiket, className = '' }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold
                  ${ETIKET_TONU[etiket] ?? 'bg-slate-100 text-slate-700'} ${className}`}
    >
      {ETIKET_ADI[etiket] ?? etiket}
    </span>
  )
}

/**
 * Yazar satırı: avatar + ad + (yönetim rozeti) + seviye.
 *
 * Gönderide ve yorumda AYNI bileşen: yazarın nasıl gösterildiği iki yerde ayrı
 * yazılsaydı, rozet birine eklenip diğerine eklenmeden kalabilirdi.
 */
function YazarSatiri({ yazar, boyut = 'xs' }) {
  return (
    <span className="flex min-w-0 items-center gap-1.5">
      <Avatar userId={yazar?.userId} name={yazar?.displayName} size={boyut} />
      <span className="truncate text-xs font-medium text-slate-700">
        {yazar?.displayName ?? 'Kullanıcı'}
      </span>
      {yazar?.isStaff && <YonetimRozeti kucuk={boyut === 'xs'} />}
      {/* Seviye yalnızca `level` alanıyla besleniyor; rozet ilerleme verisi olmadan
          ilerleme iddia etmiyor (bkz. SeviyeRozeti). Puan başkasının verisi ve forum
          DTO'su onu göndermiyor. */}
      <SeviyeRozeti kaynak={{ level: yazar?.level }} boyut="sm" ton="acik" className="shrink-0" />
    </span>
  )
}

/* ─── SAYFA ────────────────────────────────────────────────────────────────── */

export default function Topluluk() {
  const { session } = useAuth()

  const [sira, setSira] = useState('yeni')
  const [zaman, setZaman] = useState('hepsi')
  const [etiket, setEtiket] = useState('hepsi')
  const [sayfa, setSayfa] = useState(1)

  const [akis, setAkis] = useState(null)
  const [yukleniyor, setYukleniyor] = useState(true)
  const [hata, setHata] = useState(null)

  const [acikYorum, setAcikYorum] = useState(null)
  const [acilanGizli, setAcilanGizli] = useState([])
  const [sikayetHedefi, setSikayetHedefi] = useState(null)
  const [bildirim, setBildirim] = useState(null)
  const [yaziyor, setYaziyor] = useState(false)

  /*
    YORUMLAR GÖNDERİ AÇILINCA ÇEKİLİYOR, akışla birlikte değil.

    Akışta 20 gönderi var ve hepsinin yorumlarını önden indirmek, kullanıcının
    açmayacağı 20 istek demek. Açılan gönderinin yorumları burada birikiyor
    ({ [postId]: { yukleniyor, hata, liste } }) ve gönderi kapanıp yeniden açılınca
    yeniden istenmiyor — yorum yazınca liste yerinde güncelleniyor.
  */
  const [yorumlar, setYorumlar] = useState({})

  /*
    UÇUŞTAKİ OYLAR. Aynı içeriğe ikinci tık, yanıt gelmeden YOK SAYILIYOR.

    Kuyruğa alınsaydı iki isteğin sırası garanti olmazdı: ikinci yanıt önce dönerse
    ekrandaki sayı sunucudakinden kalıcı olarak ayrışırdı. Ref, state değil — bu
    bilginin ekranda karşılığı yok ve her tıkta yeniden çizim yapmaya değmez.
  */
  const oyKilidi = useRef(new Set())

  /* Yeni gönderi paylaşınca akışı yeniden çekmek için: sayaç değişince efekt koşuyor. */
  const [yenilemeSayaci, setYenilemeSayaci] = useState(0)

  useEffect(() => {
    /*
      ESKİ İSTEĞİ İPTAL ET. Filtreler hızlı değiştirildiğinde (üç etikete arka arkaya
      basmak gibi) yanıtların GELİŞ SIRASI garanti değil: iptal olmasaydı önce
      gönderilen isteğin geç dönen yanıtı, sonra seçilen filtrenin sonucunu ezerdi ve
      ekranda seçili olmayan bir filtrenin listesi kalırdı.
    */
    const kontrol = new AbortController()
    let iptalEdildi = false

    setYukleniyor(true)
    setHata(null)

    api
      .forumFeed(
        {
          sort: SIRA_ENUM[sira],
          range: ZAMAN_ENUM[zaman],
          tag: etiket === 'hepsi' ? null : ETIKET_ENUM[etiket],
          page: sayfa,
        },
        kontrol.signal,
      )
      .then((sonuc) => {
        if (iptalEdildi) return
        setAkis(sonuc)
        setYukleniyor(false)
      })
      .catch((err) => {
        // AbortError bir hata değil, bizim kararımız: kullanıcıya gösterilmemeli.
        if (iptalEdildi || err.name === 'AbortError') return
        setHata(err)
        setYukleniyor(false)
      })

    return () => {
      iptalEdildi = true
      kontrol.abort()
    }
  }, [sira, zaman, etiket, sayfa, yenilemeSayaci])

  const yenile = useCallback(() => setYenilemeSayaci((n) => n + 1), [])

  /* Filtre değişince ilk sayfaya dön: 4. sayfadayken etiket değiştirmek, çoğu zaman
     sonucu olmayan bir sayfaya düşürürdü ve kullanıcı listeyi boş sanırdı. */
  const filtreDegistir = (uygula) => {
    uygula()
    setSayfa(1)
    setAcikYorum(null)
  }

  /* ─── OY ───────────────────────────────────────────────────────────────── */

  const gonderiOyla = async (postId, yon) => {
    if (oyKilidi.current.has(postId)) return
    oyKilidi.current.add(postId)

    /*
      Geri alma için tıklama ÖNCESİ hâli. Snapshot setState'in DIŞINDA alınıyor:
      güncelleyici fonksiyonun içinde dış bir değişkene yazmak onu saf olmaktan
      çıkarır ve React güncelleyiciyi (StrictMode'da olduğu gibi) iki kez
      çağırdığında hangi değerin yakalandığı belirsizleşir.
    */
    const oncekiHal = akis?.items.find((g) => g.postId === postId) ?? null

    setAkis((mevcut) =>
      mevcut
        ? {
            ...mevcut,
            items: mevcut.items.map((g) => (g.postId === postId ? oyUygula(g, yon) : g)),
          }
        : mevcut,
    )

    try {
      const sonuc = await api.voteForumPost(postId, yon)
      // Sunucunun sayaçları YAZILIYOR: iki kişi aynı anda oy verdiyse optimistik
      // tahmin eksik kalır ve yalnızca kendi oyumu sayardı.
      setAkis((mevcut) =>
        mevcut
          ? {
              ...mevcut,
              items: mevcut.items.map((g) => (g.postId === postId ? { ...g, ...sonuc } : g)),
            }
          : mevcut,
      )
    } catch (err) {
      if (oncekiHal) {
        setAkis((mevcut) =>
          mevcut
            ? { ...mevcut, items: mevcut.items.map((g) => (g.postId === postId ? oncekiHal : g)) }
            : mevcut,
        )
      }
      setHata(err)
    } finally {
      oyKilidi.current.delete(postId)
    }
  }

  const yorumOyla = async (postId, commentId, yon) => {
    if (oyKilidi.current.has(commentId)) return
    oyKilidi.current.add(commentId)

    // Snapshot setState'in dışında (bkz. gonderiOyla'daki not).
    const oncekiHal = yorumlar[postId]?.liste?.find((y) => y.commentId === commentId) ?? null

    const listeyiDegistir = (donustur) =>
      setYorumlar((mevcut) => {
        const durum = mevcut[postId]
        if (!durum?.liste) return mevcut
        return { ...mevcut, [postId]: { ...durum, liste: durum.liste.map(donustur) } }
      })

    listeyiDegistir((y) => (y.commentId === commentId ? oyUygula(y, yon) : y))

    try {
      const sonuc = await api.voteForumComment(commentId, yon)
      listeyiDegistir((y) => (y.commentId === commentId ? { ...y, ...sonuc } : y))
    } catch (err) {
      if (oncekiHal) listeyiDegistir((y) => (y.commentId === commentId ? oncekiHal : y))
      setHata(err)
    } finally {
      oyKilidi.current.delete(commentId)
    }
  }

  /* ─── YORUMLAR ─────────────────────────────────────────────────────────── */

  const yorumlariAc = (postId) => {
    if (acikYorum === postId) {
      setAcikYorum(null)
      return
    }

    setAcikYorum(postId)
    // Zaten çekildiyse tekrar isteme: kapat-aç, ağ isteği değil bir görünürlük kararı.
    if (yorumlar[postId]?.liste) return

    setYorumlar((m) => ({ ...m, [postId]: { yukleniyor: true, hata: null, liste: null } }))
    api
      .forumComments(postId)
      .then((liste) =>
        setYorumlar((m) => ({ ...m, [postId]: { yukleniyor: false, hata: null, liste } })),
      )
      .catch((err) =>
        setYorumlar((m) => ({ ...m, [postId]: { yukleniyor: false, hata: err, liste: null } })),
      )
  }

  const yorumEkle = async (postId, metin) => {
    await api.createForumComment(postId, metin)

    /*
      YAZILAN YORUM SUNUCUDAN YENİDEN OKUNUYOR, elle listeye eklenmiyor.

      POST yalnızca id döndürüyor; yazarın adı, seviyesi ve yönetim işareti orada yok.
      Elle kurulsaydı bu üç alan istemcide TAHMİN edilmiş olurdu — özellikle yönetim
      rozeti, istemcide üretilmemesi gereken tam olarak o bilgi.
    */
    const liste = await api.forumComments(postId)
    setYorumlar((m) => ({ ...m, [postId]: { yukleniyor: false, hata: null, liste } }))

    /*
      Kartın yorum sayısı GÖRÜNEN listeyle eşitleniyor, +1 ile artırılmıyor.

      Sunucudaki CommentCount kaldırılmış yorumları da sayıyor; liste yalnızca
      görünenleri getiriyor. "5 yorum" yazan bir kartı açınca dört yorum görmek,
      kullanıcıya bir şeyin yüklenmediğini düşündürür. Bir sonraki akış çekilişinde
      sunucunun sayısı geri geliyor — bu düzeltme yalnızca açık duran kart için.
    */
    setAkis((mevcut) =>
      mevcut
        ? {
            ...mevcut,
            items: mevcut.items.map((g) =>
              g.postId === postId ? { ...g, commentCount: liste.length } : g,
            ),
          }
        : mevcut,
    )
  }

  /* ─── GÖNDERİ ──────────────────────────────────────────────────────────── */

  const gonderiEkle = async ({ baslik, etiket: yeniEtiket, ozet }) => {
    await api.createForumPost(ETIKET_ENUM[yeniEtiket], baslik, ozet)

    setYaziyor(false)
    /* Yazdığı şeyi görebilsin: filtreler onu gizliyor olabilir, o yüzden akış
       varsayılana dönüyor. Sessizce "kayboldu" görünen bir gönderi, kullanıcıya
       paylaşımın başarısız olduğunu düşündürür. */
    setSira('yeni')
    setZaman('hepsi')
    setEtiket('hepsi')
    setSayfa(1)
    setAcikYorum(null)
    yenile()
    setBildirim('Gönderin paylaşıldı.')
  }

  /* ─── ŞİKAYET ──────────────────────────────────────────────────────────── */

  const sikayetGonder = async (sebepAnahtari, aciklama) => {
    const hedef = sikayetHedefi
    const sebep = SEBEP_ENUM[sebepAnahtari]

    if (hedef.tur === 'Yorum') await api.reportForumComment(hedef.id, sebep, aciklama)
    else await api.reportForumPost(hedef.id, sebep, aciklama)

    setSikayetHedefi(null)
    setBildirim('Şikayetin iletildi. Moderasyon ekibi inceleyip sonucunu değerlendirecek.')

    /*
      AKIŞ YENİLENİYOR: bu şikayet eşiği (3) geçmiş olabilir ve gönderi artık
      perdeli. Yenilemeseydik, kullanıcı az önce bildirdiği içeriği hiçbir şey
      olmamış gibi görmeye devam ederdi.
    */
    yenile()
  }

  const seciliSiralama = SIRALAMALAR.find((s) => s.key === sira)
  const gonderiler = akis?.items ?? []

  return (
    <div className="space-y-6">
      {/* ── BAŞLIK ─────────────────────────────────────────────────────────── */}
      <header>
        <h1 className="text-2xl font-bold text-slate-900">Topluluk</h1>
        <p className="mt-3 max-w-2xl text-sm leading-relaxed text-slate-600">
          Sınav stresinden soru çözümüne, kaynak tartışmasından tercih kararına — herkesin aynı
          sıralarda olduğu ortak alan. Ders almak için eşleşmene gerek yok; buraya yazıp
          topluluğa sorabilirsin.
        </p>
      </header>

      {bildirim && (
        <Notice tone="success" onDismiss={() => setBildirim(null)}>
          {bildirim}
        </Notice>
      )}

      {/*
        İKİ SÜTUN: akış + kurallar. Kurallar sütunu lg altında akışın ALTINA düşüyor
        (ızgara sırası doğal akış sırası) — mobilde forumun kendisinden önce dört maddelik
        bir kural listesi okutmak, kimsenin okumadığı bir duvar üretirdi. Masaüstünde ise
        yan sütun boş alanı dolduruyor ve kurallar akışla aynı anda görünüyor.
      */}
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_18rem]">
        <div className="min-w-0 space-y-4">
          <GonderiKutusu session={session} onAc={() => setYaziyor(true)} />

          <SiralamaSeridi
            sira={sira}
            onSira={(k) => filtreDegistir(() => setSira(k))}
            zaman={zaman}
            onZaman={(k) => filtreDegistir(() => setZaman(k))}
            etiket={etiket}
            onEtiket={(k) => filtreDegistir(() => setEtiket(k))}
            aciklama={seciliSiralama?.aciklama}
            sonuc={akis?.totalCount ?? 0}
            yukleniyor={yukleniyor}
          />

          {/* Hata akışın ÜSTÜNDE ve liste yerinde kalıyor: oy verirken düşen bir istek,
              okunmakta olan sayfayı silmemeli. */}
          <ErrorBox error={hata} onRetry={yenile} />

          {yukleniyor && !akis ? (
            <CamKart className="py-6">
              <Loading label="Gönderiler yükleniyor…" />
            </CamKart>
          ) : gonderiler.length === 0 ? (
            <CamKart className="py-10 text-center">
              {/* Boş sonuç iki sebepten gelebilir (etiket ya da tarih) ve hangisi
                  olduğunu söylemek yerine ikisini birden gösteriyoruz: yanlış sebebi
                  tahmin eden bir metin, kullanıcıyı çalışmayan düzeltmeye yollar.
                  Hiç gönderi yoksa (filtre de yoksa) metin bunu ayrıca söylüyor. */}
              {etiket === 'hepsi' && zaman === 'hepsi' ? (
                <>
                  <p className="text-sm font-semibold text-slate-900">Burada henüz kimse yazmadı.</p>
                  <p className="mt-1 text-sm text-slate-600">
                    İlk gönderiyi sen paylaşabilirsin — bir soru sormak da yeterli.
                  </p>
                </>
              ) : (
                <>
                  <p className="text-sm font-semibold text-slate-900">
                    Bu filtrelerle gösterilecek gönderi yok.
                  </p>
                  <p className="mt-1 text-sm text-slate-600">
                    Tarih aralığını genişlet ya da etiketi “Tümü”ne al.
                  </p>
                </>
              )}
            </CamKart>
          ) : (
            <div className="space-y-4">
              {gonderiler.map((gonderi) => (
                <GonderiKarti
                  key={gonderi.postId}
                  gonderi={gonderi}
                  benimUserId={session?.userId}
                  yorumDurumu={yorumlar[gonderi.postId]}
                  onOy={gonderiOyla}
                  yorumlarAcik={acikYorum === gonderi.postId}
                  onYorumlar={() => yorumlariAc(gonderi.postId)}
                  onYorumYaz={(metin) => yorumEkle(gonderi.postId, metin)}
                  onYorumOy={(commentId, yon) => yorumOyla(gonderi.postId, commentId, yon)}
                  gizliAcik={acilanGizli.includes(gonderi.postId)}
                  onGizliAc={() => setAcilanGizli((l) => [...l, gonderi.postId])}
                  onSikayet={setSikayetHedefi}
                />
              ))}

              <Pagination
                page={akis?.page ?? 1}
                totalPages={akis?.totalPages ?? 1}
                onChange={(n) => {
                  setSayfa(n)
                  setAcikYorum(null)
                }}
                disabled={yukleniyor}
              />
            </div>
          )}
        </div>

        <aside className="space-y-4">
          <KurallarKarti />
          <OnlemlerKarti />
        </aside>
      </div>

      <GonderiModali open={yaziyor} onClose={() => setYaziyor(false)} onPaylas={gonderiEkle} />

      <SikayetModali
        hedef={sikayetHedefi}
        onClose={() => setSikayetHedefi(null)}
        onGonder={sikayetGonder}
      />
    </div>
  )
}

/* ─── GÖNDERİ KUTUSU ───────────────────────────────────────────────────────── */

/*
  Gönderi kutusu — forumun ANA EYLEMİ. Bir akış, yazma yolu görünmeden anlaşılmıyor:
  kullanıcı "burada ben ne yapıyorum" sorusunun cevabını gönderilerden değil bu
  kutudan alıyor.

  Gerçek bir <input> DEĞİL, MODALI AÇAN bir <button>. Sebep: gönderi başlık + etiket +
  metin istiyor, yani tek satırlık bir kutuya sığmıyor. Satır içi bir alan kullanıcıya
  "bir cümle yaz ve gönder" diye söz verip sonra üç alanlık bir forma çıkarırdı;
  düğme baştan doğru sözü veriyor. (Twitter satır içi yazdırıyor çünkü orada başlık ve
  etiket yok; Reddit modal açıyor çünkü var.)

  DOSYA EKLEME DÜĞMESİ YOK ve bu tasarımın kendisi bir önlem: telif ihlalinin bu üründe
  en olası yolu izinsiz PDF paylaşımı; en ucuz çözüm, o yolu arayüzde hiç açmamak.
  Kutunun altındaki şerit bunu kural olarak da söylüyor.
*/
function GonderiKutusu({ session, onAc }) {
  return (
    <CamKart className="p-4">
      <div className="flex items-center gap-3">
        <Avatar userId={session?.userId} name={session?.displayName} size="sm" />
        <button
          type="button"
          onClick={onAc}
          className="min-h-11 min-w-0 flex-1 truncate rounded-xl border border-slate-200
                     bg-white/70 px-4 py-2.5 text-left text-sm text-slate-500 transition
                     hover:border-brand-300 hover:bg-white hover:text-slate-700"
        >
          Bir soru sor ya da neler olduğunu anlat…
        </button>
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1.5 border-t border-slate-200/70 pt-3">
        {['Yalnızca metin', 'Dosya yükleme kapalı', 'Etiket seçmek zorunlu'].map((madde) => (
          <span key={madde} className="flex items-center gap-1.5 text-xs text-slate-600">
            <BilgiIkonu className="h-3.5 w-3.5 text-slate-400" />
            {madde}
          </span>
        ))}
      </div>
    </CamKart>
  )
}

/* ─── YENİ GÖNDERİ MODALI ──────────────────────────────────────────────────── */

/*
  Üç alan: başlık, etiket, metin. Dördüncüsü yok ve olmayacak — dosya eki, bağlantı
  alanı ve anket, hepsi ayrı birer moderasyon yükü açıyor.

  ALT SINIRLAR (başlık 10, metin 20 karakter) BİR KALİTE KAPISI ve sunucudaki
  ForumRules ile aynı sayılar. "yardım" diye açılan tek kelimelik başlıklar bir forumu
  en hızlı bozan şey; kimseye cevap veremeyecek kadar boş bir gönderi, paylaşılmadan
  önce durdurulmalı. Sayılar düşük tutuldu: amaç yazmayı zorlaştırmak değil, boş
  göndermeyi engellemek.

  ETİKET ZORUNLU. Etiketsiz gönderilere izin verilseydi çoğu etiketsiz gelirdi (en az
  dirençli yol) ve akıştaki filtre şeridi işe yaramaz hâle gelirdi.

  ⚠️ SUNUCU HATASI MODALI KAPATMAZ. Günlük tavan (429) ve bağlantı eşiği (400) gibi
  reddler ancak POST anında bilinebiliyor; modal kapansaydı kullanıcı yazdığı metni
  kaybederdi ve neden reddedildiğini de göremezdi.
*/
function GonderiModali({ open, onClose, onPaylas }) {
  const [baslik, setBaslik] = useState('')
  const [etiket, setEtiket] = useState('')
  const [metin, setMetin] = useState('')
  const [gonderiliyor, setGonderiliyor] = useState(false)
  const [hata, setHata] = useState(null)

  const temizle = () => {
    setBaslik('')
    setEtiket('')
    setMetin('')
    setHata(null)
  }

  const kapat = () => {
    temizle()
    onClose()
  }

  const paylasilabilir =
    baslik.trim().length >= 10 && etiket !== '' && metin.trim().length >= 20

  const paylas = async () => {
    if (!paylasilabilir || gonderiliyor) return
    setGonderiliyor(true)
    setHata(null)
    try {
      await onPaylas({ baslik: baslik.trim(), etiket, ozet: metin.trim() })
      temizle()
    } catch (err) {
      setHata(err)
    } finally {
      setGonderiliyor(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={kapat}
      title="Yeni gönderi"
      genis
      footer={
        <>
          <Button variant="secondary" onClick={kapat} disabled={gonderiliyor}>
            Vazgeç
          </Button>
          <Button onClick={paylas} loading={gonderiliyor} disabled={!paylasilabilir || gonderiliyor}>
            Paylaş
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <ErrorBox error={hata} />

        <Field label="Başlık" hint="Sorunu tek cümlede özetle — akışta önce bu okunuyor.">
          <input
            className="input"
            value={baslik}
            onChange={(e) => setBaslik(e.target.value)}
            maxLength={120}
            placeholder="Örn. Deneme netlerim düşünce panik oluyorum, sizde de böyle mi?"
          />
        </Field>

        <Field label="Etiket" hint="Gönderinin hangi başlıkta okunacağını belirler.">
          <select className="input" value={etiket} onChange={(e) => setEtiket(e.target.value)}>
            <option value="">Seç…</option>
            {/* 'hepsi' bir etiket değil, filtrenin "tümü" seçeneği — burada listelenmez. */}
            {ETIKETLER.filter((e) => e.key !== 'hepsi').map(({ key, label }) => (
              <option key={key} value={key}>
                {label}
              </option>
            ))}
          </select>
        </Field>

        <Field label="Ne olduğunu anlat" hint="Ayrıntı ver: ne denedin, nerede tıkandın.">
          <textarea
            className="input h-40 resize-none"
            value={metin}
            onChange={(e) => setMetin(e.target.value)}
            maxLength={2000}
            placeholder="Durumu birkaç cümleyle anlat…"
          />
        </Field>

        <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
          <p className="flex items-center gap-2 text-xs font-semibold text-slate-800">
            <KalkanIkonu className="h-4 w-4 text-slate-500" />
            Paylaşmadan önce
          </p>
          <ul className="mt-2 space-y-1">
            {[
              'Telif hakkı olan kitap, PDF ve deneme paylaşma — kaynağın adını yaz.',
              'Telefon, adres ve sosyal hesap bilgisi yazma.',
              'Reklam ve yönlendirme bağlantısı yasak.',
            ].map((madde) => (
              <li key={madde} className="text-xs leading-relaxed text-slate-600">
                {madde}
              </li>
            ))}
          </ul>
        </div>
      </div>
    </Modal>
  )
}

/* ─── SIRALAMA + ETİKET ŞERİDİ ─────────────────────────────────────────────── */

/*
  Sıralama ŞERİT (segment), açılır menü değil: üç seçenek var ve üçü de aynı anda
  görünüyor — açılır menü, seçenekleri görmek için fazladan bir tık isterdi ve
  kullanıcı "başka nasıl sıralayabilirim"i hiç öğrenmezdi. Discover'daki YKS/Üniversite
  şeridiyle aynı bileşen dili; uygulama içinde ikinci bir sekme biçimi doğmuyor.

  ⚠️ MOBİLDE ŞERİT DİKEY. "En Çok Oy Alanlar" uzun bir etiket; 320px'te üç düğme tek
  satıra sığmıyor. Yatay kaydırma seçilmedi — kaydırılabildiği görünmeyen bir şerit,
  gizli seçenek demektir. Onun yerine düğmeler sm altında TAM GENİŞLİK alıp alt alta
  diziliyor: sarma zaten oluyordu, `w-full` onu kazaya değil karara çeviriyor (seçili
  olan satırın tamamını dolduruyor, yarısını değil).

  Etiket pilleri ayrı bir satırda ve SIRALAMADAN sonra: ikisi farklı sorular (“neye
  göre sıralansın” / “ne konuşulsun”) ve aynı satıra konsalar tek bir denetim gibi
  okunurlardı.
*/
function SiralamaSeridi({ sira, onSira, zaman, onZaman, etiket, onEtiket, aciklama, sonuc, yukleniyor }) {
  const zamanAdi = ZAMAN_ARALIKLARI.find((z) => z.key === zaman)?.label

  return (
    <CamKart className="p-4">
      {/* Sıralama solda, tarih filtresi sağda: aynı satır, ama aynı denetim değil.
          Dar ekranda ikisi de tam genişliğe geçip alt alta diziliyor. */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div
          className="flex w-full flex-wrap gap-1 rounded-xl bg-slate-100 p-1 sm:inline-flex sm:w-auto"
          role="tablist"
          aria-label="Gönderi sıralaması"
        >
          {SIRALAMALAR.map(({ key, label, Ikon }) => (
            <button
              key={key}
              type="button"
              role="tab"
              aria-selected={sira === key}
              onClick={() => onSira(key)}
              className={`flex min-h-11 w-full items-center gap-2 rounded-lg px-3 text-sm
                          font-medium transition sm:w-auto lg:min-h-9 ${
                            sira === key
                              ? 'bg-white text-brand-700 shadow-sm'
                              : 'text-slate-600 hover:text-slate-900'
                          }`}
            >
              <Ikon className="h-4 w-4" strokeWidth={sira === key ? 2.4 : 2} />
              {label}
            </button>
          ))}
        </div>

        {/*
          TARİH FİLTRESİ — yerel <select>, özel açılır menü DEĞİL.

          Dört seçenekli, tek seçimli bir daraltma için özel bir popover yazmak; odak
          tuzağı, Esc, dışarı tıklama, ok tuşlarıyla gezinme ve mobil klavye davranışını
          elde yeniden kurmak demek. Yerel select bunların hepsini işletim sisteminden
          getiriyor ve mobilde parmakla kullanılan asıl doğru denetim o. Uygulamanın
          geri kalanı da aynı kalıbı kullanıyor (Keşfet ve Derslerim modalleri).

          .input sınıfı 16px punto veriyor: iOS, 16px'ten küçük yazılı bir alana
          odaklanınca sayfayı otomatik yakınlaştırıyor (bkz. index.css).

          Görünür etiket yerine aria-label: "Tarih" diye bir başlık koymak satıra
          üçüncü bir metin ekliyordu ve seçili değerin kendisi ("Bu hafta") zaten ne
          olduğunu söylüyor.
        */}
        <select
          className="input sm:w-auto"
          value={zaman}
          onChange={(e) => onZaman(e.target.value)}
          aria-label="Tarih filtresi"
        >
          {ZAMAN_ARALIKLARI.map(({ key, label }) => (
            <option key={key} value={key}>
              {label}
            </option>
          ))}
        </select>
      </div>

      {/* Seçilen sıralamanın ne yaptığı YAZIYOR: "Tartışmalı" hiçbir kullanıcının
          tahmin edemeyeceği bir ölçüt ve etiketin kendisi bunu anlatmıyor. Tarih
          aralığı da burada tekrar ediyor — sonuç sayısının neden düştüğü, sayının
          yanında yazmazsa fark edilmiyor. */}
      <p className="mt-3 text-xs text-slate-600">
        {aciklama} <span className="text-slate-400" aria-hidden="true">·</span> {zamanAdi}{' '}
        <span className="text-slate-400" aria-hidden="true">·</span>{' '}
        {/* Yüklenirken eski sayıyı göstermek yanlış olurdu: filtre değişmiş ama sayı
            hâlâ önceki filtrenin sonucunu söylüyor olurdu. */}
        {yukleniyor ? 'yükleniyor…' : `${sonuc} gönderi`}
      </p>

      <div className="mt-4 flex flex-wrap gap-2 border-t border-slate-200/70 pt-4">
        {ETIKETLER.map(({ key, label }) => (
          <button
            key={key}
            type="button"
            aria-pressed={etiket === key}
            onClick={() => onEtiket(key)}
            className={`min-h-11 rounded-full border px-3 text-xs font-semibold transition lg:min-h-9 ${
              etiket === key
                ? 'border-brand-600 bg-brand-600 text-white'
                : 'border-slate-200 bg-white text-slate-600 hover:border-brand-300 hover:text-brand-700'
            }`}
          >
            {label}
          </button>
        ))}
      </div>
    </CamKart>
  )
}

/* ─── GÖNDERİ KARTI ────────────────────────────────────────────────────────── */

function GonderiKarti({
  gonderi,
  benimUserId,
  yorumDurumu,
  onOy,
  yorumlarAcik,
  onYorumlar,
  onYorumYaz,
  onYorumOy,
  gizliAcik,
  onGizliAc,
  onSikayet,
}) {
  const etiketAnahtari = ETIKET_ANAHTARI[gonderi.tag] ?? gonderi.tag
  const benimGonderim = gonderi.author?.userId === benimUserId

  /*
    İNCELEMEDEKİ GÖNDERİ AKIŞTA KAPALI GELİR.

    Silinmiyor, perdeleniyor. İkisi arasındaki fark moderasyonun görünürlüğü: sessizce
    silinen içerik, hem yazarına hem okuyanına hiçbir şey söylemez ve "burada sansür
    var mı" sorusunu cevaplanamaz hâle getirir. Perde ise sebebi yazıyor, sayıyı
    veriyor ve kararı okuyana bırakıyor.
  */
  if (gonderi.underReview && !gizliAcik) {
    return (
      <CamKart className="border-amber-200/80 bg-amber-50/70 p-4">
        <div className="flex items-start gap-3">
          <UyariIkonu className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
          <div className="min-w-0">
            <p className="text-sm font-semibold text-slate-900">Bu gönderi incelemede</p>
            <p className="mt-1 text-sm leading-relaxed text-slate-700">
              {gonderi.reportCount} kişi topluluk kurallarını ihlal ettiğini bildirdi. Moderasyon
              sonuçlanana kadar akışta kapalı tutuluyor.
            </p>
            <div className="mt-3 flex flex-wrap items-center gap-3">
              <button
                type="button"
                onClick={onGizliAc}
                className="min-h-11 rounded-lg border border-amber-300 bg-white px-3 text-xs
                           font-semibold text-amber-900 transition hover:bg-amber-100 lg:min-h-9"
              >
                Yine de göster
              </button>
              <span className="text-xs text-slate-600">Etiket: {ETIKET_ADI[etiketAnahtari]}</span>
            </div>
          </div>
        </div>
      </CamKart>
    )
  }

  return (
    <CamKart className="p-0">
      {/* Perde açıldıysa uyarı kartın ÜSTÜNDE kalıyor: kullanıcı "yine de göster"e
          bastığı anı unutabilir, içeriğin durumu unutulmamalı. */}
      {gonderi.underReview && (
        <div className="flex items-center gap-2 rounded-t-2xl border-b border-amber-200 bg-amber-50 px-4 py-2">
          <UyariIkonu className="h-4 w-4 shrink-0 text-amber-600" />
          <p className="text-xs font-medium text-amber-900">
            İncelemede — {gonderi.reportCount} şikayet aldı, moderasyon sürüyor.
          </p>
        </div>
      )}

      <div className="flex gap-3 p-4 sm:gap-4 sm:p-5">
        <OyRayi
          arti={gonderi.upvoteCount}
          eksi={gonderi.downvoteCount}
          oy={gonderi.myVote}
          onOy={(yon) => onOy(gonderi.postId, yon)}
        />

        <div className="min-w-0 flex-1">
          {/* ÜST SATIR: etiket + yazar + zaman solda, şikayet sağ üstte. */}
          <div className="flex items-start justify-between gap-3">
            {/*
              AYIRAÇ NOKTALARI KENDİ BAŞLARINA BİR ÖĞE DEĞİL, ait oldukları metnin
              başında duruyor. 320px'te bu satır sarıyor ve nokta ayrı bir flex öğesi
              olduğunda satır sonunda tek başına asılı kalıyordu ("Sınav Stresi ·" /
              yeni satır / "Elif A."). Noktayı takip ettiği metne bağlamak, sarmanın
              nereden olursa olsun düzgün görünmesini sağlıyor.
            */}
            <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
              <EtiketPili etiket={etiketAnahtari} />
              <YazarSatiri yazar={gonderi.author} boyut="xs" />

              <span className="flex items-center gap-1.5 text-xs text-slate-600">
                <span className="text-slate-400" aria-hidden="true">
                  ·
                </span>
                {zamanKisalt(yasDakika(gonderi.createdAtUtc))}
              </span>
            </div>

            {/* KENDİ GÖNDERİNİ ŞİKAYET EDEMEZSİN: sunucu da reddediyor ("Kendini
                şikayet edemezsin"), ama hatayı göstermektense düğmeyi hiç çizmemek
                doğru — tıklandığında reddedilen bir düğme, kırık bir düğmedir. */}
            {!benimGonderim && (
              <SikayetDugmesi
                onClick={() =>
                  onSikayet({
                    tur: 'Gönderi',
                    id: gonderi.postId,
                    baslik: gonderi.title,
                    yazar: gonderi.author?.displayName,
                  })
                }
              />
            )}
          </div>

          {/* Başlık gönderinin kendisi: kartın tıklanabilir hissi buradan geliyor.
              Şimdilik ayrı bir gönderi sayfası yok, o yüzden bağlantı değil — var
              olmayan bir yere giden bir link, kırık bir vaat olurdu. */}
          <h3 className="mt-2.5 text-[17px] font-bold leading-snug text-slate-900">
            {gonderi.title}
          </h3>

          {/* line-clamp-3: akış TARANABİLİR kalmalı. Tam metin gönderi sayfasında.
              whitespace-pre-line: kullanıcı satır arası bıraktıysa o boşluk anlam
              taşıyor (madde madde yazılmış bir soru, tek paragrafa çökerse okunmaz). */}
          <p className="mt-2 line-clamp-3 whitespace-pre-line text-sm leading-relaxed text-slate-600">
            {gonderi.body}
          </p>

          <div className="mt-3.5 flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={onYorumlar}
              aria-expanded={yorumlarAcik}
              className={`flex min-h-11 items-center gap-2 rounded-lg px-2.5 text-xs font-semibold
                          transition lg:min-h-9 ${
                            yorumlarAcik
                              ? 'bg-brand-50 text-brand-700'
                              : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900'
                          }`}
            >
              <MesajIkonu className="h-4 w-4" />
              {gonderi.commentCount > 0 ? `${gonderi.commentCount} yorum` : 'Yorumlar'}
            </button>
          </div>

          {yorumlarAcik && (
            <YorumListesi
              durum={yorumDurumu}
              benimUserId={benimUserId}
              onSikayet={onSikayet}
              onYaz={onYorumYaz}
              onOy={onYorumOy}
            />
          )}
        </div>
      </div>
    </CamKart>
  )
}

/*
  OY RAYI — dikey, kartın solunda.

  Dokunma hedefi lg altında 44px (min-h-11): oy okları bu ekranda birbirine en yakın
  duran iki düğme ve yanlış oku basmak, kullanıcının kendi oyunu ters çevirmesi demek.
  lg üstünde fare hassas olduğu için 36px yetiyor.

  Renk oyun yönünü söylüyor: yukarı marka mavisi (bu ürünün "evet" rengi), aşağı rose.
  Sayı da oyun rengini alıyor — kullanıcı kendi oyunu, okların hangisinin dolu olduğuna
  bakmadan, tek bir sayıya bakarak görebiliyor.

  ⚠️ SAYI ARTIK OYU AYRICA EKLEMİYOR. Sabit veriyle çalışırken gösterilen değer
  `puan + oy` idi, çünkü taban sayı kullanıcının kendi oyunu içermiyordu. Sunucudan
  gelen upvoteCount/downvoteCount İÇERİYOR; toplamayı sürdürmek kendi oyumuzu iki kez
  saymak olurdu.
*/
function OyRayi({ arti, eksi, oy = 0, onOy, kucuk = false }) {
  const olcu = kucuk ? 'h-9 w-9 lg:h-8 lg:w-8' : 'h-11 w-11 lg:h-9 lg:w-9'
  const ortak =
    `grid ${olcu} place-items-center rounded-lg transition ` +
    'focus:outline-none focus:ring-2 focus:ring-brand-200'

  return (
    <div className="flex shrink-0 flex-col items-center gap-0.5">
      <button
        type="button"
        aria-label="Yukarı oy ver"
        aria-pressed={oy === 1}
        onClick={() => onOy(1)}
        className={`${ortak} ${
          oy === 1 ? 'bg-brand-50 text-brand-600' : 'text-slate-400 hover:bg-slate-100 hover:text-brand-600'
        }`}
      >
        <OyOkuIkonu className="h-[18px] w-[18px]" strokeWidth={oy === 1 ? 2.6 : 2} />
      </button>

      <span
        className={`text-sm font-bold tabular-nums ${
          oy === 1 ? 'text-brand-700' : oy === -1 ? 'text-rose-700' : 'text-slate-800'
        }`}
      >
        {arti - eksi}
      </span>

      <button
        type="button"
        aria-label="Aşağı oy ver"
        aria-pressed={oy === -1}
        onClick={() => onOy(-1)}
        className={`${ortak} ${
          oy === -1 ? 'bg-rose-50 text-rose-600' : 'text-slate-400 hover:bg-slate-100 hover:text-rose-600'
        }`}
      >
        {/* Tek çizim, iki yön: aşağı ok ayrı bir ikon değil, aynı okun 180° dönmüşü. */}
        <OyOkuIkonu className="h-[18px] w-[18px] rotate-180" strokeWidth={oy === -1 ? 2.6 : 2} />
      </button>
    </div>
  )
}

/*
  ŞİKAYET DÜĞMESİ — her gönderide ve her yorumda, aynı çizim, aynı yer mantığı.

  Sessiz duruyor (slate-500, ikon + küçük metin) ama saklı değil. İki uç da yanlış
  olurdu: dikkat çeken bir "Şikayet Et" düğmesi forumu bir ihbar hattı gibi gösterir;
  üç nokta menüsünün içine gömülen bir şikayet ise ihlali gören kullanıcının vazgeçtiği
  bir yol olur. Hover'da rose'a dönüyor — eylemin ağırlığı ancak niyet edildiğinde
  görünüyor.

  Metin lg altında GİZLİ, ikon kalıyor: dar ekranda üst satırda etiket, yazar ve zaman
  zaten yarışıyor. Erişilebilir ad her iki durumda da aria-label'da.
*/
function SikayetDugmesi({ onClick, kucuk = false }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label="Şikayet et"
      title="Şikayet et"
      className={`flex shrink-0 items-center gap-1.5 rounded-lg text-slate-500 transition
                  hover:bg-rose-50 hover:text-rose-700 ${
                    kucuk ? 'min-h-11 px-2 text-[11px] lg:min-h-8' : 'min-h-11 px-2 text-xs lg:min-h-9'
                  }`}
    >
      <BayrakIkonu className={kucuk ? 'h-3.5 w-3.5' : 'h-4 w-4'} />
      <span className="hidden font-medium sm:inline">Şikayet et</span>
    </button>
  )
}

/* ─── YORUMLAR ─────────────────────────────────────────────────────────────── */

/*
  Yorumlar gönderinin İÇİNDE açılıyor, ayrı bir sayfada değil. Sebep: bu bir iskelet ve
  gönderi sayfası henüz yok; ama karar geçici değil — akranlar arası kısa cevaplar için
  yerinde açılan bir iplik, sayfa değiştirip geri dönmekten daha az iş.

  Sol kenardaki dikey çizgi (border-l) yorumları gönderiye bağlıyor: girinti tek başına
  "bu yorumlar o gönderiye ait" demiyor, çizgi diyor.

  YORUM OYU ARTIK TIKLANABİLİR. Sabit veriyle çalışırken salt okunurdu ("basıldığında
  hiçbir şey olmayan bir düğme, hiç olmayan bir düğmeden kötüdür") çünkü uç yoktu.
  Uç geldi (POST /api/community/comments/{id}/vote) ve satır gönderinin rayıyla aynı
  bileşene döndü — vaat edilen ile yapılan yeniden aynı şey.
*/
function YorumListesi({ durum, benimUserId, onSikayet, onYaz, onOy }) {
  const [taslak, setTaslak] = useState('')
  const [gonderiliyor, setGonderiliyor] = useState(false)
  const [hata, setHata] = useState(null)

  /* Alt sınır 5 karakter (sunucudaki ForumRules.CommentMinLength ile aynı): "+1" ya da
     "aynen" gibi tek kelimelik onaylar bir tartışmayı ilerletmiyor ama boş bir yorumu
     göndermeyi engellemek yeterli — gönderi formundaki 20 karakterlik eşik burada fazla
     olurdu, kısa ve isabetli cevaplar meşru. */
  const gonderilebilir = taslak.trim().length >= 5 && !gonderiliyor

  const gonder = async (e) => {
    e.preventDefault()
    if (!gonderilebilir) return
    setGonderiliyor(true)
    setHata(null)
    try {
      await onYaz(taslak.trim())
      setTaslak('')
    } catch (err) {
      // Taslak SİLİNMİYOR: yazdığı yorumu kaybeden kullanıcı yeniden yazmıyor, vazgeçiyor.
      setHata(err)
    } finally {
      setGonderiliyor(false)
    }
  }

  const liste = durum?.liste

  return (
    <div className="mt-4 border-t border-slate-200/70 pt-4">
      {durum?.yukleniyor ? (
        <Loading label="Yorumlar yükleniyor…" />
      ) : durum?.hata ? (
        <ErrorBox error={durum.hata} />
      ) : !liste || liste.length === 0 ? (
        <p className="text-sm text-slate-600">Bu gönderide henüz yorum yok.</p>
      ) : (
        <ul className="space-y-4 border-l-2 border-slate-100 pl-3 sm:pl-4">
          {liste.map((yorum) => (
            <li key={yorum.commentId}>
              <div className="flex items-start gap-2.5">
                <OyRayi
                  kucuk
                  arti={yorum.upvoteCount}
                  eksi={yorum.downvoteCount}
                  oy={yorum.myVote}
                  onOy={(yon) => onOy(yorum.commentId, yon)}
                />

                <div className="min-w-0 flex-1">
                  <div className="flex items-start justify-between gap-2">
                    <span className="flex min-w-0 flex-wrap items-center gap-x-1.5 gap-y-1">
                      <YazarSatiri yazar={yorum.author} boyut="xs" />
                      <span className="flex items-center gap-1.5 text-xs text-slate-600">
                        <span className="text-slate-400" aria-hidden="true">
                          ·
                        </span>
                        {zamanKisalt(yasDakika(yorum.createdAtUtc))}
                      </span>
                    </span>

                    {/* Yorumun şikayet düğmesi de aynı yerde: sağ üst. Gönderiyle
                        aynı konum, aynı ikon — kullanıcı kuralı bir kez öğreniyor. */}
                    {yorum.author?.userId !== benimUserId && (
                      <SikayetDugmesi
                        kucuk
                        onClick={() =>
                          onSikayet({
                            tur: 'Yorum',
                            id: yorum.commentId,
                            baslik: yorum.body,
                            yazar: yorum.author?.displayName,
                          })
                        }
                      />
                    )}
                  </div>

                  {yorum.underReview && (
                    <p className="mt-1 flex items-center gap-1.5 text-xs font-medium text-amber-800">
                      <UyariIkonu className="h-3.5 w-3.5 shrink-0" />
                      Bu yorum incelemede.
                    </p>
                  )}

                  <p className="mt-1 whitespace-pre-line text-sm leading-relaxed text-slate-700">
                    {yorum.body}
                  </p>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}

      {/*
        Yorum kutusu SATIR İÇİ, modal değil — gönderiden farkı burada: yorumun tek bir
        alanı var ve bağlamı (üstündeki tartışma) ekranda kalmalı. Modal açsaydı,
        cevap yazarken cevapladığın şeyi görmez olurdun.

        Düğme metnin ALTINDA ve alan boşken pasif: hedef 44px, dar ekranda da rahat
        basılıyor. Enter'la göndermek YOK — çok satırlı bir alanda Enter satır başıdır.
      */}
      <form onSubmit={gonder} className="mt-4">
        <ErrorBox error={hata} />
        <textarea
          className="input mt-2 h-20 resize-none"
          value={taslak}
          onChange={(e) => setTaslak(e.target.value)}
          maxLength={1000}
          placeholder="Yorumunu yaz…"
          aria-label="Yorum yaz"
        />
        <div className="mt-2 flex justify-end">
          <Button
            type="submit"
            loading={gonderiliyor}
            disabled={!gonderilebilir}
            className="px-4 py-1.5 text-xs"
          >
            Yorumla
          </Button>
        </div>
      </form>
    </div>
  )
}

/* ─── YAN SÜTUN ────────────────────────────────────────────────────────────── */

function KurallarKarti() {
  return (
    <CamKart className="p-5">
      <div className="flex items-center gap-2.5">
        <KalkanIkonu className="h-5 w-5 shrink-0 text-brand-600" />
        <h2 className="text-sm font-bold text-slate-900">Topluluk kuralları</h2>
      </div>

      <ol className="mt-3 space-y-2.5">
        {KURALLAR.map((kural, i) => (
          <li key={kural} className="flex gap-2.5">
            {/* Numara madde işaretinden daha iyi: kurallar bir moderasyon kararında
                referans veriliyor ("3. kural"), numarasız bir liste bunu yapamaz. */}
            <span
              className="mt-0.5 grid h-5 w-5 shrink-0 place-items-center rounded-md bg-slate-100
                         text-[11px] font-bold text-slate-600"
              aria-hidden="true"
            >
              {i + 1}
            </span>
            <span className="text-xs leading-relaxed text-slate-600">{kural}</span>
          </li>
        ))}
      </ol>

      <p className="mt-4 border-t border-slate-200/70 pt-3 text-xs leading-relaxed text-slate-500">
        Kuralları ihlal eden içerik moderasyon ekibince kaldırılır; tekrarlayan ihlallerde
        hesaba yaptırım uygulanır.
      </p>
    </CamKart>
  )
}

function OnlemlerKarti() {
  return (
    <CamKart className="p-5">
      <div className="flex items-center gap-2.5">
        <BilgiIkonu className="h-5 w-5 shrink-0 text-slate-500" />
        <h2 className="text-sm font-bold text-slate-900">Nasıl korunuyor?</h2>
      </div>

      <dl className="mt-3 space-y-3">
        {ONLEMLER.map(({ baslik, metin }) => (
          <div key={baslik}>
            <dt className="text-xs font-semibold text-slate-800">{baslik}</dt>
            <dd className="mt-0.5 text-xs leading-relaxed text-slate-600">{metin}</dd>
          </div>
        ))}
      </dl>
    </CamKart>
  )
}

/* ─── ŞİKAYET MODALI ───────────────────────────────────────────────────────── */

/*
  Şikayet formu SEBEP SORUYOR. Tek düğmelik bir şikayet moderatöre "biri bundan
  hoşlanmadı"dan başka bir şey söylemez; gelen yığını sıraya sokan şey sebeptir.

  Şikayet edilen içeriğin bir parçası formda GÖRÜNÜYOR: yanlış içeriği şikayet etmek,
  moderatörün zamanını harcayan sessiz bir hata. Kullanıcı neyi bildirdiğini görmeli.

  "Anonim" bilgisi yazıyor: şikayet etmenin önündeki en büyük engel, şikayet edilenin
  bunu öğreneceği korkusudur.
*/
function SikayetModali({ hedef, onClose, onGonder }) {
  const [sebep, setSebep] = useState(null)
  const [detay, setDetay] = useState('')
  const [gonderiliyor, setGonderiliyor] = useState(false)
  const [hata, setHata] = useState(null)

  const kapat = () => {
    setSebep(null)
    setDetay('')
    setHata(null)
    onClose()
  }

  const gonderilebilir = sebep !== null && detay.trim().length >= EN_AZ_ACIKLAMA

  const gonder = async () => {
    if (!gonderilebilir || gonderiliyor) return
    setGonderiliyor(true)
    setHata(null)
    try {
      await onGonder(sebep, detay.trim())
      setSebep(null)
      setDetay('')
    } catch (err) {
      setHata(err)
    } finally {
      setGonderiliyor(false)
    }
  }

  return (
    <Modal
      open={hedef !== null}
      onClose={kapat}
      title="Şikayet et"
      footer={
        <>
          <Button variant="secondary" onClick={kapat} disabled={gonderiliyor}>
            Vazgeç
          </Button>
          <Button
            variant="danger"
            onClick={gonder}
            loading={gonderiliyor}
            disabled={!gonderilebilir || gonderiliyor}
          >
            Şikayeti gönder
          </Button>
        </>
      }
    >
      {hedef && (
        <div className="space-y-4">
          <ErrorBox error={hata} />

          <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
            <p className="text-xs font-semibold text-slate-600">
              {hedef.tur} · {hedef.yazar}
            </p>
            <p className="mt-1 line-clamp-2 text-sm text-slate-800">{hedef.baslik}</p>
          </div>

          <fieldset>
            <legend className="text-sm font-medium text-slate-700">Sebep</legend>
            <div className="mt-2 space-y-1.5">
              {SIKAYET_SEBEPLERI.map(({ key, baslik, aciklama }) => (
                <label
                  key={key}
                  className={`flex cursor-pointer items-start gap-3 rounded-xl border p-3 transition ${
                    sebep === key
                      ? 'border-brand-500 bg-brand-50'
                      : 'border-slate-200 hover:border-slate-300 hover:bg-slate-50'
                  }`}
                >
                  <input
                    type="radio"
                    name="sikayet-sebebi"
                    value={key}
                    checked={sebep === key}
                    onChange={() => setSebep(key)}
                    className="mt-0.5 h-4 w-4 shrink-0 accent-brand-600"
                  />
                  <span className="min-w-0">
                    <span className="block text-sm font-semibold text-slate-900">{baslik}</span>
                    <span className="mt-0.5 block text-xs leading-relaxed text-slate-600">
                      {aciklama}
                    </span>
                  </span>
                </label>
              ))}
            </div>
          </fieldset>

          <Field
            label="Ne oldu?"
            hint={`En az ${EN_AZ_ACIKLAMA} karakter. Moderatörün elindeki tek anlatım bu olacak.`}
          >
            <textarea
              className="input h-24 resize-none"
              value={detay}
              onChange={(e) => setDetay(e.target.value)}
              maxLength={2000}
              placeholder="Örn. gönderi izinsiz PDF bağlantısı paylaşıyor."
            />
          </Field>

          <p className="rounded-lg bg-slate-50 p-3 text-xs leading-relaxed text-slate-600">
            Şikayetin <span className="font-semibold">anonimdir</span>; şikayet ettiğin kişiye kim
            olduğun gösterilmez.
          </p>
        </div>
      )}
    </Modal>
  )
}
