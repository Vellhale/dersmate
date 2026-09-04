using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

/// <summary>
/// HESABI SİL — kullanıcının kendi talebiyle.
///
/// NEDEN VAR: Google Play, hesap açtıran uygulamalarda hem uygulama içinde hem de herkese
/// açık bir adreste hesap silme yolu ZORUNLU tutuyor; KVKK/GDPR'ın silme hakkı da aynı
/// şeyi istiyor. Bu uç olmadan uygulama mağazaya kabul edilmiyordu.
///
/// ── SATIR SİLİNMİYOR, KİŞİSEL VERİ SİLİNİYOR ────────────────────────────────────────
///
/// identity.Users'a 23 yabancı anahtar bakıyor ve çoğu RESTRICT: ders oturumları,
/// eşleşmeler, mesajlar, değerlendirmeler, kredi defteri, şikayet ve yaptırım kayıtları.
/// Bunların büyük kısmı KARŞI TARAFA ait — silinen kullanıcının satırını gerçekten
/// kaldırmak, başka birinin ders geçmişini ve kazandığı puanı yok etmek olurdu. Kredi
/// defteri de tutarsızlaşırdı: puan basılmış ama basıldığı ders ortadan kalkmış olurdu.
///
/// Bu yüzden yapılan şey ANONİMLEŞTİRME: kimliği taşıyan her alan temizleniyor, kaydın
/// kendisi mezar taşı olarak kalıyor. Karşı taraf geçmişinde "Silinmiş kullanıcı" görüyor;
/// dersi, puanı ve değerlendirmesi yerinde duruyor.
///
/// ── NE SİLİNİYOR, NE KALIYOR ────────────────────────────────────────────────────────
///
/// SİLİNEN (kişiyi tanımlayan veri):
///   • Ad, e-posta, telefon, biyografi, üniversite/bölüm, profil fotoğrafı
///   • Parola özeti — kullanılamaz bir değerle değiştiriliyor, giriş imkânsız
///   • Veri tercihleri (UserPreferences) ve öğretmen adaylığı beyanı/belgesi
///   • İlanlar (PortfolioEntries) — aktif arz/talep, silinen hesapta keşfette durmamalı
///   • Cihaz kayıtları (UserDevices) — BANLI HESAPLAR HARİÇ, aşağıya bakın
///
/// KALAN (kişiyi tanımlamayan ya da başkasına ait kayıt):
///   • Ders oturumları, eşleşmeler, mesaj satırları, değerlendirmeler
///   • Cüzdan ve kredi defteri — ledger'a dokunmak karşı tarafın bakiyesini de ilgilendirir
///   • Şikayet, itiraz, yaptırım ve yönetici denetim izi — hesap verebilirlik kaydı
///
/// ── BANLI KULLANICI ─────────────────────────────────────────────────────────────────
///
/// Silme ENGELLENMİYOR (silme hakkı bir yaptırımla kaldırılmaz), ama banlı bir hesabın
/// CİHAZ KAYITLARI korunuyor. Ban zaten HwidBans tablosuna yazılmış durumda ve o tablonun
/// Users'a yabancı anahtarı yok, yani ban silmeden etkilenmiyor. Cihaz kayıtlarını da
/// tutmak, yönetimin geçmişe dönük izleyebilmesi için: bu meşru menfaat kapsamında ve
/// HWID cihazı tanımlar, kişiyi değil.
///
/// ⚠️ E-POSTA SERBEST KALIYOR. Mezar taşı e-postası benzersizlik indeksini işgal etmiyor,
/// yani aynı adresle yeniden kayıt olunabilir. Bu NORMAL kullanıcı için doğru davranış
/// (geri dönebilmeli); banlı kullanıcı için kaçış yolu değil, çünkü kayıt ve giriş
/// akışları HWID banına bakıyor.
/// </summary>
public sealed record DeleteAccountCommand(Guid UserId, string Password) : IRequest<DeleteAccountResult>;

/// <param name="AvatarStorageKey">
/// Silinecek profil fotoğrafının depo anahtarı (yoksa null). Dosya silme işlemi
/// veritabanı işlemiyle atomik olmadığı için burada DÖNDÜRÜLÜYOR ve çağıran tarafından
/// commit'ten SONRA yapılıyor — bkz. ProfileController.
/// </param>
public sealed record DeleteAccountResult(Guid UserId, string? AvatarStorageKey);

public sealed class DeleteAccountHandler : IRequestHandler<DeleteAccountCommand, DeleteAccountResult>
{
    private const string SilinmisAd = "Silinmiş kullanıcı";

    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;

