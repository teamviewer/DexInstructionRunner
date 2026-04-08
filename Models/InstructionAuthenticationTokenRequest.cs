using Newtonsoft.Json;

namespace DexInstructionRunner.Models
{
    public sealed class InstructionAuthenticationTokenRequest
    {
        [JsonProperty("Token")]
        public string Token { get; set; } = string.Empty;

        [JsonProperty("Id")]
        public long Id { get; set; }
    }
}