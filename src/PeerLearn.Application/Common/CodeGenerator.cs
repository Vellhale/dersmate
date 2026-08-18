using System.Security.Cryptography;

namespace PeerLearn.Application.Common;

public static class CodeGenerator
{
    // 0/O, 1/I gibi karıştırılabilir karakterler yok: kod ekran görüntüsünde elle okunacak.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Ders doğrulama kodu (Session ID). Örn: "K7PMQ2XR".</summary>
    public static string NewVerificationCode(int length = 8)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }
}
