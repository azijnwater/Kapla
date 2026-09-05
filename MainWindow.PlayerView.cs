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

            sleepTimerCaption = FigmaText("SLEEP", 7, FontWeights.SemiBold, Brush("#8A7E7A"));
            sleepTimerCaption.Width = 32;
            sleepTimerCaption.Height = 9;
            sleepTimerCaption.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(sleepTimerCaption, 298);
            Canvas.SetTop(sleepTimerCaption, 139);
            controls.Children.Add(sleepTimerCaption);

            sleepTimerButton = MakeSleepTimerButton();
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
                    ShowChoiceMenu(button, speedBox.Items.Cast<string>(), speedBox.SelectedItem as string,
                        value => speedBox.SelectedItem = value);
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

        private Button MakeSleepTimerButton()
        {
            var button = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "Sleep timer",
                Template = MakeRoundedButtonTemplate(7)
            };
            AttachMicroInteraction(button, 1.04);
            UpdateSleepTimerButtonVisual(false, null, button);
            return button;
        }

        private void UpdateSleepTimerButtonVisual(bool active, string label, Button target = null)
        {
            var button = target ?? sleepTimerButton;
            if (button == null)
            {
                return;
            }
            var surface = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(7),
                Background = active ? accentSoftBrush : Brushes.Transparent,
                Child = SvgIconFactory.LoadTinted("Figma", "moon.svg", 16, 16,
                    active ? accentBrush.Color : HeaderIconColor(false))
            };
            button.Content = surface;
            button.ToolTip = active && !String.IsNullOrWhiteSpace(label)
                ? "Sleep timer: " + label
                : "Sleep timer";
            if (sleepTimerCaption != null)
            {
                sleepTimerCaption.Text = active && sleepTimer.Mode == SleepTimerMode.EndOfChapter
                    ? "END"
                    : active && sleepTimer.Duration.HasValue
                        ? Math.Max(1, (int)Math.Round(sleepTimer.Duration.Value.TotalMinutes)) + "m"
                        : "SLEEP";
                sleepTimerCaption.Foreground = active ? accentBrush : (IsDarkTheme ? Brush("#AAB3BD") : Brush("#8A7E7A"));
            }
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

    }
}
