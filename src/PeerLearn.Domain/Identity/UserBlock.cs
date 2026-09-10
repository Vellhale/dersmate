using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Identity;

/// <summary>
/// Bir kullanıcının başka bir kullanıcıyı engellemesi. Tek yönlü kayıt, ÇİFT YÖNLÜ etki.
/// </summary>
/// <remarks>
/// ─── NEDEN identity ŞEMASINDA, moderation'DA DEĞİL ──────────────────────────
/// moderation şemasındaki beş tablonun (Reports, Disputes, AdminActionLogs, HwidBans,
/// UserSanctions) hepsinde ikinci bacak YÖNETİMDİR: bir admin aktörü, bir denetim izi ya
/// da bir admin kuyruğu var. Engelleme ise kullanıcı↔kullanıcı simetrik bir ilişki;
/// yönetim hiç haberdar olmuyor, bir yaptırım değil bir tercih.
///
/// Bu depoda şema, sınıfın durduğu klasöre değil VERİNİN KİME AİT OLDUĞUNA göre seçiliyor
/// — emsali <c>TeacherCandidateProfile</c>: Domain/Community altında duruyor ama tablosu
/// identity şemasında. Engelleme de kullanıcının kendi hesabına ait bir kayıt, o yüzden
/// Users/UserPreferences/UserDevices üçlüsünün yanına oturuyor.
///
/// ─── NEDEN ŞİMDİ GEREKLİ OLDU ───────────────────────────────────────────────
/// Tanışma isteğinin eski kapısı "karşı taraf üniversitesini girmiş olmalı" idi ve
/// gerekçesi <c>MatchRequests</c> içinde yazılıydı:
///
///   "Bu kapı olmadan uç, herhangi bir kullanıcıya doğrudan mesaj isteği hâline gelirdi
///    — bu üründe ENGELLEME/BLOK MEKANİZMASI OLMADIĞI için bunun bedeli yüksek olurdu."
///
/// Kapı, isimle arama gelince kaldırıldı. Yani o cümlenin işaret ettiği bedel şimdi
/// gerçek: engelleme, aramanın açılmasının ÖN KOŞULU. İkisi ayrı ayrı sevk edilemez.
///
/// ─── TEK YÖNLÜ KAYIT, ÇİFT YÖNLÜ ETKİ ───────────────────────────────────────
/// A, B'yi engellediğinde tek satır yazılıyor (A → B). Ama kontroller HER İKİ YÖNÜ de
/// sorguluyor: B de A'ya istek gönderemiyor, ikisi de birbirine yazamıyor.
///
/// Alternatif — yalnızca engelleyen yönü kapatmak — engellemeyi işlevsiz kılardı:
/// rahatsız eden taraf istek göndermeye devam edebilirdi. Karşılıklı iki satır yazmak da
/// yanlış olurdu: B "engelledim" demediği hâlde engellemiş görünür, engeli kaldırma
/// hakkı da ona geçerdi.
///
/// ⚠️ ENGELLEME GİZLİ DEĞİL AMA İLAN DA EDİLMİYOR. Şikayet (<c>Report</c>) bu üründe tek
/// yönlü ve gizli: şikayet edilen hiçbir yerde görmüyor. Engelleme farklı bir şey —
/// karşı taraf istek gönderemediğinde bunu dolaylı olarak anlar. Yine de ona "engellendin"
/// diyen bir bildirim YOK; hata mesajı nötr tutuluyor ki engelleme misillemeye dönüşmesin.
/// </remarks>
public class UserBlock : BaseEntity
{
    /// <summary>
    /// Notun en fazla uzunluğu.
    /// </summary>
    /// <remarks>
    /// SUNUCUDA DA DOĞRULANIYOR (BlockUserHandler) ve yapılandırmadaki
    /// <c>HasMaxLength(500)</c> ile AYNI olmak zorunda. Ayrışırlarsa iki kötü sonuçtan
    /// biri olur: ya kolon sınırına çarpılıp kullanıcıya 500 Internal Server Error döner
    /// (girdi hatası sunucu hatası gibi görünür), ya da geçerli bir not boşuna reddedilir.
    /// Arayüzdeki <c>maxLength</c> bir kolaylık, güvence değil — uç doğrudan çağrılabiliyor.
    /// </remarks>
    public const int NotEnFazla = 500;

    /// <summary>Engelleyen (kaydı oluşturan) kullanıcı.</summary>
    public Guid BlockerUserId { get; set; }

    /// <summary>Engellenen kullanıcı.</summary>
    public Guid BlockedUserId { get; set; }

    /// <summary>
    /// Kullanıcının kendi tuttuğu not — yalnızca engelleyene görünür, moderasyona gitmez.
    /// </summary>
    /// <remarks>
    /// Bu bir ŞİKAYET DEĞİL. Şikayet ayrı bir akış (<c>Report</c>) ve yönetime gidiyor.
    /// İkisini karıştırmamak önemli: engelleme kişisel bir tercih, şikayet bir ihbar.
    /// Kullanıcı taciz bildirmek istiyorsa şikayet yolunu kullanmalı; arayüz engelleme
    /// sonrası ona bu yolu ayrıca öneriyor.
    /// </remarks>
    public string? Note { get; set; }
}