    public DeleteAccountHandler(IAppDbContext db, IPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<DeleteAccountResult> Handle(DeleteAccountCommand request, CancellationToken ct)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == request.UserId, ct)
                   ?? throw new AppException(ErrorCodes.UserNotFound, "Kullanıcı bulunamadı.", statusCode: 404);

        if (user.Status == UserStatus.Deleted)
        {
            throw new AppException(ErrorCodes.UserNotFound, "Bu hesap zaten silinmiş.", statusCode: 404);
        }

        /*
          PAROLA YENİDEN İSTENİYOR. Bu işlem geri alınamaz ve jeton 2 saat geçerli:
          telefonu bir süreliğine başkasının eline geçen kullanıcının hesabı, yalnızca
          açık bir oturumla silinebilir olmamalı. Aynı gerekçeyle mesaj, giriş hatasıyla
          AYNI: hangi adımda takıldığını söylemek, oturumu ele geçirenin işine yarar.
        */
        if (string.IsNullOrEmpty(user.PasswordHash) || !_hasher.Verify(user.PasswordHash, request.Password))
        {
            throw new AppException(ErrorCodes.InvalidCredentials, "Parola doğrulanamadı.", statusCode: 401);
        }

        var avatarKey = user.AvatarUrl;

        /*
          Durum EZİLMEDEN ÖNCE okunuyor. İlk yazımda cihaz kayıtları kontrolü
          "user.Status is not UserStatus.Banned" ile aşağıda yapılıyordu — ama o noktada
          durum çoktan Deleted'a çevrilmiş oluyordu, yani koşul HER ZAMAN doğru çıkıp
          banlı hesabın cihaz kayıtlarını da siliyordu. Sessiz bir hata: yalnızca banlı
          bir kullanıcı hesabını sildiğinde ortaya çıkardı.
        */
        var banliydi = user.Status == UserStatus.Banned;

        // --- Kimlik alanları ------------------------------------------------------
        user.Status = UserStatus.Deleted;
        user.DisplayName = SilinmisAd;

        /*
          E-POSTA benzersiz indeks taşıdığı için boş bırakılamaz; kullanıcı kimliğinden
          türetilen, teslim edilemez bir yer tutucu yazılıyor. ".invalid" IETF tarafından
          bu iş için ayrılmış üst düzey alan adıdır (RFC 2606): yanlışlıkla gerçek bir
          adrese e-posta gitmesi mümkün değil.
        */
        user.Email = $"silinmis+{user.Id:N}@dersmate.invalid";

        /*
          Parola özeti BOŞ BIRAKILMIYOR, kullanılamaz bir değerle DOLDURULUYOR. Boş bir
          özet, doğrulayıcının beklemediği bir girdidir ve giriş ucunda istisnaya düşerdi
          (500). Rastgele bir GUID'in özeti hem biçimsel olarak geçerli hem de kimsenin
          bilemeyeceği bir parolaya karşılık geliyor.
        */
        user.PasswordHash = _hasher.Hash(Guid.NewGuid().ToString("N"));

        user.Bio = null;
        user.PhoneNumber = null;
        user.PhoneVerifiedAtUtc = null;
        user.University = null;
        user.Department = null;
        user.AvatarUrl = null;

        // --- Kişisel yan kayıtlar -------------------------------------------------
        var tercihler = await _db.UserPreferences.Where(p => p.UserId == user.Id).ToListAsync(ct);
        _db.UserPreferences.RemoveRange(tercihler);

        var adaylik = await _db.TeacherCandidateProfiles.Where(t => t.UserId == user.Id).ToListAsync(ct);
        _db.TeacherCandidateProfiles.RemoveRange(adaylik);

        // İlanlar keşfette görünür durumda: silinen hesabın arzı/talebi orada kalmamalı.
        var ilanlar = await _db.PortfolioEntries.Where(e => e.UserId == user.Id).ToListAsync(ct);
        _db.PortfolioEntries.RemoveRange(ilanlar);

        // Cihaz kayıtları: banlı hesapta KORUNUYOR (yukarıdaki gerekçe).
        if (!banliydi)
        {
            var cihazlar = await _db.UserDevices.Where(d => d.UserId == user.Id).ToListAsync(ct);
            _db.UserDevices.RemoveRange(cihazlar);
        }

        await _db.SaveChangesAsync(ct);

        return new DeleteAccountResult(user.Id, avatarKey);
    }
}
