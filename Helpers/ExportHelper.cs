// ExportHelper.cs (Runner project)
// Includes Instruction Results export with TSV fallback if >1M rows for XLSX,
// Experience export with multi-sheet XLSX, and CSV/TSV/XLSX format support.
// No placeholders; full implementations provided.

using Avalonia.Controls;
using Avalonia.Threading;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace DexInstructionRunner.Helpers
{
    public static class ExportHelper
    {
        // ------------------------------
        // Lightweight file logger (optional)
        // ------------------------------
        public static class FileLogger
        {
            public static void LogToFile(string? filePath, string message)
            {
                if (string.IsNullOrWhiteSpace(filePath)) return;
                try
                {
                    File.AppendAllText(filePath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                }
                catch
                {
                    // ignore file logging errors
                }
            }
        }

        // --------------------------------------------------------------------
        // BASIC dictionary export (kept for compatibility with existing runner)
        // --------------------------------------------------------------------
        public static async Task ExportDictionaryListAsync(
            List<Dictionary<string, string>> rows,
            string filePath,
            string format,
            TextBox logBox = null)
        {
            try
            {
                if (rows == null || rows.Count == 0)
                {
                    if (logBox != null) logBox.Text += "⚠️ No data to export.";
                    return;
                }

                var headers = rows.SelectMany(d => d.Keys).Distinct().ToList();
                var normalizedFormat = (format ?? "").Trim().ToLowerInvariant();

                if (normalizedFormat == "xlsx")
                {
                    var wb = new XLWorkbook();
                    var ws = wb.Worksheets.Add("Export");

                    for (int i = 0; i < headers.Count; i++)
                        ws.Cell(1, i + 1).Value = headers[i];

                    for (int r = 0; r < rows.Count; r++)
                        for (int c = 0; c < headers.Count; c++)
                            ws.Cell(r + 2, c + 1).Value = rows[r].TryGetValue(headers[c], out var val) ? val : "";

                    wb.SaveAs(filePath);
                }
                else if (normalizedFormat == "csv" || normalizedFormat == "tsv")
                {
                    char sep = normalizedFormat == "tsv" ? '\t' : ',';
                    var sb = new StringBuilder();
                    sb.AppendLine(string.Join(sep, headers));

                    foreach (var row in rows)
                    {
                        var line = headers.Select(h =>
                        {
                            var v = row.ContainsKey(h) ? row[h] : "";
                            // quote/escape to be safe
                            return $"\"{v.Replace("\"", "\"\"")}\"";
                        }).ToArray();

                        sb.AppendLine(string.Join(sep, line));
                    }

                    await File.WriteAllTextAsync(filePath, sb.ToString());
                }
                else
                {
                    if (logBox != null) logBox.Text += "❌ Unsupported export format.";
                    return;
                }

                if (logBox != null) logBox.Text += $"✅ Exported to: {filePath}";
            }
            catch (Exception ex)
            {
                if (logBox != null) logBox.Text += $"❌ Export failed: {ex.Message}";
            }
        }

        // -------------------------------------------------------------------------------------
        // ENHANCED dictionary export (schema + logger + CSV/TSV/XLSX)
        // -------------------------------------------------------------------------------------
        public sealed class SchemaColumn { public string Name { get; set; } = ""; }

        public static async Task ExportDictionaryListAsync(
            List<Dictionary<string, string>> rows,
            string filePath,
            string format,
            List<SchemaColumn> schema = null,
            Action<string> logFunc = null,
            string? logFilePath = null)
        {
            void Log(string message)
            {
                FileLogger.LogToFile(logFilePath, message);
                logFunc?.Invoke(message);
            }

            try
            {
                if (rows == null || rows.Count == 0)
                {
                    Log("⚠️ No data to export.");
                    return;
                }

                // Prefer schema header order if provided
                var headers = schema?.Select(col => col.Name).Distinct().ToList()
                              ?? rows.SelectMany(d => d.Keys).Distinct().ToList();

                var normalized = (format ?? "").Trim().ToUpperInvariant();
                if (normalized == "CSV" || normalized == "TSV")
                {
                    var delimiter = normalized == "TSV" ? "\t" : ",";
                    var sb = new StringBuilder();
                    sb.AppendLine(string.Join(delimiter, headers));

                    foreach (var row in rows)
                    {
                        var line = string.Join(delimiter, headers.Select(h =>
                        {
                            var val = row.TryGetValue(h, out var v) ? v : "";
                            return $"\"{val.Replace("\"", "\"\"")}\"";
                        }));
                        sb.AppendLine(line);
                    }

                    await File.WriteAllTextAsync(filePath, sb.ToString());
                    Log($"✅ {normalized} export complete.");
                }
                else if (normalized == "XLSX")
                {
                    var wb = new XLWorkbook();
                    var ws = wb.Worksheets.Add("Results");

                    for (int i = 0; i < headers.Count; i++)
                        ws.Cell(1, i + 1).Value = headers[i];

                    int currentRow = 2;
                    foreach (var row in rows)
                    {
                        for (int i = 0; i < headers.Count; i++)
                        {
                            var header = headers[i];
                            ws.Cell(currentRow, i + 1).Value = row.TryGetValue(header, out var val) ? val : "";
                        }
                        currentRow++;
                    }

                    wb.SaveAs(filePath);
                    Log("✅ XLSX export complete.");
                }
                else
                {
                    Log("❌ Unsupported format.");
                }

                Log($"✅ Exported to: {filePath}");
            }
            catch (Exception ex)
            {
                Log($"❌ Export failed: {ex.Message}");
            }
        }

        // --------------------------------------------------------------------------------
        // EXPERIENCE (device metrics) exports — complete implementation
        // --------------------------------------------------------------------------------
        public static async Task ExportExperienceResultsAsync(
            string platformUrl,
            string token,
            List<string> selectedMetrics,
            Dictionary<string, List<string>> selectedFilters,
            string filePath,
            string format,
            TextBox logBox = null,
            IProgress<(int RowCount, int TotalCount, double ElapsedSeconds)> progress = null)
        {
            await ExportLargeDatasetAsync(
                platformUrl,
                token,
                selectedMetrics,
                selectedFilters,
                filePath,
                format,
                logBox,
                progress);
        }

        public static async Task ExportLargeDatasetAsync(
            string platformUrl,
            string token,
            List<string> selectedMetrics,
            Dictionary<string, List<string>> selectedFilters,
            string filePath,
            string format,
            TextBox logBox = null,
            IProgress<(int RowCount, int TotalCount, double ElapsedSeconds)> progress = null)
        {
            try
            {
                int pageSize = 2000;
                int start = 1;
                int rowCount = 0;
                int sheetRowLimit = 1_000_000;
                int currentSheetIndex = 1;

                var headers = new List<string> { "Fqdn", "TachyonGuid", "Timestamp", "OperatingSystemType" };
                headers.AddRange(selectedMetrics);

                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", token);
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", "Explorer");

                var stopwatch = Stopwatch.StartNew();

                void Log(string message)
                {
                    if (logBox != null)
                        logBox.Text += message + Environment.NewLine;
                }

                if ((format ?? "").Trim().ToLowerInvariant() == "xlsx")
                {
                    var wb = new XLWorkbook();
                    var ws = wb.Worksheets.Add($"Sheet{currentSheetIndex}");

                    for (int i = 0; i < headers.Count; i++)
                        ws.Cell(1, i + 1).Value = headers[i];

                    int currentRow = 2;
                    int totalCount = 0;
                    object lockObj = new object();
                    int concurrency = 6;

                    await Task.Run(async () =>
                    {
                        while (true)
                        {
                            var tasks = new List<Task<(List<Dictionary<string, string>> Rows, int Total)>>();

                            for (int i = 0; i < concurrency; i++)
                            {
                                int pageStart = start + (i * pageSize);

                                tasks.Add(Task.Run(async () =>
                                {
                                    var payload = BuildSearchPayload(selectedFilters, selectedMetrics, pageStart, pageSize);
                                    var json = JsonConvert.SerializeObject(payload);

                                    HttpResponseMessage response = null;
                                    int maxRetries = 3;
                                    int delaySeconds = 2;

                                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                                    {
                                        try
                                        {
                                            response = await client.PostAsync(
                                                $"https://{platformUrl}/Experience/DeviceMetrics/Search",
                                                new StringContent(json, Encoding.UTF8, "application/json"));

                                            if (response.IsSuccessStatusCode)
                                                break;

                                            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                                            {
                                                await Dispatcher.UIThread.InvokeAsync(() =>
                                                    Log($"⚠️ Attempt {attempt}: Server={response.StatusCode}. Retrying..."));
                                            }
                                            else break;
                                        }
                                        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
                                        {
                                            await Dispatcher.UIThread.InvokeAsync(() =>
                                                Log($"⏱️ Timeout attempt {attempt}, retrying..."));
                                        }
                                        catch (Exception ex)
                                        {
                                            await Dispatcher.UIThread.InvokeAsync(() =>
                                                Log($"❌ Attempt {attempt}: {ex.Message}"));
                                            break;
                                        }

                                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds * attempt));
                                    }

                                    if (!response.IsSuccessStatusCode)
                                    {
                                        await Dispatcher.UIThread.InvokeAsync(() =>
                                            Log($"❌ API error at start={pageStart}: {response.StatusCode}"));
                                        return (new List<Dictionary<string, string>>(), 0);
                                    }

                                    var content = await response.Content.ReadAsStringAsync();
                                    return ParseResultsJson(content);
                                }));
                            }

                            var results = await Task.WhenAll(tasks);
                            var totalThisBatch = results.Sum(r => r.Rows.Count);
                            if (totalThisBatch == 0) break;

                            foreach (var (rows, total) in results)
                            {
                                if (totalCount == 0 && total > 0)
                                    totalCount = total;

                                foreach (var row in rows)
                                {
                                    lock (lockObj)
                                    {
                                        for (int c = 0; c < headers.Count; c++)
                                            ws.Cell(currentRow, c + 1).Value = row.TryGetValue(headers[c], out var val) ? val : "";
                                        currentRow++;
                                        rowCount++;

                                        if (currentRow > sheetRowLimit)
                                        {
                                            currentSheetIndex++;
                                            ws = wb.Worksheets.Add($"Sheet{currentSheetIndex}");
                                            for (int i = 0; i < headers.Count; i++)
                                                ws.Cell(1, i + 1).Value = headers[i];
                                            currentRow = 2;
                                        }
                                    }
                                }
                            }

                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                                Log($"📤 Exported {rowCount:N0} of {totalCount:N0} rows... ⏱️ {stopwatch.Elapsed:hh\\:mm\\:ss}");
                                progress?.Report((rowCount, totalCount, elapsedSeconds));
                            });

                            start += concurrency * pageSize;
                        }

                        wb.SaveAs(filePath);
                    });

                    var elapsed = stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        Log($"✅ Export complete. Total rows: {rowCount:N0} - ⏱️ {elapsed}"));
                }
                else if ((format ?? "").Trim().ToLowerInvariant() == "csv" || (format ?? "").Trim().ToLowerInvariant() == "tsv")
                {
                    char sep = (format ?? "").Trim().ToLowerInvariant() == "tsv" ? '\t' : ',';
                    var sb = new StringBuilder();
                    sb.AppendLine(string.Join(sep, headers));

                    int totalCount = 0;
                    int concurrency = 6;

                    while (true)
                    {
                        var tasks = new List<Task<(List<Dictionary<string, string>> Rows, int Total)>>();

                        for (int i = 0; i < concurrency; i++)
                        {
                            int pageStart = start + (i * pageSize);
                            tasks.Add(Task.Run(async () =>
                            {
                                var payload = BuildSearchPayload(selectedFilters, selectedMetrics, pageStart, pageSize);
                                var json = JsonConvert.SerializeObject(payload);

                                var response = await client.PostAsync(
                                    $"https://{platformUrl}/Experience/DeviceMetrics/Search",
                                    new StringContent(json, Encoding.UTF8, "application/json"));

                                if (!response.IsSuccessStatusCode)
                                {
                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                        Log($"❌ API error at start={pageStart}: {response.StatusCode}"));
                                    return (new List<Dictionary<string, string>>(), 0);
                                }

                                var content = await response.Content.ReadAsStringAsync();
                                return ParseResultsJson(content);
                            }));
                        }

                        var results = await Task.WhenAll(tasks);
                        var totalThisBatch = results.Sum(r => r.Rows.Count);
                        if (totalThisBatch == 0) break;

                        foreach (var (rows, total) in results)
                        {
                            if (totalCount == 0 && total > 0)
                                totalCount = total;

                            foreach (var row in rows)
                            {
                                var line = string.Join(sep, headers.Select(h =>
                                {
                                    var v = row.TryGetValue(h, out var val) ? val : "";
                                    return $"\"{v.Replace("\"", "\"\"")}\"";
                                }));
                                sb.AppendLine(line);
                                rowCount++;
                            }
                        }

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                            Log($"📤 Exported {rowCount:N0} of {totalCount:N0} rows... ⏱️ {stopwatch.Elapsed:hh\\:mm\\:ss}");
                            progress?.Report((rowCount, totalCount, elapsedSeconds));
                        });

                        start += concurrency * pageSize;
                    }

                    await File.WriteAllTextAsync(filePath, sb.ToString());
                    var elapsed2 = stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        Log($"✅ Export complete. Total rows: {rowCount:N0} - ⏱️ {elapsed2}"));
                }
                else
                {
                    Log("❌ Unsupported export format.");
                }
            }
            catch (Exception ex)
            {
                if (logBox != null) logBox.Text += $"❌ Export failed: {ex.Message}{Environment.NewLine}";
            }
        }

        private static object BuildSearchPayload(Dictionary<string, List<string>> selectedFilters, List<string> metrics, int start, int pageSize)
        {
            var filterOperands = new List<object>();

            foreach (var kvp in selectedFilters)
            {
                var key = kvp.Key;
                var values = kvp.Value?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                if (values == null || values.Count == 0) continue;

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
                        }).ToList()
                    });
                }
            }

            // Always include IsLatest = true
            filterOperands.Add(new
            {
                Attribute = "IsLatest",
                Operator = "==",
                Value = "true"
            });

            return new
            {
                Filter = new
                {
                    Operator = "AND",
                    Operands = filterOperands
                },
                GroupBy = new[] { "Fqdn", "TachyonGuid", "Timestamp", "OperatingSystemType" },
                Measures = metrics,
                RollUp = false,
                Start = start,
                PageSize = pageSize
            };
        }

        private static (List<Dictionary<string, string>> Rows, int TotalCount) ParseResultsJson(string json)
        {
            var root = JObject.Parse(json);
            int total = root["TotalCount"]?.Value<int>() ?? 0;
            var rows = new List<Dictionary<string, string>>();

            foreach (var item in root["Items"] ?? Enumerable.Empty<JToken>())
            {
                var row = item.Children<JProperty>().ToDictionary(p => p.Name, p => p.Value.ToString());
                rows.Add(row);
            }

            return (rows, total);
        }

        // -------------------------------------------------------------------------
        // Instruction Results export with progress and TSV fallback for >1M rows
        // -------------------------------------------------------------------------
        public static async Task ExportInstructionResultsWithProgressAsync(
            string platformUrl,
            string token,
            string consumerName,
            int responseId,
            string filePath,
            string format,
            ILogger? logger,
            int pageSize = 2000,
            int maxConcurrentRequests = 6)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", token);
            client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", consumerName);

            var statsUrl = $"https://{platformUrl}/Consumer/InstructionStatistics/Combined/{responseId}";
            logger?.LogInformation("📊 Retrieving statistics from: {Url}", statsUrl);
            var statsResponse = await client.GetAsync(statsUrl);

            if (!statsResponse.IsSuccessStatusCode)
            {
                logger?.LogWarning("❌ Failed to get instruction statistics: {Status}", statsResponse.StatusCode);
                return;
            }

            var statsContent = await statsResponse.Content.ReadAsStringAsync();
            var statsJson = JObject.Parse(statsContent);

            // TotalRowInserts is best for total rows; ReceivedCount indicates device responses
            int totalRowsExpected = statsJson["Summary"]?["TotalRowInserts"]?.Value<int>() ?? 0;
            int receivedCount = statsJson["Summary"]?["ReceivedCount"]?.Value<int>() ?? 0;

            if (receivedCount == 0)
            {
                logger?.LogWarning("⚠️ No responses received for ResponseId {ResponseId} — skipping export.", responseId);
                return;
            }

            // Auto-switch to TSV if caller asked for XLSX but stats exceed Excel's per-sheet row limit
            var requestedFormat = (format ?? "").Trim().ToUpperInvariant();
            if (requestedFormat == "XLSX" && totalRowsExpected > 1_000_000)
            {
                logger?.LogInformation(
                    "ℹ️ Estimated {Total} rows for ResponseId {ResponseId} exceeds Excel per-sheet capacity. " +
                    "Switching export format from XLSX to TSV for safety.",
                    totalRowsExpected, responseId);

                // Update both format and the file extension to .tsv
                format = "TSV";
                try
                {
                    var dir = Path.GetDirectoryName(filePath) ?? "";
                    var name = Path.GetFileNameWithoutExtension(filePath);
                    filePath = Path.Combine(dir, $"{name}.tsv");
                }
                catch { }
            }

            if (totalRowsExpected == 0)
            {
                logger?.LogWarning("⚠️ No row statistics available; proceeding without progress tracking.");
                totalRowsExpected = int.MaxValue; // avoid divide-by-zero in progress math
            }
            else
            {
                logger?.LogInformation("ℹ️ Total rows expected: {Total}", totalRowsExpected);
            }

            var allResults = new List<Dictionary<string, string>>();
            int totalFetched = 0;
            int lastLoggedCount = 0;
            int progressInterval = 12_000; // log every N rows
            bool morePages = true;

            string EncodeToken(string raw) => Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));

            List<string> GenerateTokens(int batchStartOffset, int pageSz, int batchCount)
            {
                var tokens = new List<string>();
                for (int i = 0; i < batchCount; i++)
                {
                    int offset = batchStartOffset + i * pageSz;
                    string raw = offset == 0 ? "0;0" : $"1;{offset + 1}";
                    string tok = offset == 0 ? raw : EncodeToken(raw);
                    tokens.Add(tok);
                }
                return tokens;
            }

            int currentOffset = 0;
            var url = $"https://{platformUrl}/Consumer/Responses/{responseId}";

            while (morePages)
            {
                var batchTokens = GenerateTokens(currentOffset, pageSize, maxConcurrentRequests);
                var tasks = batchTokens.Select(t => FetchPageAsync(client, url, t, pageSize, logger)).ToList();
                var results = await Task.WhenAll(tasks);

                int batchTotal = 0;
                foreach (var (rows, _nextRange) in results)
                {
                    allResults.AddRange(rows);
                    batchTotal += rows.Count;
                }

                if (batchTotal == 0)
                {
                    morePages = false;
                    break;
                }

                totalFetched += batchTotal;

                if (totalFetched - lastLoggedCount >= progressInterval || totalFetched >= totalRowsExpected)
                {
                    lastLoggedCount = totalFetched;
                    double pct = Math.Min(100.0, (totalFetched * 100.0) / totalRowsExpected);
                    logger?.LogInformation("📥 Progress: {Fetched} / {Expected} rows ({Pct:F2}%)",
                        totalFetched, totalRowsExpected, pct);
                }

                currentOffset += maxConcurrentRequests * pageSize;
            }

            if (allResults.Count == 0)
            {
                logger?.LogWarning("⚠️ No data returned for export.");
                return;
            }

            logger?.LogInformation("💾 Exporting {Count} rows to {Path}", allResults.Count, filePath);

            await ExportDictionaryListAsync(
                allResults,
                filePath,
                format,
                schema: null,
                logFunc: msg => logger?.LogInformation("{Msg}", msg),
                logFilePath: null);

            logger?.LogInformation("✅ Export complete.");
        }

        public static async Task<int> GetEstimatedRowCountFromStatistics(
            string platformUrl,
            int responseId,
            string token)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", token);
            client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", "Explorer");

            var statsUrl = $"https://{platformUrl}/Consumer/InstructionStatistics/Combined/{responseId}";
            var statsResponse = await client.GetAsync(statsUrl);

            if (!statsResponse.IsSuccessStatusCode)
                return 0;

            var statsContent = await statsResponse.Content.ReadAsStringAsync();
            var statsJson = JObject.Parse(statsContent);
            return statsJson["Summary"]?["ReceivedCount"]?.Value<int>() ?? 0;
        }

        private static async Task<(List<Dictionary<string, string>> rows, string? nextRange)> FetchPageAsync(
            HttpClient client,
            string url,
            string startToken,
            int pageSize,
            ILogger? logger)
        {
            var payload = new
            {
                Filter = (object?)null,
                Start = startToken,
                PageSize = pageSize
            };

            var jsonPayload = JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                logger?.LogWarning("❌ Failed to fetch page for token '{Token}': {Status}", startToken, response.StatusCode);
                return (new List<Dictionary<string, string>>(), null);
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var parsed = JObject.Parse(jsonResponse);

            var responsesArray = parsed["Responses"] as JArray;
            var nextRange = parsed["Range"]?.ToString();

            var rows = new List<Dictionary<string, string>>();
            if (responsesArray != null)
            {
                foreach (var obj in responsesArray)
                {
                    var fqdn = obj["Fqdn"]?.ToString()
                               ?? obj["Device"]?["Fqdn"]?.ToString()
                               ?? "Unknown";

                    var values = obj["Values"]?.ToObject<Dictionary<string, string>>() ?? new();
                    values["Fqdn"] = fqdn;
                    rows.Add(values);
                }
            }

            return (rows, nextRange);
        }

        // ---------------------------
        // Convenience CSV export
        // ---------------------------
        public static async Task ExportToCsvAsync(
            List<Dictionary<string, string>> rows,
            string path,
            TextBox? logBox = null)
        {
            var allHeaders = rows.SelectMany(d => d.Keys).Distinct().ToList();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", allHeaders));

            foreach (var row in rows)
            {
                var line = string.Join(",", allHeaders.Select(h =>
                {
                    var val = row.TryGetValue(h, out var v) ? v : "";
                    return $"\"{val.Replace("\"", "\"\"")}\"";
                }));
                sb.AppendLine(line);
            }

            await File.WriteAllTextAsync(path, sb.ToString());
            if (logBox != null)
                logBox.Text += "✅ Export complete." + Environment.NewLine;
        }

        // --------------------------------------
        // Default export file path (Documents)
        // --------------------------------------
        public static string GetAutoExportPath(int instructionId, string format)
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "InstructionExports");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, $"Instruction_{instructionId}_{DateTime.Now:yyyyMMdd_HHmmss}.{format}");
        }
    }

    // Kept outside the helper class to match prior runner structure
    public class ExperienceFilter
    {
        public string Attribute { get; set; }
        public string Operator { get; set; }
        public string Value { get; set; }
    }
}
