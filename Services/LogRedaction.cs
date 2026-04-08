using System;

namespace DexInstructionRunner.Services
{
    internal static class LogRedaction
    {
        public static string SafeAliasFromHost(string? hostOrUrl)
        {
            if (string.IsNullOrWhiteSpace(hostOrUrl))
                return "(no-alias)";

            var host = hostOrUrl.Trim();

            // If a full URL was pasted/constructed, parse it.
            if (host.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                if (Uri.TryCreate(host, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
                    host = uri.Host;
            }

            // Strip path
            var slash = host.IndexOf('/');
            if (slash >= 0)
                host = host.Substring(0, slash);

            // Strip port
            var colon = host.IndexOf(':');
            if (colon >= 0)
                host = host.Substring(0, colon);

            host = host.Trim().TrimEnd('.');

            // First label of fqdn
            var dot = host.IndexOf('.');
            if (dot > 0)
                return host.Substring(0, dot);

            return host;
        }
    }
}
