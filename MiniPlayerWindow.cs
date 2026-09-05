using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Kapla
{
    internal sealed class MiniPlayerWindow : Window
    {
        private readonly Func<BookEntry> bookProvider;
        private readonly Func<bool> playingProvider;
        private readonly Func<double> positionProvider;
        private readonly Func<double> durationProvider;
        private readonly Action togglePlay;
        private readonly Action skipBack;
        private readonly Action skipForward;
        private readonly Action<double> seekToPosition;
        private readonly Action restoreMain;
        private readonly Action hidePlayer;

        private readonly Border cover;
        private readonly TextBlock title;
        private readonly TextBlock author;
        private readonly TextBlock position;
        private readonly TextBlock remaining;
        private readonly Border progressFill;
        private readonly Slider progressSlider;
        private readonly Button playButton;
        private bool updatingProgress;

        public MiniPlayerWindow(
            Func<BookEntry> bookProvider,
            Func<bool> playingProvider,
            Func<double> positionProvider,
            Func<double> durationProvider,
            Action togglePlay,
            Action skipBack,
            Action skipForward,
            Action<double> seekToPosition,
            Action restoreMain,
            Action hidePlayer)
        {
            this.bookProvider = bookProvider;
            this.playingProvider = playingProvider;
            this.positionProvider = positionProvider;
            this.durationProvider = durationProvider;
            this.togglePlay = togglePlay;
            this.skipBack = skipBack;
            this.skipForward = skipForward;
            this.seekToPosition = seekToPosition;
            this.restoreMain = restoreMain;
            this.hidePlayer = hidePlayer;

            Title = "Kapla mini player";
            Width = 380;
            Height = 104;
            MinWidth = Width;
            MaxWidth = Width;
            MinHeight = Height;
            MaxHeight = Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Segoe UI");

            var surface = new Border
            {
                Width = Width,
                Height = Height,
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromRgb(30, 37, 44)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(67, 78, 88)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.28,
                    BlurRadius = 18,
                    ShadowDepth = 6,
                    Direction = 270
                }
            };
            surface.MouseLeftButtonDown += SurfaceOnMouseLeftButtonDown;

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });

            cover = new Border
            {
                Width = 58,
                Height = 58,
                CornerRadius = new CornerRadius(9),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromRgb(55, 68, 77)),
                Child = BuildPlaceholder()
            };
            cover.MouseLeftButtonDown += OpenMainOnMouseLeftButtonDown;
            Grid.SetColumn(cover, 0);
            Grid.SetRowSpan(cover, 3);
            root.Children.Add(cover);

            var details = new Grid { Margin = new Thickness(12, 1, 9, 0) };
            details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(25) });
            title = MakeText(12, FontWeights.SemiBold, Colors.White);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.MouseLeftButtonDown += OpenMainOnMouseLeftButtonDown;
            details.Children.Add(title);

            author = MakeText(10, FontWeights.Normal, Color.FromRgb(185, 195, 203));
            author.TextTrimming = TextTrimming.CharacterEllipsis;
            author.MouseLeftButtonDown += OpenMainOnMouseLeftButtonDown;
            Grid.SetRow(author, 1);
            details.Children.Add(author);

            var timeline = new Grid { Height = 25, VerticalAlignment = VerticalAlignment.Top };
            var track = new Border
            {
                Height = 4,
                Margin = new Thickness(0, 1, 0, 0),
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromRgb(70, 82, 91)),
                VerticalAlignment = VerticalAlignment.Top
            };
            timeline.Children.Add(track);
            progressFill = new Border
            {
                Height = 4,
                Margin = new Thickness(0, 1, 0, 0),
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromRgb(125, 211, 252)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = new Point(0, 0.5),
                RenderTransform = new ScaleTransform(0, 1)
            };
            timeline.Children.Add(progressFill);
            progressSlider = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                Value = 0,
                Height = 12,
                Margin = new Thickness(-4, -3, -4, 0),
                Opacity = 0.01,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top
            };
            progressSlider.ValueChanged += ProgressSliderOnValueChanged;
            timeline.Children.Add(progressSlider);

            position = MakeText(9, FontWeights.Medium, Color.FromRgb(173, 183, 191));
            position.VerticalAlignment = VerticalAlignment.Bottom;
            timeline.Children.Add(position);
            remaining = MakeText(9, FontWeights.Medium, Color.FromRgb(173, 183, 191));
            remaining.TextAlignment = TextAlignment.Right;
            remaining.HorizontalAlignment = HorizontalAlignment.Right;
            remaining.VerticalAlignment = VerticalAlignment.Bottom;
            timeline.Children.Add(remaining);
            Grid.SetRow(timeline, 2);
            details.Children.Add(timeline);

            Grid.SetColumn(details, 1);
            Grid.SetRowSpan(details, 3);
            root.Children.Add(details);

            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            var back = MakeButton(SvgIconFactory.LoadTinted("Figma", "skip-back-15.svg", 18, 18, Colors.White), "Skip back 15 seconds", 28, 32);
            back.Click += delegate { skipBack(); };
            controls.Children.Add(back);
            playButton = MakeButton(BuildPlayIcon(false), "Play", 38, 38);
            playButton.Background = new SolidColorBrush(Color.FromRgb(125, 211, 252));
            playButton.Foreground = new SolidColorBrush(Color.FromRgb(23, 56, 74));
            playButton.Template = MakeButtonTemplate(19);
            playButton.Click += delegate { togglePlay(); };
            controls.Children.Add(playButton);
            var forward = MakeButton(SvgIconFactory.LoadTinted("Figma", "skip-forward-15.svg", 18, 18, Colors.White), "Skip forward 15 seconds", 28, 32);
            forward.Click += delegate { skipForward(); };
            controls.Children.Add(forward);
            Grid.SetColumn(controls, 2);
            Grid.SetRowSpan(controls, 3);
            root.Children.Add(controls);

            var close = MakeButton(SvgIconFactory.LoadTinted("BootstrapIcons", "x-lg.svg", 12, 12, Color.FromRgb(180, 191, 199)), "Hide mini player", 20, 24);
            close.Click += delegate { hidePlayer(); };
            Grid.SetColumn(close, 3);
            Grid.SetRowSpan(close, 3);
            root.Children.Add(close);

            surface.Child = root;
            Content = surface;
        }

        public void ShowAtBottomRight()
        {
            Left = SystemParameters.WorkArea.Right - Width - 18;
            Top = SystemParameters.WorkArea.Bottom - Height - 18;
            Refresh();
            if (!IsVisible)
            {
                Show();
            }
            Activate();
        }

        public void Refresh()
        {
            var book = bookProvider == null ? null : bookProvider();
            if (book == null)
            {
                return;
            }

            title.Text = String.IsNullOrWhiteSpace(book.Title) ? "Kapla audiobook" : book.Title;
            author.Text = String.IsNullOrWhiteSpace(book.Author) ? "Unknown author" : book.Author;
            SetCover(book.CoverPath);
            var duration = Math.Max(0, durationProvider == null ? book.DurationSeconds : durationProvider());
            var current = Math.Max(0, positionProvider == null ? book.PositionSeconds : positionProvider());
            if (duration > 0)
            {
                current = Math.Min(duration, current);
            }
            position.Text = FormatTime(current);
            remaining.Text = "-" + FormatTime(Math.Max(0, duration - current));
            updatingProgress = true;
            progressSlider.Value = duration <= 0 ? 0 : current / duration;
            updatingProgress = false;
            var scale = progressFill.RenderTransform as ScaleTransform;
            if (scale != null)
            {
                scale.ScaleX = duration <= 0 ? 0 : Math.Max(0, Math.Min(1, current / duration));
            }
            var isPlaying = playingProvider != null && playingProvider();
            playButton.Content = BuildPlayIcon(isPlaying);
            playButton.ToolTip = isPlaying ? "Pause" : "Play";
        }

        private void ProgressSliderOnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (updatingProgress || durationProvider == null)
            {
                return;
            }
            var duration = Math.Max(0, durationProvider());
            if (duration <= 0)
            {
                return;
            }
            var target = Math.Max(0, Math.Min(duration, e.NewValue * duration));
            if (seekToPosition != null)
            {
                seekToPosition(target);
            }
        }

        private void SetCover(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                cover.Child = BuildPlaceholder();
                return;
            }
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                cover.Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
            }
            catch
            {
                cover.Child = BuildPlaceholder();
            }
        }

        private void SurfaceOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left
                && FindVisualParent<Button>(e.OriginalSource as DependencyObject) == null
                && FindVisualParent<Slider>(e.OriginalSource as DependencyObject) == null)
            {
                DragMove();
            }
        }

        private void OpenMainOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && restoreMain != null)
            {
                e.Handled = true;
                restoreMain();
            }
        }

        private static T FindVisualParent<T>(DependencyObject source) where T : DependencyObject
        {
            var current = source;
            while (current != null)
            {
                var match = current as T;
                if (match != null)
                {
                    return match;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static TextBlock MakeText(double size, FontWeight weight, Color color)
        {
            return new TextBlock
            {
                FontSize = size,
                FontWeight = weight,
                Foreground = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
            };
        }

        private static Button MakeButton(UIElement content, string tooltip, double width, double height)
        {
            return new Button
            {
                Width = width,
                Height = height,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                FocusVisualStyle = null,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Template = MakeButtonTemplate(7),
                Content = content
            };
        }

        private static ControlTemplate MakeButtonTemplate(double radius)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82));
            template.Triggers.Add(hover);
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.64));
            template.Triggers.Add(pressed);
            return template;
        }

        private static UIElement BuildPlayIcon(bool pause)
        {
            var canvas = new Canvas { Width = 17, Height = 17 };
            if (pause)
            {
                canvas.Children.Add(new Border { Width = 3, Height = 11, Background = new SolidColorBrush(Color.FromRgb(23, 56, 74)), Margin = new Thickness(3, 3, 0, 0) });
                canvas.Children.Add(new Border { Width = 3, Height = 11, Background = new SolidColorBrush(Color.FromRgb(23, 56, 74)), Margin = new Thickness(11, 3, 0, 0) });
            }
            else
            {
                var triangle = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M4,2 L14,8.5 L4,15 Z"),
                    Fill = new SolidColorBrush(Color.FromRgb(23, 56, 74))
                };
                canvas.Children.Add(triangle);
            }
            return canvas;
        }

        private static UIElement BuildPlaceholder()
        {
            return new TextBlock
            {
                Text = "K",
                FontSize = 25,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(125, 211, 252)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
        }

        private static string FormatTime(double seconds)
        {
            var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
        }
    }
}
