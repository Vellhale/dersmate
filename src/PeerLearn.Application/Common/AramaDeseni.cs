namespace PeerLearn.Application.Common;

/// <summary>
/// LIKE aramaları için desen üretir. Kullanıcı girdisindeki LIKE joker karakterleri
/// (<c>%</c> ve <c>_</c>) ve kaçış karakterinin kendisi (<c>\</c>) etkisizleştirilir:
/// böylece "%" araması "her şey", "a_b" araması "a herhangi-bir-harf b" anlamına gelmez.
///
/// Bu SQL ENJEKSİYONU DEĞİL — terim zaten parametreli gidiyor; kapatılan tek şey joker
/// semantiği: kaçışlanmamış bir joker beklenmeyen (çok geniş) eşleşme ve baştan-sona tam
/// tablo taraması maliyeti doğuruyordu. <see cref="EF.Functions.Like"/> ile birlikte,
/// ESCAPE cümlesine <see cref="KacisKarakteri"/> verilerek kullanılmalı.
/// </summary>
public static class AramaDeseni
{
    /// <summary>LIKE'ın ESCAPE cümlesine verilecek kaçış karakteri (ters bölü).</summary>
    public const string KacisKarakteri = "\\";

    /// <summary>Ham terimi "%…%" (içeren) desenine çevirir; joker karakterler kaçışlanır.</summary>
    public static string Iceren(string ham) => $"%{Kacisla(ham)}%";

    /// <summary>
    /// Joker karakterleri kaçışlar. Ters bölü ÖNCE kaçışlanır — sonra eklenen kaçış
    /// karakterlerinin kendisi yeniden kaçışlanıp bozulmasın.
    /// </summary>
    public static string Kacisla(string ham) => ham
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
