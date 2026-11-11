using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NexaTextGrab;

public sealed class ToastWindow : Window
{
    public ToastWindow(string title, string subtitle)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(21, 53, 40)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(17, 12, 19, 13),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Bold },
                    new TextBlock { Text = subtitle, Foreground = new SolidColorBrush(Color.FromRgb(199, 223, 210)), FontSize = 11, Margin = new Thickness(0, 3, 0, 0) }
                }
            }
        };
        Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth - 22;
            Top = work.Bottom - ActualHeight - 22;
        };
    }
}
