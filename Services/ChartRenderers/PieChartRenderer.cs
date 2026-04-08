using Avalonia.Controls;
using Avalonia.Media;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.Collections.Generic;

namespace DexInstructionRunner.Services.ChartRenderers

{
    public static class PieChartRenderer
    {
        public static void Render(JArray chartData, StackPanel resultsPanel, string xField, string yField, bool isDark)
        {
            var series = new List<PieSeries<double>>();
            var palette = isDark ? new[]
            {
        SKColors.DodgerBlue, SKColors.Orange, SKColors.MediumSeaGreen,
        SKColors.HotPink, SKColors.Goldenrod, SKColors.MediumPurple
    } : new[]
            {
        SKColors.SteelBlue, SKColors.OrangeRed, SKColors.OliveDrab,
        SKColors.MediumVioletRed, SKColors.Sienna, SKColors.CadetBlue
    };

            int i = 0;
            foreach (var group in chartData)
            {
                var items = group["Items"] as JArray;
                if (items == null) continue;

                foreach (var item in items)
                {
                    string label = item[xField]?.ToString() ?? "Unknown";
                    if (!double.TryParse(item[yField]?.ToString(), out double value)) continue;

                    var color = palette[i % palette.Length];
                    series.Add(new PieSeries<double>
                    {
                        Values = new[] { value },
                        Name = label,
                        Fill = new SolidColorPaint(color),
                        DataLabelsPaint = ChartStyleHelper.GetThemeAwarePaint(),
                        DataLabelsSize = value > 5 ? 14 : 0,
                        DataLabelsFormatter = point => $"{label}: {value}"
                    });

                    i++;
                }
            }

            var chart = new PieChart
            {
                Series = series,
                LegendPosition = LegendPosition.Bottom,
                Height = 500,
                Background = new SolidColorBrush(isDark ? Colors.Black : Colors.White)
            };

            resultsPanel.Children.Add(chart);
        }

    }
}