using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NSEdge.App;

/// <summary>
/// A self-contained, read-only diagnostics viewer. No controller access, IPC,
/// disk writes or new dependencies. The caller supplies sanitized diagnostic text.
/// </summary>
internal sealed class DiagnosticsWindow : Window
{
    public DiagnosticsWindow(string diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        Title = "NSEdge diagnostics";
        Width = 720;
        Height = 610;
        MinWidth = 480;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        Background = Brush("#0E1624");
        Foreground = Brush("#E8EEF8");
        FontFamily = new FontFamily("Segoe UI");
        ShowInTaskbar = false;

        var layout = new Grid { Margin = new Thickness(20) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Diagnostics",
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 7)
        };
        Grid.SetRow(heading, 0);
        layout.Children.Add(heading);

        var notice = new TextBlock
        {
            Text = "Read-only information. Review software profile names before sharing.",
            Foreground = Brush("#A8B8D0"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(notice, 1);
        layout.Children.Add(notice);

        var content = new TextBox
        {
            Text = diagnostics,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brush("#182338"),
            Foreground = Brush("#E5ECF7"),
            BorderBrush = Brush("#33445B"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            IsTabStop = false
        };
        Grid.SetRow(content, 2);
        layout.Children.Add(content);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 15, 0, 0)
        };

        var copy = MakeButton("Copy diagnostics", "#263A55");
        copy.Margin = new Thickness(0, 0, 9, 0);
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(content.Text);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(this, "The clipboard is currently unavailable.",
                    "NSEdge", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        actions.Children.Add(copy);

        var close = MakeButton("Close", "#3773B8");
        close.IsDefault = true;
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        actions.Children.Add(close);

        Grid.SetRow(actions, 3);
        layout.Children.Add(actions);
        Content = layout;
    }

    private static Button MakeButton(string label, string background) => new()
    {
        Content = label,
        MinWidth = 105,
        MinHeight = 34,
        Padding = new Thickness(12, 5, 12, 5),
        Background = Brush(background),
        Foreground = Brush("#FFFFFF"),
        BorderBrush = Brush("#41546D"),
        BorderThickness = new Thickness(1),
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private static SolidColorBrush Brush(string hex)
    {
        var value = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(value);
        brush.Freeze();
        return brush;
    }
}