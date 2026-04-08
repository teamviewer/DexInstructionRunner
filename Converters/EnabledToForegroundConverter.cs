using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace DexInstructionRunner.Converters
{
    public class EnabledToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool enabled && enabled)
                ? Brushes.White
                : Brushes.LightGray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
