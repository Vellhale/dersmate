using Microsoft.Extensions.DependencyInjection;
using PeerLearn.Application.Economy;
using PeerLearn.Application.Features.Community;
using PeerLearn.Application.Identity;

namespace PeerLearn.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(AssemblyReference).Assembly));
        services.AddScoped<CreditLedgerService>();
        services.AddScoped<RefreshTokenService>();
        services.AddScoped<MintGuard>();
        services.AddScoped<BadgeEngine>();
        services.AddScoped<SubjectBadgeEngine>();
        return services;
    }
}
