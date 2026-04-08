using Avalonia.Controls;
using Avalonia.Threading;
using System.Text.RegularExpressions;

namespace DexInstructionRunner.Helpers
{
    public static class LogHelper
    {
        public static void ClearLog(TextBox? box)
        {
            if (box == null)
                return;

            if (Dispatcher.UIThread.CheckAccess())
                box.Text = string.Empty;
            else
                Dispatcher.UIThread.Post(() => box.Text = string.Empty);
        }

        public static void AppendLog(TextBox? box, string message)
        {
            if (box == null)
                return;

            // Never reveal full platform FQDNs in logs.
            message = RedactPlatformFqdns(message ?? string.Empty);

            if (Dispatcher.UIThread.CheckAccess())
                box.Text += message;
            else
                Dispatcher.UIThread.Post(() => box.Text += message);
        }

        private static string RedactPlatformFqdns(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Redact common platform hostnames. Keep this narrowly scoped to avoid hiding device FQDNs.
            // Matches things like customersuccess.uksouth1.cloud.1e.com or *.cloud.*
            try
            {
                return Regex.Replace(
                    text,
                    @"\b([a-zA-Z0-9-]+\.)+[a-zA-Z0-9-]*cloud\.[a-zA-Z0-9-]+\.[a-zA-Z]{2,}\b",
                    m => Obfuscate(m.Value),
                    RegexOptions.Compiled);
            }
            catch
            {
                return text;
            }
        }

        private static string Obfuscate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            value = value.Trim();
            if (value.Length == 1)
                return "*";
            return new string('*', value.Length - 1) + value[^1];
        }
    }
}
