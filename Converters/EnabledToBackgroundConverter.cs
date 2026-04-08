using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace DexInstructionRunner.Converters
{
    public class EnabledToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool enabled && enabled)
                ? new SolidColorBrush(Color.Parse("#2979FF"))
                : new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
