using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace DexInstructionRunner.Converters
{
    public class ScoreToThemeBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Brushes.Transparent;

            if (!double.TryParse(value.ToString(), out var score))
                return Brushes.Transparent;

            var app = Application.Current;

            var background = score switch
            {
                <= 50 => GetBrush(app, "ScoreLowBrush", Brushes.DarkRed),
                <= 70 => GetBrush(app, "ScoreMediumBrush", Brushes.DarkOrange),
                <= 85 => GetBrush(app, "ScoreGoodBrush", Brushes.Goldenrod),
                _ => GetBrush(app, "ScoreHighBrush", Brushes.DarkGreen)
            };

            if (parameter?.ToString()?.ToLower() == "foreground")
            {
                if (background is SolidColorBrush solid)
                {
                    var brightness = (solid.Color.R * 0.299 + solid.Color.G * 0.587 + solid.Color.B * 0.114) / 255;
                    return brightness > 0.6 ? Brushes.Black : Brushes.White;
                }
                return Brushes.White;
            }

            return background;
        }

        private static IBrush GetBrush(Application app, string key, IBrush fallback)
        {
            return app?.Resources.TryGetResource(key, null, out var brush) == true && brush is IBrush b
                ? b
                : fallback;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
