using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace LevelApp.Instruments.Leveltronic.UI.Converters;

/// <summary>true → Visible, false → Collapsed; any ConverterParameter inverts it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool visible = value is true;
        if (parameter is not null) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}
