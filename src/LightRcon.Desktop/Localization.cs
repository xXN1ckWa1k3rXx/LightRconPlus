using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using LightRcon.Core;

namespace LightRcon.Desktop;

public sealed class LocalizationView : INotifyPropertyChanged
{
    public static LocalizationView Instance { get; } = new();
    public int Version { get; private set; }
    private LocalizationView()
    {
        UiText.LanguageChanged += () =>
        {
            void Refresh() { Version++; PropertyChanged?.Invoke(this, new(nameof(Version))); }
            if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess()) dispatcher.Invoke(Refresh);
            else Refresh();
        };
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class TranslationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => UiText.Get((string)parameter);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider)
        => CreateBinding(Key).ProvideValue(serviceProvider);
    public static Binding CreateBinding(string key) => new(nameof(LocalizationView.Version))
    {
        Source = LocalizationView.Instance, Mode = BindingMode.OneWay, Converter = new TranslationConverter(), ConverterParameter = key
    };
    public static void Bind(DependencyObject target, DependencyProperty property, string key)
        => BindingOperations.SetBinding(target, property, CreateBinding(key));
}

public sealed record LocalizedChoice(string Code, string Key)
{
    public string Label => UiText.Get(Key);
    public override string ToString() => Label;
}
