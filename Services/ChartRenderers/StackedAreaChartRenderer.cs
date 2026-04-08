using Avalonia.Controls;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.Collections.Generic;
using System.Linq;

namespace DexInstructionRunner.Services.ChartRenderers
{
    public static class StackedAreaChartRenderer
    {
        public static void Render(JArray chartData, StackPanel resultsPanel, string xField, string yField)
        {
            var series = new List<ISeries>();
            var allLabels = new List<string>();
            var labelSet = new HashSet<string>();

            foreach (var group in chartData)
            {
                var items = group["Items"] as JArray ?? new JArray();
                foreach (var item in items)
                {
                    string label = item[xField]?.ToString() ?? "Unknown";
                    if (labelSet.Add(label)) allLabels.Add(label);
                }
            }

            foreach (var group in chartData)
            {
                string groupName = group["Name"]?.ToString() ?? "Unknown";
                var items = group["Items"] as JArray ?? new JArray();

                var values = allLabels.Select(label =>
                {
                    var item = items.FirstOrDefault(i => (i[xField]?.ToString() ?? "Unknown") == label);
                    return item != null && double.TryParse(item[yField]?.ToString(), out double val) ? val : 0;
                }).ToList();

                series.Add(new StackedAreaSeries<double>
                {
                    Name = groupName,
                    Values = values
                });
            }

            var chart = new CartesianChart
            {
                Series = series,
                XAxes = new[] { new Axis { Labels = allLabels.ToArray() } },
                YAxes = new[] { new Axis { Name = yField } }
            };

            resultsPanel.Children.Add(chart);
        }
        private static SolidColorPaint GetThemeAwarePaint()
        {
            var currentTheme = Avalonia.Application.Current.ActualThemeVariant;
            var color = currentTheme == Avalonia.Styling.ThemeVariant.Dark ? SKColors.White : SKColors.Black;
            return new SolidColorPaint(color);
        }
    }

}