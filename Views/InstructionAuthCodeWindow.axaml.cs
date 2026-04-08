using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DexInstructionRunner.Views
{
    public partial class InstructionAuthCodeWindow : Window
    {
        private readonly TextBox _codeTextBox;

        public InstructionAuthCodeWindow(long executionId, string? instructionName = null)
        {
            InitializeComponent();

            var instructionTextBlock = this.FindControl<TextBlock>("InstructionTextBlock");
            _codeTextBox = this.FindControl<TextBox>("CodeTextBox");

            var submitButton = this.FindControl<Button>("SubmitButton");
            var cancelButton = this.FindControl<Button>("CancelButton");

            instructionTextBlock.Text = instructionName == null
                ? $"Execution ID: {executionId}"
                : $"{instructionName} (Execution {executionId})";

            submitButton.Click += SubmitButton_Click;
            cancelButton.Click += CancelButton_Click;
        }

        private void SubmitButton_Click(object? sender, RoutedEventArgs e)
        {
            var code = _codeTextBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(code))
                return;

            Close(code);
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}