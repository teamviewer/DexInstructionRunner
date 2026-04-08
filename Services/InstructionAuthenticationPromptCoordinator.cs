using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using DexInstructionRunner.Views;

namespace DexInstructionRunner.Services
{
    public sealed class InstructionAuthenticationPromptCoordinator
    {
        private readonly InstructionAuthenticationService _authService;

        private readonly HashSet<long> _prompted = new();

        public InstructionAuthenticationPromptCoordinator(
            InstructionAuthenticationService authService)
        {
            _authService = authService;
        }

        public async Task HandleAuthenticationStateAsync(
            long executionId,
            int workflowState,
            string baseUrl,
            string token,
            string? instructionName = null)
        {
            if (workflowState != 11)
                return;

            if (_prompted.Contains(executionId))
                return;

            _prompted.Add(executionId);

            var code = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new InstructionAuthCodeWindow(executionId, instructionName);
                return await dialog.ShowDialog<string?>(null);
            });

            if (string.IsNullOrWhiteSpace(code))
                return;

            await _authService.SubmitAuthenticationCodeAsync(
                baseUrl,
                token,
                executionId,
                code);
        }
    }
}