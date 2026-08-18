using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

/// <summary>
/// Kullanıcının tercih kaydı. Yoksa "hiç sorulmamış" varsayılanı döner — kayıt satırı
/// tembel (lazy) oluşturulur: her kayıt olan kullanıcıya boş satır açmak, hiç giriş
/// yapmayacak hesaplar için gereksiz yazma demektir.
/// </summary>
public sealed record GetMyPreferencesQuery(Guid UserId) : IRequest<UserPreferencesDto>;

public sealed record UserPreferencesDto(
    bool OnboardingCompleted,
    int OnboardingLastStep,
    bool OnboardingSuppressed,
    string AnalyticsConsent,
    string FunctionalConsent,
    string? ConsentVersion,
    DateTime? ConsentUpdatedAtUtc);

public sealed class GetMyPreferencesHandler : IRequestHandler<GetMyPreferencesQuery, UserPreferencesDto>
{
    private readonly IAppDbContext _db;

    public GetMyPreferencesHandler(IAppDbContext db) => _db = db;

    public async Task<UserPreferencesDto> Handle(GetMyPreferencesQuery request, CancellationToken ct)
    {
        var pref = await _db.UserPreferences.AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == request.UserId, ct);

        if (pref is null)
        {
            return new UserPreferencesDto(
                false, 0, false,
                CookieConsentState.NotAsked.ToString(),
                CookieConsentState.NotAsked.ToString(),
                null, null);
        }

        return new UserPreferencesDto(
            pref.OnboardingCompleted,
            pref.OnboardingLastStep,
            pref.OnboardingSuppressed,
            pref.AnalyticsConsent.ToString(),
            pref.FunctionalConsent.ToString(),
            pref.ConsentVersion,
            pref.ConsentUpdatedAtUtc);
    }
}

/// <summary>
/// Ürün turu durumu (Modül 5).
///
/// Üç ayrı bilgi, üç ayrı alan: nerede kaldı (LastStep), bitirdi mi (Completed), bir daha
/// istemiyor mu (Suppressed). Tek bayrağa indirmek "yarıda bıraktı" ile "istemiyorum"u
/// ayırt edilemez kılardı; ilkine bir dahaki girişte kaldığı yerden devam önerilebilir,
/// ikincisine hiç sorulmamalıdır.
/// </summary>
public sealed record UpdateOnboardingCommand(
    Guid UserId,
    int LastStep,
    bool Completed,
    bool Suppressed) : IRequest;

public sealed class UpdateOnboardingHandler : IRequestHandler<UpdateOnboardingCommand>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public UpdateOnboardingHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task Handle(UpdateOnboardingCommand request, CancellationToken ct)
    {
        if (request.LastStep < 0)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Adım numarası negatif olamaz.");
        }

        var pref = await _db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == request.UserId, ct);

        if (pref is null)
        {
            pref = new UserPreference { UserId = request.UserId };
            _db.UserPreferences.Add(pref);
        }

        pref.OnboardingLastStep = request.LastStep;
        pref.OnboardingSuppressed = request.Suppressed;

        // Tamamlanma anı bir kez yazılır: turu tekrar izleyen kullanıcının ilk bitirme
        // tarihi, "ne zaman öğrendi" sorusunun cevabıdır ve üzerine yazılmamalı.
        if (request.Completed)
        {
            pref.OnboardingCompleted = true;
            pref.OnboardingCompletedAtUtc ??= _clock.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Çerez rızasının kaydı (KVKK/GDPR).
///
/// <paramref name="ConsentIpHash"/> API katmanında hesaplanır: Application katmanının
/// HttpContext'i yoktur ve olmamalıdır. Ham IP asla saklanmaz (veri minimizasyonu) —
/// saklanan yalnızca "aynı ağdan mı geldi" karşılaştırmasına yeten hash'tir.
/// </summary>
public sealed record UpdateCookieConsentCommand(
    Guid UserId,
    bool Analytics,
    bool Functional,
    string ConsentVersion,
    string? ConsentIpHash) : IRequest;

public sealed class UpdateCookieConsentHandler : IRequestHandler<UpdateCookieConsentCommand>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public UpdateCookieConsentHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task Handle(UpdateCookieConsentCommand request, CancellationToken ct)
    {
        var version = request.ConsentVersion?.Trim();
        if (string.IsNullOrEmpty(version) || version.Length > 40)
        {
            // Sürümsüz rıza kanıt değeri taşımaz: hangi aydınlatma metnine onay verildiği
            // bilinmezse, metin değişince eski onayın kapsamı da belirsizleşir.
            throw new AppException(ErrorCodes.ValidationFailed, "Rıza metni sürümü zorunludur.");
        }

        var pref = await _db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == request.UserId, ct);

        if (pref is null)
        {
            pref = new UserPreference { UserId = request.UserId };
            _db.UserPreferences.Add(pref);
        }

        pref.AnalyticsConsent = request.Analytics ? CookieConsentState.Granted : CookieConsentState.Denied;
        pref.FunctionalConsent = request.Functional ? CookieConsentState.Granted : CookieConsentState.Denied;
        pref.ConsentVersion = version;
        pref.ConsentUpdatedAtUtc = _clock.UtcNow;
        pref.ConsentIpHash = request.ConsentIpHash;

        await _db.SaveChangesAsync(ct);
    }
}
