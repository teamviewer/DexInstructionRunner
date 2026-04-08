using System;
using System.Security.Cryptography;
using System.Text;

namespace DexInstructionRunner.Services
{
    public static class MachineKeyHelper
    {
        private static readonly byte[] _key = GenerateKey();

        private static byte[] GenerateKey()
        {
            // Very basic: uses machine-specific data + a secret salt
            string machineId = Environment.MachineName ?? "UnknownMachine";
            string secret = "TVSecretSalt"; // 🔥 You can change this

            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(machineId + secret));
            }
        }

        public static string Encrypt(string plainText)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = _key;
                aes.GenerateIV();

                ICryptoTransform encryptor = aes.CreateEncryptor();
                byte[] inputBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encryptedBytes = encryptor.TransformFinalBlock(inputBytes, 0, inputBytes.Length);

                // Combine IV + encrypted data
                byte[] combinedBytes = new byte[aes.IV.Length + encryptedBytes.Length];
                Buffer.BlockCopy(aes.IV, 0, combinedBytes, 0, aes.IV.Length);
                Buffer.BlockCopy(encryptedBytes, 0, combinedBytes, aes.IV.Length, encryptedBytes.Length);

                return Convert.ToBase64String(combinedBytes);
            }
        }

        public static string Decrypt(string encryptedText)
        {
            byte[] combinedBytes = Convert.FromBase64String(encryptedText);

            using (Aes aes = Aes.Create())
            {
                aes.Key = _key;

                // Extract IV
                byte[] iv = new byte[16];
                Buffer.BlockCopy(combinedBytes, 0, iv, 0, iv.Length);
                aes.IV = iv;

                // Extract encrypted data
                int cipherTextLength = combinedBytes.Length - iv.Length;
                byte[] cipherBytes = new byte[cipherTextLength];
                Buffer.BlockCopy(combinedBytes, iv.Length, cipherBytes, 0, cipherTextLength);

                ICryptoTransform decryptor = aes.CreateDecryptor();
                byte[] decryptedBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

                return Encoding.UTF8.GetString(decryptedBytes);
            }
        }
    }
}
