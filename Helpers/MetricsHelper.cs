
using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace DexInstructionRunner.Services
{
    public class MetricsHelper
    {
        private readonly string _defaultMetricsPath = Path.Combine(AppContext.BaseDirectory, "Config", "default_metrics.json");
        private readonly ListBox _metricsListBox;

        public MetricsHelper(ListBox metricsListBox)
        {
            _metricsListBox = metricsListBox;

            try
            {
                var configDir = Path.GetDirectoryName(_defaultMetricsPath);
                if (!Directory.Exists(configDir))
                    Directory.CreateDirectory(configDir);

                var testFile = Path.Combine(configDir, "write_test.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine("⚠️ Config folder is not writable: " + ex.Message);
            }
        }

        public List<string> GetSelectedMetrics()
        {
            var selected = new List<string>();

            foreach (var item in _metricsListBox.Items)
            {
                // Example assumes item is a wrapper ViewModel like:
                // new { Measure = { Title = "ResponsivenessScore" }, IsSelected = true }
                dynamic metricItem = item;
                if (metricItem?.IsSelected == true)
                {
                    selected.Add(metricItem?.Measure?.Name?.ToString());
                }
            }

            return selected.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        }

        public async Task<List<string>> GetActiveOrDefaultMetricsAsync()
        {
            var selected = GetSelectedMetrics();
            if (selected.Any())
                return selected;

            if (File.Exists(_defaultMetricsPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_defaultMetricsPath);
                    return JsonSerializer.Deserialize<List<string>>(json) ?? GetHardcodedDefaults();
                }
                catch
                {
                    return GetHardcodedDefaults();
                }
            }

            return GetHardcodedDefaults();
        }

        public async Task SaveDefaultMetricsAsync(List<string> selected)
        {
            var json = JsonSerializer.Serialize(selected.Distinct().ToList());
            await File.WriteAllTextAsync(_defaultMetricsPath, json);
        }

        private List<string> GetHardcodedDefaults()
        {
            return new List<string>
            {
                "ExperienceScore",
                "StabilityScore",
                "PerformanceScore",
                "ResponsivenessScore"
            };
        }
    }
}
