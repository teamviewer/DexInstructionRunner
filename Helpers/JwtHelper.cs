using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text.Json;

namespace DexInstructionRunner.Helpers
{
    public static class JwtHelper
    {
        // Parse the JWT token and return the expiration date.
        public static DateTimeOffset? GetExpiration(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            var handler = new JwtSecurityTokenHandler();
            try
            {
                var jwtToken = handler.ReadJwtToken(token);
                var expClaim = jwtToken?.Payload?.FirstOrDefault(c => c.Key == "exp").Value;
                if (expClaim != null && long.TryParse(expClaim.ToString(), out long expUnix))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(expUnix);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing token expiration: {ex.Message}");
            }

            return null;
        }

        // Check if the token is expired.
        public static bool IsExpired(string token)
        {
            var expiry = GetExpiration(token);
            return !expiry.HasValue || expiry.Value < DateTimeOffset.UtcNow;
        }

        // Parse and return the entire payload from the token as JSON string.
        public static string ParseTokenPayload(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            var handler = new JwtSecurityTokenHandler();
            try
            {
                var jwtToken = handler.ReadJwtToken(token);
                var payload = jwtToken?.Payload;
                var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                return jsonPayload;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing token: {ex.Message}");
            }

            return null;
        }
    }
}
