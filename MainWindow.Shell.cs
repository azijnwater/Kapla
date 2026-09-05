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
    internal sealed class LocalCoverImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed partial class MainWindow : Window
    {
        private void BuildLayout()
        {
            rootLayout = new Grid { Background = Brushes.Transparent };
            rootLayout.PreviewMouseLeftButtonDown += HeaderSurfaceOnMouseLeftButtonDown;
            shellSurface = new Border
            {
                Width = 560,
                Height = 300,
                Background = Brush("#FDF8F4"),
                BorderBrush = Brush("#101A1111"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0),
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(26, 17, 17),
                    Opacity = 0.08,
                    BlurRadius = 32,
                    ShadowDepth = 12,
                    Direction = 270
                }
            };
            rootLayout.Children.Add(shellSurface);

            appCard = new Border
            {
                Width = 560,
                Height = 300,
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(18),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0)
            };

            cardLayout = new Grid { Height = 300, ClipToBounds = true };
            var header = BuildHeader();
            ((FrameworkElement)header).Margin = new Thickness(28, 20, 28, 0);
            ((FrameworkElement)header).HorizontalAlignment = HorizontalAlignment.Stretch;
            ((FrameworkElement)header).VerticalAlignment = VerticalAlignment.Top;
            cardLayout.Children.Add(header);
            var player = BuildFigmaPlayerPanel();
            ((FrameworkElement)player).Margin = new Thickness(0, 56, 0, 0);
            ((FrameworkElement)player).HorizontalAlignment = HorizontalAlignment.Center;
            ((FrameworkElement)player).VerticalAlignment = VerticalAlignment.Top;
            cardLayout.Children.Add(player);
            appCard.Child = cardLayout;
            rootLayout.Children.Add(appCard);

            librarySurface = BuildExpandedPanel() as Border;
            librarySurface.Width = 560;
            librarySurface.Height = ExpandedPanelHeight;
            librarySurface.HorizontalAlignment = HorizontalAlignment.Center;
            librarySurface.VerticalAlignment = VerticalAlignment.Top;
            librarySurface.Margin = new Thickness(0);
            librarySurface.Visibility = Visibility.Collapsed;
            rootLayout.Children.Add(librarySurface);

            media = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Volume = appSettings.Volume,
                Opacity = 0,
                IsHitTestVisible = false,
                Width = 1,
                Height = 1
            };
            media.MediaOpened += MediaOnMediaOpened;
            media.MediaEnded += MediaOnMediaEnded;
            media.MediaFailed += MediaOnMediaFailed;
            rootLayout.Children.Add(media);

            windowSurface = new Border
            {
                Background = Brushes.Transparent,
                Child = rootLayout
            };
            Content = windowSurface;
        }

        private void ToggleLibraryExpanded()
        {
            var anchoredBottom = Top + (ActualHeight > 0 ? ActualHeight : Height);
            libraryExpanded = !libraryExpanded;
            if (libraryExpanded)
            {
                SetAppCardChrome(true);
            }
            var targetHeight = libraryExpanded ? ExpandedWindowHeight : CollapsedWindowHeight;
            var targetTop = anchoredBottom - targetHeight;
            if (libraryExpanded)
            {
                librarySurface.Visibility = Visibility.Visible;
            }
            ResizeWindowPanel(targetTop, targetHeight, libraryExpanded);
            var toggleSurface = libraryToggleButton.Content as Border;
            if (toggleSurface != null)
            {
                toggleSurface.Child = MakeChevronIcon(!libraryExpanded);
            }
            libraryToggleButton.ToolTip = libraryExpanded ? "Hide audiobook library" : "Show audiobook library";
            if (libraryExpanded)
            {
                ShowExpandedView(expandedView);
            }
        }

        private void SetExpandedChrome(bool expanded)
        {
            shellSurface.Height = expanded ? ExpandedShellHeight : CollapsedWindowHeight;
            SetAppCardChrome(expanded);
        }

        private void SetAppCardChrome(bool expanded)
        {
            appCard.CornerRadius = expanded ? new CornerRadius(0, 0, 18, 18) : new CornerRadius(18);
            appCard.BorderBrush = expanded ? Brush("#101A1111") : Brushes.Transparent;
            appCard.BorderThickness = expanded ? new Thickness(0, 1, 0, 0) : new Thickness(0);
        }

        private UIElement BuildHeader()
        {
            headerSurface = new Border
            {
                Height = 26,
                Background = Brushes.Transparent,
                Padding = new Thickness(0)
            };

            headerCanvas = new Canvas { Width = 504, Height = 26 };
            var brand = new Canvas { Width = 59, Height = 16 };
            brandIcon = BuildBrandIcon();
            brand.Children.Add(brandIcon);
            var brandText = new TextBlock
            {
                Text = "Kapla",
                FontFamily = interFont,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#1A1111"),
                LineHeight = 16
            };
            Canvas.SetLeft(brandText, 22);
            Canvas.SetTop(brandText, -1);
            brand.Children.Add(brandText);
            Canvas.SetLeft(brand, 0);
            Canvas.SetTop(brand, 5);
            headerCanvas.Children.Add(brand);

            syncIcon = BuildSyncIcon();
            syncButton = MakeMicroHeaderButton(syncIcon, "Connect or sync Kobo");
            var syncSurface = syncButton.Content as Border;
            if (syncSurface != null)
            {
                syncSurface.Background = Brushes.Transparent;
                syncSurface.CornerRadius = new CornerRadius(0);
            }
            System.Windows.Automation.AutomationProperties.SetName(syncButton, "Sync Kobo now");
            syncButton.Click += SyncButtonOnClick;
            Canvas.SetLeft(syncButton, 64);
            Canvas.SetTop(syncButton, 5);
            headerCanvas.Children.Add(syncButton);

            headerSyncText = FigmaText("", 8, FontWeights.Medium, Brush("#8A7E7A"));
            headerSyncText.Width = 156;
            headerSyncText.Height = 14;
            Canvas.SetLeft(headerSyncText, 86);
            Canvas.SetTop(headerSyncText, 6);
            headerCanvas.Children.Add(headerSyncText);

            libraryToggleButton = MakeHeaderButton(MakeChevronIcon(true), "Show audiobook library");
            var expandSurface = libraryToggleButton.Content as Border;
            if (expandSurface != null)
            {
                expandSurface.Background = Brushes.Transparent;
                expandSurface.CornerRadius = new CornerRadius(0);
            }
            System.Windows.Automation.AutomationProperties.SetName(libraryToggleButton, "Expand library");
            libraryToggleButton.Click += delegate { ToggleLibraryExpanded(); };
            headerCanvas.Children.Add(libraryToggleButton);
            pinButton = MakeMicroHeaderButton(BuildPinIcon(isPinned), isPinned ? "Unpin Kapla" : "Keep Kapla on top");
            System.Windows.Automation.AutomationProperties.SetName(pinButton, "Toggle always on top");
            pinButton.Click += delegate { TogglePin(); };
            Canvas.SetTop(pinButton, 5);
            headerCanvas.Children.Add(pinButton);
            minimizeButton = MakeMicroHeaderButton(BuildBootstrapIcon("dash-lg.svg", 12, false), "Minimize Kapla");
            System.Windows.Automation.AutomationProperties.SetName(minimizeButton, "Minimize");
            minimizeButton.Click += delegate { WindowState = WindowState.Minimized; };
            Canvas.SetTop(minimizeButton, 5);
            headerCanvas.Children.Add(minimizeButton);
            closeButton = MakeMicroHeaderButton(BuildBootstrapIcon("x-lg.svg", 11, false), "Close Kapla");
            System.Windows.Automation.AutomationProperties.SetName(closeButton, "Close");
            closeButton.Click += delegate { Close(); };
            Canvas.SetTop(closeButton, 5);
            headerCanvas.Children.Add(closeButton);

            headerSurface.PreviewMouseLeftButtonDown += HeaderSurfaceOnMouseLeftButtonDown;
            headerSurface.Child = headerCanvas;
            return headerSurface;
        }

        private void ResizeWindowPanel(double targetTop, double targetHeight, bool expanding)
        {
            BeginAnimation(TopProperty, null);
            BeginAnimation(HeightProperty, null);
            MinHeight = CollapsedWindowHeight;
            MaxHeight = ExpandedWindowHeight;

            if (!appSettings.AnimationsEnabled || appSettings.ReduceMotion)
            {
                Top = targetTop;
                Height = targetHeight;
                librarySurface.Visibility = expanding ? Visibility.Visible : Visibility.Collapsed;
                SetExpandedChrome(expanding);
                UpdateResponsiveLayout();
                return;
            }

            var duration = TimeSpan.FromMilliseconds(240);
            var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

            if (expanding)
            {
                Top = targetTop;
                Height = targetHeight;
                librarySurface.Visibility = Visibility.Visible;
                librarySurface.Opacity = 0;
                shellSurface.BeginAnimation(HeightProperty,
                    new DoubleAnimation(CollapsedWindowHeight, ExpandedShellHeight, duration) { EasingFunction = easing },
                    HandoffBehavior.SnapshotAndReplace);
                var translate = new TranslateTransform(0, 12);
                librarySurface.RenderTransform = translate;
                librarySurface.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, duration) { EasingFunction = easing },
                    HandoffBehavior.SnapshotAndReplace);
                var slide = new DoubleAnimation(12, 0, duration) { EasingFunction = easing };
                slide.Completed += delegate
                {
                    librarySurface.BeginAnimation(OpacityProperty, null);
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    shellSurface.BeginAnimation(HeightProperty, null);
                    shellSurface.Height = ExpandedShellHeight;
                    librarySurface.Opacity = 1;
                    translate.Y = 0;
                };
                translate.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);
                UpdateResponsiveLayout();
                return;
            }

            var collapseTranslate = new TranslateTransform(0, 0);
            librarySurface.RenderTransform = collapseTranslate;
            SetAppCardChrome(false);
            shellSurface.BeginAnimation(HeightProperty,
                new DoubleAnimation(ExpandedShellHeight, CollapsedWindowHeight, duration) { EasingFunction = easing },
                HandoffBehavior.SnapshotAndReplace);
            var fade = new DoubleAnimation(librarySurface.Opacity, 0, duration) { EasingFunction = easing };
            fade.Completed += delegate
            {
                librarySurface.BeginAnimation(OpacityProperty, null);
                collapseTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                shellSurface.BeginAnimation(HeightProperty, null);
                librarySurface.Visibility = Visibility.Collapsed;
                librarySurface.Opacity = 1;
                librarySurface.RenderTransform = Transform.Identity;
                SetExpandedChrome(false);
                Top = targetTop;
                Height = targetHeight;
                UpdateResponsiveLayout();
            };
            librarySurface.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
            collapseTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, 12, duration) { EasingFunction = easing },
                HandoffBehavior.SnapshotAndReplace);
        }

        private Viewbox BuildSyncIcon()
        {
            var icon = BuildBootstrapIcon("arrow-repeat.svg", 12, false);
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = new RotateTransform(0);
            return icon;
        }

        private void UpdateResponsiveLayout()
        {
            if (updatingWindowLayout || shellSurface == null || appCard == null || librarySurface == null)
            {
                return;
            }
            updatingWindowLayout = true;
            try
            {
                var surfaceWidth = appSettings.ShowCoverArtwork ? ArtworkWindowWidth : CompactWindowWidth;
                if (WindowState == WindowState.Normal && Math.Abs(Width - surfaceWidth) > 0.1)
                {
                    var right = !Double.IsNaN(Left) && ActualWidth > 0 ? Left + ActualWidth : Double.NaN;
                    Width = surfaceWidth;
                    if (!Double.IsNaN(right))
                    {
                        Left = right - surfaceWidth;
                    }
                }
                MinWidth = surfaceWidth;
                MaxWidth = surfaceWidth;
                shellSurface.Width = surfaceWidth;
                appCard.Width = surfaceWidth;
                librarySurface.Width = surfaceWidth;
                if (headerSurface != null && headerCanvas != null)
                {
                    var headerWidth = Math.Max(326, surfaceWidth - 56);
                    headerSurface.Width = headerWidth;
                    headerCanvas.Width = headerWidth;
                    Canvas.SetLeft(libraryToggleButton, headerWidth - 108);
                    Canvas.SetLeft(pinButton, headerWidth - 72);
                    Canvas.SetLeft(minimizeButton, headerWidth - 46);
                    Canvas.SetLeft(closeButton, headerWidth - 20);
                    if (headerSyncText != null)
                    {
                        headerSyncText.Width = Math.Max(32, headerWidth - 182);
                    }
                }
                ApplyCoverVisibility(false);

                if (libraryExpanded)
                {
                    librarySurface.Height = ExpandedPanelHeight;
                    shellSurface.Height = ExpandedShellHeight;
                }
                else
                {
                    librarySurface.Height = ExpandedPanelHeight;
                    shellSurface.Height = 300;
                }
            }
            finally
            {
                updatingWindowLayout = false;
            }
        }

        private void ApplyCoverVisibility(bool animate)
        {
            if (coverBorder == null || playerSurface == null || playerCanvas == null || playerControlsCanvas == null)
            {
                return;
            }
            var show = appSettings.ShowCoverArtwork;
            coverBorder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            playerSurface.Width = show ? 504 : 330;
            playerCanvas.Width = show ? 504 : 330;
            Canvas.SetLeft(playerControlsCanvas, show ? 174 : 0);
            if (animate && appSettings.AnimationsEnabled && !appSettings.ReduceMotion)
            {
                AnimateIn(playerSurface, 200, show ? -4 : 4);
            }
        }

        private void AnimateIn(UIElement element, int milliseconds, double offsetY)
        {
            if (element == null || !appSettings.AnimationsEnabled || appSettings.ReduceMotion)
            {
                if (element != null) element.Opacity = 1;
                return;
            }
            var transform = new TranslateTransform(0, offsetY);
            element.RenderTransform = transform;
            element.Opacity = 0;
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds)));
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offsetY, 0, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private UIElement BuildLibraryPanel()
        {
            librarySurface = new Border
            {
                Background = Brush("#FDF8F4"),
                Padding = new Thickness(18),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(20),
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(26, 17, 17),
                    Opacity = 0.06,
                    BlurRadius = 22,
                    ShadowDepth = 8,
                    Direction = 270
                }
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(new TextBlock
            {
                Text = "Your library",
                FontFamily = interFont,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#1A1111"),
                VerticalAlignment = VerticalAlignment.Center
            });
            libraryCount = new TextBlock
            {
                Text = "0",
                FontFamily = interFont,
                FontSize = 10,
                Foreground = Brush("#AB9F9A"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(libraryCount, 1);
            heading.Children.Add(libraryCount);
            connectKoboButton = MakeHeaderButton(MakePlusIcon(), "Add an audiobook");
            connectKoboButton.Click += delegate { ShowAddMenu(connectKoboButton); };
            Grid.SetColumn(connectKoboButton, 2);
            heading.Children.Add(connectKoboButton);
            grid.Children.Add(heading);

            searchBox = new TextBox
            {
                Height = 28,
                Margin = new Thickness(0, 10, 0, 8),
                Padding = new Thickness(9, 5, 9, 3),
                BorderBrush = Brush("#E8DDD7"),
                BorderThickness = new Thickness(1),
                Background = Brush("#FFFFFF"),
                FontFamily = interFont,
                FontSize = 10,
                Foreground = Brush("#1A1111"),
                ToolTip = "Search your audiobook library"
            };
            searchBox.TextChanged += delegate { RefreshVisibleBooks(); };
            Grid.SetRow(searchBox, 1);
            grid.Children.Add(searchBox);

            libraryList = new ListBox
            {
                ItemsSource = visibleBooks,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            libraryList.SelectionChanged += LibraryListOnSelectionChanged;
            libraryList.MouseDoubleClick += delegate { if (currentBook != null) PlayCurrent(); };
            libraryList.ItemTemplate = BuildBookTemplate();
            Grid.SetRow(libraryList, 2);
            grid.Children.Add(libraryList);

            statusText = new TextBlock
            {
                Text = "Press + to add an audiobook.",
                TextWrapping = TextWrapping.Wrap,
                FontFamily = interFont,
                FontSize = 9,
                Foreground = Brush("#AB9F9A"),
                Margin = new Thickness(2, 8, 8, 0)
            };
            Grid.SetRow(statusText, 3);
            grid.Children.Add(statusText);

            librarySurface.Child = grid;
            return librarySurface;
        }

        private DataTemplate BuildBookTemplate()
        {
            var template = new DataTemplate(typeof(BookEntry));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            border.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 5));
            border.SetValue(Border.BackgroundProperty, Brush("#FFFFFF"));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new Binding("Title"));
            title.SetValue(TextBlock.FontFamilyProperty, interFont);
            title.SetValue(TextBlock.FontSizeProperty, 11.0);
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            title.SetValue(TextBlock.ForegroundProperty, Brush("#261D1B"));
            title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            stack.AppendChild(title);

            var author = new FrameworkElementFactory(typeof(TextBlock));
            author.SetBinding(TextBlock.TextProperty, new Binding("Author"));
            author.SetValue(TextBlock.FontFamilyProperty, interFont);
            author.SetValue(TextBlock.FontSizeProperty, 9.0);
            author.SetValue(TextBlock.ForegroundProperty, Brush("#9B908C"));
            author.SetValue(TextBlock.MarginProperty, new Thickness(0, 3, 0, 0));
            author.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            stack.AppendChild(author);

            var progress = new FrameworkElementFactory(typeof(TextBlock));
            progress.SetBinding(TextBlock.TextProperty, new Binding("ProgressText"));
            progress.SetValue(TextBlock.FontFamilyProperty, interFont);
            progress.SetValue(TextBlock.FontSizeProperty, 8.0);
            progress.SetValue(TextBlock.ForegroundProperty, accentBrush);
            progress.SetValue(TextBlock.MarginProperty, new Thickness(0, 7, 0, 0));
            stack.AppendChild(progress);

            border.AppendChild(stack);
            template.VisualTree = border;
            return template;
        }

        private UIElement BuildExpandedPanel()
        {
            librarySurface = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(24, 12, 24, 10),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(18, 18, 0, 0)
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });

            var navigation = new Grid { Background = Brushes.Transparent };
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            expandedSyncBadge = BuildExpandedSyncBadge();
            expandedSyncBadge.HorizontalAlignment = HorizontalAlignment.Left;
            expandedSyncBadge.VerticalAlignment = VerticalAlignment.Top;
            navigation.Children.Add(expandedSyncBadge);

            var tabs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            };
            libraryTabButton = MakePanelTabButton("Library", "library");
            tabs.Children.Add(libraryTabButton);
            koboTabButton = MakePanelTabButton("Kobo", "kobo");
            koboTabButton.Margin = new Thickness(6, 0, 0, 0);
            tabs.Children.Add(koboTabButton);
            settingsTabButton = MakePanelTabButton("Settings", "settings");
            settingsTabButton.Margin = new Thickness(6, 0, 0, 0);
            tabs.Children.Add(settingsTabButton);
            Grid.SetColumn(tabs, 1);
            navigation.Children.Add(tabs);

            connectKoboButton = MakeHeaderButton(MakePlusIcon(), "Add an audiobook");
            connectKoboButton.Click += delegate { ShowAddMenu(connectKoboButton); };
            connectKoboButton.HorizontalAlignment = HorizontalAlignment.Right;
            connectKoboButton.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(connectKoboButton, 2);
            navigation.Children.Add(connectKoboButton);
            navigation.PreviewMouseLeftButtonDown += HeaderSurfaceOnMouseLeftButtonDown;

            var navigationRail = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = IsDarkTheme ? Brush("#30363D") : Brush("#E8DDD7"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = navigation
            };
            root.Children.Add(navigationRail);

            expandedContentHost = new ContentControl
            {
                Margin = new Thickness(0, 7, 0, 3),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            Grid.SetRow(expandedContentHost, 1);
            root.Children.Add(expandedContentHost);

            statusText = new TextBlock
            {
                Text = "Press + to add an audiobook.",
                FontFamily = interFont,
                FontSize = 9.5,
                Foreground = Brush("#8A7E7A"),
                VerticalAlignment = VerticalAlignment.Bottom,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(statusText, 2);
            root.Children.Add(statusText);

            librarySurface.Child = root;
            ShowExpandedView("library");
            return librarySurface;
        }

        private Border BuildExpandedSyncBadge()
        {
            expandedSyncDot = new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Center
            };
            expandedSyncText = new TextBlock
            {
                FontFamily = interFont,
                FontSize = 9.5,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(expandedSyncDot);
            content.Children.Add(expandedSyncText);
            var badge = new Border
            {
                Padding = new Thickness(8, 3, 8, 3),
                CornerRadius = new CornerRadius(10),
                Child = content
            };
            UpdateExpandedSyncBadge();
            return badge;
        }

        private void UpdateExpandedSyncBadge()
        {
            if (expandedSyncBadge == null || expandedSyncDot == null || expandedSyncText == null)
            {
                return;
            }
            var connected = koboClient != null && koboSession != null && !String.IsNullOrWhiteSpace(koboSession.AccessToken);
            var synced = connected && String.Equals(currentSyncStatus, "Synced", StringComparison.OrdinalIgnoreCase);
            var error = currentSyncStatus.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0;
            var color = synced ? Brush("#2EA043") : error ? Brush("#C36B6B") : connected ? accentBrush : (IsDarkTheme ? Brush("#8B949E") : Brush("#8A7E7A"));
            expandedSyncText.Text = synced ? "Synced" : error ? "Sync issue" : connected ? "Kobo ready" : "Kobo offline";
            expandedSyncText.Foreground = color;
            expandedSyncDot.Background = color;
            expandedSyncBadge.Background = WithOpacity(((SolidColorBrush)color).Color, IsDarkTheme ? 0.14 : 0.10);
        }

        private Button MakePanelTabButton(string label, string view)
        {
            var button = new Button
            {
                Content = label,
                Height = 24,
                MinWidth = 56,
                Padding = new Thickness(10, 3, 10, 3),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brush("#8A1A1111"),
                FontFamily = interFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Template = MakeRoundedButtonTemplate(6)
            };
            button.Click += delegate { ShowExpandedView(view); };
            return button;
        }

        private void ShowExpandedView(string view)
        {
            if (expandedContentHost == null)
            {
                return;
            }
            expandedView = String.IsNullOrWhiteSpace(view) ? "library" : view;
            UpdatePanelTabStates();
            if (expandedView == "settings")
            {
                expandedContentHost.Content = BuildSettingsView();
                statusText.Text = String.Empty;
            }
            else if (expandedView == "kobo")
            {
                expandedContentHost.Content = BuildKoboView();
            }
            else if (expandedView == "add")
            {
                expandedContentHost.Content = BuildAddView();
                statusText.Text = "Choose where this audiobook comes from.";
            }
            else if (expandedView == "sleep")
            {
                expandedContentHost.Content = BuildSleepTimerView();
            }
            else
            {
                expandedContentHost.Content = BuildLibraryShelfView();
            }
            ApplyThemeToElement(expandedContentHost);
            UpdatePanelTabStates();
            AnimateIn(expandedContentHost, 190, 4);
            Dispatcher.BeginInvoke(new Action(delegate
            {
                ApplyThemeToElement(expandedContentHost);
                UpdatePanelTabStates();
            }), DispatcherPriority.Loaded);
        }

        private void UpdatePanelTabStates()
        {
            SetPanelTabState(libraryTabButton, expandedView == "library");
            SetPanelTabState(settingsTabButton, expandedView == "settings");
            SetPanelTabState(koboTabButton, expandedView == "kobo");
        }

        private void SetPanelTabState(Button button, bool active)
        {
            if (button == null)
            {
                return;
            }
            button.Background = active ? accentSoftBrush : Brushes.Transparent;
            button.BorderBrush = active ? accentBrush : Brushes.Transparent;
            button.BorderThickness = active ? new Thickness(1.25) : new Thickness(1.25);
            button.Foreground = active
                ? (IsDarkTheme ? Brush("#8DD3FF") : Brush("#285D78"))
                : (IsDarkTheme ? Brush("#AAB3BD") : Brush("#8A1A1111"));
        }

        private static ControlTemplate MakeRoundedButtonTemplate(double radius)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.84));
            template.Triggers.Add(hover);
            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.68));
            template.Triggers.Add(pressed);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42));
            template.Triggers.Add(disabled);
            return template;
        }

        private void EnsureExpanded(string view)
        {
            expandedView = view;
            if (!libraryExpanded)
            {
                ToggleLibraryExpanded();
            }
            else
            {
                ShowExpandedView(view);
            }
        }

        private UIElement BuildLibraryShelfView()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            searchBox = new TextBox
            {
                Height = 22,
                Padding = new Thickness(8, 2, 8, 2),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontFamily = interFont,
                FontSize = 9.5,
                Foreground = Brush("#1A1111"),
                ToolTip = "Search your audiobook library"
            };
            searchBox.TextChanged += delegate { RefreshVisibleBooks(); };
            grid.Children.Add(new Border
            {
                Height = 24,
                Background = Brush("#AFFFFFFF"),
                BorderBrush = Brush("#18A7DDF7"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Child = searchBox
            });
            libraryCount = new TextBlock
            {
                FontFamily = interFont,
                FontSize = 9.5,
                Foreground = Brush("#8A7E7A"),
                Margin = new Thickness(10, 4, 2, 0)
            };
            Grid.SetColumn(libraryCount, 1);
            grid.Children.Add(libraryCount);

            libraryList = new ListBox
            {
                ItemsSource = visibleBooks,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 3, 0, 0),
                ItemTemplate = BuildBookCoverTemplate(),
                ItemContainerStyle = BuildShelfItemStyle(),
                SelectionMode = SelectionMode.Single
            };
            var panelFactory = new FrameworkElementFactory(typeof(StackPanel));
            panelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            libraryList.ItemsPanel = new ItemsPanelTemplate(panelFactory);
            ScrollViewer.SetHorizontalScrollBarVisibility(libraryList, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(libraryList, ScrollBarVisibility.Disabled);
            libraryList.SelectionChanged += LibraryListOnSelectionChanged;
            Grid.SetRow(libraryList, 1);
            Grid.SetColumnSpan(libraryList, 2);
            grid.Children.Add(libraryList);
            if (currentBook != null)
            {
                libraryList.SelectedItem = currentBook;
            }
            RefreshVisibleBooks();
            return grid;
        }

        private DataTemplate BuildBookCoverTemplate()
        {
            var template = new DataTemplate(typeof(BookEntry));
            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.WidthProperty, 110.0);

            var cover = new FrameworkElementFactory(typeof(Border));
            cover.SetValue(FrameworkElement.WidthProperty, 90.0);
            cover.SetValue(FrameworkElement.HeightProperty, 120.0);
            cover.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cover.SetValue(Border.BackgroundProperty, Brush("#DDF3FC"));
            cover.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            cover.SetValue(Border.ClipToBoundsProperty, true);
            var coverLayers = new FrameworkElementFactory(typeof(Grid));
            var placeholder = new FrameworkElementFactory(typeof(TextBlock));
            placeholder.SetValue(TextBlock.TextProperty, "K");
            placeholder.SetValue(TextBlock.FontFamilyProperty, interFont);
            placeholder.SetValue(TextBlock.FontSizeProperty, 20.0);
            placeholder.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            placeholder.SetValue(TextBlock.ForegroundProperty, Brush("#5FAED2"));
            placeholder.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            placeholder.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            coverLayers.AppendChild(placeholder);
            var image = new FrameworkElementFactory(typeof(Image));
            image.SetBinding(Image.SourceProperty, new Binding("CoverSource")
            {
                Converter = new LocalCoverImageConverter()
            });
            image.SetValue(Image.StretchProperty, Stretch.UniformToFill);
            image.SetValue(Image.WidthProperty, 90.0);
            image.SetValue(Image.HeightProperty, 120.0);
            coverLayers.AppendChild(image);
            cover.AppendChild(coverLayers);
            stack.AppendChild(cover);

            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new Binding("Title"));
            title.SetValue(TextBlock.FontFamilyProperty, interFont);
            title.SetValue(TextBlock.FontSizeProperty, 10.0);
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            title.SetValue(TextBlock.ForegroundProperty, Brush("#1A1111"));
            title.SetValue(TextBlock.WidthProperty, 110.0);
            title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            title.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
            stack.AppendChild(title);

            var author = new FrameworkElementFactory(typeof(TextBlock));
            author.SetBinding(TextBlock.TextProperty, new Binding("Author"));
            author.SetValue(TextBlock.FontFamilyProperty, interFont);
            author.SetValue(TextBlock.FontSizeProperty, 9.0);
            author.SetValue(TextBlock.ForegroundProperty, Brush("#8A7E7A"));
            author.SetValue(TextBlock.WidthProperty, 110.0);
            author.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            stack.AppendChild(author);

            var progress = new FrameworkElementFactory(typeof(TextBlock));
            progress.SetBinding(TextBlock.TextProperty, new Binding("ProgressText"));
            progress.SetValue(TextBlock.FontFamilyProperty, interFont);
            progress.SetValue(TextBlock.FontSizeProperty, 8.5);
            progress.SetValue(TextBlock.ForegroundProperty, accentBrush);
            progress.SetValue(TextBlock.WidthProperty, 110.0);
            progress.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
            stack.AppendChild(progress);
            var timeLeft = new FrameworkElementFactory(typeof(TextBlock));
            timeLeft.SetBinding(TextBlock.TextProperty, new Binding("TimeLeftText"));
            timeLeft.SetValue(TextBlock.FontFamilyProperty, interFont);
            timeLeft.SetValue(TextBlock.FontSizeProperty, 8.5);
            timeLeft.SetValue(TextBlock.ForegroundProperty, Brush("#8A7E7A"));
            timeLeft.SetValue(TextBlock.WidthProperty, 110.0);
            timeLeft.SetValue(TextBlock.MarginProperty, new Thickness(0, 1, 0, 0));
            stack.AppendChild(timeLeft);
            template.VisualTree = stack;
            return template;
        }

        private Style BuildShelfItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5)));
            style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 8, 0)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            var template = new ControlTemplate(typeof(ListBoxItem));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, accentBrush));
            selected.Setters.Add(new Setter(Control.BackgroundProperty, accentSoftBrush));
            selected.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2.5)));
            style.Triggers.Add(selected);
            return style;
        }

        private UIElement BuildAddView()
        {
            var root = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            root.Children.Add(FigmaText("Add to Kapla", 12, FontWeights.Bold, Brush("#1A1111")));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var local = MakeCompactActionButton("Choose local audiobook…", true, IconAdd);
            local.Click += delegate { ImportLocalAudiobook(); };
            row.Children.Add(local);
            var kobo = MakeCompactActionButton("Open Kobo library", false, IconLink);
            kobo.Margin = new Thickness(8, 0, 0, 0);
            kobo.Click += delegate { EnsureExpanded("kobo"); };
            row.Children.Add(kobo);
            root.Children.Add(row);
            root.Children.Add(new TextBlock
            {
                Text = "Local files use embedded artwork and chapters when present. Kobo titles appear in the Kobo tab.",
                FontFamily = interFont,
                FontSize = 10,
                Foreground = Brush("#8A7E7A"),
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            return root;
        }
    }
}
