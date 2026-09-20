using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Community;
using Xunit;

namespace PeerLearn.UnitTests;

public class ForumOyKuraliTests
{
    private static readonly Guid Sahip = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Baskasi = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Kendi_icerigine_oy_reddedilir()
    {
        var ex = Assert.Throws<AppException>(() => ForumOyKurali.SahibiOyVeremez(Sahip, Sahip));

        Assert.Equal(ErrorCodes.SelfVote, ex.Code);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void Baskasinin_icerigine_oy_serbest()
    {
        // İstisna atmamalı.
        ForumOyKurali.SahibiOyVeremez(Sahip, Baskasi);
    }
}
