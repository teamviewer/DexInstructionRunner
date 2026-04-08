using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace DexInstructionRunner.Services
{
    public class VersionService
    {
        private readonly string _baseUrl;
        private readonly string _token;
        private string? _cachedVersion;

        public VersionService(string baseUrl, string token)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _token = token;
        }

        public async Task<string> GetPlatformVersionAsync()
        {
            if (!string.IsNullOrEmpty(_cachedVersion))
                return _cachedVersion;

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);

                var response = await client.GetAsync($"https://{_baseUrl}/consumer/information");
                var content = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);

                _cachedVersion = json["Version"]?.ToString() ?? "unknown";
                return _cachedVersion;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to fetch platform version: {ex.Message}");
                _cachedVersion = "unknown";
                return _cachedVersion;
            }
        }

        public async Task<bool> IsLegacyVersionAsync()
        {
            var version = await GetPlatformVersionAsync();

            // Compare major version
            if (Version.TryParse(version, out var parsed))
            {
                return parsed.Major < 25;
            }

            return true; // fallback to legacy if unknown
        }
    }
}
