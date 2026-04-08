using DexInstructionRunner.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DexInstructionRunner.Services
{
    public class ConfigHelper
    {
        private readonly IConfigurationRoot _config;
        private readonly string _configPath;
        private readonly string _configFullPath;

        public ConfigHelper(string configPath = "appsettings.json")
        {
            _configPath = configPath;

            // Resolve a single source-of-truth path for both reads and writes.
            // Prefer the current working directory (dev scenario), then fall back to the app base directory.
            _configFullPath = ResolveConfigFullPath(configPath);

            var baseDir = Path.GetDirectoryName(_configFullPath) ?? AppContext.BaseDirectory;
            var fileName = Path.GetFileName(_configFullPath);

            // Optional so the app can start even if the file does not exist.
            _config = new ConfigurationBuilder()
                .SetBasePath(baseDir)
                .AddJsonFile(fileName, optional: true, reloadOnChange: true)
                .Build();
        }

        private static string ResolveConfigFullPath(string configPath)
        {
            // If the caller passes an absolute path, honor it.
            if (Path.IsPathRooted(configPath))
                return configPath;

            var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), configPath);
            if (File.Exists(cwdPath))
                return cwdPath;

            var basePath = Path.Combine(AppContext.BaseDirectory, configPath);
            if (File.Exists(basePath))
                return basePath;

            // Default to current working directory so subsequent writes land where developers expect.
            return cwdPath;
        }

        public static string NormalizePlatformUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            url = url.Trim();
            // Users often paste full https:// URLs; keep config normalized to host only.
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                url = url.Substring(7);
            else if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = url.Substring(8);

            // Remove any trailing path segments and trailing slashes.
            int slash = url.IndexOf('/');
            if (slash >= 0)
                url = url.Substring(0, slash);

            return url.Trim().TrimEnd('/');
        }


        public string ConfigFullPath => _configFullPath;

        private string GetConfigFullPath()
        {
            return _configFullPath;
        }

        private void EnsureConfigFileExists()
        {
            try
            {
                var fullPath = GetConfigFullPath();
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(fullPath))
                    return;

                // Minimal skeleton that keeps schema stable and allows the UI to operate.
                var root = new JObject
                {
                    ["AuthenticationConfig"] = new JObject
                    {
                        ["EncryptedPlatformUrls"] = new JArray()
                    }
                };

                File.WriteAllText(fullPath, root.ToString(Formatting.Indented));

                try { (_config as IConfigurationRoot)?.Reload(); } catch { }
            }
            catch
            {
                // Ignore; callers will handle failure when attempting to read/write.
            }
        }

        public IConfigurationRoot GetConfiguration() => _config;

        public List<PlatformConfig> GetPlatformConfigs()
        {
            EnsureConfigFileExists();

            try
            {
                var fullPath = GetConfigFullPath();
                var json = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
                var root = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
                var auth = root["AuthenticationConfig"] as JObject;

                var list = new List<PlatformConfig>();
                var encrypted = auth?["EncryptedPlatformUrls"] as JArray;
                if (encrypted != null && encrypted.Count > 0)
                {
                    foreach (var token in encrypted.OfType<JObject>())
                    {
                        var alias = SanitizeAlias((token["Alias"]?.ToString() ?? string.Empty).Trim());
                        var plainUrl = NormalizePlatformUrl(PlatformUrlProtector.DecryptPlatformUrl(alias, token["UrlEnc"]?.ToString()));
                        if (string.IsNullOrWhiteSpace(plainUrl))
                            continue;

                        if (string.IsNullOrWhiteSpace(alias))
                            alias = DeriveAliasFromHost(plainUrl);

                        list.Add(new PlatformConfig
                        {
                            Url = plainUrl,
                            Alias = alias,
                            DefaultMG = (token["DefaultMG"]?.ToString() ?? string.Empty).Trim(),
                            Consumer = string.IsNullOrWhiteSpace(token["Consumer"]?.ToString()) ? "Explorer" : token["Consumer"]!.ToString()!.Trim()
                        });
                    }

                    return DeduplicateAndSortPlatforms(list);
                }

                var legacy = auth?["PlatformUrls"] as JArray;
                if (legacy != null)
                {
                    foreach (var token in legacy.OfType<JObject>())
                    {
                        var plainUrl = NormalizePlatformUrl(token["Url"]?.ToString() ?? string.Empty);
                        if (string.IsNullOrWhiteSpace(plainUrl))
                            continue;

                        var alias = SanitizeAlias((token["Alias"]?.ToString() ?? string.Empty).Trim());
                        if (string.IsNullOrWhiteSpace(alias))
                            alias = DeriveAliasFromHost(plainUrl);

                        list.Add(new PlatformConfig
                        {
                            Url = plainUrl,
                            Alias = alias,
                            DefaultMG = (token["DefaultMG"]?.ToString() ?? string.Empty).Trim(),
                            Consumer = string.IsNullOrWhiteSpace(token["Consumer"]?.ToString()) ? "Explorer" : token["Consumer"]!.ToString()!.Trim()
                        });
                    }
                }

                return DeduplicateAndSortPlatforms(list);
            }
            catch
            {
                return new List<PlatformConfig>();
            }
        }

        public PlatformConfig? GetSelectedPlatform(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            return GetPlatformConfigs()
                .FirstOrDefault(p => string.Equals(p.Url, url, StringComparison.OrdinalIgnoreCase));
        }

        public string? GetRawSetting(string key) => _config[key];

        public string? GetDefaultPlatformAlias()
        {
            var a = _config["AuthenticationConfig:DefaultPlatformAlias"];
            if (string.IsNullOrWhiteSpace(a))
                a = _config["AuthenticationConfig:SelectedPlatformAlias"];
            return string.IsNullOrWhiteSpace(a) ? null : a.Trim();
        }

        public bool SetDefaultPlatformAlias(string alias)
        {
            try
            {
                alias = (alias ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(alias))
                    return false;

                EnsureConfigFileExists();
                var fullPath = GetConfigFullPath();
                var json = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
                var jObj = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);

                var auth = jObj["AuthenticationConfig"] as JObject;
                if (auth == null)
                {
                    auth = new JObject();
                    jObj["AuthenticationConfig"] = auth;
                }

                auth["DefaultPlatformAlias"] = alias;
                File.WriteAllText(fullPath, jObj.ToString(Formatting.Indented));

                try { (_config as IConfigurationRoot)?.Reload(); } catch { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SetDefaultManagementGroup(string platformUrl, string mgName)
        {
            try
            {
                EnsureConfigFileExists();
                var fullPath = GetConfigFullPath();
                var json = File.ReadAllText(fullPath);
                var jObj = JObject.Parse(json);

                var auth = jObj["AuthenticationConfig"] as JObject ?? new JObject();
                jObj["AuthenticationConfig"] = auth;

                var encrypted = auth["EncryptedPlatformUrls"] as JArray;
                if (encrypted != null)
                {
                    foreach (JObject platform in encrypted)
                    {
                        var alias = (platform["Alias"]?.ToString() ?? string.Empty).Trim();
                        var url = NormalizePlatformUrl(
                            PlatformUrlProtector.DecryptPlatformUrl(alias, platform["UrlEnc"]?.ToString()));

                        if (string.Equals(url, NormalizePlatformUrl(platformUrl), StringComparison.OrdinalIgnoreCase))
                        {
                            platform["DefaultMG"] = mgName ?? string.Empty;
                            File.WriteAllText(fullPath, jObj.ToString(Formatting.Indented));
                            try { (_config as IConfigurationRoot)?.Reload(); } catch { }
                            return true;
                        }
                    }
                }

                var platforms = auth["PlatformUrls"] as JArray;
                var legacyPlatform = platforms?.FirstOrDefault(p =>
                    string.Equals(
                        NormalizePlatformUrl(p["Url"]?.ToString() ?? string.Empty),
                        NormalizePlatformUrl(platformUrl),
                        StringComparison.OrdinalIgnoreCase));

                if (legacyPlatform != null)
                {
                    legacyPlatform["DefaultMG"] = mgName ?? string.Empty;
                    File.WriteAllText(fullPath, jObj.ToString(Formatting.Indented));
                    try { (_config as IConfigurationRoot)?.Reload(); } catch { }
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to update DefaultMG: {ex.Message}");
                return false;
            }
        }

        public bool UpdateDefaultManagementGroup(string platformUrl, string mgName)
        {
            return SetDefaultManagementGroup(platformUrl, mgName);
        }


        public bool SavePlatformConfigs(List<PlatformConfig> platforms)
        {
            try
            {
                EnsureConfigFileExists();
                var cleaned = DeduplicateAndSortPlatforms((platforms ?? new List<PlatformConfig>())
                    .Where(p => p != null)
                    .Select(p => new PlatformConfig
                    {
                        Url = NormalizePlatformUrl(p.Url),
                        Alias = string.IsNullOrWhiteSpace(p.Alias) ? DeriveAliasFromHost(p.Url) : SanitizeAlias(p.Alias),
                        DefaultMG = (p.DefaultMG ?? string.Empty).Trim(),
                        Consumer = string.IsNullOrWhiteSpace(p.Consumer) ? "Explorer" : p.Consumer.Trim()
                    })
                    .ToList());

                var aliasErrors = cleaned
                    .Where(p => string.IsNullOrWhiteSpace(p.Alias) || !IsValidAlias(p.Alias))
                    .Select(p => p.Alias)
                    .ToList();
                if (aliasErrors.Count > 0)
                    return false;

                if (cleaned.GroupBy(p => p.Alias, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                    return false;

                var fullPath = GetConfigFullPath();
                JObject jObj;
                try
                {
                    var json = File.ReadAllText(fullPath);
                    jObj = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
                }
                catch
                {
                    jObj = new JObject();
                }

                var authObj = jObj["AuthenticationConfig"] as JObject;
                if (authObj == null)
                {
                    authObj = new JObject();
                    jObj["AuthenticationConfig"] = authObj;
                }

                var arr = new JArray();
                foreach (var p in cleaned)
                {
                    var item = new JObject
                    {
                        ["Alias"] = p.Alias,
                        ["UrlEnc"] = PlatformUrlProtector.EncryptPlatformUrl(p.Alias, p.Url),
                        ["DefaultMG"] = p.DefaultMG ?? string.Empty,
                        ["Consumer"] = string.IsNullOrWhiteSpace(p.Consumer) ? "Explorer" : p.Consumer
                    };
                    arr.Add(item);
                }

                authObj["EncryptedPlatformUrls"] = arr;
                authObj.Remove("PlatformUrls");

                var defaultAlias = (authObj["DefaultPlatformAlias"]?.ToString() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(defaultAlias) || !cleaned.Any(p => string.Equals(p.Alias, defaultAlias, StringComparison.OrdinalIgnoreCase)))
                    authObj["DefaultPlatformAlias"] = cleaned.FirstOrDefault()?.Alias ?? string.Empty;

                File.WriteAllText(fullPath, jObj.ToString(Formatting.Indented));
                try { (_config as IConfigurationRoot)?.Reload(); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to save EncryptedPlatformUrls: {ex.Message}");
                return false;
            }
        }

        public bool AddOrUpdatePlatform(PlatformConfig platform)
        {
            if (platform == null || string.IsNullOrWhiteSpace(platform.Url))
                return false;

            platform.Url = NormalizePlatformUrl(platform.Url);

            var list = GetPlatformConfigs();
            var existing = list.FirstOrDefault(p =>
                string.Equals(p.Url, platform.Url, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Alias = platform.Alias;
                existing.DefaultMG = platform.DefaultMG;
                existing.Consumer = string.IsNullOrWhiteSpace(platform.Consumer) ? "Explorer" : platform.Consumer;
            }
            else
            {
                list.Add(new PlatformConfig
                {
                    Url = NormalizePlatformUrl(platform.Url),
                    Alias = (platform.Alias ?? string.Empty).Trim(),
                    DefaultMG = (platform.DefaultMG ?? string.Empty).Trim(),
                    Consumer = string.IsNullOrWhiteSpace(platform.Consumer) ? "Explorer" : (platform.Consumer ?? string.Empty).Trim()
                });
            }

            return SavePlatformConfigs(list);
        }

        public bool RemovePlatform(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            url = NormalizePlatformUrl(url);

            var list = GetPlatformConfigs();
            int removed = list.RemoveAll(p => string.Equals(p.Url, url, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return false;

            return SavePlatformConfigs(list);
        }

        public static string DeriveAliasFromHost(string? url)
        {
            var host = NormalizePlatformUrl(url ?? string.Empty);
            if (string.IsNullOrWhiteSpace(host))
                return string.Empty;

            var first = host.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? host;
            var alias = Regex.Replace(first, "[^A-Za-z0-9._-]", string.Empty);
            if (string.IsNullOrWhiteSpace(alias))
                alias = "platform";
            return alias;
        }

        public static string SanitizeAlias(string? alias)
        {
            alias = (alias ?? string.Empty).Trim();
            if (alias.Length == 0)
                return string.Empty;
            return Regex.Replace(alias, "[^A-Za-z0-9._-]", string.Empty);
        }

        public static bool IsValidAlias(string? alias)
        {
            return !string.IsNullOrWhiteSpace(alias) && Regex.IsMatch(alias.Trim(), "^[A-Za-z0-9._-]+$");
        }

        private static List<PlatformConfig> DeduplicateAndSortPlatforms(List<PlatformConfig> list)
        {
            return (list ?? new List<PlatformConfig>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Url))
                .GroupBy(p => p.Url, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => string.IsNullOrWhiteSpace(p.Alias) ? p.Url : p.Alias, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Url, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public int GetInstructionHistoryLimit()
        {
            var value = _config["InstructionHistoryLimit"];
            return int.TryParse(value, out int result) ? result : 50;
        }

        public string GetDefaultTargetingMode()
        {
            return _config["DefaultTargetingMode"] ?? "Management Group";
        }

        public int GetMaxDisplayRows()
        {
            var value = _config["ResultDisplayLimit"];
            return int.TryParse(value, out int result) ? result : 2000;
        }

        public int GetIntSetting(string key, int defaultValue)
        {
            try
            {
                var v = _config[key];
                if (int.TryParse(v, out var i)) return i;
            }
            catch
            {
                // ignore
            }
            return defaultValue;
        }

        public int GetExportMaxRows()
        {
            var value = _config["ExportMaxRows"];
            return int.TryParse(value, out int result) ? result : 100000;
        }

        public string DefaultExportFormat => _config["DefaultExportFormat"] ?? "xlsx";
    }
}
