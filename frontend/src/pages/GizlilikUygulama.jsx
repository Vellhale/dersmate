import { Link } from 'react-router-dom'
import { Bolum, Maddeler, MetinSayfasi } from './MetinSayfasi'
import { ILETISIM_EPOSTA, ISLETMECI, ISLETMECI_ALAN_ADI, MARKA } from '../lib/kunye'
import { SOZLESME_TARIHI } from '../lib/yasalMetinler'

/*
  MOBİL UYGULAMANIN GİZLİLİK METNİ — herkese açık adres (/gizlilik-uygulama).

  ⛔ BU SAYFA /gizlilik'in KOPYASI DEĞİL ve olmamalı. İki mağaza da (App Store Connect
  ve Google Play) zorunlu alan olarak herkese açık bir gizlilik ADRESİ istiyor ve o
  adresin, mağazadaki uygulamanın GERÇEĞİNİ anlatması gerekiyor. Bugüne kadar
  verilebilecek tek adres /gizlilik'ti; o sayfa WEB'i anlatıyor ve uygulamada OLMAYAN
  üç şeyi beyan ediyor: çerez kategorileri, Google Analytics ve canvas tabanlı tarayıcı
  parmak izi. Apple ve Google formdaki beyanı, politikayı ve uygulamanın davranışını
  karşılaştırıyor — o adresi vermek yanlış beyan olurdu.

  Metnin kaynağı mobil depodaki app/gizlilik.jsx. Buraya MEKANİK olarak taşındı
  (Paragraf→p, Kalin→strong, Madde→li; Bolum ve Maddeler iki depoda da aynı);
  cümlelerin hiçbirine dokunulmadı. Mobil dosyanın kendi başlığı bu sayfayı
  "ayrı PR" diye zaten bekliyordu.

  ⚠️ BAKIM KURALI: app/gizlilik.jsx değişirse bu dosya AYNI GÜN değişir. İkisi tek bir
  metin; ayrışırlarsa mağazaya verilen adres uygulamanın davranışını anlatmaz hâle gelir.
  /gizlilik (web metni) ise AYRI bir belge ve ayrı kalmalı — ikisi aynı OLGULARI
  söylemeli (taşıyıcılar, süreler, silme noktaları), ifade platforma göre ayrışır.

  SOZLESME_SURUMU ARTMADI: bu sayfa var olan bir metni yeni bir adreste yayınlıyor,
  yeni bir ifşa getirmiyor. Sürüm sabiti üç yerde eşit kalmalı (bkz. lib/yasalMetinler.js).
*/
export default function GizlilikUygulama() {
  return (
    <MetinSayfasi
      baslik="Mobil uygulama — gizlilik ve KVKK aydınlatma metni"
      ozet="dersmate mobil uygulamasının hangi verini topladığı, neden topladığı, ne kadar sakladığı ve ne isteyebileceğin."
      sonGuncelleme={SOZLESME_TARIHI}
    >
      {/* Okuyucu yanlış belgeye düşmesin diye iki metin birbirine bağlı. */}
      <p className="rounded-lg border border-brand-200 bg-brand-50 px-4 py-3 text-[15px] text-brand-900">
        Bu metin <strong>{MARKA} mobil uygulaması</strong> içindir. dersmate.com’u
        tarayıcıdan kullanıyorsan{' '}
        <Link to="/gizlilik" className="font-medium underline underline-offset-2">
          web sürümünün gizlilik metnini
        </Link>{' '}
        oku: tarayıcıda çerezler ve ölçüm taşıyıcısı var, uygulamada yok.
      </p>
      {/*
        ⚠️ BÖLÜM NUMARASI DEĞİŞMEDİ, YALNIZCA BAŞLIK (2026-09-21, web'le aynı).

        Veri sorumlusu kimliği bir KVKK aydınlatma metninin İLK maddesidir, yani doğal
        yeri yeni bir §1 açmaktı. AÇILMADI: §4, §5, §6, §7 ve §9'a hem bu metnin içinden
        hem Koşullar'dan atıf var; hepsini bir kaydırmak, doğru metni yanlış yere işaret
        eden atıflarla bırakırdı.
      */}
      <Bolum no="1" baslik="Kısaca ve veri sorumlusu">
        <p>
          <strong>{MARKA}</strong>, öğrencilerin birbirine ders anlattığı bir platformdur ve{' '}
          <strong>{ISLETMECI}</strong> ({ISLETMECI_ALAN_ADI}) tarafından işletilmektedir. Bu
          metinde geçen “biz”, {ISLETMECI}’tir; verinle ilgili taleplerin muhatabı da odur.
          İletişim bilgileri sayfanın altındaki künyededir.
        </p>
        <p>
          Verini reklam için kullanmıyoruz, satmıyoruz ve üçüncü taraflara pazarlama
          amacıyla aktarmıyoruz. Topladığımız her şey hesabını çalıştırmak, açtıysan sana
          bildirimle haber vermek ya da platformu kötüye kullanımdan korumak için.
        </p>
      </Bolum>

      <Bolum no="2" baslik="Topladığımız veriler">
        <p>
          <strong>Hesap bilgileri:</strong> e-posta adresin, adın (görünen ad), şifrenin geri
          döndürülemez özeti (hash). Şifreni düz metin olarak hiçbir yerde saklamıyoruz.
        </p>
        <p>
          <strong>İsteğe bağlı profil bilgileri:</strong> profil fotoğrafın, kendini anlattığın
          metin, okulun ve bölümün. Bunların hiçbiri zorunlu değildir; boş bırakabilirsin.
        </p>
        <p>
          <strong>Kullanım verileri:</strong> anlattığın ders sayısı ve süresi, kazandığın puan,
          aldığın değerlendirmeler, son giriş zamanın.
        </p>
        {/* Topluluk (2026-09-27, 2026-09-25 içinde, yayından önce; web Gizlilik.jsx aynı
            olgular). Forum 2026-08-27'den beri var ama bu satır yalnızca mesaj ve kanıtı
            sayıyordu. Kaynak: Domain/Community/Forum.cs (CommunityPost, CommunityComment,
            CommunityVote) ve Features/Moderation/Reports.cs (şikayet). */}
        <p>
          <strong>İçerik:</strong> arkadaşlarınla yazıştığın mesajlar, dersin yapıldığını
          gösteren kanıt görselleri ve Topluluk’ta yazdığın gönderiler ve yorumlar, verdiğin
          oylar ve yaptığın şikayetler.
        </p>
        <p>
          <strong>Fotoğraflarına erişim:</strong> profil fotoğrafı ya da ders kanıtı yüklerken
          telefonun galerisini açıyoruz. Yalnızca <strong>senin seçtiğin</strong> görsel
          uygulamaya gelir; galerin taranmaz, seçmediğin hiçbir görsel okunmaz. İzni
          vermezsen uygulamanın geri kalanı çalışmaya devam eder.
        </p>
        <p>
          <strong>Cihaz kimliği (önemli):</strong> giriş yaptığında cihazından bir kimlik özeti
          üretiyoruz. Bu özet şu bilgilerin birleştirilip geri döndürülemez biçimde
          özetlenmesiyle oluşuyor: işletim sisteminin uygulamalara verdiği cihaz kimliği
          (Android’de Android ID, iOS’ta üretici kimliği), işletim sisteminin adı, cihazın
          markası ve modeli, toplam bellek miktarı. Bu bilgilerin kendisini değil, yalnızca
          özetini saklıyoruz. Bu özet bir <strong>reklam kimliği değildir</strong>: reklam için
          kullanılmaz ve hiçbir üçüncü tarafa gönderilmez.
        </p>
        {/* HWID cümlesiyle ÇELİŞMEMELİ: dışarı giden bildirim adresi, HWID özeti değil.
            Özet yalnızca sunucuda, adresi hangi cihaza ait olduğuna bağlamak için duruyor. */}
        <p>
          <strong>Bildirim kaydı (yalnızca bildirimleri açarsan):</strong> telefonuna bildirim
          gönderebilmek için telefonunun <strong>bildirim adresini</strong> (Expo’nun verdiği ve
          Android’de Google’ın, iPhone’da Apple’ın bildirim adresini taşıyan bir numara),
          telefonunun türünü (Android ya da iOS), adresin hangi cihazına ait olduğunu bilmek
          için yukarıdaki cihaz kimliği özetini, Android’de telefon ayarlarından kapattığın
          bildirim türlerini ve kaydın tarihlerini saklıyoruz. Bildirim adresi bildirimi
          ileten hizmetlere gider (bkz. §6); cihaz kimliği özeti gitmez.
        </p>
        <p>
          <strong>Bildirim tercihlerin ve bildirim kayıtları:</strong> hangi bildirim türlerini
          almak istediğin, bildirimlerle ilgili açıklamayı görüp bildirimleri açtığın an,
          bildirim sorusunu kaç kez ertelediğin ve en son ne zaman ertelediğin (soruyu
          sık sık tekrarlamamak için). Bildirimleri açmamış olsan da sunucu,
          sana bildirim gerektiren her olay için kısa bir kayıt tutar: bildirimin türü, olayın
          ve olayı başlatan kişinin kayıt numaraları, ne zaman gönderildiği ya da neden
          gönderilmediği. Bildirimin metni ve mesajlarının içeriği bu kayda{' '}
          <strong>kopyalanmaz</strong>.
        </p>
      </Bolum>

      <Bolum no="3" baslik="Neden topluyoruz">
        <Maddeler>
          <li>
            <strong>Hesabını çalıştırmak için:</strong> e-posta, ad, şifre özeti. Bunlar olmadan
            giriş yapamazsın.
          </li>
          <li>
            <strong>Arkadaşlık ve ders için:</strong> profil bilgilerin ve konu tercihlerin — kimin
            kime ders anlatabileceğini bunlar belirliyor.
          </li>
          <li>
            <strong>Kötüye kullanımı önlemek için:</strong> cihaz kimliği. Kuralları ağır biçimde
            ihlal eden bir hesap kapatıldığında, aynı kişinin hemen yeni hesap açıp devam
            etmesini engelleyen tek şey bu. Öğrencilerin bir arada olduğu bir platformda bu
            korumanın karşılığı somut.
          </li>
          <li>
            <strong>Anlaşmazlıkları çözmek için:</strong> ders kanıtları ve şikayet kayıtları.
          </li>
          <li>
            <strong>Sana haber vermek için (bildirimler):</strong> bildirim adresi, bildirim
            tercihlerin ve bildirim kayıtları. Yeni mesajı, arkadaş isteğini, ders onayını ve
            yaklaşan dersi zamanında haber vermek; kapattığın türleri göndermemek; aynı olayı
            iki kez bildirmemek ve aynı kişinin sana art arda istek bildirimi düşürmesini
            sınırlamak için. Hukuki sebebi, kullandığın hizmetin parçası olduğu için
            sözleşmenin ifasıdır (KVKK m.5/2-c). Bildirimler isteğe bağlıdır: açmazsan
            uygulamanın geri kalanı aynı biçimde çalışır.
          </li>
        </Maddeler>
      </Bolum>

      <Bolum no="4" baslik="Uygulamanın cihazında sakladıkları">
        <p>
          Mobil uygulamada çerez yoktur. Cihazında yalnızca şunlar durur:
        </p>
        <Maddeler>
          <li>
            <strong>Oturum anahtarı ve cihaz kimliği özeti:</strong> cihazın güvenli anahtar
            deposunda (iOS Anahtar Zinciri / Android Keystore) şifreli olarak tutulur.
            Bunlar olmadan giriş yapılamaz. Uygulamayı sildiğinde iOS’ta bu kayıt cihazda
            kalmaya devam edebilir — yeniden kurduğunda aynı cihaz olarak tanınırsın.
          </li>
          <li>
            <strong>Arayüz tercihlerin:</strong> uygulamanın kendi tercih deposunda tutulur;
            uygulama silinince gider.
          </li>
          {/* İki madde de "zorunlu" kategoride (IzinContext → IZIN_KATEGORILERI) ve orada da
              yazılı: hizmetin gereği, izne bağlanamaz. IZIN_SURUMU bu yüzden ARTMADI. */}
          <li>
            <strong>Bildirim bileşeninin kayıtları (yalnızca bildirimleri açtıysan):</strong>{' '}
            bildirimlerin bu telefona ulaşabilmesi için bildirim bileşeni rastgele bir kurulum
            numarası ve telefonun bildirim adresini saklar; oturumun açıkken adres
            değiştiğinde ve en geç haftada bir, adresi Expo’ya kendisi yeniden bildirir.
            Çıkış yaptığında bu yeniden bildirim durur ve bileşenin sakladığı adres silinir;
            kurulum numarası kalır. Android’de Google’ın bildirim hizmeti de kendi kurulum
            kimliğini tutar. Android’de bunlar uygulamanın kendi alanında durur ve
            uygulamayla birlikte silinir; iOS’ta Anahtar Zinciri’nde tutulur ve uygulamayı
            sildikten sonra da kalabilir. Bildirim tercihlerin cihazda değil, hesabında
            tutulur.
          </li>
          {/* İki anahtar: KEYS.pushKayitli ve KEYS.pushUnutulacak (storage.js). Birincisi
              2026-09-25'te eklendi: işaret yalnızca "bu süreçte token alındıysa" yazılıyordu
              ve uygulama çevrimdışı açılıp çevrimdışı çıkış yapılınca hiç yazılmıyordu —
              §5'in "bir sonraki açılışta silinir" cümlesi o durumda tutmuyordu. */}
          <li>
            <strong>Bildirim kaydını silmek için iki küçük işaret (yalnızca bildirimleri
            açtıysan):</strong> bu telefon bildirimlere kaydolunca cihazın güvenli anahtar
            deposuna kaydın zamanı yazılır. İnternet yokken çıkış yaparsan sunucudaki
            bildirim kaydını o an silemeyiz; o zaman yanına çıkış zamanı yazılır ve uygulama
            bir sonraki açılışta internete ulaşınca kaydı sildirir. İki işaret de kayıt
            sunucudan silindiğinde (çıkışta ya da o sonraki açılışta) kaldırılır.
          </li>
          <li>
            <strong>Ölçüm ve izleme yok:</strong> uygulama hiçbir analitik ya da reklam
            bileşeni içermez. “Yüklenir ama veri göndermez” değil — böyle bir bileşen
            uygulamada hiç bulunmuyor.
          </li>
          <li>
            <strong>Veri tercihin hesabına ait:</strong> Profil › Ayarlar › “Veri
            tercihleri”nde yaptığın analitik seçimi dersmate <strong>hesabına</strong>{' '}
            kaydedilir ve web sitesinde de geçerli olur. Mobil uygulama bugün hiçbir ölçüm
            yapmadığı için bu tercih burada bir şeyi açıp kapatmaz; ileride ölçüm
            eklenirse, eklenmeden önce senin verdiğin cevaba bakılır.
          </li>
        </Maddeler>
      </Bolum>

      <Bolum no="5" baslik="Ne kadar saklıyoruz">
        <Maddeler>
          <li>
            <strong>Ders kanıt görselleri: 180 gün.</strong> Sürenin sonunda görsel silinir.
            Görselin parmak izi (özeti) kayıtta kalır: aynı görselin başka bir derste yeniden
            kullanılmasını yalnızca bu tespit ediyor. Hakkında açık bir anlaşmazlık varsa
            kanıt, karar verilene kadar silinmez.
          </li>
          <li>
            <strong>Hesap verileri:</strong> hesabın açık olduğu sürece.
          </li>
          <li>
            <strong>Mesajlar:</strong> konuşma silinene kadar.
          </li>
          {/* Süreler ve silme noktaları sunucudan: PushDevice.cs başındaki liste,
              RefreshToken ömrü (60 gün, her yenilemede baştan), temizlik işinin 30 gün /
              24 saat değerleri. Son cümledeki sınır BİLEREK yazılı — "çıkınca hemen biter"
              demek yanlış beyan olurdu.

              ⚠️ 2026-09-25'te düzeltildi: "uygulamayı kaldırırsan kayıt bir sonraki bildirim
              denemesinde silinir" diyordu. Deneme ancak oturum canlıyken oluyor: dağıtıcı
              yalnızca BAĞLI cihazları sorguluyor (DispatchNotifications → OturumBagi) ve
              oturumu kapanmış cihazın satırını silen bir iş YOKTU; satır hesap silinene kadar
              kalıyordu. Son cümlenin "en geç 7 gün"ü sunucuya aynı turda eklenen temizlikten:
              CleanupNotificationsHandler.BaglantisizCihazSaklama (5 gün, son başarılı kayıttan)
              + günde bir temizlik. O değer değişirse bu cümle ve web §5 birlikte değişir. */}
          <li>
            <strong>Bildirim kaydı (telefonunun bildirim adresi):</strong> o telefonda çıkış
            yapana kadar. Çıkış yaptığında, “her yerden çıkış” yaptığında ya da parolanı
            sıfırladığında, hesabın kalıcı olarak kapatıldığında ya da hesabını sildiğinde
            hemen silinir. Hesabın geçici olarak askıya alınırsa kayıt silinmez, yalnızca
            bildirim gönderilmez. İnternet yokken çıkış yaptıysan sunucu çıkışını o an
            öğrenemez: kayıt, uygulamayı internete bağlıyken bir sonraki açışında silinir;
            uygulamayı bir daha hiç açmazsan, o telefondaki oturumunun süresi dolana kadar
            (en fazla 60 gün) bu telefona bildirim gelmeye devam edebilir. Uygulamayı
            telefondan kaldırırsan adres geçersizleşir; oturumunun süresi dolmadan sana bir
            bildirim gönderilirse kayıt o gönderimde silinir. Oturumunun süresi, uygulamayı
            o telefonda son kullandığın andan 60 gün sonra dolar; bundan sonra o telefona
            bildirim gönderilmez. Bu yollardan hiçbiri işlemese de kayıt süresiz kalmaz: o
            telefondaki oturum kapandıktan ya da süresi dolduktan en geç 7 gün sonra silinir.
          </li>
          <li>
            <strong>Bildirim tercihlerin:</strong> hesabın açık olduğu sürece.
          </li>
          <li>
            <strong>Bildirim kayıtları: 30 gün.</strong> Bildirim gönderildikten ya da
            gönderilmeyeceği anlaşıldıktan 30 gün sonra silinir. Bildirim hizmetinin teslim
            makbuzları (bildirimin telefona ulaşıp ulaşmadığını gösteren kısa kayıt) ve aynı
            sohbetten art arda bildirim gitmesin diye tutulan son gönderim zamanı ise bir
            gün dolduktan sonraki ilk temizlikte silinir.
          </li>
          <li>
            <strong>Yedekler.</strong> Sistemi bir arıza ya da veri kaybından geri
            getirebilmek için düzenli yedek alıyoruz. Bir veri canlı sistemden silindiğinde{' '}
            <strong>o an</strong> silinir, ama daha önce alınmış yedeklerde bir süre daha
            durur. Sunucudaki yedekler <strong>en fazla 14 gün</strong> saklanır ve süresi
            dolanlar kendiliğinden silinir. Yedeklerin bir kopyası, sunucunun tümden
            kaybolduğu durumlara karşı ayrı bir bulut deposunda tutulur. Yedekler{' '}
            <strong>yalnızca</strong> geri yükleme amacıyla kullanılır; içlerinde arama
            yapmıyor, analiz etmiyor, kimseyle paylaşmıyoruz.
          </li>
        </Maddeler>
      </Bolum>

      <Bolum no="6" baslik="Kimlerle paylaşıyoruz">
        <p>
          Profilinde <strong>senin girdiğin</strong> bilgiler (adın, fotoğrafın, okulun, kendini
          anlattığın metin, anlatabildiğin konular, aldığın değerlendirmeler) platformdaki
          diğer kullanıcılara açıktır. E-posta adresin ve cihaz kimliğin{' '}
          <strong>hiçbir kullanıcıya gösterilmez</strong>.
        </p>
        <p>
          <strong>Arkadaş sayın</strong> profilinde herkese görünür. Tam arkadaş listeni
          yalnızca sen görürsün; başka bir kullanıcı profiline baktığında yalnızca{' '}
          <strong>ortak arkadaşlarınızı</strong> — yani zaten ikinizin de arkadaşı olan
          kişileri — görür. Engellediğin kişiler bu sayıya, tam listene ve ortak arkadaş
          listelerine girmez; Arkadaşlar ekranında ise arkadaşlığı sen sonlandırana kadar
          görünmeye devam eder.
        </p>
        {/* ⚠️ Son cümle web'den AYRIŞTI; web'in yetişmesi gerekiyor (Gizlilik.jsx §6).
            Eski hâli "bu sayıya ve listelere hiç girmez" diyordu. Oysa engelleme kabul
            edilmiş arkadaşlığı kapatmıyor (UserBlocks.cs) ve Arkadaşlar ekranının ucu
            (GetMyMatches) engel süzmüyor: engellenen arkadaş orada ve "Arkadaş (N)"
            sayısında duruyor. Metin gerçeğe daraltıldı, uç süzülmedi: Sonlandır düğmesi
            YALNIZCA o ekranda ve kişiyi oradan gizlemek o arkadaşlığı bitirmenin tek
            yolunu kaldırırdı. */}
        {/* Topluluk görünürlüğü (2026-09-27). Kaynak: CommunityController [Authorize] (giriş
            yapmamış kimse okuyamıyor), ForumAuthorDto (ad, seviye, yönetim işareti; fotoğraf
            kullanıcı kimliğiyle ayrıca çekiliyor), ForumPostDto yalnızca oy TOPLAMLARI ve
            isteyenin kendi oyu (MyVote) — kimin oy verdiği hiçbir uçta yok. Profil ucu katkı
            sayılarını veriyor (ProfileQueries: gönderi, yorum, aldığı net oy); başkasının
            profilinde ToplulukRozetleri onları ilk kademeden sonra çiziyor, metin bu yüzden
            "görünebilir". Şikayeti yapanın kimliği yalnızca yönetim kuyruğunda (Reports.cs).
            Web Gizlilik.jsx §6 ile BİREBİR aynı cümleler. */}
        <p>
          <strong>Topluluk’ta yazdığın gönderiler ve yorumlar</strong> adın, fotoğrafın ve
          seviyenle birlikte platformdaki diğer kullanıcılara açıktır; Topluluk’u yalnızca
          giriş yapmış kullanıcılar görür. Profilinde de Topluluk rozetin ve katkı sayıların
          (gönderi, yorum ve aldığın net oy) görünebilir. Verdiğin oyların yalnızca toplamı
          görünür, kimin oy verdiği gösterilmez. Şikayetlerini yalnızca yönetim görür; incelemeye alınan
          içerikte kimin şikayet ettiği değil, yalnızca kaç şikayet aldığı yazar.
        </p>
        <p>
          Görünen adınla <strong>aranabilirsin</strong>: Keşfet’teki “Arkadaş Ekle”
          bölümünde adını bilen bir kullanıcı seni bulup istek gönderebilir. Bu, profilini
          doldurmamış olsan da geçerlidir. İstemediğin kişiyi{' '}
          <strong>engelleyebilirsin</strong> — engellediğin kişi seni aramada göremez,
          sana istek gönderemez ve açık sohbetinize yazamaz. Engellediğin karşı tarafa
          bildirilmez.
        </p>
        {/* ⛔ 2026-09-25'e kadar bu paragraf "uygulamanın konuştuğu tek sunucu dersmate'in
            kendi sunucusudur", altındaki "uygulama doğrudan hiçbir üçüncü tarafa
            bağlanmasa da" diyordu. Push ile ikisi de YANLIŞ oldu: bildirimler açılınca
            telefon Expo'ya ve işletim sisteminin bildirim hizmetine bağlanıyor. */}
        <p>
          Verini pazarlama amacıyla üçüncü taraflara <strong>aktarmıyoruz</strong> ve
          satmıyoruz. Mobil uygulama, verini dışarı taşıyan hiçbir reklam ağı, analitik ya
          da çökme raporlama bileşeni <strong>içermez</strong>. Uygulama dersmate’in kendi
          sunucusuyla konuşur; <strong>tek istisna bildirimlerdir</strong>: bildirimleri
          açarsan telefonun, bildirim adresini almak ve güncel tutmak için Expo’ya ve
          telefonunun bildirim hizmetine (Android’de Google, iPhone’da Apple) bağlanır.
        </p>
        <p>
          <strong>Hizmet sağlayıcılarımız (veri işleyenler).</strong> dersmate’in sunucusu
          platformu çalıştırabilmek için birkaç dış hizmetten yararlanıyor. Bunlar verini{' '}
          <strong>bizim adımıza ve yalnızca aşağıdaki amaçla</strong> işler; kendi amaçları
          için kullanamazlar.
        </p>
        <Maddeler>
          <li>
            <strong>Sunucu barındırma.</strong> Platformun sunucusunu ve veritabanını
            barındıran hizmet sağlayıcı. Hesap verilerinin tamamı burada tutulur.
          </li>
          <li>
            <strong>E-posta gönderimi — Resend.</strong> Doğrulama kodu, parola sıfırlama ve
            bildirim e-postalarını iletir. Ona giden veri: e-posta adresin ve iletinin
            içeriği.
          </li>
          <li>
            <strong>Yedek deposu — Google Drive.</strong> Yedeklerin sunucu dışındaki kopyası
            burada tutulur (bkz. §5).
          </li>
          {/* Taşıyıcı cümlesi aydınlatma sorusu (BildirimIzniSorusu → TASIYICI_METNI) ve
              Bildirim ayarları ekranıyla AYNI olguyu söylemeli: kullanıcı "Aç"a basarken
              okuduğundan farklı bir şeyi burada bulmamalı. */}
          <li>
            <strong>
              Bildirim iletimi — Expo, Google (Firebase Cloud Messaging) ve Apple (Apple Push
              Notification service).
            </strong>{' '}
            Yalnızca bildirimleri açtıysan. Sunucumuz bildirimi Expo’ya verir; Expo onu
            Android’de Google’ın, iPhone’da Apple’ın bildirim hizmeti üzerinden telefonuna
            ulaştırır. Onlara giden veri: telefonunun bildirim adresi; bildirimin başlığı ve
            metni; açıldığında doğru ekrana gidebilmek için bildirimin türü ve{' '}
            <strong>anlamsız, rastgele bir kayıt numarası</strong> (sohbetin ya da dersin
            numarası); aynı telefonda başka bir hesap açıkken bildirimin gösterilmemesi için
            hesabından türetilen ve geri çözülemeyen kısa bir etiket. Bildirim adresini
            alırken uygulama Expo’ya ayrıca bildirim bileşeninin rastgele kurulum numarasını
            gönderir. Mesajlarının içeriği bildirimlere <strong>hiçbir zaman</strong> girmez;
            kişi adı yalnızca yeni mesaj ve kabul edilen istek bildiriminde, arkadaşının
            görünen adı olarak geçer. Diğer istek bildirimlerinde ve ders bildirimlerinde
            kimsenin adı geçmez; dersin ya da isteğin konusu geçebilir. E-posta adresin ve
            cihaz kimliği özetin
            bu hizmetlere gönderilmez.
          </li>
        </Maddeler>
        <p>
          <strong>Kilit ekranı.</strong> Telefonunun kilit ekranı ayarına göre bildirimin
          başlığı görünebilir; içeriğini telefon ayarlarından gizleyebilirsin. iPhone’da:
          Ayarlar › Bildirimler › Önizlemeleri Göster. Gece 22.00–09.00 arasında acil olmayan
          bildirimler sabaha kalır; yeni mesaj ve yaklaşan ders bildirimleri beklemez.
        </p>
        <p>
          <strong>Yurt dışına aktarım.</strong> Bu sağlayıcıların bir kısmı sunucularını{' '}
          <strong>Türkiye dışında</strong> işletiyor. Bu, KVKK m.9 anlamında yurt dışına
          aktarım sayılır ve hesap açarken verdiğin onay bunu da kapsar; bildirim iletimi
          için yapılan aktarım ise yalnızca bildirimleri açtığında başlar. Aktarılan veri,
          her sağlayıcı için yalnızca o hizmetin gerektirdiği kadarıdır: e-posta gönderimi
          için adresin ve iletinin içeriği, yedekleme için yedek dosyalarının kendisi,
          bildirim iletimi için yukarıda sayılan bildirim bilgileri.
        </p>
        <p>
          Bu listeyi değiştirdiğimizde metni günceller ve üstteki tarihi değiştiririz
          (bkz. §9).
        </p>
      </Bolum>

      <Bolum no="7" baslik="Haklarını nasıl kullanırsın">
        <p>
          KVKK kapsamında verine erişme, düzeltme, silinmesini isteme ve işlenmesine itiraz
          etme hakkın var.
        </p>
        <Maddeler>
          <li>
            <strong>Düzeltme:</strong> profil bilgilerinin çoğunu Profil › Ayarlar › “Profili
            düzenle”den değiştirebilirsin; profil fotoğrafını Profil ekranında fotoğrafına
            dokunarak değiştirirsin.
          </li>
          {/* YOL (2026-09-26): ayarlar Profil ekranından Ayarlar ekranına taşındı; Profil'e
              sol üstteki menüden (çekmece başlığındaki ad), Ayarlar'a Profil'in sağ
              üstündeki dişliden gidiliyor. "Profil sekmesi" 2026-09-23'te, "Profil ekranının
              en altı" 2026-09-26'da bayatladı. Web HesapSilme §1 aynı yolu anlatıyor; biri
              değişirse ikisi birlikte. Silinenler listesi app/ayarlar.jsx → HesabiSilModali
              "Silinecekler" ile, kalanlar "Kalacaklar" ile aynı olmalı.
              Topluluk içeriği KALIYOR (2026-09-27'de yazıldı): DeleteAccount forum
              tablolarına dokunmuyor, yalnızca adı "Silinmiş kullanıcı" yapıyor ve fotoğrafı
              siliyor; akış yazarın durumuna bakmıyor, yani gönderi ve yorum o adla görünmeye
              devam ediyor. Sunucu davranışı değişirse bu cümle, Kalacaklar ve web HesapSilme
              §3 birlikte değişir. */}
          <li>
            <strong>Silme:</strong> hesabını <strong>kendin silebilirsin</strong> — Profil ›
            Ayarlar › “Hesabımı sil” (Profil’e sol üstteki menüden adına dokunarak, Ayarlar’a
            Profil ekranının sağ üstündeki dişli simgesiyle gidersin). Onay için parolan
            yeniden sorulur ve işlem geri alınamaz.
            Kimlik bilgilerin siliniyor; bildirim ayarların, bildirim kayıtların ve bildirim
            alan cihazların da siliniyor. Ders geçmişi, kazandırdığın puanlar ve
            değerlendirmeler karşı tarafa ait olduğu için kalıyor ve orada adın yerine
            “Silinmiş kullanıcı” görünüyor. Topluluk’taki gönderilerin, yorumların ve oyların
            da kalıyor; gönderi ve yorumlarında adın yerine “Silinmiş kullanıcı” görünüyor.
            Yedeklerdeki kopyaların ne zaman düştüğü §5’te yazılı.
          </li>
          <li>
            <strong>Bildirimleri kapatma:</strong> Profil › Ayarlar › Bildirim ayarları’ndan
            bildirim türlerini tek tek kapatabilirsin; kapattığın türler sana hiç gönderilmez.
            Bildirimleri telefonunun ayarlarından da tamamen kapatabilirsin. Bu telefonun
            bildirim kaydını sunucudan kaldırmak için çıkış yapman yeterli (bkz. §5).
          </li>
          <li>
            <strong>Erişim ve hesabına giremiyorsan:</strong> verinin bir kopyasını alma
            talebini ya da hesabına hiç erişemediğin durumda silme talebini aşağıdaki
            adrese ilettiğinde işleme alıyoruz.
          </li>
        </Maddeler>
        <a
            href={`mailto:${ILETISIM_EPOSTA}`}
            className="font-medium text-brand-700 underline underline-offset-2 hover:text-brand-800"
          >
            {ILETISIM_EPOSTA}
          </a>
      </Bolum>

      <Bolum no="8" baslik="Yaş">
        <p>
          Platform lise ve üniversite öğrencilerine yönelik. 18 yaşından küçüksen hesabını
          velinin bilgisi ve onayıyla açmalısın. Kayıt sırasında bunu beyan etmeni istiyoruz.
        </p>
      </Bolum>

      {/*
        Web'in kapanışı "çerez tercihini etkileyen değişiklikte seçimi yeniden soruyoruz"
        diyor; mobilde çerez seçimi olmadığı için o cümlenin karşılığı yok. Yerine
        UYDURULMUŞ bir vaat (ör. "uygulama içinde bildiririz") yazılmadı — kodda karşılığı
        olmayan koruma sözü, bu metinlerin en tehlikeli hatası. Yazılan şey gerçekten
        işleyen mekanizma: sürüm değişince yeni kayıtlar yeni metni onaylıyor
        (LegalDocuments.CurrentVersion), eski onaylar kendiliğinden geçersizleşmiyor.
      */}
      <Bolum no="9" baslik="Değişiklikler">
        <p>
          Bu metin değişirse yayınlanma tarihini güncelliyoruz ve yeni metin uygulamanın
          bir sonraki sürümüyle gelir. Değişiklikten sonra kayıt olan herkes yeni metni
          onaylar; daha önce verdiğin onay, onayladığın tarihle birlikte kayıtlıdır.
        </p>
      </Bolum>
    </MetinSayfasi>
  )
}
