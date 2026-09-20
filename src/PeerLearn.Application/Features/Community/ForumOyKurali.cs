using PeerLearn.Application.Common;

namespace PeerLearn.Application.Features.Community;

/// <summary>
/// Forum oyu iş kuralları. Saf (DB'siz) tutulur ki mutasyonla sınanabilsin;
/// karar tek yerde durur, çağıran taraf yalnızca sahibi okuyup buraya verir.
/// </summary>
public static class ForumOyKurali
{
    /// <summary>
    /// KENDİ İÇERİĞİNE OY YOK. Forum net oyu topluluk ödülüne (puan basımı) besleniyor
    /// (<see cref="PeerLearn.Domain.Community.CommunityRewardRules"/>): kişi kendi
    /// gönderisini ya da yorumunu artı oylayarak seviyesini ve puanını tek başına
    /// şişirebilirdi. Bu yalnızca bir muhafız — defter/sayaç yazmadan ÖNCE reddeder.
    /// </summary>
    /// <param name="icerikSahibi">Oy verilen gönderi/yorumun yazarı.</param>
    /// <param name="oyVeren">Oyu veren kullanıcı.</param>
    public static void SahibiOyVeremez(Guid icerikSahibi, Guid oyVeren)
    {
        if (icerikSahibi == oyVeren)
        {
            throw new AppException(ErrorCodes.SelfVote,
                "Kendi içeriğine oy veremezsin.", statusCode: 409);
        }
    }
}
