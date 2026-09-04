import { Link } from 'react-router-dom'
import { Bolum, Maddeler, MetinSayfasi } from './MetinSayfasi'
import { SOZLESME_TARIHI } from '../lib/yasalMetinler'

/*
  HESAP SİLME — HERKESE AÇIK SAYFA.

  NEDEN AYRI BİR SAYFA: Google Play, hesap açtıran uygulamalarda silmeyi İKİ yerde
  birden istiyor — uygulamanın içinde (Profil → "Hesabımı sil") ve uygulamayı KURMADAN
  açılabilen bir web adresinde. İkincisinin gerekçesi şu: telefonunu kaybetmiş, uygulamayı
  silmiş ya da hesabına hiç giremeyen biri de silme talebinde bulunabilmeli. Bu yüzden
  sayfa oturum GEREKTİRMİYOR ve kabuğun (Layout) dışında duruyor.

  Bu sayfa mağaza kaydındaki "hesap silme URL'i" alanına yazılacak adrestir.

  METİN NEYİN KALDIĞINI DA SÖYLÜYOR ve bu bir tercih değil, doğruluk meselesi: sunucu
  kaydı anonimleştiriyor, yok etmiyor (bkz. DeleteAccountHandler — identity.Users'a 23
  yabancı anahtar bakıyor ve çoğu karşı tarafa ait). "Her şey silinir" demek, kullanıcıya
  olmayan bir şey vaat etmek olurdu.
*/
export default function HesapSilme() {
  return (
    <MetinSayfasi
      baslik="Hesabını silme"
      ozet="Hesabını nasıl silersin, ne siliniyor, ne kalıyor ve hesabına giremiyorsan ne yapmalısın."
      sonGuncelleme={SOZLESME_TARIHI}
    >
      <Bolum no="1" baslik="Uygulamadan ya da siteden sil">
        <p>
          Hesabını kendin silebilirsin; kimseye başvurmana gerek yok. İşlem geri alınamaz.
        </p>
        <Maddeler>
          <li>
            <strong>Mobil uygulamada:</strong> Profil sekmesini aç, sayfanın en altındaki{' '}
            <strong>“Hesabımı sil”</strong> bağlantısına dokun.
          </li>
          <li>
            <strong>Web sitesinde:</strong>{' '}
            <Link to="/profil" className="font-medium text-brand-700 hover:underline">
              Profil
            </Link>{' '}
            sayfasının en altındaki <strong>“Hesabımı sil”</strong> bağlantısına tıkla.
          </li>
        </Maddeler>
        <p>
          Her iki yolda da onay için parolan yeniden soruluyor. Bunun sebebi, açık kalmış
          bir oturumu eline geçiren birinin hesabını tek tıkla silememesi.
        </p>
      </Bolum>

      <Bolum no="2" baslik="Ne siliniyor">
        <Maddeler>
          <li>Adın, e-posta adresin, telefon numaran ve profil fotoğrafın</li>
          <li>Biyografin, üniversite ve bölüm bilgin</li>
          <li>Açtığın ders ilanları (arz ve talep)</li>
          <li>Veri toplama tercihlerin</li>
          <li>Cihaz kaydın (giriş yaptığın cihazların kimliği)</li>
        </Maddeler>
        <p>
          Profil fotoğrafın dosya olarak da sunucudan kaldırılıyor, yalnızca kaydı
          değil.
        </p>
      </Bolum>

      <Bolum no="3" baslik="Ne kalıyor ve neden">
        <p>
          Yaptığın dersler, kazandırdığın puanlar ve yazdığın değerlendirmeler{' '}
          <strong>karşı tarafın geçmişine ait</strong>. Bunları silmek, senin verini
          değil, başka bir kullanıcının ders geçmişini ve kazandığı puanı yok etmek
          olurdu. Bu yüzden o kayıtlar duruyor — ama adın yerine{' '}
          <strong>“Silinmiş kullanıcı”</strong> görünüyor; kim olduğun anlaşılmıyor.
        </p>
        <Maddeler>
          <li>Ders oturumları, eşleşmeler ve mesaj kayıtları</li>
          <li>Kredi defteri (puanların basıldığı kayıtlar)</li>
          <li>
            Şikayet, itiraz ve yaptırım kayıtları — bunlar hesap verebilirlik kaydıdır ve
            silinmesi, kötüye kullanımın izini kaybetmek anlamına gelirdi
          </li>
        </Maddeler>
      </Bolum>

      <Bolum no="4" baslik="Hesabına giremiyorsan">
        <p>
          Parolanı unuttuysan önce{' '}
          <Link to="/sifre-sifirla" className="font-medium text-brand-700 hover:underline">
            parola sıfırlama
          </Link>{' '}
          adımını dene; hesabına girdiğinde silme işlemini kendin yapabilirsin.
        </p>
        <p>
          Hesabına hiçbir şekilde erişemiyorsan silme talebini{' '}
          <a
            href="mailto:iletisim@dersmate.com"
            className="font-medium text-brand-700 hover:underline"
          >
            iletisim@dersmate.com
          </a>{' '}
          adresine gönder. Talebi işleme almadan önce hesabın sahibi olduğunu doğrulamamız
          gerekiyor — bu, başkasının hesabını sildirmesini engellemek için.
        </p>
      </Bolum>

      <Bolum no="5" baslik="Ders kanıtı görselleri">
        <p>
          Ders sonunda yüklenen kanıt görselleri, hesabından bağımsız bir saklama süresine
          tabi ve süresi dolduğunda otomatik olarak siliniyor. Ayrıntılar{' '}
          <Link to="/gizlilik" className="font-medium text-brand-700 hover:underline">
            gizlilik metninde
          </Link>{' '}
          yazılı.
        </p>
      </Bolum>
    </MetinSayfasi>
  )
}
