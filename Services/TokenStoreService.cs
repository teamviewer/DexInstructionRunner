using Avalonia.Controls;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DexInstructionRunner.Services
{
    public class TokenStoreService
    {
        private readonly TextBox _logTextBox;
        private readonly string _tokenFilePath;
        private string _platformUrl;

        // Constructor that initializes the TokenStoreService with a logTextBox for logging
        public TokenStoreService(TextBox logTextBox)
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DexConsole", "tvappdata.bin");
            _tokenFilePath = appDataPath;
            _logTextBox = logTextBox;

        }

        // StoredToken class represents the structure of the token, including PlatformUrl, EncryptedToken, and ExpirationUtc
        private class StoredToken
        {
            public string PlatformUrl { get; set; }
            public string EncryptedToken { get; set; }
            public DateTime ExpirationUtc { get; set; }
        }

        // SaveToken method stores the token in an encrypted form
        public void SaveToken(string platformUrl, string token, DateTime expirationUtc)
        {
            var tokens = LoadAllTokens();
            var encryptedToken = MachineKeyHelper.Encrypt(token);

            // Remove existing entry for same platform
            tokens.RemoveAll(t => t.PlatformUrl.Equals(platformUrl, StringComparison.OrdinalIgnoreCase));

            tokens.Add(new StoredToken
            {
                PlatformUrl = platformUrl,
                EncryptedToken = encryptedToken,
                ExpirationUtc = expirationUtc
            });

            var json = JsonConvert.SerializeObject(tokens, Formatting.Indented);
            File.WriteAllText(_tokenFilePath, json);
        }

        // LoadToken method loads and decrypts a token for a given platform URL
        public string LoadToken(string platformUrl)
        {
            _logTextBox.Text += $"🔎 Searching for token with PlatformUrl: {platformUrl}\n";

            var tokens = LoadAllTokens();

            if (tokens == null || tokens.Count == 0)
            {
                _logTextBox.Text += "⚠️ No tokens found or failed to load tokens.\n";
                return null;
            }

            // Log the tokens being searched (for debugging purposes)
            _logTextBox.Text += $"✅ {tokens.Count} tokens loaded. Searching for platform: {platformUrl}\n";

            var tokenEntry = tokens.FirstOrDefault(t =>
                t.PlatformUrl.Equals(platformUrl, StringComparison.OrdinalIgnoreCase) &&
                t.ExpirationUtc > DateTime.UtcNow);

            if (tokenEntry != null)
            {
                _logTextBox.Text += $"🔑 Token found for platform: {platformUrl}. Decrypting...\n";
                var decryptedToken = MachineKeyHelper.Decrypt(tokenEntry.EncryptedToken);
                _logTextBox.Text += $"✅ Token decrypted successfully.\n";
                return decryptedToken;
            }
            else
            {
                _logTextBox.Text += $"⚠️ No token found for platform: {platformUrl} or token is expired.\n";

            }

            return null;
        }


        // RemoveToken method removes a token from the list based on the platform URL and writes the updated list back to the file
        public void RemoveToken(string platformUrl)
        {
            try
            {
                if (!File.Exists(_tokenFilePath))
                {
                    _logTextBox.Text += "⚠️ Token file not found.\n";
                    return;
                }

                string machineId = GetMachineId();
                string compositeKey = $"{platformUrl}_{machineId}";
                string hashedKey = ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(compositeKey)));

                _logTextBox.Text += "🔒 Deleting token...\n";

                var allLines = File.ReadAllLines(_tokenFilePath).ToList();
                int removed = allLines.RemoveAll(l => l.StartsWith(hashedKey + ":"));

                if (removed > 0)
                {
                    File.WriteAllLines(_tokenFilePath, allLines);
                    _logTextBox.Text += $"✅ Token deleted for platform: {platformUrl}\n";
                }
                else
                {
                    _logTextBox.Text += $"⚠️ No matching token found for deletion.\n";
                }
            }
            catch (Exception ex)
            {
                _logTextBox.Text += $"❌ Error deleting token: {ex.Message}\n";
            }
        }
        private static string GetMachineId()
        {
            try
            {
                var networkInterfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                var firstUpInterface = networkInterfaces
                    .FirstOrDefault(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);

                if (firstUpInterface != null)
                {
                    return BitConverter.ToString(firstUpInterface.GetPhysicalAddress().GetAddressBytes()).Replace("-", "");
                }
            }
            catch
            {
                // fallback if needed
            }

            // fallback: random per-install GUID if MAC not available (rare edge case)
            var fallbackId = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DexConsole", "machineid.txt");
            if (File.Exists(fallbackId))
                return File.ReadAllText(fallbackId);

            var newId = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fallbackId));
            File.WriteAllText(fallbackId, newId);
            return newId;
        }

        private static string ToBase64Url(byte[] input)
        {
            return Convert.ToBase64String(input)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        private static string GetSafeHash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant().Substring(0, 16); // short 16 characters
        }
        // LoadAllTokens method loads all tokens from the file and decrypts them
        private List<StoredToken> LoadAllTokens()
        {
            if (!File.Exists(_tokenFilePath))
            {
                _logTextBox.Text += $"⚠️ Token file does not exist at {_tokenFilePath}\n";
                return new List<StoredToken>();
            }

            _logTextBox.Text += $"🔎 Token file found at: {_tokenFilePath}\n";

            var content = File.ReadAllText(_tokenFilePath);

            if (string.IsNullOrWhiteSpace(content))
            {
                _logTextBox.Text += "⚠️ Token file is empty.\n";
                return new List<StoredToken>();
            }

            try
            {
                var tokensData = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var tokens = new List<StoredToken>();

                foreach (var tokenData in tokensData)
                {
                    // Extract platform URL and expiration time from tokenData if applicable
                    var platformUrl = ExtractPlatformUrl(tokenData); // Implement this method based on your token structure
                    var expirationUtc = ExtractExpirationUtc(tokenData); // Implement this method based on your token structure

                    var token = new StoredToken
                    {
                        PlatformUrl = platformUrl,
                        EncryptedToken = tokenData,
                        ExpirationUtc = expirationUtc
                    };

                    token.EncryptedToken = MachineKeyHelper.Decrypt(token.EncryptedToken);
                    _logTextBox.Text += $"🔑 Decrypted Token: {token.EncryptedToken}\n";
                    tokens.Add(token);
                }

                _logTextBox.Text += $"✅ Successfully loaded {tokens.Count} tokens from the file.\n";
                return tokens;
            }
            catch (JsonException jsonEx)
            {
                _logTextBox.Text += $"❌ JSON deserialization error: {jsonEx.Message}\n";
                return new List<StoredToken>();
            }
            catch (Exception ex)
            {
                _logTextBox.Text += $"❌ Error reading or deserializing token file: {ex.Message}\n";
                return new List<StoredToken>();
            }
        }

        // Placeholder methods for extracting platform URL and expiration time
        private string ExtractPlatformUrl(string tokenData)
        {
            // Implement logic to extract platform URL from tokenData
            return "YourPlatformUrlHere";
        }

        private DateTime ExtractExpirationUtc(string tokenData)
        {
            // Implement logic to extract expiration time from tokenData
            return DateTime.UtcNow.AddHours(1);
        }


    }
}
