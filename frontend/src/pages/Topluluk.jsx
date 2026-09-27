import { useCallback, useEffect, useRef, useState } from 'react'
import { useAuth } from '../state/AuthContext'
import { Avatar } from '../components/Avatar'
import { GonderiKarti } from '../components/GonderiKarti'
import { CamKart } from '../components/SayfaZemini'
import { Button, ErrorBox, Field, Loading, Modal, Notice, Pagination } from '../components/ui'
import { api } from '../lib/api'
import { ETIKET_ENUM, ETIKETLER, oyUygula } from '../lib/forum'
import {
  AlevIkonu,
  ArtanIkonu,
  BilgiIkonu,
  KalkanIkonu,
  SaatIkonu,
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

  ─── KART: INSTAGRAM TARZI (2026-09-26), REDDIT TARZI GERİ DÖNÜŞ ─────────────
  Kart ve parçaları (oy rayı, şikayet düğmesi, yorum ipliği, yazar satırı, etiket
  pili) components/GonderiKarti.jsx'te; etiket sözlüğü, zaman biçimi ve oy hesabı
  lib/forum.js'te. Varsayılan tarz Instagram: avatarlı yazar başlığı (profile gider),
  başlık + üç satırlık özet, çizgiyle ayrılmış eylem satırı (yatay oy, yorum, şikayet)
  ve ilk yorumun iki satırlık önizlemesi (sunucu: ForumPostDto.firstComment). Eski
  Reddit düzeni (sol oy rayı) aynı dosyada duruyor; GonderiKarti.jsx → AKIS_TARZI
  tek satırla geri getirir. Gerekçe o dosyanın başında.

  İki tarzda da korunanlar: başlık + özet (kart içeriği bitirmez, açmaya davet eder;
  özet üç satırda kesilir), etiket (bir öğrenci forumunda "soru" ile "motivasyon" iki
  ayrı okuma kipi) ve ferahlık — Reddit'in yoğunluğu hiç alınmadı: bu ürünün geri
  kalanı (Keşfet, Derslerim) ferah kartlarla çalışıyor ve forum tek başına sıkışık bir
  liste olsaydı uygulamanın içinde başka bir uygulama gibi dururdu.

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
   ('Newest', 'ExamStress'). Çeviri anahtar başına TEK YERDE: sıralama ve tarih
   burada, etiket lib/forum.js'te (kart da kullanıyor). Anahtarları sunucununkilerle
   değiştirmek arayüzün geri kalanını İngilizceye çevirmek demekti.   */

const SIRA_ENUM = { yeni: 'Newest', oy: 'Top', tartismali: 'Controversial' }
const ZAMAN_ENUM = { hepsi: 'All', gun: 'Day', hafta: 'Week', ay: 'Month' }

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
          sıralarda olduğu ortak alan. Ders almak için arkadaş olmana gerek yok; buraya yazıp
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
            /* space-y-5: kartlar cam, zemin üstünde zaten ayrı duruyor; ayrımı asıl
               kartın İÇ düzeni (başlık / gövde / eylem satırı) veriyor. Bant ya da ek
               çizgi yok. */
            <div className="space-y-5">
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
