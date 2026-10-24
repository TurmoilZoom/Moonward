using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;
using System.Globalization;

namespace Starward.Converters;

/// <summary>
/// 数值不小于 ConverterParameter 给出的阈值时返回 <see cref="Visibility.Visible"/>，否则返回 <see cref="Visibility.Collapsed"/>。
/// </summary>
public partial class DoubleAtLeastToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double threshold = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : 0;
        return value is double d && d >= threshold ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
