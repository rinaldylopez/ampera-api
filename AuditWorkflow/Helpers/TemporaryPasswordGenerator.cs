using System.Security.Cryptography;
using System.Text;

namespace AuditWorkflow.Helpers;

public static class TemporaryPasswordGenerator
{
    /// <summary>Genera una contraseña provisional con mayúsculas, minúsculas, dígitos y un símbolo.</summary>
    public static string Generate(int length = 14)
    {
        if (length < 12)
            length = 12;

        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "@#$%&*!";

        Span<char> pool = stackalloc char[upper.Length + lower.Length + digits.Length + special.Length];
        var i = 0;
        foreach (var c in upper) pool[i++] = c;
        foreach (var c in lower) pool[i++] = c;
        foreach (var c in digits) pool[i++] = c;
        foreach (var c in special) pool[i++] = c;

        var required = new[] { upper[RandomNumberGenerator.GetInt32(upper.Length)], lower[RandomNumberGenerator.GetInt32(lower.Length)], digits[RandomNumberGenerator.GetInt32(digits.Length)], special[RandomNumberGenerator.GetInt32(special.Length)] };

        var sb = new StringBuilder(length);
        foreach (var c in required)
            sb.Append(c);

        for (var n = sb.Length; n < length; n++)
            sb.Append(pool[RandomNumberGenerator.GetInt32(pool.Length)]);

        // Barajar (Fisher–Yates)
        var chars = sb.ToString().ToCharArray();
        for (var j = chars.Length - 1; j > 0; j--)
        {
            var k = RandomNumberGenerator.GetInt32(j + 1);
            (chars[j], chars[k]) = (chars[k], chars[j]);
        }

        return new string(chars);
    }
}
