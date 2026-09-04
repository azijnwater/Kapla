using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Kapla
{
    public sealed partial class MainWindow : Window
    {
        private const string KoboActivationPage = "https://www.kobo.com/activate";

        private bool ShowKoboActivationDialog(KoboActivation activation)
        {
            var browserOpened = OpenKoboActivationPage();
            var dialog = new Window
            {
                Title = "Connect Kobo",
                Owner = this,
                Width = 430,
                Height = 390,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                FontFamily = interFont,
                Topmost = Topmost
            };

            var card = new Border
            {
                Margin = new Thickness(18),
                Padding = new Thickness(26, 22, 26, 22),
                CornerRadius = new CornerRadius(14),
                Background = IsDarkTheme ? Brush("#232830") : Brush("#FDF8F4"),
                BorderBrush = IsDarkTheme ? Brush("#405063") : Brush("#E8DDD7"),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.22,
                    BlurRadius = 28,
                    ShadowDepth = 8,
                    Direction = 270
                }
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var headingText = new TextBlock
            {
                Text = "Connect your Kobo account",
                FontFamily = interFont,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = IsDarkTheme ? Brush("#F4F0EC") : Brush("#1A1111"),
                VerticalAlignment = VerticalAlignment.Center
            };
            heading.Children.Add(headingText);
            var close = MakeMicroHeaderButton(BuildBootstrapIcon("x-lg.svg", 11, false), "Close");
            close.Click += delegate { dialog.DialogResult = false; };
            Grid.SetColumn(close, 1);
            heading.Children.Add(close);
            heading.MouseLeftButtonDown += delegate { dialog.DragMove(); };
            root.Children.Add(heading);

            var reassurance = new TextBlock
            {
                Text = "A secure Kobo page has opened in your browser. Kapla never sees your Kobo password.",
                FontFamily = interFont,
                FontSize = 11,
                LineHeight = 17,
                TextWrapping = TextWrapping.Wrap,
                Foreground = IsDarkTheme ? Brush("#AAB3BD") : Brush("#6F625E"),
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(reassurance, 1);
            root.Children.Add(reassurance);

            var codeCard = new Border
            {
                Margin = new Thickness(0, 16, 0, 14),
                Padding = new Thickness(16, 11, 16, 11),
                CornerRadius = new CornerRadius(8),
                Background = accentSoftBrush,
                BorderBrush = accentBrush,
                BorderThickness = new Thickness(1)
            };
            var codeStack = new StackPanel();
            codeStack.Children.Add(new TextBlock
            {
                Text = "YOUR ACTIVATION CODE",
                FontFamily = interFont,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = IsDarkTheme ? Brush("#8DD3FF") : Brush("#285D78"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            codeStack.Children.Add(new TextBlock
            {
                Text = activation.Code,
                FontFamily = interFont,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = IsDarkTheme ? Brush("#F4F0EC") : Brush("#1A1111"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            });
            codeStack.Children.Add(new TextBlock
            {
                Text = "Copied to your clipboard",
                FontFamily = interFont,
                FontSize = 9.5,
                Foreground = IsDarkTheme ? Brush("#AAB3BD") : Brush("#8A7E7A"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });
            codeCard.Child = codeStack;
            Grid.SetRow(codeCard, 2);
            root.Children.Add(codeCard);

            var steps = new StackPanel();
            steps.Children.Add(BuildActivationStep("1", "Sign in on the Kobo page that opened."));
            steps.Children.Add(BuildActivationStep("2", "Enter the code above and approve this device."));
            steps.Children.Add(BuildActivationStep("3", "Come back here and choose “I’ve connected”."));
            if (!browserOpened)
            {
                steps.Children.Add(new TextBlock
                {
                    Text = "The browser did not open automatically. Choose “Open Kobo” below.",
                    FontFamily = interFont,
                    FontSize = 9.5,
                    Foreground = Brush("#C36B6B"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }
            Grid.SetRow(steps, 3);
            root.Children.Add(steps);

            var actions = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var open = MakeCompactActionButton("Open Kobo", false);
            open.Height = 32;
            open.FontSize = 10.5;
            open.Click += delegate { OpenKoboActivationPage(); };
            actions.Children.Add(open);
            var complete = MakeCompactActionButton("I’ve connected", true);
            complete.Height = 32;
            complete.FontSize = 10.5;
            complete.Click += delegate { dialog.DialogResult = true; };
            Grid.SetColumn(complete, 2);
            actions.Children.Add(complete);
            Grid.SetRow(actions, 4);
            root.Children.Add(actions);

            card.Child = root;
            dialog.Content = card;
            return dialog.ShowDialog() == true;
        }

        private UIElement BuildActivationStep(string number, string instruction)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                Background = accentSoftBrush,
                BorderBrush = accentBrush,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = number,
                    FontFamily = interFont,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = IsDarkTheme ? Brush("#8DD3FF") : Brush("#285D78"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            var text = new TextBlock
            {
                Text = instruction,
                FontFamily = interFont,
                FontSize = 10.5,
                Foreground = IsDarkTheme ? Brush("#DCE3EA") : Brush("#261D1B"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            return row;
        }

        private static bool OpenKoboActivationPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo(KoboActivationPage) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
