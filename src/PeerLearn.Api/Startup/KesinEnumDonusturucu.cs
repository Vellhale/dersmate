using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeerLearn.Api.Startup;

/// <summary>
/// Enum'ları YANITLARDA ad olarak yazar, İSTEKLERDE addan okur ve TANIMSIZ değeri reddeder.
///
/// System.Text.Json'ın hazır <see cref="JsonStringEnumConverter"/>'ı ad-adına çalışır ama
/// gövdedeki JSON tamsayısını sınamadan kabul eder: tanımsız bir tamsayı (ör. <c>999</c>)
/// enum'a sızabiliyordu. Enum'lar bu projede DB'de METİN saklandığı için (HasConversion&lt;
/// string&gt;), tanımsız bir üye kalıcılaşır, sonra <c>.ToString()</c> ile geri okunurken
/// çözülemeyip başka bir yerde patlardı. Bu dönüştürücü hem adı hem tamsayıyı
/// <see cref="Enum.IsDefined{TEnum}(TEnum)"/> ile sınar; tutmayan giriş, gövde bağlama
/// aşamasında <see cref="JsonException"/> ile 400'e düşer.
///
/// Yazma davranışı hazır dönüştürücüyle aynı (üye adı) — yanıt sözleşmesi değişmez.
/// </summary>
public sealed class KesinEnumDonusturucu : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(
            typeof(Donusturucu<>).MakeGenericType(typeToConvert))!;

    private sealed class Donusturucu<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    var ad = reader.GetString();
                    if (Enum.TryParse<TEnum>(ad, ignoreCase: true, out var addan)
                        && Enum.IsDefined(addan))
                    {
                        return addan;
                    }
                    throw new JsonException($"Tanımsız {typeToConvert.Name} değeri: \"{ad}\".");

                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out var sayi))
                    {
                        var sayidan = (TEnum)Enum.ToObject(typeof(TEnum), sayi);
                        if (Enum.IsDefined(sayidan))
                        {
                            return sayidan;
                        }
                        throw new JsonException($"Tanımsız {typeToConvert.Name} değeri: {sayi}.");
                    }
                    throw new JsonException($"{typeToConvert.Name} tamsayı olmalı.");

                default:
                    throw new JsonException($"{typeToConvert.Name} bir dize ya da tamsayı olmalı.");
            }
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}
