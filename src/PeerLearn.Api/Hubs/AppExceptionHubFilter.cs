using Microsoft.AspNetCore.SignalR;
using PeerLearn.Application.Common;

namespace PeerLearn.Api.Hubs;

/// <summary>
/// SignalR yalnızca HubException mesajlarını istemciye taşır; düz AppException jenerik
/// "unexpected error" olurdu. Bu filter AppException'ı "CODE|mesaj" formatlı HubException'a
/// çevirir — istemci '|' ile ayırıp kullanıcıya doğru hatayı gösterir.
/// (HTTP tarafındaki ExceptionHandlingMiddleware hub çağrılarını KAPSAMAZ; muadili budur.)
/// </summary>
public sealed class AppExceptionHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (AppException ex)
        {
            throw new HubException($"{ex.Code}|{ex.Message}");
        }
    }
}
