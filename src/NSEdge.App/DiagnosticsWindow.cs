using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using NSEdge.App.Services;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using FontFamily = System.Windows.Media.FontFamily;
using Brush = System.Windows.Media.Brush;

namespace NSEdge.App;

// Snapshot viewer only: callers provide a whitelisted, redacted report.
// No HID writes, controller queries or DS4Windows state changes occur here.
internal sealed class DiagnosticsWindow : Window
{
    private readonly TextBox reportBox;

    public DiagnosticsWindow(string diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        Title = "NSEdge diagnostics";
        Width = 690;
        Height = 545;
        MinWidth = 540;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = (Brush)FindResource("BaseBrush");
        Foreground = (Brush)FindResource("PrimaryTextBrush");
        FontFamily = new FontFamily("Segoe UI");
        ShowInTaskbar = false;
        SourceInitialized += OnSourceInitialized;

        var layout = new Grid { Margin = new Thickness(24, 20, 24, 18) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock
        {
            Text = "Diagnostics", FontSize = 21, FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Current controller and DS4Windows compatibility",
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            Margin = new Thickness(0, 4, 0, 0), FontSize = 12
        });
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        var overview = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        overview.ColumnDefinitions.Add(new ColumnDefinition());
        overview.ColumnDefinitions.Add(new ColumnDefinition());
        var controller = SummaryCard("CONTROLLER",
            ReadField(diagnostics, "Connected: ") is string line && line.StartsWith("True", StringComparison.Ordinal)
                ? "Connected · " + (line.Contains("Bluetooth", StringComparison.Ordinal) ? "Bluetooth" : "USB")
                : "Unavailable",
            "Profile: " + (ReadField(diagnostics, "Profile: ") ?? "Unavailable") +
            "   |   " + (ReadField(diagnostics, "Battery: ") ?? "Battery unavailable"));
        controller.Margin = new Thickness(0, 0, 6, 0);
        overview.Children.Add(controller);

        var ds4 = SummaryCard("DS4WINDOWS",
            ReadField(diagnostics, "Status: ") ?? "Unavailable",
            "Slot: " + (ReadField(diagnostics, "Matched slot: ") ?? "None") +
            "   |   " + (ReadField(diagnostics, "Transport: ") is string transport && transport is "BT" or "USB"
                ? transport : "See technical details"));
        ds4.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(ds4, 1);
        overview.Children.Add(ds4);
        Grid.SetRow(overview, 1);
        layout.Children.Add(overview);

        var detailHeading = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        detailHeading.ColumnDefinitions.Add(new ColumnDefinition());
        detailHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        detailHeading.Children.Add(new TextBlock
        {
            Text = "TECHNICAL DETAILS",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("SecondaryTextBrush")
        });
        var annotation = new TextBlock
        {
            Text = "Read-only · Point-in-time",
            FontSize = 11,
            Foreground = (Brush)FindResource("SecondaryTextBrush")
        };
        Grid.SetColumn(annotation, 1);
        detailHeading.Children.Add(annotation);
        Grid.SetRow(detailHeading, 2);
        layout.Children.Add(detailHeading);

        reportBox = new TextBox
        {
            Text = diagnostics,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = (Brush)FindResource("SurfaceBrush"),
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(13, 11, 13, 11),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12
        };
        Grid.SetRow(reportBox, 3);
        layout.Children.Add(reportBox);

        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock
        {
            Text = "Identifiers and saved profile names are omitted from the copied report.",
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Margin = new Thickness(0, 0, 12, 0)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var copy = Button("Copy diagnostics", "ActionButtonStyle");
        copy.Margin = new Thickness(0, 0, 8, 0);
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(reportBox.Text); }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(this, "Clipboard unavailable.", "NSEdge",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        buttons.Children.Add(copy);
        var close = Button("Close", "PrimaryButtonStyle");
        close.IsDefault = true;
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 4);
        layout.Children.Add(footer);
        Content = layout;
    }

    private Border SummaryCard(string label, string value, string detail)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("SecondaryTextBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = value, FontSize = 16, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 3)
        });
        stack.Children.Add(new TextBlock
        {
            Text = detail, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("SecondaryTextBrush")
        });
        return new Border
        {
            Background = (Brush)FindResource("SurfaceBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14, 12, 14, 12),
            Child = stack
        };
    }

    private Button Button(string label, string style) => new()
    {
        Content = label,
        Style = (Style)FindResource(style),
        MinWidth = 112,
        Height = 35
    };

    private static string? ReadField(string report, string prefix)
    {
        foreach (string line in report.Split('\n'))
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line[prefix.Length..].Trim();
        return null;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowAppearance.Apply(new WindowInteropHelper(this).Handle);
        using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        if (icon is not null)
            Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(16, 16));
    }

    public void AssertAppearanceForSmoke()
    {
        if (Owner is null || Icon is null || reportBox.Text.Length == 0 ||
            !ReferenceEquals(Background, FindResource("BaseBrush")))
            throw new InvalidOperationException("Diagnostics theme, owner or report unavailable.");
        if (ActualWidth < MinWidth || ActualHeight < MinHeight)
            throw new InvalidOperationException("Diagnostics window clipped.");
    }
}
