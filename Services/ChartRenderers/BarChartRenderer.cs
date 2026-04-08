using Avalonia.Controls;
using Avalonia.Media;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.Collections.Generic;
using System.Linq;

namespace DexInstructionRunner.Services.ChartRenderers
{
    public static class BarChartRenderer
    {
        public static void Render(JArray chartData, StackPanel resultsPanel, string xField, string yField, bool isDark)
        {
            var series = new List<ISeries>();
            var allLabels = new List<string>();
            var labelSet = new HashSet<string>();

            var palette = isDark ? new[]
            {
        SKColors.DeepSkyBlue, SKColors.Orange, SKColors.SeaGreen,
        SKColors.MediumVioletRed, SKColors.Gold, SKColors.SkyBlue
    } : new[]
            {
        SKColors.Blue, SKColors.DarkOrange, SKColors.Green,
        SKColors.Crimson, SKColors.DarkGoldenrod, SKColors.Teal
    };

            foreach (var group in chartData)
            {
                var items = group["Items"] as JArray ?? new JArray();
                foreach (var item in items)
                {
                    string label = item[xField]?.ToString() ?? "Unknown";
                    if (labelSet.Add(label)) allLabels.Add(label);
                }
            }

            int groupIndex = 0;
            foreach (var group in chartData)
            {
                string groupName = group["Name"]?.ToString() ?? "Unknown Group";
                var items = group["Items"] as JArray ?? new JArray();

                var values = allLabels.Select(label =>
                {
                    var item = items.FirstOrDefault(i => (i[xField]?.ToString() ?? "Unknown") == label);
                    return item != null && double.TryParse(item[yField]?.ToString(), out double val) ? val : 0;
                }).ToList();

                series.Add(new RowSeries<double>
                {
                    Name = groupName,
                    Values = values,
                    Fill = new SolidColorPaint(palette[groupIndex % palette.Length]),
                    Stroke = null,
                    MaxBarWidth = 24,
                    DataLabelsPaint = new SolidColorPaint(isDark ? SKColors.White : SKColors.Black),
                    DataLabelsPosition = DataLabelsPosition.End,
                    DataLabelsSize = 14
                });

                groupIndex++;
            }

            var axisTextColor = isDark ? SKColors.White : SKColors.Black;

            var chart = new CartesianChart
            {
                Series = series,
                XAxes = new[] {
            new Axis
            {
                Name = yField,
                LabelsPaint = new SolidColorPaint(axisTextColor),
                TicksPaint = new SolidColorPaint(axisTextColor),
                Position = AxisPosition.Start
            }
        },
                YAxes = new[] {
            new Axis
            {
                Labels = allLabels.ToArray(),
                LabelsPaint = new SolidColorPaint(axisTextColor),
                TicksPaint = new SolidColorPaint(axisTextColor),
                Position = AxisPosition.Start
            }
        },
                LegendPosition = LegendPosition.Bottom,
                LegendTextPaint = new SolidColorPaint(axisTextColor),
                LegendBackgroundPaint = new SolidColorPaint(isDark ? SKColors.Black : SKColors.White),
                Height = 600,
                Background = new SolidColorBrush(isDark ? Colors.Black : Colors.White),
                DrawMarginFrame = null
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