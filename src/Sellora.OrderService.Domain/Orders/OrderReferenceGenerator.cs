using System.Security.Cryptography;

namespace Sellora.OrderService.Domain.Orders;

/// <summary>
/// Produces short references a shop owner can read aloud on the phone,
/// e.g. <c>ORD-260918-K7MQ4R</c>. The alphabet drops 0/O, 1/I/L and U/V
/// so spoken or handwritten references are not misread.
/// </summary>
public static class OrderReferenceGenerator
{
    public const string Prefix = "ORD";
    public const int RandomPartLength = 6;
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTWXYZ";

    public static string Generate(DateTimeOffset orderDate)
    {
        Span<char> random = stackalloc char[RandomPartLength];

        for (var i = 0; i < random.Length; i++)
        {
            random[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"{Prefix}-{orderDate.UtcDateTime:yyMMdd}-{new string(random)}";
    }
}
