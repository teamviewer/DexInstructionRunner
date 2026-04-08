using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
public static class MetricPresetHelper
{
    public static List<string> LoadMetricsFromPreset(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Measures", out var measuresElement))
            {
                return measuresElement.EnumerateArray().Select(m => m.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to load metrics preset: {ex.Message}");
        }
        return new List<string>();
    }

    public static void SaveMetricsPreset(string filePath, List<string> metrics, string name = "Custom")
    {
        var payload = new
        {
            Name = name,
            Measures = metrics
        };
        File.WriteAllText(filePath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}
