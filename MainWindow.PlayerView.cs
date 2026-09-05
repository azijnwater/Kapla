using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.Serialization.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Kapla
{
    public sealed partial class MainWindow : Window
    {
        private UIElement BuildFigmaPlayerPanel()
        {
            playerSurface = new Border
            {
                Width = 504,
                Height = 212,
                Background = Brushes.Transparent
            };

            playerCanvas = new Canvas { Width = 504, Height = 212 };
            coverBorder = new Border
            {
                Width = 152,
                Height = 152,
                CornerRadius = new CornerRadius(10),
                Background = Brush("#EFE8E8"),
                ClipToBounds = true,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(26, 17, 17),
                    Opacity = 0.10,
                    BlurRadius = 16,
                    ShadowDepth = 8,
                    Direction = 270
                },
                Child = BuildEmptyArtwork()
            };
            Canvas.SetLeft(coverBorder, 0);
            Canvas.SetTop(coverBorder, 30);
            playerCanvas.Children.Add(coverBorder);

            playerControlsCanvas = new Canvas { Width = 330, Height = 212 };
            Canvas.SetLeft(playerControlsCanvas, 174);
            playerCanvas.Children.Add(playerControlsCanvas);
            var controls = playerControlsCanvas;

            playerStateText = FigmaText("READY WHEN YOU ARE", 10, FontWeights.Bold, Brush("#4D9FC4"));
            playerStateText.Width = 330;
            playerStateText.Height = 12;
            Canvas.SetLeft(playerStateText, 0);
            Canvas.SetTop(playerStateText, 19);
            controls.Children.Add(playerStateText);

            titleText = FigmaText("Choose an audiobook", 17, FontWeights.Bold, Brush("#1A1111"));
            titleText.Width = 330;
            titleText.Height = 22;
            titleText.LineHeight = 22;
            titleText.TextTrimming = TextTrimming.CharacterEllipsis;
            Canvas.SetLeft(titleText, 0);
            Canvas.SetTop(titleText, 33);
            controls.Children.Add(titleText);

            authorText = FigmaText("Your selected book will appear here", 12, FontWeights.Normal, Brush("#661A1111"));
            authorText.Width = 330;
            authorText.Height = 15;
            authorText.TextTrimming = TextTrimming.CharacterEllipsis;
            Canvas.SetLeft(authorText, 0);
            Canvas.SetTop(authorText, 57);
            controls.Children.Add(authorText);

            chapterRow = new Grid { Width = 330, Height = 20, Background = Brushes.Transparent };
            Canvas.SetLeft(chapterRow, 0);
            Canvas.SetTop(chapterRow, 84);
            controls.Children.Add(chapterRow);
            var chapterCanvas = new Canvas { Width = 330, Height = 20 };
            chapterRow.Children.Add(chapterCanvas);
            var layersIcon = SvgIconFactory.Load("layers.svg", 12, 12);
            Canvas.SetLeft(layersIcon, 0);
            Canvas.SetTop(layersIcon, 4);
            chapterCanvas.Children.Add(layersIcon);
            chapterTitleText = FigmaText("Choose a chapter", 11, FontWeights.Medium, Brush("#781A1111"));
            chapterTitleText.Width = 225;
            chapterTitleText.Height = 13;
            chapterTitleText.TextTrimming = TextTrimming.CharacterEllipsis;
            Canvas.SetLeft(chapterTitleText, 17);
            Canvas.SetTop(chapterTitleText, 3);
            chapterCanvas.Children.Add(chapterTitleText);

            chapterPreviousButton = MakeChapterButton("chevron-left.svg", "Previous chapter");
            chapterPreviousButton.Click += delegate { MoveChapter(-1); };
            Canvas.SetLeft(chapterPreviousButton, 250);
            chapterCanvas.Children.Add(chapterPreviousButton);
            chapterIndexText = FigmaText("—", 10, FontWeights.SemiBold, Brush("#541A1111"));
            chapterIndexText.Width = 34;
            chapterIndexText.Height = 12;
            chapterIndexText.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(chapterIndexText, 273);
            Canvas.SetTop(chapterIndexText, 4);
            chapterCanvas.Children.Add(chapterIndexText);
            chapterNextButton = MakeChapterButton("chevron-right.svg", "Next chapter");
            chapterNextButton.Click += delegate { MoveChapter(1); };
            Canvas.SetLeft(chapterNextButton, 310);
            chapterCanvas.Children.Add(chapterNextButton);

            progressFill = new Border
            {
                Width = 82,
                Height = 4,
                Background = accentBrush,
                CornerRadius = new CornerRadius(2)
            };
            var progressTrack = new Border
            {
                Width = 330,
                Height = 4,
                Background = Brush("#EFE8E8"),
                CornerRadius = new CornerRadius(2)
            };
            Canvas.SetTop(progressTrack, 116);
            controls.Children.Add(progressTrack);
            Canvas.SetTop(progressFill, 116);
            controls.Children.Add(progressFill);
            progressThumb = new Border
            {
                Width = 10,
                Height = 10,
                Background = accentBrush,
                CornerRadius = new CornerRadius(5),
                Effect = new DropShadowEffect
                {
                    Color = accentBrush.Color,
                    Opacity = 0.25,
                    BlurRadius = 4,
                    ShadowDepth = 2,
                    Direction = 270
                }
            };
            Canvas.SetLeft(progressThumb, 77);
            Canvas.SetTop(progressThumb, 113);
            controls.Children.Add(progressThumb);

            progressSlider = new Slider
            {
                Width = 330,
                Height = 18,
                Minimum = 0,
                Maximum = 1,
                Value = 0,
                Opacity = 0.01,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            progressSlider.PreviewMouseLeftButtonDown += ProgressSliderOnMouseLeftButtonDown;
            progressSlider.PreviewMouseMove += ProgressSliderOnMouseMove;
            progressSlider.PreviewMouseLeftButtonUp += ProgressSliderOnMouseLeftButtonUp;
            progressSlider.ValueChanged += ProgressSliderOnValueChanged;
            Canvas.SetLeft(progressSlider, 0);
            Canvas.SetTop(progressSlider, 109);
            controls.Children.Add(progressSlider);

            positionText = FigmaText("0:00", 10, FontWeights.SemiBold, Brush("#AB1A1111"));
            positionText.Width = 60;
            positionText.Height = 12;
            Canvas.SetLeft(positionText, 0);
            Canvas.SetTop(positionText, 125);
            controls.Children.Add(positionText);
            durationText = FigmaText("-0:00", 10, FontWeights.Medium, Brush("#661A1111"));
            durationText.Width = 60;
            durationText.Height = 12;
            durationText.TextAlignment = TextAlignment.Right;
            Canvas.SetLeft(durationText, 270);
            Canvas.SetTop(durationText, 125);
            controls.Children.Add(durationText);

            speedButton = MakeSpeedButton();
            Canvas.SetLeft(speedButton, 0);
            Canvas.SetTop(speedButton, 157);
            controls.Children.Add(speedButton);

            rewindButton = MakeTransportButton("skip-back-15.svg", "Skip back " + appSettings.RewindSeconds + " seconds", appSettings.RewindSeconds);
            rewindButton.Click += delegate { Skip(-appSettings.RewindSeconds); };
            Canvas.SetLeft(rewindButton, 72);
            Canvas.SetTop(rewindButton, 155);
            controls.Children.Add(rewindButton);

            playButton = MakeFigmaPlayButton();
            playButton.Click += delegate { TogglePlay(); };
            Canvas.SetLeft(playButton, 143);
            Canvas.SetTop(playButton, 149);
            controls.Children.Add(playButton);

            forwardButton = MakeTransportButton("skip-forward-15.svg", "Skip forward " + appSettings.ForwardSeconds + " seconds", appSettings.ForwardSeconds);
            forwardButton.Click += delegate { Skip(appSettings.ForwardSeconds); };
            Canvas.SetLeft(forwardButton, 226);
            Canvas.SetTop(forwardButton, 155);
            controls.Children.Add(forwardButton);

            sleepTimerButton = MakeTransportButton("moon.svg", "Sleep timer", (int?)null);
            sleepTimerButton.Click += SleepTimerButtonOnClick;
            Canvas.SetLeft(sleepTimerButton, 298);
            Canvas.SetTop(sleepTimerButton, 155);
            controls.Children.Add(sleepTimerButton);

            speedBox = new ComboBox { Width = 1, Height = 1, Visibility = Visibility.Collapsed };
            speedBox.Items.Add("0.75x");
            speedBox.Items.Add("1.0x");
            speedBox.Items.Add("1.25x");
            speedBox.Items.Add("1.5x");
            speedBox.Items.Add("2.0x");
            speedBox.SelectionChanged += delegate
            {
                ApplySpeed();
                speedButton.Content = MakeSpeedContent(speedBox.SelectedItem == null ? "1.0x" : speedBox.SelectedItem.ToString());
            };
            SetSpeedSelection(appSettings.DefaultPlaybackSpeed);
            chapterBox = new ComboBox
            {
                Width = 1,
                Height = 1,
                Visibility = Visibility.Collapsed,
                DisplayMemberPath = "DisplayText"
            };
            chapterBox.SelectionChanged += ChapterBoxOnSelectionChanged;
            metadataText = new TextBlock { Visibility = Visibility.Collapsed };
            descriptionText = new TextBlock { Visibility = Visibility.Collapsed };
            syncText = new TextBlock { Visibility = Visibility.Collapsed };
            syncDetailText = new TextBlock { Visibility = Visibility.Collapsed };
            volumeSlider = new Slider { Minimum = 0, Maximum = 1, Value = appSettings.Volume, Visibility = Visibility.Collapsed };
            volumeSlider.ValueChanged += delegate
            {
                appSettings.Volume = volumeSlider.Value;
                if (media != null) media.Volume = volumeSlider.Value;
            };

            playerSurface.Child = playerCanvas;
            ApplyCoverVisibility(false);
            return playerSurface;
        }

        private FontFamily CreateInterFont()
        {
            try
            {
                return new FontFamily(new Uri(AppDomain.CurrentDomain.BaseDirectory, UriKind.Absolute), "./Assets/Fonts/#Inter");
            }
            catch
            {
                return new FontFamily("Segoe UI");
            }
        }

        private TextBlock FigmaText(string text, double size, FontWeight weight, Brush foreground)
        {
            var block = new TextBlock
            {
                Text = text,
                FontFamily = interFont,
                FontSize = size,
                FontWeight = weight,
                Foreground = foreground,
                TextWrapping = TextWrapping.NoWrap
            };
            TextOptions.SetTextFormattingMode(block, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(block, TextRenderingMode.ClearType);
            return block;
        }

        private UIElement BuildEmptyArtwork()
        {
            return new Grid
            {
                Background = Brush("#EFE8E8"),
                Children =
                {
                    new Border
                    {
                        Width = 32,
                        Height = 32,
                        CornerRadius = new CornerRadius(8),
                        Background = accentBrush,
                        Child = SvgIconFactory.Load("book-open.svg", 20, 20),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
        }

        private UIElement BuildCoverImage(BitmapSource source)
        {
            const double side = 152;
            var sourceWidth = Math.Max(1, source.PixelWidth);
            var sourceHeight = Math.Max(1, source.PixelHeight);
            var scale = Math.Max(side / sourceWidth, side / sourceHeight);
            var renderedWidth = sourceWidth * scale;
            var renderedHeight = sourceHeight * scale;
            var image = new Image
            {
                Source = source,
                Width = renderedWidth,
                Height = renderedHeight,
                Stretch = Stretch.Fill
            };
            Canvas.SetLeft(image, (side - renderedWidth) / 2);
            Canvas.SetTop(image, (side - renderedHeight) / 2);
            var crop = new Canvas
            {
                Width = side,
                Height = side,
                Clip = new RectangleGeometry(new Rect(0, 0, side, side), 10, 10)
            };
            crop.Children.Add(image);
            return crop;
        }

        private Button MakeHeaderButton(UIElement icon, string tooltip)
        {
            var button = new Button
            {
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Template = MakeRoundedButtonTemplate(7),
                Content = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(7),
                    Background = Brush("#081A1111"),
                    Child = icon
                }
            };
            AttachMicroInteraction(button, 1.035);
            return button;
        }

        private UIElement MakeChevronIcon(bool up)
        {
            var icon = SvgIconFactory.LoadTinted("Figma", "chevron-up.svg", 12, 12, HeaderIconColor(false));
            if (!up)
            {
                icon.RenderTransformOrigin = new Point(0.5, 0.5);
                icon.RenderTransform = new RotateTransform(180);
            }
            return icon;
        }

        private static UIElement MakePlusIcon()
        {
            return MakeButtonIcon(IconAdd, 11, Brush("#881A1111"));
        }

        private Button MakeMicroHeaderButton(UIElement icon, string tooltip)
        {
            var button = new Button
            {
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Template = MakeRoundedButtonTemplate(5),
                Content = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(5),
                    Background = Brushes.Transparent,
                    Child = icon
                }
            };
            AttachMicroInteraction(button, 1.04);
            return button;
        }

        private UIElement BuildPinIcon(bool pinned)
        {
            return BuildBootstrapIcon("pin-angle-fill.svg", 11, pinned);
        }

        private Viewbox BuildBootstrapIcon(string fileName, double size, bool accent)
        {
            return SvgIconFactory.LoadTinted("BootstrapIcons", fileName, size, size, HeaderIconColor(accent));
        }

        private Color HeaderIconColor(bool accent)
        {
            if (accent)
            {
                return accentBrush.Color;
            }
            return (Color)ColorConverter.ConvertFromString(IsDarkTheme ? "#DCE3EA" : "#6F625E");
        }

        private void TogglePin()
        {
            isPinned = !isPinned;
            Topmost = isPinned;
            var surface = pinButton == null ? null : pinButton.Content as Border;
            if (surface != null)
            {
                surface.Background = isPinned ? accentSoftBrush : Brushes.Transparent;
                surface.Child = BuildPinIcon(isPinned);
            }
            if (pinButton != null)
            {
                pinButton.ToolTip = isPinned ? "Unpin Kapla" : "Keep Kapla on top";
            }
        }

        private Button MakeChapterButton(string assetName, string tooltip)
        {
            var button = new Button
            {
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Template = MakeRoundedButtonTemplate(6),
                Content = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = Brush("#081A1111"),
                    Child = SvgIconFactory.Load(assetName, 10, 10)
                }
            };
            AttachMicroInteraction(button, 1.035);
            return button;
        }

        private Button MakeSpeedButton()
        {
            var button = new Button
            {
                Width = 36,
                Height = 28,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "Playback speed",
                Template = MakeRoundedButtonTemplate(6),
                Content = MakeSpeedContent("1.0x")
            };
            AttachMicroInteraction(button, 1.03);
            button.Click += delegate
            {
                if (speedBox != null && speedBox.Items.Count > 0)
                {
                    speedBox.SelectedIndex = (speedBox.SelectedIndex + 1) % speedBox.Items.Count;
                }
            };
            return button;
        }

        private UIElement MakeSpeedContent(string label)
        {
            return new Border
            {
                Width = 36,
                Height = 28,
                CornerRadius = new CornerRadius(6),
                Background = accentSoftBrush,
                Child = new TextBlock
                {
                    Text = label,
                    FontFamily = interFont,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = IsDarkTheme ? Brush("#8DD3FF") : Brush("#285D78"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private Button MakeTransportButton(string assetName, string tooltip, int? number)
        {
            var canvas = new Canvas { Width = 32, Height = 32 };
            var iconSize = number.HasValue ? 20 : 16;
            var icon = SvgIconFactory.Load(assetName, iconSize, iconSize);
            Canvas.SetLeft(icon, number.HasValue ? 6 : 8);
            Canvas.SetTop(icon, number.HasValue ? 4 : 8);
            canvas.Children.Add(icon);
            if (number.HasValue)
            {
                var label = FigmaText(number.Value.ToString(CultureInfo.InvariantCulture), 7, FontWeights.Bold, Brush("#1A1111"));
                label.Width = 12;
                label.Height = 8;
                label.TextAlignment = TextAlignment.Center;
                Canvas.SetLeft(label, 8);
                Canvas.SetTop(label, 20);
                canvas.Children.Add(label);
            }
            var button = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Template = MakeRoundedButtonTemplate(7),
                Content = canvas
            };
            AttachMicroInteraction(button, 1.04);
            return button;
        }

        private Button MakeFigmaPlayButton()
        {
            var button = new Button
            {
                Width = 44,
                Height = 44,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "Play or pause",
                Template = MakeRoundedButtonTemplate(22),
                Content = new Border
                {
                    Width = 44,
                    Height = 44,
                    CornerRadius = new CornerRadius(22),
                    Background = BuildPlayButtonBrush(),
                    Effect = new DropShadowEffect
                    {
                        Color = accentBrush.Color,
                        Opacity = 0.20,
                        BlurRadius = 6,
                        ShadowDepth = 4,
                        Direction = 270
                    },
                    Child = BuildPlayIcon(false)
                }
            };
            AttachMicroInteraction(button, 1.035);
            return button;
        }

        private void AttachMicroInteraction(Button button, double hoverScale)
        {
            if (button == null)
            {
                return;
            }
            button.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = new ScaleTransform(1, 1);
            button.RenderTransform = scale;
            Action<double, int> animate = delegate(double target, int milliseconds)
            {
                if (!button.IsEnabled)
                {
                    target = 1;
                }
                if (!appSettings.AnimationsEnabled || appSettings.ReduceMotion)
                {
                    scale.ScaleX = target;
                    scale.ScaleY = target;
                    return;
                }
                var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing });
            };
            button.MouseEnter += delegate { if (!button.IsPressed) animate(hoverScale, 150); };
            button.MouseLeave += delegate { animate(1, 150); };
            button.PreviewMouseLeftButtonDown += delegate { animate(0.96, 80); };
            button.PreviewMouseLeftButtonUp += delegate { animate(button.IsMouseOver ? hoverScale : 1, 140); };
            button.IsEnabledChanged += delegate { button.Opacity = button.IsEnabled ? 1 : 0.42; animate(1, 0); };
        }

        private UIElement BuildPlayIcon(bool paused)
        {
            var canvas = new Canvas { Width = 20, Height = 20 };
            canvas.Children.Add(SvgIconFactory.Load("play-circle.svg", 20, 20));
            if (paused)
            {
                var first = new Border
                {
                    Width = 2,
                    Height = 7,
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(1)
                };
                Canvas.SetLeft(first, 7.2);
                Canvas.SetTop(first, 6.5);
                canvas.Children.Add(first);
                var second = new Border
                {
                    Width = 2,
                    Height = 7,
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(1)
                };
                Canvas.SetLeft(second, 10.8);
                Canvas.SetTop(second, 6.5);
                canvas.Children.Add(second);
            }
            else
            {
                var triangle = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M8,6.4 L14,10 L8,13.6 Z"),
                    Fill = Brushes.White
                };
                canvas.Children.Add(triangle);
            }
            return new Viewbox { Width = 20, Height = 20, Child = canvas };
        }

        private void UpdatePlayButtonVisual()
        {
            if (playButton == null)
            {
                return;
            }
            var surface = playButton.Content as Border;
            if (surface != null)
            {
                surface.Child = BuildPlayIcon(isPlaying);
            }
            playButton.ToolTip = isPlaying ? "Pause" : "Play";
            UpdateWindowsMediaPlaybackState();
            UpdateMiniPlayer();
        }

        private void UpdateWindowsMediaMetadata()
        {
            if (windowsMediaControls == null)
            {
                return;
            }
            var chapter = chapterBox == null ? null : chapterBox.SelectedItem as KoboChapter;
            windowsMediaControls.UpdateMetadata(currentBook, chapter == null ? null : chapter.Title);
        }

        private void UpdateWindowsMediaPlaybackState()
        {
            if (windowsMediaControls != null)
            {
                windowsMediaControls.UpdatePlaybackState(currentBook != null, isPlaying);
            }
        }

        private void UpdateWindowsMediaTimeline()
        {
            if (windowsMediaControls == null || currentBook == null)
            {
                return;
            }
            windowsMediaControls.UpdateTimeline(CurrentAbsolutePosition(), currentBook.DurationSeconds);
            lastWindowsTimelineUpdateUtc = DateTime.UtcNow;
        }

        private void HeaderSurfaceOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            var current = e.OriginalSource as DependencyObject;
            while (current != null)
            {
                if (current is System.Windows.Controls.Primitives.ButtonBase
                    || current is System.Windows.Controls.Primitives.TextBoxBase
                    || current is System.Windows.Controls.Primitives.Selector
                    || current is System.Windows.Controls.Primitives.Thumb
                    || current is System.Windows.Controls.Primitives.ScrollBar
                    || current is Slider
                    || current is ListBoxItem)
                {
                    return;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            try
            {
                e.Handled = true;
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // Native hit testing normally owns the drag. This is only a fallback
                // for environments that do not deliver WM_NCHITTEST to layered windows.
            }
        }

        private void UpdateProgressVisual()
        {
            if (progressSlider == null || progressFill == null || progressThumb == null)
            {
                return;
            }
            var ratio = progressSlider.Maximum <= progressSlider.Minimum
                ? 0
                : (progressSlider.Value - progressSlider.Minimum) / (progressSlider.Maximum - progressSlider.Minimum);
            ratio = Math.Max(0, Math.Min(1, ratio));
            var trackWidth = progressSlider.ActualWidth > 1 ? progressSlider.ActualWidth : progressSlider.Width;
            var width = trackWidth * ratio;
            progressFill.Width = width;
            Canvas.SetLeft(progressThumb, Math.Max(0, Math.Min(trackWidth - progressThumb.Width, width - progressThumb.Width / 2)));
        }

        private UIElement BuildPlayerPanel()
        {
            playerSurface = new Border
            {
                Background = Brushes.Transparent,
                Margin = new Thickness(0),
                Padding = new Thickness(0, 55, 0, 0)
            };
            Grid.SetColumn(playerSurface, 1);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var player = new Grid();
            player.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            player.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(33) });
            player.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            coverBorder = new Border
            {
                Width = 230,
                Height = 230,
                CornerRadius = new CornerRadius(16),
                Background = Brush("#E9F0EC"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            coverBorder.Child = new TextBlock
            {
                Text = "♫",
                FontSize = 56,
                Foreground = Brush("#4C806A"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            player.Children.Add(coverBorder);

            var details = new Grid();
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            playerStateText = new TextBlock
            {
                Text = "READY WHEN YOU ARE",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = accentBrush
            };
            details.Children.Add(playerStateText);
            titleText = new TextBlock
            {
                Text = "Choose an audiobook",
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#261D1B"),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 72,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 9, 0, 0)
            };
            Grid.SetRow(titleText, 1);
            details.Children.Add(titleText);
            authorText = new TextBlock
            {
                Text = "Your selected book will appear here",
                FontSize = 17,
                Foreground = Brush("#A29B98"),
                Margin = new Thickness(0, 3, 0, 0)
            };
            Grid.SetRow(authorText, 2);
            details.Children.Add(authorText);
            metadataText = new TextBlock
            {
                Text = String.Empty,
                FontSize = 11,
                Foreground = Brush("#A29B98"),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 32,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(metadataText, 3);
            details.Children.Add(metadataText);
            descriptionText = new TextBlock
            {
                Text = String.Empty,
                FontSize = 12,
                Foreground = Brush("#7C7470"),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 46,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 7, 0, 0),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(descriptionText, 4);
            details.Children.Add(descriptionText);

            chapterRow = new Grid { Margin = new Thickness(0, 17, 0, 0), Visibility = Visibility.Collapsed };
            chapterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            chapterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            chapterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            chapterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var chapterIcon = new TextBlock
            {
                Text = "▤",
                FontSize = 17,
                Foreground = Brush("#A8A19D"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0)
            };
            chapterRow.Children.Add(chapterIcon);
            chapterBox = new ComboBox
            {
                Height = 28,
                Background = Brush("#FCF9F7"),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brush("#7C7470"),
                FontSize = 13,
                DisplayMemberPath = "DisplayText",
                IsEnabled = false,
                ToolTip = "Choose a chapter"
            };
            chapterBox.SelectionChanged += ChapterBoxOnSelectionChanged;
            Grid.SetColumn(chapterBox, 1);
            chapterRow.Children.Add(chapterBox);
            chapterIndexText = new TextBlock
            {
                Text = "—",
                FontSize = 12,
                Foreground = Brush("#A8A19D"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 6, 0)
            };
            Grid.SetColumn(chapterIndexText, 2);
            chapterRow.Children.Add(chapterIndexText);
            var chapterButtons = new StackPanel { Orientation = Orientation.Horizontal };
            chapterPreviousButton = MakeTinyCircleButton("‹", "Previous chapter");
            chapterPreviousButton.Click += delegate { MoveChapter(-1); };
            chapterNextButton = MakeTinyCircleButton("›", "Next chapter");
            chapterNextButton.Click += delegate { MoveChapter(1); };
            chapterButtons.Children.Add(chapterPreviousButton);
            chapterButtons.Children.Add(chapterNextButton);
            Grid.SetColumn(chapterButtons, 3);
            chapterRow.Children.Add(chapterButtons);
            Grid.SetRow(chapterRow, 5);
            details.Children.Add(chapterRow);

            progressSlider = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                Value = 0,
                Height = 22,
                Foreground = accentBrush,
                Background = Brush("#E9E3E0"),
                Margin = new Thickness(0, 8, 0, 0)
            };
            progressSlider.PreviewMouseLeftButtonDown += delegate { isDraggingProgress = true; };
            progressSlider.PreviewMouseLeftButtonUp += ProgressSliderOnMouseLeftButtonUp;
            progressSlider.ValueChanged += ProgressSliderOnValueChanged;
            Grid.SetRow(progressSlider, 6);
            details.Children.Add(progressSlider);

            var times = new Grid { Margin = new Thickness(0, -2, 0, 0) };
            times.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            times.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            positionText = MakeTimeText("0:00");
            durationText = MakeTimeText("0:00");
            times.Children.Add(positionText);
            Grid.SetColumn(durationText, 1);
            durationText.HorizontalAlignment = HorizontalAlignment.Right;
            times.Children.Add(durationText);
            Grid.SetRow(times, 7);
            details.Children.Add(times);

            player.Children.Add(details);
            Grid.SetColumn(details, 2);
            grid.Children.Add(player);

            var controls = new Grid { Margin = new Thickness(0, 13, 0, 0) };
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var speedButton = new Button
            {
                Width = 54,
                Height = 36,
                Content = "1.0×",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = accentBrush,
                Background = Brush("#FFF0E7"),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Playback speed"
            };
            speedButton.Click += delegate
            {
                if (speedBox != null)
                {
                    speedBox.SelectedIndex = (speedBox.SelectedIndex + 1) % speedBox.Items.Count;
                }
            };
            controls.Children.Add(speedButton);

            var back = MakeTransportButton("◀", "15", "Skip back 15 seconds");
            back.Click += delegate { Skip(-15); };
            back.Margin = new Thickness(23, 0, 0, 0);
            Grid.SetColumn(back, 1);
            controls.Children.Add(back);

            playButton = MakePlayButton();
            playButton.Margin = new Thickness(15, 0, 15, 0);
            playButton.Click += delegate { TogglePlay(); };
            Grid.SetColumn(playButton, 2);
            controls.Children.Add(playButton);

            var forward = MakeTransportButton("▶", "15", "Skip forward 15 seconds");
            forward.Click += delegate { Skip(15); };
            Grid.SetColumn(forward, 3);
            controls.Children.Add(forward);

            var night = MakeGlyphButton("☾", "Night mode");
            Grid.SetColumn(night, 5);
            controls.Children.Add(night);

            speedBox = new ComboBox
            {
                Width = 1,
                Height = 1,
                Opacity = 0,
                IsHitTestVisible = false
            };
            speedBox.Items.Add("0.75×");
            speedBox.Items.Add("1.0×");
            speedBox.Items.Add("1.25×");
            speedBox.Items.Add("1.5×");
            speedBox.Items.Add("2.0×");
            speedBox.SelectedIndex = 1;
            speedBox.SelectionChanged += delegate
            {
                ApplySpeed();
                speedButton.Content = speedBox.SelectedItem == null ? "1.0×" : speedBox.SelectedItem.ToString();
            };
            Grid.SetColumn(speedBox, 0);
            controls.Children.Add(speedBox);

            Grid.SetRow(controls, 8);
            details.Children.Add(controls);

            var footer = new Grid { Margin = new Thickness(0, 25, 0, 0) };
            footer.Visibility = Visibility.Collapsed;
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var syncStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            syncText = new TextBlock
            {
                Text = "Kobo account",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#7C7470")
            };
            syncStack.Children.Add(syncText);
            syncDetailText = new TextBlock
            {
                Text = "Connect Kobo to browse and sync your audiobooks.",
                FontSize = 11,
                Foreground = Brush("#B0A8A4"),
                Margin = new Thickness(8, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            syncStack.Children.Add(syncDetailText);
            footer.Children.Add(syncStack);

            volumeSlider = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                Value = 0.9,
                Width = 78,
                Height = 18,
                Foreground = Brush("#B8B0AC"),
                Background = Brush("#E9E3E0"),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Volume"
            };
            volumeSlider.ValueChanged += delegate { if (media != null) media.Volume = volumeSlider.Value; };
            Grid.SetColumn(volumeSlider, 1);
            footer.Children.Add(volumeSlider);
            Grid.SetRow(footer, 2);
            grid.Children.Add(footer);

            playerSurface.Child = grid;
            return playerSurface;
        }

        private static TextBlock MakeTimeText(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = Brush("#8F8783"),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private ImageSource CreateApplicationIcon()
        {
            const int size = 64;
            var badge = BuildBrandBadge(size, 16, 38);
            badge.Measure(new Size(size, size));
            badge.Arrange(new Rect(0, 0, size, size));
            badge.UpdateLayout();
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(badge);
            bitmap.Freeze();
            return bitmap;
        }

        private Brush BuildBrandBrush()
        {
            var highlight = Color.FromRgb(
                (byte)Math.Min(255, accentBrush.Color.R + 24),
                (byte)Math.Min(255, accentBrush.Color.G + 18),
                (byte)Math.Min(255, accentBrush.Color.B + 8));
            return new LinearGradientBrush(highlight, accentBrush.Color, new Point(0.15, 0), new Point(0.85, 1));
        }

        private Border BuildBrandBadge(double size, double radius, double glyphSize)
        {
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(radius),
                Background = BuildBrandBrush(),
                BorderBrush = WithOpacity(Colors.White, 0.5),
                BorderThickness = new Thickness(Math.Max(0.5, size / 64.0)),
                Child = SvgIconFactory.Load("book-open.svg", glyphSize, glyphSize),
                Effect = size > 20
                    ? new DropShadowEffect { Color = accentBrush.Color, Opacity = 0.18, BlurRadius = 10, ShadowDepth = 2, Direction = 270 }
                    : null
            };
        }

        private Border BuildBrandIcon()
        {
            return BuildBrandBadge(16, 4, 10);
        }

        private static TextBlock MakeChevron(string glyph)
        {
            return new TextBlock
            {
                Text = glyph,
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#9A918C"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Button MakeCircleButton(UIElement content, string tooltip)
        {
            return new Button
            {
                Width = 40,
                Height = 40,
                Padding = new Thickness(0),
                Content = new Border
                {
                    Width = 38,
                    Height = 38,
                    CornerRadius = new CornerRadius(19),
                    Background = Brush("#F5F1EE"),
                    Child = content
                },
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = tooltip,
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private static Button MakeTinyCircleButton(string glyph, string tooltip)
        {
            return new Button
            {
                Width = 32,
                Height = 32,
                Content = new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new CornerRadius(15),
                    Background = Brush("#F5F1EE"),
                    Child = new TextBlock
                    {
                        Text = glyph,
                        FontSize = 21,
                        Foreground = Brush("#9A918C"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                },
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
        }

        private static Button MakeTransportButton(string arrow, string number, string tooltip)
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = arrow,
                FontSize = 22,
                Foreground = Brush("#261D1B"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = number,
                FontSize = 9,
                Foreground = Brush("#261D1B"),
                Margin = new Thickness(0, -6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return new Button
            {
                Width = 43,
                Height = 40,
                Content = stack,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
        }

        private static Button MakePlayButton()
        {
            return new Button
            {
                Width = 68,
                Height = 68,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Content = new Border
                {
                    Width = 68,
                    Height = 68,
                    CornerRadius = new CornerRadius(34),
                    Background = Brush("#7DD3FC"),
                    Child = new TextBlock
                    {
                        Text = "▷",
                        FontSize = 30,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 0, 0)
                    }
                },
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private static Button MakeGlyphButton(string glyph, string tooltip)
        {
            return new Button
            {
                Width = 34,
                Height = 38,
                Content = new TextBlock
                {
                    Text = glyph,
                    FontSize = glyph == "☾" ? 28 : 27,
                    Foreground = Brush("#261D1B"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
        }

        private static TextBlock MakeSmallText(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = Brush("#72807A")
            };
        }

        private static Button MakeButton(string text, bool primary)
        {
            return new Button
            {
                Content = text,
                Height = 34,
                Padding = new Thickness(14, 4, 14, 4),
                FontSize = 12,
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = primary ? Brushes.White : Brush("#315B4C"),
                Background = primary ? Brush("#7DD3FC") : Brushes.White,
                BorderBrush = primary ? Brush("#7DD3FC") : Brush("#D7DFDA"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
        }

        private static Button MakeIconButton(string glyph, string tooltip)
        {
            return new Button
            {
                Width = 40,
                Height = 40,
                Padding = new Thickness(0),
                Content = new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(18),
                    Background = Brush("#F7F0EA"),
                    Child = new TextBlock
                    {
                        Text = glyph,
                        FontSize = glyph == "+" ? 22 : 19,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brush("#8D807B"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                },
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = tooltip,
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private static Button MakeRoundButton(string text, bool primary)
        {
            var button = MakeButton(text, primary);
            button.Padding = new Thickness(13, 4, 13, 4);
            return button;
        }
    }
}
