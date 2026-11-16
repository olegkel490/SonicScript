using Avalonia.Markup.Xaml;
using SonicScript.Core.Localization;

namespace SonicScript.App;

/// <summary>XAML: <c>Text="{l:Tr settings.title}"</c>. Sprache gilt ab Programmstart.</summary>
public sealed class TrExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
