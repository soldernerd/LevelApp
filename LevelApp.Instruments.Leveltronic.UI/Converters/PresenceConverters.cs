using System.Collections;
using LevelApp.Instruments.Leveltronic.UI.Calibration;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LevelApp.Instruments.Leveltronic.UI.Converters;

/// <summary>
/// Visible when the value is "present": a non-empty string, a non-empty collection,
/// <c>true</c>, or any other non-null object. Any ConverterParameter inverts it.
/// </summary>
public sealed class PresenceToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool present = value switch
        {
            null => false,
            string s => s.Length > 0,
            bool b => b,
            ICollection c => c.Count > 0,
            IEnumerable e => e.GetEnumerator().MoveNext(),
            _ => true,
        };
        if (parameter is not null) present = !present;
        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Colour of a <see cref="VerdictSeverity"/>: green, amber or red (as the phone app).</summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        new SolidColorBrush(value switch
        {
            VerdictSeverity.Good => Color.FromArgb(255, 0x43, 0xA0, 0x47),
            VerdictSeverity.Warning => Color.FromArgb(255, 0xFF, 0xA0, 0x00),
            _ => Color.FromArgb(255, 0xE5, 0x39, 0x35),
        });

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Picks the accent-button template for primary wizard actions.</summary>
public sealed class ActionTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Primary { get; set; }
    public DataTemplate? Normal { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is WizardAction { Primary: true } ? Primary : Normal;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
