using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LightRcon.Desktop;

public partial class NumberInput : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(NumberInput), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Changed));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public decimal Minimum { get; set; }
    public decimal Step { get; set; } = 1;
    public event EventHandler? ValueChanged;
    public NumberInput() => InitializeComponent();
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e) => ((NumberInput)target).ValueChanged?.Invoke(target, EventArgs.Empty);
    public static bool TryNumber(string text, out decimal value) => decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out value);
    internal void Increment(int direction)
    {
        if (!IsEnabled) return;
        if (!TryNumber(Text, out var value)) value = Minimum;
        try { Text = Math.Max(Minimum, checked(value + Step * direction)).ToString(CultureInfo.InvariantCulture); }
        catch (OverflowException) { }
    }
    private void Increase_Click(object sender, RoutedEventArgs e) => Increment(1);
    private void Decrease_Click(object sender, RoutedEventArgs e) => Increment(-1);
    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down)) return;
        Increment(e.Key == Key.Up ? 1 : -1); e.Handled = true;
    }
}
