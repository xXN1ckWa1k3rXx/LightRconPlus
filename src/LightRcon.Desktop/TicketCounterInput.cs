using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LightRcon.Core;

namespace LightRcon.Desktop;

public sealed class TicketCounterInput : UserControl
{
    public NumberInput Percentage { get; } = new() { Width = 130, Minimum = 1, Margin = new(12, 0, 0, 0) };
    public Slider PercentageSlider { get; } = new() { Minimum = 1, Maximum = 500, TickFrequency = 1, SmallChange = 1, LargeChange = 10, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    public TextBlock Preview { get; } = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly TextBlock context = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private bool updating;
    public event EventHandler? ValueChanged;
    public string Text { get => Percentage.Text; set => Percentage.Text = value; }
    public int? BaseTickets { get; private set; }
    public TicketCounterInput()
    {
        var panel = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(PercentageSlider);
        Grid.SetColumn(Percentage, 1); row.Children.Add(Percentage);
        var unit = new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 0, 0) };
        Grid.SetColumn(unit, 2); row.Children.Add(unit);
        panel.Children.Add(row); panel.Children.Add(context); panel.Children.Add(Preview);
        context.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Content = panel;
        Percentage.ValueChanged += (_, _) => { UpdatePreview(); ValueChanged?.Invoke(this, EventArgs.Empty); };
        PercentageSlider.ValueChanged += (_, e) =>
        {
            if (!updating) Text = Math.Round(e.NewValue, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
        };
        LocExtension.Bind(PercentageSlider, System.Windows.Automation.AutomationProperties.NameProperty, "Game mode counter (%)");
        LocExtension.Bind(Percentage, System.Windows.Automation.AutomationProperties.NameProperty, "Game mode counter (%)");
    }
    public void SetContext(string map, string mode, GameId game = GameId.BF4)
    {
        context.Text = MapCatalog.MapName(map, game) + " — " + MapCatalog.ModeName(mode, game);
        BaseTickets = game == GameId.BF4 ? TicketEstimate.DefaultBase(mode) : null;
        UpdatePreview();
    }
    private void UpdatePreview()
    {
        var previous = updating; updating = true;
        try
        {
            var valid = NumberInput.TryNumber(Text, out var percent) && percent > 0;
            PercentageSlider.IsEnabled = valid;
            if (valid)
            {
                PercentageSlider.Minimum = Math.Min(1, (double)percent);
                PercentageSlider.Maximum = (double)Math.Max(500, percent);
                PercentageSlider.Value = (double)percent;
            }
            if (!valid || BaseTickets is not int baseline)
            {
                Preview.Text = UiText.Format("Estimated tickets: {0}", "—"); return;
            }
            try { Preview.Text = UiText.Format("Estimated tickets: {0}", TicketEstimate.Calculate(percent, baseline).ToString("0.##", CultureInfo.CurrentCulture)); }
            catch (OverflowException) { Preview.Text = UiText.Get("The ticket estimate is too large."); }
        }
        finally { updating = previous; }
    }
}
