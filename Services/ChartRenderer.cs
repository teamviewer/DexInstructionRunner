using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DexInstructionRunner.Services.ChartRenderers;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace DexInstructionRunner.Services
{
    public static class ChartRenderer
    {
        public static void RenderChartDropdown(JObject chartData, JArray responseTemplateConfigs, StackPanel resultsPanel, bool isDark)
        {
            var chartConfigs = responseTemplateConfigs?.ToObject<List<JObject>>() ?? new();
            if (chartConfigs.Count == 0) return;

            var wrapper = new StackPanel { Orientation = Orientation.Vertical };

            var chartComboBox = new ComboBox
            {
                Width = 300,
                Margin = new Thickness(0, 10, 0, 0),
                ItemsSource = chartConfigs.Select(c => c["Title"]?.ToString()).ToList(),
                SelectedIndex = 0,
                Background = Brushes.White,
                Foreground = Brushes.Black
            };

            chartComboBox.SelectionChanged += (_, __) =>
            {
                var selectedConfig = chartConfigs[chartComboBox.SelectedIndex];
                var chartId = selectedConfig["Id"]?.ToString();
                var chartType = selectedConfig["Type"]?.ToString();
                var xField = selectedConfig["X"]?.ToString();
                var yField = selectedConfig["Y"]?.ToString();

                if (!string.IsNullOrWhiteSpace(chartId) && chartData[chartId] is JArray selectedData)
                {
                    wrapper.Children.RemoveRange(1, wrapper.Children.Count - 1);
                    var chartPanel = new StackPanel();
                    RenderChartResults(selectedData, chartType ?? "Bar", chartPanel, xField ?? "Product", yField ?? "Count", isDark);
                    wrapper.Children.Add(chartPanel);
                }
            };

            var initialConfig = chartConfigs[0];
            var initialChartId = initialConfig["Id"]?.ToString();
            var initialChartType = initialConfig["Type"]?.ToString();
            var initialX = initialConfig["X"]?.ToString();
            var initialY = initialConfig["Y"]?.ToString();

            var chartHolder = new StackPanel();
            if (!string.IsNullOrWhiteSpace(initialChartId) && chartData[initialChartId] is JArray initialData)
            {
                RenderChartResults(initialData, initialChartType ?? "Bar", chartHolder, initialX ?? "Product", initialY ?? "Count", isDark);
            }

            wrapper.Children.Add(chartComboBox);
            wrapper.Children.Add(chartHolder);
            resultsPanel.Children.Add(wrapper);
        }

        public static void RenderChartResults(JArray chartData, string chartType, StackPanel resultsPanel, string xField, string yField, bool isDark)
        {
            resultsPanel.Children.Clear();

            switch (chartType?.ToLowerInvariant())
            {
                case "pie":
                    PieChartRenderer.Render(chartData, resultsPanel, xField, yField, isDark);
                    break;
                case "bar":
                case "smartbar":
                case "column":
                    BarChartRenderer.Render(chartData, resultsPanel, xField, yField, isDark);
                    break;
                case "line":
                    LineChartRenderer.Render(chartData, resultsPanel, xField, yField);
                    break;
                case "stackedarea":
                    StackedAreaChartRenderer.Render(chartData, resultsPanel, xField, yField);
                    break;
                default:
                    BarChartRenderer.Render(chartData, resultsPanel, xField, yField, isDark);
                    break;
            }
        }

    }
}



