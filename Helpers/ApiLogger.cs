using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public static class ApiLogger
{
    private static readonly string LogDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");

    private static string RedactEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return endpoint;

        try
        {
            // Replace any full URL host with a non-revealing token.
            // Example: https://example.domain.com/foo -> https://[platform]/foo
            endpoint = Regex.Replace(
                endpoint,
                @"\bhttps?://([^/]+)",
                m => m.Value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    ? "http://[platform]"
                    : "https://[platform]",
                RegexOptions.IgnoreCase);

            // Also redact raw hostnames that appear without scheme.
            // This targets common platform host formats while avoiding over-redacting arbitrary text.
            endpoint = Regex.Replace(
                endpoint,
                @"\b([a-z0-9][a-z0-9\-]*\.)+[a-z]{2,}\b",
                "[platform]",
                RegexOptions.IgnoreCase);
        }
        catch
        {
            // ignore
        }

        return endpoint;
    }

    public static async Task<string> LogApiCallAsync(string label, string endpoint, Func<Task<string>> apiCall, string payloadJson)
    {
        Directory.CreateDirectory(LogDirectory);
        var now = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();
        string result = null;
        Exception error = null;

        try
        {
            result = await apiCall();
        }
        catch (Exception ex)
        {
            error = ex;
        }
        stopwatch.Stop();

        var logPath = Path.Combine(LogDirectory, $"apitroubleshooting-{now:yyyyMMdd}.log");
        var sb = new StringBuilder();
        var safeEndpoint = RedactEndpoint(endpoint);
        sb.AppendLine($"[{now:yyyy-MM-dd HH:mm:ss.fff}] API Label: {label} URL: {safeEndpoint} Time: {stopwatch.ElapsedMilliseconds} ms");
        if (error != null)
        {
            sb.AppendLine($"❌ Error: {error}");
        }
        // Removed the response logging part
        //sb.AppendLine(new string('-', 80));

        await File.AppendAllTextAsync(logPath, sb.ToString());
        if (error != null) throw error;
        return result;
    }

}
