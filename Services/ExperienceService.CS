using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Collections.Generic;  // For List<> and Dictionary<>
using System.IO;  // Required for File operations
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DexInstructionRunner.Services
{
    public class ExperienceService
    {
        private readonly HttpClient _httpClient;
        private string _token; // Token is assigned from AuthenticationService or cache
        private readonly TextBox _logTextBox;
        private string _baseExperienceUrl;
        private string _platformUrl;
        private DexInstructionRunner.Models.PlatformConfig _selectedPlatform;

        // Constructor: Initializes the ExperienceService with the platform URL, token, and log TextBox
        public ExperienceService(string platformUrl, string token, TextBox logTextBox)
        {
            _httpClient = new HttpClient();
            _logTextBox = logTextBox;
            UpdateContext(platformUrl, token);

            // Set headers that do not change
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!_httpClient.DefaultRequestHeaders.Contains("X-Tachyon-Consumer"))
                _httpClient.DefaultRequestHeaders.Add("X-Tachyon-Consumer", "Explorer");
        }

        // Updates platform + token context (needed when switching platforms)
        public void UpdateContext(string platformUrl, string newToken)
        {
            _platformUrl = (platformUrl ?? "").Trim();
            _baseExperienceUrl = $"https://{_platformUrl.Trim().TrimEnd('/')}/Experience";

            _token = newToken;
            try
            {
                if (_httpClient.DefaultRequestHeaders.Contains("X-Tachyon-Authenticate"))
                    _httpClient.DefaultRequestHeaders.Remove("X-Tachyon-Authenticate");
            }
            catch
            {
                // ignore
            }

            if (!string.IsNullOrWhiteSpace(newToken))
                _httpClient.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", newToken);
        }

        // Method to update the token after refresh
        public void UpdateToken(string newToken)
        {
            UpdateContext(_platformUrl, newToken);

            // Save the new token to the cache (optional, depending on your requirements)
            SaveToken(newToken);
        }

        private static bool LooksLikeErrorArray(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            for (int i = 0; i < json.Length; i++)
            {
                var ch = json[i];
                if (char.IsWhiteSpace(ch)) continue;
                return ch == '[';
            }
            return false;
        }

        private static string? TryParseErrorArrayMessage(string json)
        {
            try
            {
                var arr = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
                if (arr == null || arr.Count == 0) return null;
                var first = arr[0];
                if (first.TryGetValue("ErrorCode", out var ec) || first.TryGetValue("errorCode", out ec))
                {
                    var code = ec?.ToString();
                    if (first.TryGetValue("Message", out var msg) || first.TryGetValue("message", out msg))
                    {
                        var m = msg?.ToString();
                        if (!string.IsNullOrWhiteSpace(m))
                            return $"{code}: {m}";
                    }
                    return code;
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        /// <summary>
        /// Start-based paging (UI paging) for a list of FQDNs.
        /// IMPORTANT: Do not include Bookmark for start>1; many servers will ignore Start if Bookmark exists.
        /// </summary>
        public async Task<ExperienceApiResponse> FetchExperiencePageForFqdnsAsync(
            List<string> fqdnList,
            List<string> selectedMetrics,
            string platformUrl,
            string token,
            string? bookmark,
            int pageSize,
            int start,
            object? additionalAndOperand = null)
        {
            UpdateContext(platformUrl, token);

            var results = new ExperienceApiResponse();
            var baseUrl = $"https://{platformUrl.Trim().TrimEnd('/')}/Experience/DeviceMetrics/Search";

            // Build filter list using the FQDN list
            var filterOperands = new List<object>();

            var fqdns = fqdnList?
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            // FQDN matching:
            // - Default: begins-with using 'like' (foo -> foo%)
            // - Use '*' as wildcard (mapped to '%')
            // - Prefix with '~' to do contains (~foo -> %foo%)
            object BuildFqdnOperand(string raw)
            {
                var v = (raw ?? string.Empty).Trim();
                if (v.Length == 0)
                {
                    return new
                    {
                        Attribute = "Fqdn",
                        Operator = "like",
                        Value = "%"
                    };
                }

                // Contains operator using "~" prefix
                if (v.StartsWith("~", StringComparison.Ordinal))
                {
                    var needle = v.Substring(1).Trim();
                    if (needle.Length == 0)
                    {
                        return new { Attribute = "Fqdn", Operator = "like", Value = "%" };
                    }

                    var pat = needle.Replace('*', '%');
                    if (!pat.StartsWith("%", StringComparison.Ordinal)) pat = "%" + pat;
                    if (!pat.EndsWith("%", StringComparison.Ordinal)) pat = pat + "%";

                    return new { Attribute = "Fqdn", Operator = "like", Value = pat };
                }

                // User-provided wildcard(s)
                if (v.Contains('*'))
                {
                    var pat = v.Replace('*', '%');
                    return new { Attribute = "Fqdn", Operator = "like", Value = pat };
                }

                // Default begins-with
                return new
                {
                    Attribute = "Fqdn",
                    Operator = "like",
                    Value = v.EndsWith("%", StringComparison.Ordinal) ? v : (v + "%")
                };
            }

            if (fqdns.Count == 1)
            {
                filterOperands.Add(BuildFqdnOperand(fqdns[0]));
            }
            else if (fqdns.Count > 1)
            {
                filterOperands.Add(new
                {
                    Operator = "OR",
                    Operands = fqdns.Select(BuildFqdnOperand).ToArray()
                });
            }

            // Additional server-side filter operand(s) (already AND-scoped by the caller)
            if (additionalAndOperand != null)
                filterOperands.Add(additionalAndOperand);

            // Always include IsLatest = true
            filterOperands.Add(new
            {
                Attribute = "IsLatest",
                Operator = "==",
                Value = "true"
            });

            var filter = new
            {
                Operator = "AND",
                Operands = filterOperands
            };

            var payload = new Dictionary<string, object?>
            {
                ["Filter"] = filter,
                ["GroupBy"] = new[] { "Fqdn", "TachyonGuid", "Timestamp", "OperatingSystemType" },
                ["Measures"] = selectedMetrics?.ToArray() ?? Array.Empty<string>(),
                ["RollUp"] = false,
                ["PageSize"] = Math.Min(Math.Max(pageSize, 1), 500),
                ["Start"] = Math.Max(1, start)
            };

            // Only include Bookmark on Start==1 (prevents Start being ignored on some platforms)
            if (Math.Max(1, start) <= 1 && !string.IsNullOrWhiteSpace(bookmark))
                payload["Bookmark"] = bookmark;

            var jsonPayload = JsonSerializer.Serialize(payload);

            var request = new HttpRequestMessage(HttpMethod.Post, baseUrl)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Tachyon-Authenticate", token);

            var response = await _httpClient.SendAsync(request);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                SafeLog($"❌ Experience paged fetch failed: {(int)response.StatusCode} {response.ReasonPhrase}. {responseJson}");
                return results;
            }

            if (LooksLikeErrorArray(responseJson))
            {
                var msg = TryParseErrorArrayMessage(responseJson) ?? responseJson;
                SafeLog($"❌ Experience API error: {msg}");
                return results;
            }

            var root = JsonSerializer.Deserialize<ExperienceApiResponse>(responseJson);
            return root ?? results;
        }

        public async Task<ExperienceApiResponse> FetchExperienceScoresPagedForFqdnsAsync(
            List<string> fqdnList,
            List<string> selectedMetrics,
            string? bookmark,
            int pageSize,
            string platformUrl,
            string token)
        {
            UpdateContext(platformUrl, token);

            var results = new ExperienceApiResponse();
            if (fqdnList == null || fqdnList.Count == 0)
                return results;

            // prevent extreme payload sizes for UI paging
            const int maxFqdnsForSingleQuery = 2000;
            var effective = fqdnList.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct().Take(maxFqdnsForSingleQuery).ToList();

            var apiUrl = $"https://{platformUrl.Trim().TrimEnd('/')}/Experience/DeviceMetrics/Search";

            var payload = new Dictionary<string, object?>
            {
                ["Filter"] = new
                {
                    Operator = "AND",
                    Operands = new List<object>
                    {
                        new { Attribute = "IsLatest", Operator = "==", Value = "true" },
                        new
                        {
                            Operator = "OR",
                            Operands = effective.Select(fqdn => new
                            {
                                Attribute = "Fqdn",
                                Operator = "like",
                                Value = fqdn.EndsWith("%") ? fqdn : fqdn + "%"
                            }).Cast<object>().ToList()
                        }
                    }
                },
                ["GroupBy"] = new[] { "Fqdn", "TachyonGuid", "Timestamp", "OperatingSystemType" },
                ["Measures"] = selectedMetrics?.ToArray() ?? Array.Empty<string>(),
                ["RollUp"] = false,
                ["Start"] = 1,
                ["PageSize"] = pageSize
            };

            if (!string.IsNullOrWhiteSpace(bookmark))
                payload["Bookmark"] = bookmark;

            var jsonPayload = JsonSerializer.Serialize(payload);

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            requestMessage.Headers.Add("X-Tachyon-Authenticate", token);

            var response = await _httpClient.SendAsync(requestMessage);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                SafeLog($"❌ Experience paged fetch failed: {(int)response.StatusCode} {response.ReasonPhrase}. {responseJson}");
                return results;
            }

            if (LooksLikeErrorArray(responseJson))
            {
                var msg = TryParseErrorArrayMessage(responseJson) ?? responseJson;
                SafeLog($"❌ Experience API error: {msg}");
                return results;
            }

            var root = JsonSerializer.Deserialize<ExperienceApiResponse>(responseJson);
            return root ?? results;
        }

        // Method to save the token to cache (if needed)
        private void SaveToken(string token)
        {
            var tokenFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DexConsole", "cached_token.txt");

            // Ensure the directory exists
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(tokenFilePath));

            // Save token to file
            File.WriteAllText(tokenFilePath, token);
        }

        public async Task<List<Dictionary<string, object>>> FetchExperienceScoresWithFiltersAsync(object payload, string token)
        {
            try
            {
                var apiUrl = $"{_baseExperienceUrl}/DeviceMetrics/Search";
                var jsonPayload = JsonSerializer.Serialize(payload);

                var response = await _httpClient.PostAsync(apiUrl, new StringContent(jsonPayload, Encoding.UTF8, "application/json"));
                var resultJson = await response.Content.ReadAsStringAsync();

                response.EnsureSuccessStatusCode();

                var root = JsonSerializer.Deserialize<ExperienceApiResponse>(resultJson);
                return root?.Items ?? new List<Dictionary<string, object>>();
            }
            catch (Exception ex)
            {
                // Log or handle the exception
                Console.WriteLine($"❌ Error fetching experience scores: {ex.Message}");
                return new List<Dictionary<string, object>>();
            }
        }

        public async Task<List<Dictionary<string, object>>> FetchExperienceScoreJsonForFqdnsAsync(List<string> fqdnList, List<string> selectedMetrics, string platformUrl, string token)
        {
            if (fqdnList == null || fqdnList.Count == 0)
            {
                SafeLog("❌ No FQDNs provided for experience query.");
                return new List<Dictionary<string, object>>();
            }

            const int batchSize = 100;
            var allResults = new List<Dictionary<string, object>>();

            for (int i = 0; i < fqdnList.Count; i += batchSize)
            {
                var batch = fqdnList.Skip(i).Take(batchSize).ToList();
                SafeLog($"📦 Fetching experience scores for batch {i / batchSize + 1} with {batch.Count} FQDNs...");

                // Pass platformUrl and token to the parallel fetching function
                var batchResults = await FetchExperienceScoresInParallel(batch.ToList(), selectedMetrics, platformUrl, token);

                if (batchResults != null)
                    allResults.AddRange(batchResults);
            }

            return allResults;
        }

        public async Task<List<Dictionary<string, object>>> FetchExperienceScoreJsonForFiltersAsync(
            Dictionary<string, List<string>> selectedFilters,
            List<string> selectedMetrics,
            string platformUrl,
            string token)
        {
            var results = new List<Dictionary<string, object>>();
            var baseUrl = $"https://{platformUrl.Trim().TrimEnd('/')}/Experience/DeviceMetrics/Search";

            try
            {
                // Build filter list with OR blocks for multiple values, single for one
                var filterOperands = new List<object>();

                foreach (var kvp in selectedFilters)
                {
                    var key = kvp.Key;
                    var values = kvp.Value?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

                    if (values != null && values.Any())
                    {
                        if (values.Count == 1)
                        {
                            // Single value = no need to wrap in OR
                            filterOperands.Add(new
                            {
                                Attribute = key,
                                Operator = "==",
                                Value = values.First()
                            });
                        }
                        else
                        {
                            // Multiple values = wrap in OR block
                            filterOperands.Add(new
                            {
                                Operator = "OR",
                                Operands = values.Select(val => new
                                {
                                    Attribute = key,
                                    Operator = "==",
                                    Value = val
                                }).ToArray()
                            });
                        }
                    }
                }

                // Always include IsLatest = true
                filterOperands.Add(new
                {
                    Attribute = "IsLatest",
                    Operator = "==",
                    Value = "true"
                });

                var filter = new
                {
                    Operator = "AND",
                    Operands = filterOperands
                };

                var payload = new
                {
                    Filter = filter,
                    GroupBy = new[] { "Fqdn", "TachyonGuid", "Timestamp", "OperatingSystemType" },
                    Measures = selectedMetrics?.ToArray() ?? Array.Empty<string>(),
                    RollUp = false,
                    Start = 1,
                    PageSize = 50
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                _logTextBox.Text += $"📡 Fetching experience scores (filters) from {baseUrl}...\n";
                _logTextBox.Text += $"📤 Payload being sent:\n{jsonPayload}\n";

                var request = new HttpRequestMessage(HttpMethod.Post, baseUrl)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("X-Tachyon-Authenticate", token);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                if (doc.RootElement.TryGetProperty("Items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                {
                    results = itemsElement.EnumerateArray()
                        .Select(item => item.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind switch
                        {
                            JsonValueKind.String => (object)p.Value.GetString(),
                            JsonValueKind.Number => p.Value.TryGetInt64(out long l) ? l : p.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Null => null,
                            _ => p.Value.ToString()
                        }))
                        .ToList();
                }

                _logTextBox.Text += $"✅ Retrieved {results?.Count ?? 0} filtered experience rows.\n";
            }
            catch (Exception ex)
            {
                _logTextBox.Text += $"❌ FetchExperienceScoreJsonForFiltersAsync failed: {ex.Message}\n";
            }

            return results ?? new List<Dictionary<string, object>>();
        }

        private async Task<List<Dictionary<string, object>>> FetchExperienceScoresInParallel(List<string> allFqdns, List<string> selectedMetrics, string platformUrl, string token)
        {
            const int batchSize = 100;
            var results = new List<Dictionary<string, object>>();

            var batches = allFqdns
                .Select((fqdn, index) => new { fqdn, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.fqdn).ToList());

            var tasks = batches.Select(async batch =>
            {
                try
                {
                    // Pass platformUrl and token when fetching batches
                    SafeLog($"📦 Fetching experience scores from {platformUrl} FQDNs...");
                    var batchResults = await FetchExperienceScoreBatchAsync(batch, selectedMetrics, platformUrl, token);

                    lock (results)
                    {
                        results.AddRange(batchResults);
                    }
                }
                catch (Exception ex)
                {
                    SafeLog($"❌ Failed fetching batch: {ex.Message}");
                }
            });

            await Task.WhenAll(tasks);
            return results;
        }

        private async Task<List<Dictionary<string, object>>> FetchExperienceScoreBatchAsync(List<string> fqdnBatch, List<string> selectedMetrics, string platformUrl, string token, string defaultManagementGroup = null)
        {
            try
            {
                var apiUrl = $"https://{platformUrl}/Experience/DeviceMetrics/Search";

                // Construct the payload
                var payload = new
                {
                    Filter = new
                    {
                        Operator = "AND",
                        Operands = new List<object>
                        {
                            new { Attribute = "IsLatest", Operator = "==", Value = "true" },
                            new
                            {
                                Operator = "OR",
                                Operands = fqdnBatch.Select(fqdn => new
                                {
                                    Attribute = "Fqdn",
                                    Operator = "like",
                                    Value = fqdn.EndsWith("%") ? fqdn : fqdn + "%"
                                }).Cast<object>().ToList()
                            }
                        }
                    },
                    GroupBy = new[] { "Fqdn", "TachyonGuid", "Timestamp", "OperatingSystemType" },
                    Measures = selectedMetrics?.ToArray() ?? Array.Empty<string>(),
                    RollUp = false,
                    Start = 1,
                    PageSize = 500
                };

                var jsonPayload = JsonSerializer.Serialize(payload);

                SafeLog($"Testing API CALL to {apiUrl} Token (last 4): {token?.Substring(token.Length - 4)}");

                // Log the API call
                var resultJson = await ApiLogger.LogApiCallAsync(
                    label: "FetchExperienceScoreBatchAsync",
                    endpoint: "/DeviceMetrics/Search",
                    apiCall: async () =>
                    {
                        // Create HttpRequestMessage to add the token to headers
                        var requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                        {
                            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                        };

                        // Add token to headers
                        requestMessage.Headers.Add("X-Tachyon-Authenticate", token);

                        // Send the request and get the response
                        var response = await _httpClient.SendAsync(requestMessage);
                        var responseJson = await response.Content.ReadAsStringAsync();

                        return responseJson;
                    },
                    payloadJson: jsonPayload
                );

                // Attempt to deserialize the response into the expected object format
                try
                {
                    var root = JsonSerializer.Deserialize<ExperienceApiResponse>(resultJson);
                    return root?.Items ?? new List<Dictionary<string, object>>();
                }
                catch (JsonException ex)
                {
                    SafeLog($"❌ Error deserializing response: {ex.Message}. Response: {resultJson}");
                    return new List<Dictionary<string, object>>();
                }
            }
            catch (Exception ex)
            {
                SafeLog("❌ Exception fetching batch experience scores: " + ex.Message);
                return new List<Dictionary<string, object>>();
            }
        }

        public class ExperienceApiResponse
        {
            public string Bookmark { get; set; }
            public List<Dictionary<string, object>> Items { get; set; } = new();
            public int TotalCount { get; set; }
        }

        /// <summary>
        /// Existing overload used elsewhere in the app.
        /// Keep payload shape identical; bridge into the Start-based method with start=1.
        /// </summary>
        public async Task<ExperienceApiResponse> FetchExperiencePageForFqdnsAsync(
            List<string> fqdnList,
            List<string> selectedMetrics,
            string platformUrl,
            string token,
            string? bookmark,
            int pageSize,
            object? additionalAndOperand = null)
        {
            // Bridge to Start-based implementation (keeps other call sites working)
            return await FetchExperiencePageForFqdnsAsync(
                fqdnList: fqdnList,
                selectedMetrics: selectedMetrics,
                platformUrl: platformUrl,
                token: token,
                bookmark: bookmark,
                pageSize: pageSize,
                start: 1,
                additionalAndOperand: additionalAndOperand);
        }

        /// <summary>
        /// Fetch a single page of Experience metrics using filter selections.
        /// Start-based paging (UI paging). Do not include Bookmark for start>1.
        /// </summary>
        public async Task<ExperienceApiResponse> FetchExperiencePageForFiltersAsync(
            Dictionary<string, List<string>> selectedFilters,
            List<string> selectedMetrics,
            string platformUrl,
            string token,
            string? bookmark,
            int pageSize,
            int start,
            object? additionalAndOperand = null)
        {
            UpdateContext(platformUrl, token);

            var baseUrl = $"https://{platformUrl.Trim().TrimEnd('/')}/Experience/DeviceMetrics/Search";

            // Build filter list with OR blocks for multiple values, single for one
            var filterOperands = new List<object>();

            if (selectedFilters != null)
            {
                foreach (var kvp in selectedFilters)
                {
                    var key = kvp.Key;
                    var values = kvp.Value?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

                    if (values != null && values.Any())
                    {
                        if (values.Count == 1)
                        {
                            filterOperands.Add(new
                            {
                                Attribute = key,
                                Operator = "==",
                                Value = values.First()
                            });
                        }
                        else
                        {
                            filterOperands.Add(new
                            {
                                Operator = "OR",
                                Operands = values.Select(val => new
                                {
                                    Attribute = key,
                                    Operator = "==",
                                    Value = val
                                }).ToArray()
                            });
                        }
                    }
                }
            }

            // Additional server-side filter operand(s) (already AND-scoped by the caller)
            if (additionalAndOperand != null)
                filterOperands.Add(additionalAndOperand);

            // Always include IsLatest = true
            filterOperands.Add(new
            {
                Attribute = "IsLatest",
                Operator = "==",
                Value = "true"
            });

            var filter = new
            {
                Operator = "AND",
                Operands = filterOperands
            };

            var payload = new Dictionary<string, object?>
            {
                ["Filter"] = filter,
                ["GroupBy"] = new[] { "Fqdn", "TachyonGuid", "Timestamp", "OperatingSystemType" },
                ["Measures"] = selectedMetrics?.ToArray() ?? Array.Empty<string>(),
                ["RollUp"] = false,
                ["Start"] = Math.Max(1, start),
                ["PageSize"] = Math.Min(Math.Max(pageSize, 1), 500)
            };

            // Only include Bookmark on Start==1 (prevents Start being ignored)
            if (Math.Max(1, start) <= 1 && !string.IsNullOrWhiteSpace(bookmark))
                payload["Bookmark"] = bookmark;

            var jsonPayload = JsonSerializer.Serialize(payload);

            var request = new HttpRequestMessage(HttpMethod.Post, baseUrl)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Tachyon-Authenticate", token);

            var response = await _httpClient.SendAsync(request);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                SafeLog($"❌ Experience page (filters) fetch failed: {(int)response.StatusCode} {response.ReasonPhrase}. Response: {responseJson}");
                return new ExperienceApiResponse();
            }

            if (LooksLikeErrorArray(responseJson))
            {
                SafeLog($"❌ Experience API returned errors: {responseJson}");
                return new ExperienceApiResponse();
            }

            try
            {
                var root = JsonSerializer.Deserialize<ExperienceApiResponse>(responseJson);
                return root ?? new ExperienceApiResponse();
            }
            catch (JsonException ex)
            {
                SafeLog($"❌ Error deserializing experience page (filters): {ex.Message}. Response: {responseJson}");
                return new ExperienceApiResponse();
            }
        }

        public async Task<List<string>> FetchGroupByValuesAsync(string groupByField, string platformUrl, string token)
        {
            var results = new List<string>();
            var url = $"https://{platformUrl.Trim().TrimEnd('/')}/Experience";
            try
            {
                // Define the payload for the API request
                var payload = new
                {
                    Filter = new
                    {
                        Operator = "AND",
                        Operands = new[] { new { Attribute = "IsLatest", Operator = "==", Value = "true" } }
                    },
                    GroupBy = new[] { groupByField },
                    Measures = new[] { "DeviceCount" },
                    RollUp = false
                };

                var jsonPayload = JsonSerializer.Serialize(payload);

                _logTextBox.Text += $"📡 Fetching {groupByField} from {url}/DeviceMetrics/Search\n Token (last 4): {token?.Substring(token.Length - 4)}\\n\";\r\n";

                // Use the _httpClient with the correct headers for authentication
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{url}/DeviceMetrics/Search")
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };

                // Add authentication headers
                requestMessage.Headers.Add("X-Tachyon-Authenticate", token);  // Add the token

                var response = await _httpClient.SendAsync(requestMessage);

                // Ensure the request was successful
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();

                // Parse the API response
                var parsed = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(responseJson);

                if (parsed != null)
                {
                    foreach (var item in parsed)
                    {
                        if (item.ContainsKey(groupByField) && item[groupByField] != null)
                        {
                            results.Add(item[groupByField].ToString());
                        }
                    }
                }
                else
                {
                    _logTextBox.Text += $"⚠️ No data returned for groupBy '{groupByField}'.\n";
                }
            }
            catch (Exception ex)
            {
                _logTextBox.Text += $"❌ FetchGroupBy failed: {ex.Message}\n";
            }

            return results;
        }

        // Method to safely log messages
        private void SafeLog(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_logTextBox != null)
                    _logTextBox.Text += message + "\n";
            });
        }


        // Method to get Management Groups
        public async Task<List<string>> GetManagementGroupsAsync(string platformUrl, string token)
        {
            return await FetchGroupByValuesAsync("ManagementGroup", platformUrl, token);
        }

        // Method to get Locations
        public async Task<List<string>> GetLocationsAsync(string platformUrl, string token)
        {
            return await FetchGroupByValuesAsync("Location", platformUrl, token);
        }

        // Method to get Device Models
        public async Task<List<string>> GetDeviceModelsAsync(string platformUrl, string token)
        {
            return await FetchGroupByValuesAsync("DeviceModel", platformUrl, token);
        }

        // Method to get Criticalities
        public async Task<List<string>> GetCriticalitiesAsync(string platformUrl, string token)
        {
            return await FetchGroupByValuesAsync("Criticality", platformUrl, token);
        }

        // Method to get Operating Systems
        public async Task<List<string>> GetOperatingSystemsAsync(string platformUrl, string token)
        {
            var payload = new
            {
                Filter = new
                {
                    Operator = "AND",
                    Operands = new[] { new { Attribute = "IsLatest", Operator = "==", Value = "true" } }
                },
                GroupBy = new[] { "OperatingSystemType", "OperatingSystem" },
                Measures = new[] { "DeviceCount" },
                RollUp = true
            };

            var jsonPayload = JsonSerializer.Serialize(payload);

            Console.WriteLine($"\ud83d\udce4 OS GroupBy Payload: {jsonPayload}");

            // Use the class-level _baseExperienceUrl, not a local variable
            var response = await _httpClient.PostAsync(
                $"{_baseExperienceUrl}/DeviceMetrics/Search",
                new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            );

            Console.WriteLine($"\ud83d\udce4 HTTP Status for OS GroupBy: {response.StatusCode}");

            response.EnsureSuccessStatusCode();

            var contentStream = await response.Content.ReadAsStreamAsync();
            var parsed = await JsonSerializer.DeserializeAsync<List<OperatingSystemGroup>>(contentStream);

            var result = new List<string>();

            if (parsed != null)
            {
                foreach (var typeGroup in parsed)
                {
                    if (typeGroup.Children != null)
                    {
                        foreach (var child in typeGroup.Children)
                        {
                            if (!string.IsNullOrWhiteSpace(child.OperatingSystem))
                            {
                                result.Add(child.OperatingSystem);
                            }
                        }
                    }
                }
            }

            return result;
        }

        public async Task<List<Dictionary<string, object>>> PostExperienceQueryAsync(string jsonPayload)
        {
            // Log the API call using ApiLogger
            var resultJson = await ApiLogger.LogApiCallAsync(
                label: "PostExperienceQueryAsync",
                endpoint: $"{_baseExperienceUrl}/DeviceMetrics/Search",
                apiCall: async () =>
                {
                    var response = await _httpClient.PostAsync(
                        $"{_baseExperienceUrl}/DeviceMetrics/Search",
                        new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                    );

                    var responseJson = await response.Content.ReadAsStringAsync();
                    return responseJson;
                },
                payloadJson: jsonPayload
            );

            Console.WriteLine($"🔵 HTTP Status for PostExperienceQuery: {resultJson}");

            var parsed = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(resultJson);
            return parsed ?? new List<Dictionary<string, object>>();
        }

        // Internal classes for parsing the OS group response
        private class OperatingSystemGroup
        {
            public string OperatingSystemType { get; set; }
            public int Count { get; set; }
            public List<OperatingSystemChild> Children { get; set; }
        }

        private class OperatingSystemChild
        {
            public string OperatingSystem { get; set; }
            public int Count { get; set; }
        }
    }
}
