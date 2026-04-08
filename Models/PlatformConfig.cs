namespace DexInstructionRunner.Models
{
    public class PlatformConfig
    {
        public string Url { get; set; }
        public string Alias { get; set; }
        public string DefaultMG { get; set; }
        public string Consumer { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public string ObfuscatedUrl
        {
            get
            {
                var url = Url ?? string.Empty;
                url = url.Trim();
                if (url.Length == 0)
                    return string.Empty;

                // Show only length-matched obfuscation with the last character preserved.
                // Example: customersuccess.uksouth1.cloud.1e.com -> *******************************m
                if (url.Length == 1)
                    return "*";

                return new string('*', url.Length - 1) + url[^1];
            }
        }
    }
}
