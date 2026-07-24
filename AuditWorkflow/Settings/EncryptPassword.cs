using System.Security.Cryptography;
using System.Text;

namespace AuditWorkflow.Settings
{
    public static class EncryptPassword
    {
        public static string HashPassword(string plainPassword, string secretSuffix)
        {
            secretSuffix ??= "";
            var encoding = new ASCIIEncoding();
            StringBuilder stringBuilder = new();
            byte[] stream = SHA256.HashData(encoding.GetBytes(plainPassword + secretSuffix));
            for (int i = 0; i < stream.Length; i++) stringBuilder.AppendFormat("{0:x2}", stream[i]);

            return stringBuilder.ToString();
        }

        /// <summary>Hash con <c>Secret</c> por entorno (sin fallback a appsettings).</summary>
        public static string GenerateSHA256(this string value)
        {
            var secret = Environment.GetEnvironmentVariable("Secret") ?? "";
            return HashPassword(value, secret);
        }
    }
}
