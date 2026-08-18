using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Moderation;

namespace PeerLearn.Application.Features.Moderation;

// ---------------------------------------------------------------------------
// Ban kaldırma
// ---------------------------------------------------------------------------

public sealed record UnbanUserCommand(Guid TargetUserId, Guid AdminUserId, string Reason)
    : IRequest<UnbanUserResult>;

public sealed record UnbanUserResult(Guid UserId, int DevicesUnbanned);

/// <summary>
/// Hatalı verilmiş bir ban kararını geri alır.
///
/// NEDEN ŞART: ban hem hesabı hem kullanıcının TÜM cihazlarını (HWID) kilitliyor. Geri alma
/// yolu olmadığı için tek bir yanlış karar üründen düzeltilemiyordu — kullanıcı kalıcı
/// olarak dışarıda kalıyor, cihazı da engelli olduğu için yeni hesap bile açamıyordu.
/// Cihaz paylaşan masum kişiler (aile, okul laboratuvarı) aynı HWID üzerinden birlikte
/// engelleniyordu. Tek çözüm elle SQL'di, o da denetim izine hiç yazılmıyordu.
///
/// AdminActionType.UserUnbanned / HwidUnbanned enum değerleri ve arayüz etiketleri zaten
/// vardı; eksik olan yalnızca bu handler'dı.
/// </summary>
public sealed class UnbanUserHandler : IRequestHandler<UnbanUserCommand, UnbanUserResult>
{
    private readonly IAppDbContext _db;

    public UnbanUserHandler(IAppDbContext db) => _db = db;

    public async Task<UnbanUserResult> Handle(UnbanUserCommand request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 5 or > 500)
        {
            throw new AppException(ErrorCodes.NotAuthorized, "Gerekçe 5-500 karakter olmalı.");
        }

        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == request.TargetUserId, ct)
                   ?? throw new AppException(ErrorCodes.NotAuthorized, "Kullanıcı bulunamadı.", statusCode: 404);

        if (user.Status != UserStatus.Banned)
        {
            throw new AppException(ErrorCodes.NotAuthorized, "Bu kullanıcı banlı değil.", statusCode: 409);
        }

        /*
          E-postası doğrulanmamış kullanıcı Active'e ÇEKİLMEZ: ban öncesindeki durumuna
          döner. Aksi halde ban+unban, doğrulama adımını atlatan bir yol olurdu.
        */
        user.Status = user.EmailVerifiedAtUtc is null
            ? UserStatus.PendingVerification
            : UserStatus.Active;

        // Bu kullanıcı yüzünden konmuş cihaz banları kalkar. BAŞKA bir kullanıcı yüzünden
        // konmuş banlara dokunulmaz — aynı cihaz birden çok hesapla ilişkili olabilir.
        var cihazBanlari = await _db.HwidBans
            .Where(b => b.RelatedUserId == user.Id && b.IsActive)
            .ToListAsync(ct);

        foreach (var ban in cihazBanlari)
        {
            ban.IsActive = false;
        }

        var actorRole = await _db.Users.AsNoTracking()
            .Where(u => u.Id == request.AdminUserId)
            .Select(u => u.Role)
            .SingleAsync(ct);

        AdminAudit.Record(_db, request.AdminUserId, actorRole,
            AdminActionType.UserUnbanned, "User", user.Id,
            $"{user.Email} banı kaldırıldı; {cihazBanlari.Count} cihaz banı düşürüldü.",
            new { email = user.Email, reason, devicesUnbanned = cihazBanlari.Count, statusAfter = user.Status.ToString() });

        if (cihazBanlari.Count > 0)
        {
            AdminAudit.Record(_db, request.AdminUserId, actorRole,
                AdminActionType.HwidUnbanned, "HwidBan", user.Id,
                $"{cihazBanlari.Count} cihaz banı kaldırıldı ({user.Email}).",
                new { reason, hwidHashes = cihazBanlari.Select(b => b.HwidHash) });
        }

        await _db.SaveChangesAsync(ct);

        return new UnbanUserResult(user.Id, cihazBanlari.Count);
    }
}

// ---------------------------------------------------------------------------
// Geçici yaptırım (uyarı / süreli askı)
// ---------------------------------------------------------------------------

public sealed record ApplySanctionCommand(
    Guid TargetUserId,
    Guid ModeratorUserId,
    SanctionType Type,
    string Reason,
    int? DurationHours) : IRequest<ApplySanctionResult>;

public sealed record ApplySanctionResult(Guid UserId, string Status, DateTime? SuspendedUntilUtc);

