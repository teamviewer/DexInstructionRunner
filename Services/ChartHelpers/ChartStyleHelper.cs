using Avalonia;
using Avalonia.Styling;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

public static class ChartStyleHelper
{
    public static SolidColorPaint GetThemeAwarePaint()
    {
        try
        {
            var theme = Application.Current?.ActualThemeVariant;

            if (theme == ThemeVariant.Dark)
                return new SolidColorPaint(SKColors.White);

            return new SolidColorPaint(SKColors.Black);
        }
        catch
        {
            // Fallback: Assume Light theme
            return new SolidColorPaint(SKColors.Black);
        }
    }
}
