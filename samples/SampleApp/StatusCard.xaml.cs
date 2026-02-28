using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace SampleApp;

public sealed partial class StatusCard : UserControl
{
    public StatusCard()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(StatusCard),
            new PropertyMetadata("Title", OnTitleChanged));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(string), typeof(StatusCard),
            new PropertyMetadata("Status", OnStatusChanged));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(StatusCard),
            new PropertyMetadata("\uE946", OnIconChanged));

    public static readonly DependencyProperty StatusColorProperty =
        DependencyProperty.Register(nameof(StatusColor), typeof(string), typeof(StatusCard),
            new PropertyMetadata("#4CAF50", OnStatusColorChanged));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string StatusColor { get => (string)GetValue(StatusColorProperty); set => SetValue(StatusColorProperty, value); }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusCard card) card.TitleText.Text = e.NewValue as string ?? "Title";
    }

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusCard card) card.StatusText.Text = e.NewValue as string ?? "Status";
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusCard card) card.CardIcon.Glyph = e.NewValue as string ?? "\uE946";
    }

    private static void OnStatusColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusCard card)
        {
            var hex = e.NewValue as string ?? "#4CAF50";
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6) hex = "FF" + hex;
                var color = Color.FromArgb(
                    byte.Parse(hex[0..2], System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber),
                    byte.Parse(hex[6..8], System.Globalization.NumberStyles.HexNumber));
                card.StatusBrush.Color = color;
            }
            catch { }
        }
    }
}
