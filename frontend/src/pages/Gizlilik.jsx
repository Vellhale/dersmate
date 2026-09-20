import { Link } from 'react-router-dom'
import { Bolum, Maddeler, MetinSayfasi } from './MetinSayfasi'
import { SOZLESME_TARIHI } from '../lib/yasalMetinler'
import { ISLETMECI, ISLETMECI_ADRESI, ISLETMECI_ALAN_ADI, MARKA } from '../lib/kunye'

/*
  GİZLİLİK POLİTİKASI + KVKK AYDINLATMA METNİ.

  Metnin tamamı KODU OKUYARAK yazıldı; hiçbir madde tahmin değil:
    • Toplanan alanlar        → Domain/Identity/User.cs, UserDevice
    • Cihaz parmak izi        → lib/hwid.js (ne topluyor, satır satır)
    • Çerez kategorileri      → lib/consent.js, state/ConsentContext.jsx
    • Kanıt saklama süresi    → Features/Moderation/CleanupStorage.cs (180 gün)
    • Analitik davranışı      → state/AnalyticsGate.jsx (rıza yoksa script hiç yüklenmiyor)

  ✅ HESAP SİLME ARTIK VAR: POST /api/profile/delete (DeleteAccountHandler). §7 buna
  göre güncellendi ve ayrıntılı anlatım /hesap-silme sayfasına taşındı — mağaza kaydının
  istediği, uygulamayı kurmadan açılabilen adres orası.

  ⚠️ Sunucu kaydı ANONİMLEŞTİRİYOR, satırı yok etmiyor: identity.Users'a 23 yabancı
  anahtar bakıyor ve çoğu karşı tarafa ait (ders geçmişi, puanlar, değerlendirmeler).
  Metin bunu gizlemiyor; "her şey silinir" demek yanlış olurdu.

  ✅ SÜRÜM BORCU KAPANDI (2026-09-19). 2026-09-10'dan beri burada duran uzun not,
  §6'daki arkadaş sayısı + ortak arkadaşlar ifşasının sözleşme sürümüne yansımadığını
  kaydediyordu. Sürüm 2026-09-19'a çekildi ve borç kapandı; notun kendisi, kapandığında
  sadeleştirilmesini söylüyordu — bu paragraf o sadeleştirme. Ayrıntılı gerekçe
  (neden isimle aranabilirlik sürüm gerektirmiyordu ama arkadaş grafı gerektiriyordu)
  yasalMetinler.js ve LegalDocuments.cs'teki sürüm notlarına taşındı.

  ⚠️ SÜRÜM ARTIRMAK HÂLÂ MOBİL BİR YAYIN KAPISI. Borcun kapanması kuralı kaldırmıyor:
  SOZLESME_SURUMU sunucudaki sabitle birebir eşleşmek zorunda, mobil uygulama kendi
  kopyasını PAKETE GÖMÜLÜ taşıyor ve AYRI BİR DEPODA yazılıyor. Bu artış mobil mağazada
  henüz uygulama YOKKEN yapıldı, yani kimseyi kilitlemedi — ama mobil depodaki kopya bu
  değere çekilmeden ilk APK yayınlanırsa o kullanıcılar kayıt olamaz.

  SONRAKİ ARTIŞTA SIRA (mağazada uygulama varken):
    1. mobil depodaki src/lib/yasalMetinler.js sürümünü artır
    2. yeni APK'yı yayınla (mağaza incelemesi dahil)
    3. LegalDocuments.CurrentVersion + web yasalMetinler.js'i artır
    4. sunucuyu dağıt

  ⚠️ METİN HÂLÂ TASLAK. Sürüm borcunun kapanması metnin hukuken tamamlandığı anlamına
  GELMEZ. §1 artık veri sorumlusunu adıyla söylüyor ama tescil bilgileri (ticari unvan,
  adres, MERSIS) lib/kunye.js'te BOŞ ve bilerek boş — uydurulmadı. MetinSayfasi'ndeki
  "Taslak metin" uyarısı bu yüzden yerinde duruyor; o üç değer doldurulup metin bir
  hukukçuya okutulmadan kaldırılmamalı.
*/
export default function Gizlilik() {
  return (
    <MetinSayfasi
      baslik="Gizlilik ve KVKK aydınlatma metni"
      ozet="Hangi verini topluyoruz, neden topluyoruz, ne kadar saklıyoruz ve ne isteyebilirsin."
      sonGuncelleme={SOZLESME_TARIHI}
    >
      {/*
        ⚠️ BÖLÜM NUMARALARI DEĞİŞTİRİLEMEZ — yalnızca BAŞLIK değişti (2026-09-19).

        Veri sorumlusu kimliği bir KVKK aydınlatma metninin İLK maddesidir, bu yüzden
        doğal yeri yeni bir §1 açmaktı. AÇILMADI: §4, §5, §6, §7 ve §9'a hem bu metnin
        içinden hem docs/DEVAM-EDILECEK.md'den atıf yapılıyor ve araya bölüm eklemek
        hepsini bir kaydırıp SESSİZCE yanlış maddeye işaret ettirirdi — yasal bir
        metinde yanlış atıf, eksik atıftan kötüdür.

        Bunun yerine mevcut §1 genişletildi ve başlığına "veri sorumlusu" eklendi:
        numaralar yerinde kaldı, kimlik aranabilir oldu. Tam künye ayrıca sayfanın
        altında (MetinSayfasi → KunyeBlogu) ve tescil bilgileri doldurulduğunda orada
        kendiliğinden görünecek.
      */}
      <Bolum no="1" baslik="Kısaca ve veri sorumlusu">
        {/* Kimlik ÖNCE: "bu veriyi kim işliyor" sorusu, "ne işliyor" sorusundan önce
            gelir. ISLETMECI sabitten okunuyor, elle yazılmıyor — künye beş yüzeyde
            görünüyor ve birinin ayrışması iki farklı kimlik göstermek demek. */}
        <p>
          <strong>{MARKA}</strong>, öğrencilerin birbirine ders anlattığı bir
          platformdur ve{' '}
          <a
            href={ISLETMECI_ADRESI}
            target="_blank"
            rel="noopener noreferrer"
            className="font-medium text-brand-700 hover:underline"
          >
            {ISLETMECI}
          </a>{' '}
          ({ISLETMECI_ALAN_ADI}) tarafından işletilmektedir. Bu metinde geçen “biz”,
          {' '}{ISLETMECI}’tir; verinle ilgili taleplerin muhatabı da odur. İletişim
          bilgileri sayfanın altındaki künyededir.
        </p>
        <p>
          Verini reklam için kullanmıyoruz, satmıyoruz ve üçüncü taraflara pazarlama
          amacıyla aktarmıyoruz. Topladığımız her şey ya hesabını çalıştırmak ya da
          platformu kötüye kullanımdan korumak için.
        </p>
      </Bolum>

      <Bolum no="2" baslik="Topladığımız veriler">
        <p>
          <strong>Hesap bilgileri:</strong> e-posta adresin, adın (görünen ad), şifrenin
          geri döndürülemez özeti (hash). Şifreni düz metin olarak hiçbir yerde
          saklamıyoruz.
        </p>
        <p>
          <strong>İsteğe bağlı profil bilgileri:</strong> profil fotoğrafın, kendini
          anlattığın metin, okulun ve bölümün, telefon numaran. Bunların hiçbiri zorunlu
          değildir; boş bırakabilirsin.
        </p>
        <p>
          <strong>Kullanım verileri:</strong> anlattığın ders sayısı ve süresi,
          kazandığın puan, aldığın değerlendirmeler, son giriş zamanın.
        </p>
        <p>
          <strong>İçerik:</strong> arkadaşlarınla yazıştığın mesajlar ve dersin
          yapıldığını gösteren kanıt görselleri.
        </p>
        <p>
          <strong>Cihaz kimliği (önemli):</strong> giriş yaptığında tarayıcından bir
          cihaz parmak izi üretiyoruz. Bu parmak izi şu bilgilerin birleştirilip
          geri döndürülemez biçimde özetlenmesiyle oluşuyor: tarayıcı sürümün, dil
          ayarların, saat dilimin, ekran çözünürlüğün, işlemci çekirdek sayın, dokunmatik
          desteğin ve tarayıcının bir çizim testine verdiği sonuç. Bu bilgilerin
          kendisini değil, yalnızca özetini saklıyoruz.
        </p>
      </Bolum>

      <Bolum no="3" baslik="Neden topluyoruz">
        <Maddeler>
          <li>
            <strong>Hesabını çalıştırmak için:</strong> e-posta, ad, şifre özeti. Bunlar
            olmadan giriş yapamazsın.
          </li>
          <li>
            <strong>Arkadaşlık ve ders için:</strong> profil bilgilerin ve konu tercihlerin
            — kimin kime ders anlatabileceğini bunlar belirliyor.
          </li>
          <li>
            <strong>Kötüye kullanımı önlemek için:</strong> cihaz kimliği. Kuralları ağır
            biçimde ihlal eden bir hesap kapatıldığında, aynı kişinin hemen yeni hesap
            açıp devam etmesini engelleyen tek şey bu. Öğrencilerin bir arada olduğu bir
            platformda bu korumanın karşılığı somut.
          </li>
          <li>
            <strong>Anlaşmazlıkları çözmek için:</strong> ders kanıtları ve şikayet
            kayıtları.
          </li>
        </Maddeler>
      </Bolum>

      <Bolum no="4" baslik="Çerezler">
        <p>
          İlk girişte üç kategori sunuyoruz ve seçimini istediğin zaman
          değiştirebilirsin (sayfa altındaki “Çerez ayarları”).
        </p>
        <Maddeler>
          <li>
            <strong>Zorunlu:</strong> oturumunu açık tutan ve cihaz kimliğini taşıyan
            kayıtlar. Bunlar kapatılamaz; kapatılırsa giriş yapılamaz.
          </li>
          <li>
            <strong>Fonksiyonel:</strong> arayüz tercihlerin (ör. yan menünün dar mı
            geniş mi açılacağı). Reddedersen bu tercihler her açılışta sıfırlanır.
          </li>
          <li>
            <strong>Analitik:</strong> hangi sayfaların kullanıldığını anlamamızı
            sağlayan ölçüm. <strong>İzin vermezsen ölçüm kodu hiç yüklenmez</strong> —
            “yüklenir ama veri göndermez” değil, sayfaya hiç eklenmez. Daha önce izin
            verip sonra geri aldıysan ilgili çerezleri siliyoruz.
          </li>
        </Maddeler>
      </Bolum>

      <Bolum no="5" baslik="Ne kadar saklıyoruz">
        <Maddeler>
          <li>
            <strong>Ders kanıt görselleri: 180 gün.</strong> Sürenin sonunda görsel
            silinir. Görselin parmak izi (özeti) kayıtta kalır: aynı görselin başka bir
            derste yeniden kullanılmasını yalnızca bu tespit ediyor. Hakkında açık bir
            anlaşmazlık varsa kanıt, karar verilene kadar silinmez.
          </li>
          <li>
            <strong>Hesap verileri:</strong> hesabın açık olduğu sürece.
          </li>
          <li>
            <strong>Mesajlar:</strong> konuşma silinene kadar.
          </li>
          <li>
            <strong>Yedekler.</strong> Sistemi bir arıza ya da veri kaybından geri
            getirebilmek için düzenli yedek alıyoruz. Bir veri canlı sistemden silindiğinde
            <strong> o an</strong> silinir, ama daha önce alınmış yedeklerde bir süre daha
            durur. Sunucudaki yedekler <strong>en fazla 14 gün</strong> saklanır ve süresi
            dolanlar kendiliğinden silinir. Yedeklerin bir kopyası, sunucunun tümden
            kaybolduğu durumlara karşı ayrı bir bulut deposunda tutulur. Yedekler
            <strong> yalnızca</strong> geri yükleme amacıyla kullanılır; içlerinde arama
            yapmıyor, analiz etmiyor, kimseyle paylaşmıyoruz.
          </li>
        </Maddeler>
      </Bolum>

      <Bolum no="6" baslik="Kimlerle paylaşıyoruz">
        <p>
          Profilinde <strong>senin girdiğin</strong> bilgiler (adın, fotoğrafın,
          okulun, kendini anlattığın metin, anlatabildiğin konular, aldığın
          değerlendirmeler) platformdaki diğer kullanıcılara açıktır. E-posta adresin,
          telefon numaran ve cihaz kimliğin <strong>hiçbir kullanıcıya gösterilmez</strong>.
        </p>
        <p>
          <strong>Arkadaş sayın</strong> profilinde herkese görünür. Tam arkadaş listeni
          yalnızca sen görürsün; başka bir kullanıcı profiline baktığında yalnızca
          <strong>ortak arkadaşlarınızı</strong> — yani zaten ikinizin de arkadaşı olan
          kişileri — görür. Engellediğin kişiler bu sayıya ve listelere hiç girmez.
        </p>
        <p>
          Görünen adınla <strong>aranabilirsin</strong>: Keşfet’teki “Arkadaş Ekle”
          bölümünde adını bilen bir kullanıcı seni bulup istek gönderebilir. Bu, profilini
          doldurmamış olsan da geçerlidir. İstemediğin kişiyi{' '}
          <strong>engelleyebilirsin</strong> — engellediğin kişi seni aramada göremez,
          sana istek gönderemez ve açık sohbetinize yazamaz. Engellediğin karşı tarafa
          bildirilmez.
        </p>
        <p>
          Verini pazarlama amacıyla üçüncü taraflara <strong>aktarmıyoruz</strong> ve
          satmıyoruz.
        </p>

        <h3 className="mt-6 text-base font-semibold text-slate-900">
          Hizmet sağlayıcılarımız (veri işleyenler)
        </h3>
        <p>
          Platformu çalıştırabilmek için birkaç dış hizmetten yararlanıyoruz. Bunlar
          verini <strong>bizim adımıza ve yalnızca aşağıdaki amaçla</strong> işler; kendi
          amaçları için kullanamazlar.
        </p>
        <Maddeler>
          <li>
            <strong>Sunucu barındırma.</strong> Platformun sunucusunu ve veritabanını
            barındıran hizmet sağlayıcı. Hesap verilerinin tamamı burada tutulur.
          </li>
          <li>
            <strong>E-posta gönderimi — Resend.</strong> Doğrulama kodu, parola sıfırlama
            ve bildirim e-postalarını iletir. Ona giden veri: e-posta adresin ve iletinin
            içeriği.
          </li>
          <li>
            <strong>Yedek deposu — Google Drive.</strong> Yedeklerin sunucu dışındaki
            kopyası burada tutulur (bkz. §5).
          </li>
          <li>
            <strong>Analitik — Google Analytics.</strong> <strong>Yalnızca analitik
            çerezlere izin verirsen</strong> devreye girer ve sayfa kullanım düzeyinde
            ölçüm yapar. İzin vermezsen hiçbir istek gönderilmez. İznini istediğin zaman
            geri alabilirsin (bkz. §4).
          </li>
        </Maddeler>

        <h3 className="mt-6 text-base font-semibold text-slate-900">
          Yurt dışına aktarım
        </h3>
        <p>
          Yukarıdaki sağlayıcıların bir kısmı sunucularını <strong>Türkiye dışında</strong>{' '}
          işletiyor. Bu, KVKK m.9 anlamında yurt dışına aktarım sayılır ve hesap açarken
          verdiğin onay bunu da kapsar. Aktarılan veri, her sağlayıcı için yalnızca o
          hizmetin gerektirdiği kadarıdır: e-posta gönderimi için adresin ve iletinin
          içeriği, yedekleme için yedek dosyalarının kendisi, analitik için —
          <strong> izin verdiysen</strong> — sayfa kullanım ölçümleri.
        </p>
        <p>
          Bu listeyi değiştirdiğimizde metni günceller ve üstteki tarihi değiştiririz
          (bkz. §9).
        </p>
      </Bolum>

      <Bolum no="7" baslik="Haklarını nasıl kullanırsın">
        <p>
          KVKK kapsamında verine erişme, düzeltme, silinmesini isteme ve işlenmesine
          itiraz etme hakkın var.
        </p>
        <Maddeler>
          <li>
            <strong>Düzeltme:</strong> profil bilgilerinin çoğunu doğrudan “Profili
            düzenle” ekranından değiştirebilirsin.
          </li>
          <li>
            <strong>Silme:</strong> hesabını kendin silebilirsin — Profil sayfasının
            (mobilde Profil sekmesinin) en altındaki “Hesabımı sil” bağlantısı. Onay için
            parolan yeniden sorulur ve işlem geri alınamaz. Kimlik bilgilerin siliniyor;
            ders geçmişi, kazandırdığın puanlar ve değerlendirmeler karşı tarafa ait
            olduğu için kalıyor ve orada adın yerine “Silinmiş kullanıcı” görünüyor.
            Adım adım anlatım:{' '}
            <Link to="/hesap-silme" className="font-medium text-brand-700 hover:underline">
              hesabını silme
            </Link>
            .
          </li>
          <li>
            <strong>Erişim ve hesabına giremiyorsan:</strong> verinin bir kopyasını alma
            talebini ya da hesabına hiç erişemediğin durumda silme talebini{' '}
            <a href="mailto:iletisim@dersmate.com" className="font-medium text-brand-700 hover:underline">
              iletisim@dersmate.com
            </a>{' '}
            adresine ilettiğinde işleme alıyoruz.
          </li>
        </Maddeler>
      </Bolum>

      <Bolum no="8" baslik="Yaş">
        <p>
          Platform lise ve üniversite öğrencilerine yönelik. 18 yaşından küçüksen
          hesabını velinin bilgisi ve onayıyla açmalısın. Kayıt sırasında bunu beyan
          etmeni istiyoruz.
        </p>
      </Bolum>

      <Bolum no="9" baslik="Değişiklikler">
        <p>
          Bu metin değişirse yayınlanma tarihini güncelliyoruz. Çerez tercihini
          etkileyen bir değişiklik olursa çerez seçimini yeniden soruyoruz.
        </p>
      </Bolum>
    </MetinSayfasi>
  )
}
