using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Moderation;

namespace PeerLearn.Application.Features.Moderation;

/// <summary>
/// Kalıcı ban (Modül 4.3): hesap Banned yapılır, yaptırım kaydı düşülür ve kullanıcının
/// TÜM bilinen cihazları (UserDevices) HWID ban listesine eklenir — yeni hesapla dönüşü
/// (ban evasion) giriş/kayıt sırasındaki HWID kontrolü keser.
/// </summary>
public sealed record BanUserCommand(Guid TargetUserId, Guid AdminUserId, string Reason)
    : IRequest<BanUserResult>;

public sealed record BanUserResult(Guid UserId, int DevicesBanned);

public sealed class BanUserHandler : IRequestHandler<BanUserCommand, BanUserResult>
{
    private readonly IAppDbContext _db;

    public BanUserHandler(IAppDbContext db) => _db = db;

    public async Task<BanUserResult> Handle(BanUserCommand request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 5 or > 500)
        {
            throw new AppException(ErrorCodes.NotAuthorized, "Ban gerekçesi 5-500 karakter olmalı.");
        }

        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == request.TargetUserId, ct)
                   ?? throw new AppException(ErrorCodes.NotAuthorized, "Kullanıcı bulunamadı.", statusCode: 404);

        user.Status = UserStatus.Banned;

        _db.UserSanctions.Add(new UserSanction
        {
            UserId = user.Id,
            Type = SanctionType.PermanentBan,
            Reason = reason,
            IssuedByAdminId = request.AdminUserId
        });

        var hwids = await _db.UserDevices
            .Where(d => d.UserId == user.Id)
            .Select(d => d.HwidHash)
            .Distinct()
            .ToListAsync(ct);

        var alreadyBanned = await _db.HwidBans
            .Where(b => hwids.Contains(b.HwidHash) && b.IsActive)
            .Select(b => b.HwidHash)
            .ToListAsync(ct);

        var newBans = hwids.Except(alreadyBanned).ToList();
        foreach (var hwid in newBans)
        {
            _db.HwidBans.Add(new HwidBan
            {
                HwidHash = hwid,
                RelatedUserId = user.Id,
                Reason = reason,
                BannedByAdminId = request.AdminUserId
                // ExpiresAtUtc = null → kalıcı.
            });
        }

        var actorRole = await _db.Users.AsNoTracking()
            .Where(u => u.Id == request.AdminUserId)
            .Select(u => u.Role)
            .SingleAsync(ct);

        AdminAudit.Record(
            _db,
            request.AdminUserId,
            actorRole,
            AdminActionType.UserBanned,
            targetType: "User",
            targetId: user.Id,
            summary: $"{user.Email} kalıcı olarak banlandı; {newBans.Count} cihaz engellendi.",
            metadata: new
            {
                email = user.Email,
                reason,
                devicesBanned = newBans.Count,
                hwidHashes = newBans
            });

        await _db.SaveChangesAsync(ct);

        return new BanUserResult(user.Id, newBans.Count);
    }
}
