namespace DexInstructionRunner.Models
{
    /// <summary>
    /// Persisted entry in AuthenticationConfig.EncryptedPlatformUrls.
    /// Alias remains plaintext; UrlEnc is encrypted token.
    /// Optional fields like DefaultMG and Consumer remain plaintext for now.
    /// </summary>
    public class EncryptedPlatformEntry
    {
        public string Alias { get; set; } = string.Empty;     // required, unique
        public string UrlEnc { get; set; } = string.Empty;     // encrypted URL token
        public string? DefaultMG { get; set; }                 // optional
        public string? Consumer { get; set; }                  // optional
    }

    /// <summary>
    /// Runtime model used by UI, holds decrypted URL.
    /// </summary>
    public class PlatformItem
    {
        public string Alias { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty; // decrypted at runtime
        public string? DefaultMG { get; set; }
        public string? Consumer { get; set; }
        public bool IsDefault { get; set; }
    }
}