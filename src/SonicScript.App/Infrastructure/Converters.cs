using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using SonicScript.Core.Hotkeys;
using SonicScript.Core.Localization;

namespace SonicScript.App;

/// <summary>KeyCombo ↔ Text ("Ctrl+Win"). Leerer Text = nicht belegt.</summary>
public sealed class KeyComboConverter : IValueConverter
{
    public static readonly KeyComboConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is KeyCombo c ? c.ToString() : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return KeyCombo.None;
        return KeyCombo.TryParse(s, out var c) ? c : new BindingNotification(new FormatException(s), BindingErrorType.DataValidationError);
    }
}

/// <summary>Enum-Wert → lokalisierter Text.</summary>
public sealed class EnumTextConverter : IValueConverter
{
    public static readonly EnumTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum e ? Strings.ForEnum(e) : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