/// <summary>
/// Uyarı ve SÜRELİ askı.
///
/// NEDEN GEREKLİ: yaptırım ölçeği yalnızca iki uçtan ibaretti — hiçbir şey yapmamak ya da
/// kalıcı ban. SanctionType.Warning ve TemporaryBan enum'da tanımlıydı ama hiçbir kod yolu
/// bunları yazmıyordu. Orantısız tek seçenek, hakemin ya göz yummasına ya da ilk kusurda
/// hesabı kalıcı kapatmasına yol açar.
///
/// Kalıcı ban BURADA DEĞİL (BanUserHandler): o karar cihaz banı da uyguluyor, geri alınması
/// en pahalı işlem ve AdminOnly'ye daraltılmış durumda.
/// </summary>
public sealed class ApplySanctionHandler : IRequestHandler<ApplySanctionCommand, ApplySanctionResult>
{
    private const int MaxSuspensionHours = 24 * 90;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public ApplySanctionHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ApplySanctionResult> Handle(ApplySanctionCommand request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 5 or > 500)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Gerekçe 5-500 karakter olmalı.");
        }

        if (request.Type == SanctionType.PermanentBan)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                "Kalıcı ban bu uçtan verilemez; ban ucunu kullanın (cihaz banı da uygular).",
                statusCode: 400);
        }

        if (request.Type == SanctionType.TemporaryBan &&
            request.DurationHours is not { } saat)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Süreli askı için süre zorunludur.");
        }
        else if (request.Type == SanctionType.TemporaryBan &&
                 (request.DurationHours < 1 || request.DurationHours > MaxSuspensionHours))
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                $"Askı süresi 1-{MaxSuspensionHours} saat arasında olmalı.");
        }

        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == request.TargetUserId, ct)
                   ?? throw new AppException(ErrorCodes.ValidationFailed, "Kullanıcı bulunamadı.", statusCode: 404);

        if (user.Status == UserStatus.Banned)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                "Kalıcı banlı kullanıcıya geçici yaptırım uygulanmaz.", statusCode: 409);
        }

        var now = _clock.UtcNow;
        DateTime? bitis = null;

        if (request.Type == SanctionType.TemporaryBan)
        {
            bitis = now.AddHours(request.DurationHours!.Value);
            user.Status = UserStatus.Suspended;
            user.SuspendedUntilUtc = bitis;
        }

        // UYARI hesabı kısıtlamaz: amacı kayda geçmek ve tekrar edilirse ölçeği yükseltmek.
        _db.UserSanctions.Add(new UserSanction
        {
            UserId = user.Id,
            Type = request.Type,
            Reason = reason,
            IssuedByAdminId = request.ModeratorUserId,
            ExpiresAtUtc = bitis
        });

        var actorRole = await _db.Users.AsNoTracking()
            .Where(u => u.Id == request.ModeratorUserId)
            .Select(u => u.Role)
            .SingleAsync(ct);

        AdminAudit.Record(_db, request.ModeratorUserId, actorRole,
            AdminActionType.UserSanctioned, "User", user.Id,
            request.Type == SanctionType.Warning
                ? $"{user.Email} uyarıldı."
                : $"{user.Email} {request.DurationHours} saat askıya alındı.",
            new { email = user.Email, type = request.Type.ToString(), reason, expiresAtUtc = bitis });

        await _db.SaveChangesAsync(ct);

        return new ApplySanctionResult(user.Id, user.Status.ToString(), user.SuspendedUntilUtc);
    }
}

// ---------------------------------------------------------------------------
// Rol atama
// ---------------------------------------------------------------------------

public sealed record ChangeUserRoleCommand(Guid TargetUserId, Guid AdminUserId, UserRole NewRole)
    : IRequest;

/// <summary>
/// Rol ataması.
///
/// NEDEN GEREKLİ: rol yalnızca elle SQL ile değiştirilebiliyordu — ilk yönetici de,
/// sonraki moderatörler de. Bu, denetim izine hiç yazılmayan ve üretimde veritabanına
/// doğrudan erişim gerektiren bir işti.
///
/// KENDİ ROLÜNÜ DÜŞÜREMEZSİN: tek yöneticinin kendini öğrenciye çevirmesi sistemi
/// yöneticisiz bırakır ve geri dönüş yine elle SQL olurdu.
/// </summary>
public sealed class ChangeUserRoleHandler : IRequestHandler<ChangeUserRoleCommand>
{
    private readonly IAppDbContext _db;

    public ChangeUserRoleHandler(IAppDbContext db) => _db = db;

    public async Task Handle(ChangeUserRoleCommand request, CancellationToken ct)
    {
        if (request.TargetUserId == request.AdminUserId)
        {
            throw new AppException(ErrorCodes.NotAuthorized,
                "Kendi rolünüzü değiştiremezsiniz.", statusCode: 409);
        }

        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == request.TargetUserId, ct)
                   ?? throw new AppException(ErrorCodes.NotAuthorized, "Kullanıcı bulunamadı.", statusCode: 404);

        var eskiRol = user.Role;
        if (eskiRol == request.NewRole)
        {
            return;
        }

        user.Role = request.NewRole;

        var actorRole = await _db.Users.AsNoTracking()
            .Where(u => u.Id == request.AdminUserId)
            .Select(u => u.Role)
            .SingleAsync(ct);

        AdminAudit.Record(_db, request.AdminUserId, actorRole,
            AdminActionType.RoleChanged, "User", user.Id,
            $"{user.Email} rolü {eskiRol} → {request.NewRole} olarak değiştirildi.",
            new { email = user.Email, from = eskiRol.ToString(), to = request.NewRole.ToString() });

        await _db.SaveChangesAsync(ct);
    }
}
