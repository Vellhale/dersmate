import { Link } from 'react-router-dom'
import { Bolum, Maddeler, MetinSayfasi } from './MetinSayfasi'
import { SOZLESME_TARIHI } from '../lib/yasalMetinler'
import { ISLETMECI, ISLETMECI_ADRESI, ISLETMECI_ALAN_ADI, MARKA } from '../lib/kunye'

/*
  KULLANIM KOŞULLARI.

  Metin ürünün GERÇEK kurallarını anlatıyor, genel bir şablon değil:
    • Ders almak ücretsiz, ders puanı yalnızca ANLATANA basılıyor ve harcanmıyor
      (CreditLedgerService — tek bacaklı işlem, escrow yok)
    • Topluluk'ta net oy eşiği de puan basıyor (CommunityRewardRules,
      MintCommunityRewardAsync)
    • Kazanılan puan (ders ve topluluk) YANMIYOR: iki basımda da CreditLot.ExpiresAtUtc
      = null (CreditLedgerService → MintLessonRewardAsync, MintCommunityRewardAsync)
    • Ders kanıtla kapanıyor, 48 saatte otomatik onaylanıyor (AutoApproveHours)
    • Yaptırım ölçeği: uyarı / süreli askı / kalıcı ban + cihaz banı
      (ApplySanction, BanUser)

  ⚠️ TUTULAMAYACAK SÖZ VERME KURALI: bu sayfada anlatılan her mekanizmanın kodda
  karşılığı var. Bir maddeyi değiştirmeden önce kodun hâlâ öyle davrandığını doğrula;
  yoksa bu metin, Topluluk sayfasındaki "3 şikayette otomatik inceleme" vaadiyle aynı
  duruma düşer (kodda karşılığı olmayan koruma sözü).

  ⚠️ §3 2026-09-25 SÜRÜMÜNÜN İÇİNDE DÜZELTİLDİ (2026-09-26, yayından önce; mobil
  app/kosullar.jsx ile aynı cümleler). Önceki hâli sunucuyla çelişiyordu: "puan YALNIZCA
  ders anlatana yazılır" (oysa Topluluk oyları da puan basıyor) ve "kazanılan puan 30 günde
  yanar" (oysa ders kazancı vadesiz açılıyor; EconomyOptions.EarnedCreditValidityDays hâlâ
  tanımlı ama hiçbir kod onu okumuyor). Hakkımızda'daki "Topluluk’ta oy toplayan
  katkıların da puan getirir" güvencesi bu düzeltmeyle çelişkisiz okunuyor.
  Sözleşme sürümü ARTMADI: 2026-09-25 henüz hiçbir yerde yayında değil (üç yer de main'e
  birleşmemiş dallarda), bu metni kabul etmiş kullanıcı yok (bkz. lib/yasalMetinler.js).

  "Kazandığın puan" bilerek böyle: kayıtta tanımlanan hoş geldin hediyesi
  (WelcomeBonus) HÂLÂ vadeli (WelcomeCreditValidityDays) ve süresi dolunca yakılıyor.
  O bir kazanç değil hediye ve unvana da sayılmıyor; ama "puan yanmaz" diye genel bir
  cümle, kullanıcının kendi puan geçmişindeki eksi satırla çelişirdi. Madde bu yüzden
  kaynağı adıyla sayıyor.

  HOŞ GELDİN PUANI MADDESİ (2026-09-27, yine 2026-09-25'in içinde): yukarıdaki notun
  kabul ettiği eksi satırı metin kullanıcıya hiç anlatmıyordu. Eski §3 "süresi dolan
  puan yanar" diyerek onu kabaca kapsıyordu; yeni metin o cümleyi kaldırınca, e-posta
  doğrulamasında verilen puanın 14 gün sonra "Süresi dolan puan" satırıyla düştüğünü
  söyleyen tek açıklama da gitmişti. Dayanak: CreditLedgerService.GrantWelcomeCreditAsync
  (ExpiresAtUtc = now + WelcomeCreditValidityDays; appsettings.json'da 14, üretimde
  ezilmiyor), ExpireCreditsHandler → ExpireDueLotsAsync (BackgroundJobs), "seviyene
  sayılmaz" için User.TotalEarnedCredits'in yalnızca ders ve topluluk kazancıyla artması.
  WelcomeCreditValidityDays değişirse bu madde ve mobil app/kosullar.jsx aynı gün değişir.
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
          {MARKA}, öğrencilerin birbirine ders anlattığı bir akran öğrenme
          platformudur. Burada öğretmen değil akran vardır: anlatan da öğrenen de
          öğrencidir. Platform, dersin içeriğinden veya kalitesinden sorumlu değildir;
          yalnızca insanları buluşturur ve kayıt tutar.
        </p>
        {/*
          SÖZLEŞMENİN KARŞI TARAFI. Bir kullanım koşulları metni iki taraf arasındaki
          sözleşmedir ve taraflardan biri bugüne kadar İSİMSİZDİ: metin boyunca geçen
          "biz"in kim olduğu hiçbir yerde yazmıyordu. §7'deki sorumluluk sınırı ve
          §6'daki yaptırım yetkisi, kimin adına kullanıldığı belli olmayan haklardı.
        */}
        <p>
          {MARKA},{' '}
          <a
            href={ISLETMECI_ADRESI}
            target="_blank"
            rel="noopener noreferrer"
            className="font-medium text-brand-700 hover:underline"
          >
            {ISLETMECI}
          </a>{' '}
          ({ISLETMECI_ALAN_ADI}) tarafından işletilmektedir. Bu metindeki “biz” ve
          “{MARKA}” ifadeleri {ISLETMECI}’i anlatır. Künye ve iletişim bilgileri
          sayfanın altındadır.
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
            Puan, onaylanan dersin <strong>anlatanına</strong> yazılır: her 30 dakikalık
            blok için 50 puan. Topluluk’ta yeterli net oy toplayan katkıların da puan
            kazandırır.
          </li>
          <li>
            Puan <strong>harcanmaz</strong>. Ders almak için puana ihtiyacın yok; puan
            yalnızca seviyeni ve profilindeki görünürlüğünü belirler.
          </li>
          <li>
            Ders anlatarak ve Topluluk katkılarınla kazandığın puanın{' '}
            <strong>süresi dolmaz</strong>; bu puan yanmaz.
          </li>
          <li>
            E-posta doğrulamasında verilen hoş geldin puanı 14 gün sonra silinir; seviyene
            sayılmaz.
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
        {/* 2026-09-27'de mobille EŞİTLENDİ. Paragraf bir süre yalnızca mobilde vardı
            (app/kosullar.jsx §6, mağaza incelemesi şartı diye eklenmişti) ama anlattığı
            yetenek web'de de var: api.closeMatch → pages/Matches.jsx, "Sonlandır" düğmesi.
            Var olan bir yeteneğin tarifi, yeni ifşa değil → SOZLESME_SURUMU artmadı. */}
        <p>
          Rahatsız eden biriyle iletişimi kesmek için yönetimi beklemek zorunda değilsin:
          arkadaşlığı Arkadaşlar ekranından tek taraflı sonlandırabilirsin. Sonlandırılan
          arkadaşlıktan sana yeni mesaj gelmez.
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
