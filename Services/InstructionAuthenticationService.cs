using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DexInstructionRunner.Models;
using Newtonsoft.Json;

namespace DexInstructionRunner.Services
{
    public sealed class InstructionAuthenticationService
    {
        private readonly HttpClient _httpClient;

        public InstructionAuthenticationService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task SubmitAuthenticationCodeAsync(
            string baseUrl,
            string bearerToken,
            long executionId,
            string code,
            CancellationToken cancellationToken = default)
        {
            _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);

            var payload = new InstructionAuthenticationTokenRequest
            {
                Id = executionId,
                Token = code.Trim()
            };

            var json = JsonConvert.SerializeObject(payload);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync(
                "consumer/Authentication/Instruction/Token",
                content,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Authentication submission failed: {response.StatusCode} {body}");
            }
        }
    }
}