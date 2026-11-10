using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LevelApp.Instruments.Leveltronic.UI.Converters;

/// <summary>Live value colour: dim without a reading, white normally, amber (#FFA000) when disturbed.</summary>
public sealed class ReadingStateBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        new SolidColorBrush(value switch
        {
            ReadingState.Doubtful => Color.FromArgb(255, 0xFF, 0xA0, 0x00),
            ReadingState.Ok => Color.FromArgb(255, 0xFF, 0xFF, 0xFF),
            _ => Color.FromArgb(0x62, 0xFF, 0xFF, 0xFF),
        });

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
