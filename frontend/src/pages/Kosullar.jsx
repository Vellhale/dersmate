import { Link } from 'react-router-dom'
import { Bolum, Maddeler, MetinSayfasi } from './MetinSayfasi'
import { SOZLESME_TARIHI } from '../lib/yasalMetinler'

/*
  KULLANIM KOŞULLARI.

  Metin ürünün GERÇEK kurallarını anlatıyor, genel bir şablon değil:
    • Ders almak ücretsiz, puan yalnızca ANLATANA basılıyor ve harcanmıyor
      (CreditLedgerService — tek bacaklı işlem, escrow yok)
    • Puan 30 gün sonra yanıyor (EconomyOptions.EarnedCreditValidityDays)
    • Ders kanıtla kapanıyor, 48 saatte otomatik onaylanıyor (AutoApproveHours)
    • Yaptırım ölçeği: uyarı / süreli askı / kalıcı ban + cihaz banı
      (ApplySanction, BanUser)

  ⚠️ TUTULAMAYACAK SÖZ VERME KURALI: bu sayfada anlatılan her mekanizmanın kodda
  karşılığı var. Bir maddeyi değiştirmeden önce kodun hâlâ öyle davrandığını doğrula;
  yoksa bu metin, Topluluk sayfasındaki "3 şikayette otomatik inceleme" vaadiyle aynı
  duruma düşer (kodda karşılığı olmayan koruma sözü).
*/
export default function Kosullar() {
  return (
    <MetinSayfasi
      baslik="Kullanım koşulları"
      ozet="dersmate'i kullanırken geçerli kurallar ve karşılıklı beklentiler."
      sonGuncelleme={SOZLESME_TARIHI}
    >
      <Bolum no="1" baslik="dersmate nedir">
        <p>
          dersmate, öğrencilerin birbirine ders anlattığı bir akran öğrenme
          platformudur. Burada öğretmen değil akran vardır: anlatan da öğrenen de
          öğrencidir. Platform, dersin içeriğinden veya kalitesinden sorumlu değildir;
          yalnızca insanları buluşturur ve kayıt tutar.
        </p>
      </Bolum>

      <Bolum no="2" baslik="Hesabın">
        <Maddeler>
          <li>Gerçek bir e-posta adresiyle kayıt olur ve adresini doğrularsın.</li>
          <li>Hesabını başkasıyla paylaşamaz, başkası adına hesap açamazsın.</li>
          <li>
            18 yaşından küçüksen hesabını velinin bilgisi ve onayıyla açmalısın.
          </li>
          <li>Şifrenin güvenliği senin sorumluluğunda.</li>
        </Maddeler>
      </Bolum>

      <Bolum no="3" baslik="Para ve puan">
        <p>
          <strong>Platformda para dolaşmaz.</strong> Ders almak ücretsizdir; kimse
          kimseye ödeme yapmaz ve dersmate senden ücret almaz.
        </p>
        <Maddeler>
          <li>
            Puan <strong>yalnızca ders anlatana</strong> yazılır: her 30 dakikalık blok
            için 50 puan.
          </li>
          <li>
            Puan <strong>harcanmaz</strong>. Ders almak için puana ihtiyacın yok; puan
            yalnızca seviyeni ve profilindeki görünürlüğünü belirler.
          </li>
          <li>
            Kazanılan puanın geçerlilik süresi <strong>30 gündür</strong>; süresi dolan
            puan yanar.
          </li>
          <li>
            Puanın nakit veya başka bir değerle karşılığı yoktur, devredilemez.
          </li>
        </Maddeler>
      </Bolum>

      <Bolum no="4" baslik="Dersler">
        <Maddeler>
          <li>
            Ders saatini ve görüşme bağlantısını taraflar kendi aralarında sohbet
            üzerinden kararlaştırır.
          </li>
          <li>
            Ders bittikten sonra anlatan taraf kanıt yükler; karşı taraf onaylar.
            48 saat içinde yanıt gelmezse ders otomatik olarak onaylanmış sayılır.
          </li>
          <li>
            Sahte kanıt yüklemek ağır bir ihlaldir. Aynı görselin birden fazla derste
            kullanılması sistem tarafından tespit edilir.
          </li>
        </Maddeler>
      </Bolum>

      <Bolum no="5" baslik="Yasak davranışlar">
        <Maddeler>
          <li>Hakaret, taciz, ayrımcılık, tehdit.</li>
          <li>
            Telif hakkı olan kitap, soru bankası, deneme veya PDF paylaşmak.
          </li>
          <li>Reklam, satış, yönlendirme bağlantısı ve spam.</li>
          <li>
            Başkasının kişisel bilgisini (telefon, adres, sosyal hesap) izinsiz
            paylaşmak.
          </li>
          <li>Sahte kanıt, sahte hesap ve sistemi yanıltmaya yönelik her davranış.</li>
          <li>18 yaşından küçük kullanıcılara yönelik uygunsuz her türlü iletişim.</li>
        </Maddeler>
      </Bolum>

      <Bolum no="6" baslik="Şikayet ve yaptırımlar">
        <p>
          Bir kullanıcıyı ders ekranından şikayet edebilirsin. Şikayetin yalnızca
          yönetime gider; şikayet ettiğin kişi ne şikayeti görür ne de kim olduğunu
          öğrenir.
        </p>
        <p>Yönetimin uygulayabileceği yaptırımlar:</p>
        <Maddeler>
          <li>
            <strong>Uyarı</strong> — hesap açık kalır, karar kayda geçer.
          </li>
          <li>
            <strong>Süreli askı</strong> — belirtilen süre boyunca giriş yapılamaz.
          </li>
          <li>
            <strong>Kalıcı ban</strong> — hesap ve kullanıcının bilinen cihazları
            kapatılır. Bu, yeni hesap açarak devam etmeyi de engeller.
          </li>
        </Maddeler>
        <p>
          Ağır ihlallerde (taciz, sahte kanıt, telif ihlali) doğrudan en üst yaptırım
          uygulanabilir.
        </p>
      </Bolum>

      <Bolum no="7" baslik="Sorumluluk sınırı">
        <p>
          dersmate, kullanıcıların birbirine anlattığı içeriğin doğruluğundan,
          derslerin gerçekleşmesinden ve kullanıcılar arasındaki anlaşmazlıklardan
          sorumlu değildir. Platform “olduğu gibi” sunulur; kesintisiz çalışacağı
          garanti edilmez.
        </p>
        <p>
          Görüşmeler taraflarca seçilen üçüncü taraf araçlar üzerinden yapılır;
          o araçların kendi koşulları geçerlidir.
        </p>
      </Bolum>

      <Bolum no="8" baslik="Hesabın kapatılması">
        <p>
          Bu koşulları ihlal eden hesapları kapatabiliriz. Sen de hesabının silinmesini
          isteyebilirsin — nasıl olacağı{' '}
          <Link to="/gizlilik" className="font-medium text-brand-700 hover:underline">
            Gizlilik metninin
          </Link>{' '}
          7. bölümünde yazıyor.
        </p>
      </Bolum>

      <Bolum no="9" baslik="Değişiklikler">
        <p>
          Koşullar değişirse bu sayfadaki tarihi güncelliyoruz. Önemli bir değişiklikte
          kullanıcıları ayrıca bilgilendiriyoruz.
        </p>
      </Bolum>
    </MetinSayfasi>
  )
}
