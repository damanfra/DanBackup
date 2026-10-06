using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DanBackup.Core.Engine;
using DanBackup.Core.Model;

namespace DanBackup.App;

/// <summary>Status do item → ícone (Segoe MDL2 Assets).</summary>
public sealed class StatusToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ItemStatus.Success => "",
        ItemStatus.Warning => "",
        ItemStatus.Failed => "",
        ItemStatus.Skipped => "",
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43)));
    private static readonly Brush Orange = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)));
    private static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38)));

    internal static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ItemStatus.Success => Green,
        ItemStatus.Warning => Orange,
        ItemStatus.Failed => Red,
        LogLevel.Warning => Orange,
        LogLevel.Error => Red,
        _ => DependencyProperty.UnsetValue,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
