using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DexInstructionRunner.Services
{
    /// <summary>
    /// Encrypts platform FQDN/URL values for appsettings.json using AES-GCM.
    /// Token format: base64(AAD)|base64(Nonce)|base64(Cipher+Tag)
    /// Only the URL/FQDN is encrypted; aliases remain plaintext.
    /// </summary>
    public static class PlatformUrlProtector
    {
        private static readonly byte[] EmbeddedKey = SHA256.HashData(Encoding.UTF8.GetBytes("DexInstructionRunner.PlatformUrlKey.v1"));

        public static bool IsProtectedString(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var parts = value.Split('|');
            return parts.Length == 3 && parts.All(x => !string.IsNullOrWhiteSpace(x));
        }

        public static string ProtectToPrefixedString(string plaintext)
        {
            return EncryptPlatformUrl(null, plaintext);
        }

        public static string UnprotectPrefixedStringToPlaintext(string? protectedValue)
        {
            return DecryptPlatformUrl(null, protectedValue);
        }

        public static string EncryptPlatformUrl(string? alias, string? plaintext)
        {
            var normalized = (plaintext ?? string.Empty).Trim();
            if (normalized.Length == 0)
                return string.Empty;

            var aad = BuildAad(alias);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var plainBytes = Encoding.UTF8.GetBytes(normalized);
            var cipher = new byte[plainBytes.Length];
            var tag = new byte[16];

            using (var aes = new AesGcm(EmbeddedKey, 16))
            {
                aes.Encrypt(nonce, plainBytes, cipher, tag, aad);
            }

            var cipherAndTag = new byte[cipher.Length + tag.Length];
            Buffer.BlockCopy(cipher, 0, cipherAndTag, 0, cipher.Length);
            Buffer.BlockCopy(tag, 0, cipherAndTag, cipher.Length, tag.Length);

            return $"{Convert.ToBase64String(aad)}|{Convert.ToBase64String(nonce)}|{Convert.ToBase64String(cipherAndTag)}";
        }

        public static string DecryptPlatformUrl(string? alias, string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return string.Empty;

            token = token.Trim();
            if (!IsProtectedString(token))
                return token;

            var parts = token.Split('|');
            if (parts.Length != 3)
                return string.Empty;

            try
            {
                var aad = Convert.FromBase64String(parts[0]);
                var nonce = Convert.FromBase64String(parts[1]);
                var cipherAndTag = Convert.FromBase64String(parts[2]);
                if (nonce.Length != 12 || cipherAndTag.Length < 17)
                    return string.Empty;

                var expectedAad = BuildAad(alias);
                if (!CryptographicOperations.FixedTimeEquals(aad, expectedAad))
                    return string.Empty;

                var cipherLen = cipherAndTag.Length - 16;
                var cipher = new byte[cipherLen];
                var tag = new byte[16];
                Buffer.BlockCopy(cipherAndTag, 0, cipher, 0, cipherLen);
                Buffer.BlockCopy(cipherAndTag, cipherLen, tag, 0, 16);

                var plainBytes = new byte[cipherLen];
                using (var aes = new AesGcm(EmbeddedKey, 16))
                {
                    aes.Decrypt(nonce, cipher, tag, plainBytes, aad);
                }

                return Encoding.UTF8.GetString(plainBytes).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static byte[] BuildAad(string? alias)
        {
            return Encoding.UTF8.GetBytes($"DexInstructionRunner|PlatformUrl|{(alias ?? string.Empty).Trim().ToLowerInvariant()}");
        }
    }
}
