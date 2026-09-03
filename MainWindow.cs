using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
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
    public sealed class MainWindow : Window
    {
        private readonly string dataDirectory;
        private readonly string libraryFile;
        private readonly string windowPositionFile;
        private readonly string settingsFile;
        private readonly List<BookEntry> allBooks = new List<BookEntry>();
        private readonly ObservableCollection<BookEntry> visibleBooks = new ObservableCollection<BookEntry>();
        private readonly ObservableCollection<KoboRemoteBook> remoteKoboBooks = new ObservableCollection<KoboRemoteBook>();
        private readonly DispatcherTimer progressTimer;
        private readonly AppSettings appSettings;

        private Grid rootLayout;
        private Border windowSurface;
        private Border shellSurface;
        private Border appCard;
        private Border headerSurface;
        private Border brandIcon;
        private Border librarySurface;
        private ContentControl expandedContentHost;
        private Border playerSurface;
        private Button libraryToggleButton;
        private Button pinButton;
        private Button libraryTabButton;
        private Button settingsTabButton;
        private Button koboTabButton;
        private ListBox libraryList;
        private ListBox remoteKoboList;
        private TextBox searchBox;
        private TextBox libraryFoldersTextBox;
        private TextBlock libraryCount;
        private TextBlock statusText;
        private TextBlock koboAccountStatusText;
        private TextBlock koboActivationCodeText;
        private TextBlock koboDownloadText;
        private ProgressBar koboDownloadProgress;
        private Button completeActivationButton;
        private Button importKoboButton;
        private TextBlock titleText;
        private TextBlock authorText;
        private TextBlock metadataText;
        private TextBlock descriptionText;
        private TextBlock playerStateText;
        private TextBlock positionText;
        private TextBlock durationText;
        private TextBlock chapterIndexText;
        private TextBlock chapterTitleText;
        private Grid chapterRow;
        private TextBlock syncText;
        private TextBlock syncDetailText;
        private Button playButton;
        private Button chapterPreviousButton;
        private Button chapterNextButton;
        private Button bookmarkButton;
        private Button sleepTimerButton;
        private Button connectKoboButton;
        private Button rewindButton;
        private Button forwardButton;
        private Button speedButton;
        private Slider progressSlider;
        private Slider volumeSlider;
        private ComboBox speedBox;
        private ComboBox chapterBox;
        private Border coverBorder;
        private Border progressFill;
        private Border progressThumb;
        private MediaElement media;
        private FontFamily interFont;
        private SolidColorBrush accentBrush = Brush("#7DD3FC");
        private SolidColorBrush accentSoftBrush = Brush("#247DD3FC");

        private BookEntry currentBook;
        private BookEntry previewBook;
        private bool isDraggingProgress;
        private bool sourceLoaded;
        private bool isPlaying;
        private bool playWhenSourceReady;
        private bool updatingChapter;
        private int currentTrackIndex;
        private double currentTrackStartSeconds;
        private double pendingTrackPositionSeconds;
        private List<KoboTrack> playbackTracks = new List<KoboTrack>();
        private KoboSession koboSession;
        private KoboClient koboClient;
        private KoboActivation pendingKoboActivation;
        private DateTime lastKoboSyncUtc = DateTime.MinValue;
        private DateTime lastSaveUtc = DateTime.MinValue;
        private DateTime? sleepTimerEndUtc;
        private bool libraryExpanded;
        private bool isPinned;
        private bool isExiting;
        private string expandedView = "library";
        private string settingsCategory = "General";
        private HwndSource windowMessageSource;

        private const double CollapsedWindowHeight = 352;
        private const double ExpandedWindowHeight = 584;
        private const double ExpandedPanelHeight = 232;
        private const double ExpandedShellHeight = 532;
        private const int WindowMessageNonClientHitTest = 0x0084;
        private const int HitTestClient = 1;
        private const int HitTestCaption = 2;

        public MainWindow()
        {
            dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KoboNativePlayer");
            libraryFile = Path.Combine(dataDirectory, "library.json");
            windowPositionFile = Path.Combine(dataDirectory, "window-position.txt");
            settingsFile = Path.Combine(dataDirectory, "settings.json");
            appSettings = AppSettingsStore.Load(settingsFile);
            accentBrush = Brush(appSettings.AccentColor);
            accentSoftBrush = WithOpacity(accentBrush.Color, 0.14);
            SvgIconFactory.AccentColor = accentBrush.Color;
            interFont = CreateInterFont();

            Title = "Kapla";
            Width = 608;
            Height = CollapsedWindowHeight;
            MinWidth = 608;
            MinHeight = CollapsedWindowHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Foreground = Brush("#261D1B");
            FontFamily = interFont;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            isPinned = appSettings.AlwaysOnTopByDefault;
            Topmost = isPinned;
            Icon = CreateApplicationIcon();

            BuildLayout();
            LoadWindowPosition();
            LoadLibrary();
            LoadKoboSession();
            if (String.Equals(Environment.GetEnvironmentVariable("KAPLA_FIGMA_PREVIEW"), "1", StringComparison.Ordinal))
            {
                ShowFigmaPreviewState();
            }
            var metadataPreviewPath = Environment.GetEnvironmentVariable("KAPLA_METADATA_PREVIEW");
            if (!String.IsNullOrWhiteSpace(metadataPreviewPath) && File.Exists(metadataPreviewPath))
            {
                ShowLocalMetadataPreview(metadataPreviewPath);
            }
            Loaded += delegate
            {
                var settingsPreview = Environment.GetEnvironmentVariable("KAPLA_SETTINGS_CATEGORY");
                if (!String.IsNullOrWhiteSpace(settingsPreview))
                {
                    settingsCategory = settingsPreview;
                }
                var expandedPreview = Environment.GetEnvironmentVariable("KAPLA_EXPANDED_PREVIEW");
                if (!String.IsNullOrWhiteSpace(expandedPreview))
                {
                    EnsureExpanded(expandedPreview);
                }
            };

            progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            progressTimer.Tick += ProgressTimerOnTick;
            progressTimer.Start();

            SourceInitialized += MainWindowOnSourceInitialized;
            Closed += MainWindowOnClosed;
            Closing += MainWindowOnClosing;
        }

        private void MainWindowOnSourceInitialized(object sender, EventArgs e)
        {
            windowMessageSource = PresentationSource.FromVisual(this) as HwndSource;
            if (windowMessageSource != null)
            {
                windowMessageSource.AddHook(WindowMessageHook);
            }
        }

        private void MainWindowOnClosed(object sender, EventArgs e)
        {
            if (windowMessageSource != null)
            {
                windowMessageSource.RemoveHook(WindowMessageHook);
                windowMessageSource = null;
            }
        }

        private void MainWindowOnClosing(object sender, CancelEventArgs e)
        {
            SaveCurrentPosition();
            SaveLibrary();
            SaveWindowPosition();
            SaveSettings();
            if (appSettings.MinimizeOnClose && !isExiting)
            {
                e.Cancel = true;
                WindowState = WindowState.Minimized;
                return;
            }
            if (koboClient != null)
            {
                koboClient.Dispose();
            }
        }

        private void BuildLayout()
        {
            rootLayout = new Grid { Background = Brushes.Transparent };
            shellSurface = new Border
            {
                Width = 560,
                Height = 300,
                Background = Brush("#FDF8F4"),
                BorderBrush = Brush("#101A1111"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(28),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 38),
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
                CornerRadius = new CornerRadius(28),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 38)
            };

            var cardCanvas = new Canvas { Width = 560, Height = 300, ClipToBounds = true };
            var header = BuildHeader();
            Canvas.SetLeft(header, 28);
            Canvas.SetTop(header, 24);
            cardCanvas.Children.Add(header);
            var player = BuildFigmaPlayerPanel();
            Canvas.SetLeft(player, 28);
            Canvas.SetTop(player, 64);
            cardCanvas.Children.Add(player);
            appCard.Child = cardCanvas;
            rootLayout.Children.Add(appCard);

            librarySurface = BuildExpandedPanel() as Border;
            librarySurface.Width = 560;
            librarySurface.Height = ExpandedPanelHeight;
            librarySurface.HorizontalAlignment = HorizontalAlignment.Center;
            librarySurface.VerticalAlignment = VerticalAlignment.Top;
            librarySurface.Margin = new Thickness(0, 14, 0, 0);
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
            librarySurface.Visibility = libraryExpanded ? Visibility.Visible : Visibility.Collapsed;
            shellSurface.Height = libraryExpanded ? ExpandedShellHeight : 300;
            appCard.CornerRadius = libraryExpanded ? new CornerRadius(0, 0, 28, 28) : new CornerRadius(28);
            appCard.BorderBrush = libraryExpanded ? Brush("#101A1111") : Brushes.Transparent;
            appCard.BorderThickness = libraryExpanded ? new Thickness(0, 1, 0, 0) : new Thickness(0);
            Height = libraryExpanded ? ExpandedWindowHeight : CollapsedWindowHeight;
            MinHeight = Height;
            MaxHeight = Height;
            Top = anchoredBottom - Height;
            var toggleSurface = libraryToggleButton.Content as Border;
            if (toggleSurface != null)
            {
                toggleSurface.Child = MakeChevronIcon(!libraryExpanded);
            }
            libraryToggleButton.ToolTip = libraryExpanded ? "Hide audiobook library" : "Show audiobook library";
            if (libraryExpanded)
            {
                ShowExpandedView(expandedView);
                if (appSettings.AnimationsEnabled && !appSettings.ReduceMotion)
                {
                    librarySurface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
                }
            }
        }

        private UIElement BuildHeader()
        {
            headerSurface = new Border
            {
                Width = 504,
                Height = 26,
                Background = Brushes.Transparent,
                Padding = new Thickness(0)
            };

            var canvas = new Canvas { Width = 504, Height = 26 };
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
            canvas.Children.Add(brand);

            libraryToggleButton = MakeHeaderButton(MakeChevronIcon(true), "Show audiobook library");
            System.Windows.Automation.AutomationProperties.SetName(libraryToggleButton, "Expand library");
            libraryToggleButton.Click += delegate { ToggleLibraryExpanded(); };
            Canvas.SetLeft(libraryToggleButton, 446);
            canvas.Children.Add(libraryToggleButton);
            pinButton = MakeMicroHeaderButton(BuildPinIcon(isPinned), isPinned ? "Unpin Kapla" : "Keep Kapla on top");
            System.Windows.Automation.AutomationProperties.SetName(pinButton, "Toggle always on top");
            pinButton.Click += delegate { TogglePin(); };
            Canvas.SetLeft(pinButton, 476);
            Canvas.SetTop(pinButton, 5);
            canvas.Children.Add(pinButton);
            var secondDot = SvgIconFactory.Load("ellipse.svg", 8, 8);
            Canvas.SetLeft(secondDot, 496);
            Canvas.SetTop(secondDot, 9);
            canvas.Children.Add(secondDot);

            headerSurface.PreviewMouseLeftButtonDown += HeaderSurfaceOnMouseLeftButtonDown;
            headerSurface.Child = canvas;
            return headerSurface;
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
                Padding = new Thickness(28, 18, 28, 12),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(28, 28, 0, 0)
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });

            var navigation = new Grid { Background = Brushes.Transparent };
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            libraryTabButton = MakePanelTabButton("Library", "library");
            navigation.Children.Add(libraryTabButton);
            settingsTabButton = MakePanelTabButton("Settings", "settings");
            settingsTabButton.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(settingsTabButton, 1);
            navigation.Children.Add(settingsTabButton);
            koboTabButton = MakePanelTabButton("Kobo", "kobo");
            koboTabButton.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(koboTabButton, 2);
            navigation.Children.Add(koboTabButton);
            connectKoboButton = MakeHeaderButton(MakePlusIcon(), "Add an audiobook");
            connectKoboButton.Click += delegate { ShowAddMenu(connectKoboButton); };
            Grid.SetColumn(connectKoboButton, 4);
            navigation.Children.Add(connectKoboButton);
            navigation.PreviewMouseLeftButtonDown += HeaderSurfaceOnMouseLeftButtonDown;
            root.Children.Add(navigation);

            expandedContentHost = new ContentControl { Margin = new Thickness(0, 5, 0, 3) };
            Grid.SetRow(expandedContentHost, 1);
            root.Children.Add(expandedContentHost);

            statusText = new TextBlock
            {
                Text = "Press + to add an audiobook.",
                FontFamily = interFont,
                FontSize = 8.5,
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

        private Button MakePanelTabButton(string label, string view)
        {
            var button = new Button
            {
                Content = label,
                Height = 22,
                MinWidth = 56,
                Padding = new Thickness(10, 2, 10, 2),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brush("#8A1A1111"),
                FontFamily = interFont,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Template = MakeRoundedButtonTemplate(7)
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
            SetPanelTabState(libraryTabButton, expandedView == "library");
            SetPanelTabState(settingsTabButton, expandedView == "settings");
            SetPanelTabState(koboTabButton, expandedView == "kobo");
            if (expandedView == "settings")
            {
                expandedContentHost.Content = BuildSettingsView();
                statusText.Text = "Changes are saved automatically on this PC.";
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
            else
            {
                expandedContentHost.Content = BuildLibraryShelfView();
            }
        }

        private void SetPanelTabState(Button button, bool active)
        {
            if (button == null)
            {
                return;
            }
            button.Background = active ? accentSoftBrush : Brushes.Transparent;
            button.Foreground = active ? Brush("#285D78") : Brush("#8A1A1111");
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
                FontSize = 8.5,
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
                FontSize = 9,
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
            stack.SetValue(StackPanel.WidthProperty, 76.0);

            var cover = new FrameworkElementFactory(typeof(Border));
            cover.SetValue(FrameworkElement.WidthProperty, 76.0);
            cover.SetValue(FrameworkElement.HeightProperty, 102.0);
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
            image.SetBinding(Image.SourceProperty, new Binding("CoverSource"));
            image.SetValue(Image.StretchProperty, Stretch.UniformToFill);
            image.SetValue(Image.WidthProperty, 76.0);
            image.SetValue(Image.HeightProperty, 102.0);
            coverLayers.AppendChild(image);
            cover.AppendChild(coverLayers);
            stack.AppendChild(cover);

            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new Binding("Title"));
            title.SetValue(TextBlock.FontFamilyProperty, interFont);
            title.SetValue(TextBlock.FontSizeProperty, 8.5);
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            title.SetValue(TextBlock.ForegroundProperty, Brush("#1A1111"));
            title.SetValue(TextBlock.WidthProperty, 76.0);
            title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            title.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
            stack.AppendChild(title);
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
            style.Triggers.Add(selected);
            return style;
        }

        private UIElement BuildAddView()
        {
            var root = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            root.Children.Add(FigmaText("Add to Kapla", 12, FontWeights.Bold, Brush("#1A1111")));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var local = MakeCompactActionButton("Choose local audiobook…", true);
            local.Click += delegate { ImportLocalAudiobook(); };
            row.Children.Add(local);
            var kobo = MakeCompactActionButton("Connect or sync Kobo", false);
            kobo.Margin = new Thickness(8, 0, 0, 0);
            kobo.Click += delegate { EnsureExpanded("kobo"); };
            row.Children.Add(kobo);
            root.Children.Add(row);
            root.Children.Add(new TextBlock
            {
                Text = "Local files use embedded artwork and chapters when present. Kobo credentials remain encrypted for this Windows user.",
                FontFamily = interFont,
                FontSize = 9,
                Foreground = Brush("#8A7E7A"),
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            return root;
        }

        private UIElement BuildSettingsView()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var categories = new StackPanel { Orientation = Orientation.Horizontal };
            var content = new ContentControl { Margin = new Thickness(1, 5, 1, 0) };
            Grid.SetRow(content, 1);
            root.Children.Add(categories);
            root.Children.Add(content);

            var categoryButtons = new Dictionary<string, Button>();
            Action<string> activate = null;
            activate = delegate(string category)
            {
                if (String.Equals(category, "Account", StringComparison.Ordinal))
                {
                    ShowExpandedView("kobo");
                    return;
                }
                settingsCategory = category;
                foreach (var entry in categoryButtons)
                {
                    SetSettingsCategoryState(entry.Value, String.Equals(entry.Key, category, StringComparison.Ordinal));
                }
                content.Content = BuildSettingsCategoryContent(category);
            };

            foreach (var name in new[] { "General", "Playback", "Library", "Appearance", "Account" })
            {
                var categoryName = name;
                var button = MakeSettingsCategoryButton(categoryName);
                button.Click += delegate { activate(categoryName); };
                categoryButtons[categoryName] = button;
                categories.Children.Add(button);
            }
            activate(settingsCategory);
            return root;
        }

        private Button MakeSettingsCategoryButton(string label)
        {
            var button = new Button
            {
                Content = label,
                Height = 20,
                MinWidth = 54,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(8, 1, 8, 1),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brush("#8A7E7A"),
                FontFamily = interFont,
                FontSize = 8.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Template = MakeRoundedButtonTemplate(6)
            };
            System.Windows.Automation.AutomationProperties.SetName(button, "Settings " + label);
            return button;
        }

        private void SetSettingsCategoryState(Button button, bool active)
        {
            button.Background = active ? accentSoftBrush : Brushes.Transparent;
            button.Foreground = active ? Brush("#285D78") : Brush("#8A7E7A");
        }

        private UIElement BuildSettingsCategoryContent(string category)
        {
            if (String.Equals(category, "Playback", StringComparison.Ordinal)) return BuildPlaybackSettingsContent();
            if (String.Equals(category, "Library", StringComparison.Ordinal)) return BuildLibrarySettingsContent();
            if (String.Equals(category, "Appearance", StringComparison.Ordinal)) return BuildAppearanceSettingsContent();
            return BuildGeneralSettingsContent();
        }

        private Grid MakeSettingsColumns(StackPanel left, StackPanel right)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(left);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            return grid;
        }

        private UIElement BuildGeneralSettingsContent()
        {
            var left = new StackPanel();
            left.Children.Add(MakeSettingsSectionLabel("WINDOW"));
            left.Children.Add(MakeCompactSettingsCheck("Remember window position", appSettings.RememberWindowPosition, value => appSettings.RememberWindowPosition = value));
            left.Children.Add(MakeCompactSettingsCheck("Always on top by default", appSettings.AlwaysOnTopByDefault, value =>
            {
                appSettings.AlwaysOnTopByDefault = value;
                isPinned = value;
                Topmost = value;
                UpdatePinVisual();
            }));
            left.Children.Add(new TextBlock { Text = "Fixed 560 × 300 Figma player", FontFamily = interFont, FontSize = 8, Foreground = Brush("#8A7E7A"), Margin = new Thickness(0, 3, 0, 0) });

            var right = new StackPanel();
            right.Children.Add(MakeSettingsSectionLabel("STARTUP & CLOSE"));
            right.Children.Add(MakeCompactSettingsCheck("Launch Kapla at startup", appSettings.LaunchAtStartup, value =>
            {
                appSettings.LaunchAtStartup = value;
                ApplyLaunchAtStartup();
            }));
            right.Children.Add(MakeCompactSettingsCheck("Resume last audiobook", appSettings.ResumeLastAudiobook, value => appSettings.ResumeLastAudiobook = value));
            right.Children.Add(MakeCompactSettingsCheck("Close minimizes to taskbar", appSettings.MinimizeOnClose, value => appSettings.MinimizeOnClose = value));
            var exit = MakeCompactActionButton("Exit Kapla", false);
            exit.HorizontalAlignment = HorizontalAlignment.Left;
            exit.Margin = new Thickness(0, 3, 0, 0);
            exit.Click += delegate { isExiting = true; Close(); };
            right.Children.Add(exit);
            return MakeSettingsColumns(left, right);
        }

        private UIElement BuildPlaybackSettingsContent()
        {
            var left = new StackPanel();
            left.Children.Add(MakeSettingsSectionLabel("CONTROLS"));
            left.Children.Add(MakeCompactSettingsValue("Speed", new[] { "0.75x", "1.0x", "1.25x", "1.5x", "2.0x" }, FormatSpeed(appSettings.DefaultPlaybackSpeed), value =>
            {
                appSettings.DefaultPlaybackSpeed = ParseSpeed(value);
                SetSpeedSelection(appSettings.DefaultPlaybackSpeed);
            }));
            left.Children.Add(MakeCompactSettingsValue("Rewind", new[] { "10 seconds", "15 seconds", "30 seconds" }, appSettings.RewindSeconds + " seconds", value =>
            {
                appSettings.RewindSeconds = ParseLeadingInt(value, 15);
                UpdateSkipButtonLabels();
            }));
            left.Children.Add(MakeCompactSettingsValue("Forward", new[] { "10 seconds", "15 seconds", "30 seconds" }, appSettings.ForwardSeconds + " seconds", value =>
            {
                appSettings.ForwardSeconds = ParseLeadingInt(value, 15);
                UpdateSkipButtonLabels();
            }));

            var right = new StackPanel();
            right.Children.Add(MakeSettingsSectionLabel("LISTENING"));
            right.Children.Add(MakeCompactSettingsCheck("Auto-resume selected books", appSettings.AutoResume, value => appSettings.AutoResume = value));
            right.Children.Add(MakeCompactSettingsCheck("Remember playback position", appSettings.RememberPlaybackPosition, value => appSettings.RememberPlaybackPosition = value));
            right.Children.Add(MakeCompactSettingsValue("Sleep", new[] { "15 minutes", "30 minutes", "45 minutes", "60 minutes" }, appSettings.DefaultSleepMinutes + " minutes", value => appSettings.DefaultSleepMinutes = ParseLeadingInt(value, 30)));
            right.Children.Add(MakeCompactVolumeRow());
            return MakeSettingsColumns(left, right);
        }

        private UIElement BuildLibrarySettingsContent()
        {
            var root = new StackPanel();
            root.Children.Add(MakeSettingsSectionLabel("FOLDERS & METADATA"));
            var folderRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folderRow.Children.Add(MakeSettingsLabel("Folders"));
            libraryFoldersTextBox = new TextBox
            {
                Text = String.Join("; ", appSettings.LibraryFolders),
                Height = 20,
                Padding = new Thickness(7, 2, 7, 2),
                FontFamily = interFont,
                FontSize = 8.5,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                ToolTip = "Separate multiple folders with semicolons"
            };
            libraryFoldersTextBox.LostFocus += delegate { SaveLibraryFoldersFromText(); };
            var folderShell = new Border
            {
                Height = 22,
                Background = Brush("#9FFFFFFF"),
                BorderBrush = Brush("#18A7DDF7"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = libraryFoldersTextBox
            };
            Grid.SetColumn(folderShell, 1);
            folderRow.Children.Add(folderShell);
            root.Children.Add(folderRow);

            var lower = new Grid();
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var choices = new StackPanel();
            choices.Children.Add(MakeCompactSettingsValue("Sort", new[] { "Recently played", "Title", "Author" }, appSettings.LibrarySort, value => { appSettings.LibrarySort = value; RefreshVisibleBooks(); }));
            choices.Children.Add(MakeCompactSettingsValue("Metadata", new[] { "Embedded metadata first" }, appSettings.PreferredMetadataSource, value => appSettings.PreferredMetadataSource = value));
            lower.Children.Add(choices);
            var actions = new StackPanel();
            var rescan = MakeCompactActionButton("Rescan library folders", true);
            rescan.HorizontalAlignment = HorizontalAlignment.Left;
            rescan.Click += async delegate { await RescanLibraryFoldersAsync(); };
            actions.Children.Add(rescan);
            var refresh = MakeCompactActionButton("Refresh metadata + covers", false);
            refresh.HorizontalAlignment = HorizontalAlignment.Left;
            refresh.Margin = new Thickness(0, 5, 0, 0);
            refresh.Click += async delegate { await RefreshAllMetadataAsync(); };
            actions.Children.Add(refresh);
            Grid.SetColumn(actions, 2);
            lower.Children.Add(actions);
            root.Children.Add(lower);
            return root;
        }

        private UIElement BuildAppearanceSettingsContent()
        {
            var left = new StackPanel();
            left.Children.Add(MakeSettingsSectionLabel("COLOR"));
            left.Children.Add(MakeCompactSettingsValue("Theme", new[] { "Light" }, appSettings.AppearanceMode, value => appSettings.AppearanceMode = value));
            left.Children.Add(MakeCompactSettingsValue("Accent", new[] { "Icy blue", "Sky blue", "Frost blue" }, AccentName(appSettings.AccentColor), value => ApplyAccent(AccentFromName(value))));
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(76, 5, 0, 0) };
            foreach (var color in new[] { "#75CFFF", "#7DD3FC", "#8BD8FF" })
            {
                swatches.Children.Add(new Border { Width = 18, Height = 6, CornerRadius = new CornerRadius(3), Background = Brush(color), Margin = new Thickness(0, 0, 5, 0) });
            }
            left.Children.Add(swatches);

            var right = new StackPanel();
            right.Children.Add(MakeSettingsSectionLabel("MOTION"));
            right.Children.Add(MakeCompactSettingsCheck("Animations", appSettings.AnimationsEnabled, value => appSettings.AnimationsEnabled = value));
            right.Children.Add(MakeCompactSettingsCheck("Reduce motion", appSettings.ReduceMotion, value => appSettings.ReduceMotion = value));
            right.Children.Add(new TextBlock { Text = "Soft depth and restrained blue highlights keep the compact Figma character.", FontFamily = interFont, FontSize = 8, Foreground = Brush("#8A7E7A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
            return MakeSettingsColumns(left, right);
        }

        private Button MakeCompactSettingsCheck(string label, bool current, Action<bool> changed)
        {
            var state = current;
            var button = new Button
            {
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontFamily = interFont,
                FontSize = 8.5,
                Foreground = Brush("#6F625E"),
                Margin = new Thickness(0, 1, 0, 2),
                Cursor = Cursors.Hand,
                Template = MakeRoundedButtonTemplate(4)
            };
            button.Content = BuildSettingsToggleContent(label, state);
            button.Click += delegate
            {
                state = !state;
                button.Content = BuildSettingsToggleContent(label, state);
                if (changed != null) changed(state);
                SaveSettings();
            };
            return button;
        }

        private UIElement BuildSettingsToggleContent(string label, bool state)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var thumb = new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = state ? Brushes.White : Brush("#9E9490"),
                HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(2)
            };
            row.Children.Add(new Border
            {
                Width = 22,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = state ? accentBrush : Brush("#E7E0DC"),
                Child = thumb,
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = interFont,
                FontSize = 8.5,
                Foreground = Brush("#6F625E"),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        private UIElement MakeCompactSettingsValue(string label, IEnumerable<string> values, string selectedValue, Action<string> changed)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(MakeSettingsLabel(label));
            var options = values.ToList();
            var selectedIndex = Math.Max(0, options.FindIndex(value => String.Equals(value, selectedValue, StringComparison.OrdinalIgnoreCase)));
            var selector = new Button
            {
                Height = 21,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(7, 1, 6, 1),
                FontFamily = interFont,
                FontSize = 8.5,
                BorderBrush = Brush("#18A7DDF7"),
                BorderThickness = new Thickness(1),
                Background = Brush("#9FFFFFFF"),
                Foreground = Brush("#6F625E"),
                Cursor = options.Count > 1 ? Cursors.Hand : Cursors.Arrow,
                Template = MakeRoundedButtonTemplate(6),
                ToolTip = options.Count > 1 ? "Click to change" : null
            };
            selector.Content = BuildSettingsChoiceContent(options.Count == 0 ? String.Empty : options[selectedIndex]);
            selector.Click += delegate
            {
                if (options.Count == 0)
                {
                    return;
                }
                selectedIndex = (selectedIndex + 1) % options.Count;
                selector.Content = BuildSettingsChoiceContent(options[selectedIndex]);
                if (changed != null) changed(options[selectedIndex]);
                SaveSettings();
            };
            Grid.SetColumn(selector, 1);
            row.Children.Add(selector);
            return row;
        }

        private UIElement BuildSettingsChoiceContent(string value)
        {
            var row = new DockPanel { LastChildFill = false };
            var text = new TextBlock
            {
                Text = value,
                FontFamily = interFont,
                FontSize = 8.5,
                Foreground = Brush("#6F625E"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Width = 102,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(text, Dock.Left);
            row.Children.Add(text);
            var arrow = MakeChevronIcon(false) as FrameworkElement;
            arrow.Width = 8;
            arrow.Height = 8;
            DockPanel.SetDock(arrow, Dock.Right);
            row.Children.Add(arrow);
            return row;
        }

        private UIElement MakeCompactVolumeRow()
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(MakeSettingsLabel("Volume"));
            var sliderShell = new Grid { Width = 136, Height = 18, HorizontalAlignment = HorizontalAlignment.Left };
            var track = new Border { Width = 136, Height = 4, CornerRadius = new CornerRadius(2), Background = Brush("#EFE8E8"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var fill = new Border { Width = 136 * appSettings.Volume, Height = 4, CornerRadius = new CornerRadius(2), Background = accentBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var thumb = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = accentBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Math.Max(0, 127 * appSettings.Volume), 0, 0, 0) };
            var slider = new Slider { Minimum = 0, Maximum = 1, Value = appSettings.Volume, Width = 136, Height = 18, Opacity = 0.01, Cursor = Cursors.Hand };
            slider.ValueChanged += delegate
            {
                appSettings.Volume = slider.Value;
                fill.Width = 136 * slider.Value;
                thumb.Margin = new Thickness(Math.Max(0, 127 * slider.Value), 0, 0, 0);
                if (media != null) media.Volume = slider.Value;
                SaveSettings();
            };
            sliderShell.Children.Add(track);
            sliderShell.Children.Add(fill);
            sliderShell.Children.Add(thumb);
            sliderShell.Children.Add(slider);
            Grid.SetColumn(sliderShell, 1);
            row.Children.Add(sliderShell);
            return row;
        }

        private TextBlock MakeSettingsSectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = interFont,
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#4D9FC4"),
                Margin = new Thickness(0, 1, 0, 4)
            };
        }

        private TextBlock MakeSettingsLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = interFont,
                FontSize = 9,
                Foreground = Brush("#741A1111"),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private UIElement MakeSettingsCheck(string label, bool current, Action<bool> changed)
        {
            var check = new CheckBox
            {
                Content = label,
                IsChecked = current,
                FontFamily = interFont,
                FontSize = 9,
                Foreground = Brush("#741A1111"),
                Margin = new Thickness(105, 2, 0, 2)
            };
            check.Checked += delegate
            {
                if (changed != null) changed(true);
                SaveSettings();
            };
            check.Unchecked += delegate
            {
                if (changed != null) changed(false);
                SaveSettings();
            };
            return check;
        }

        private UIElement MakeSettingsValue(string label, IEnumerable<string> values, string selectedValue, Action<string> changed)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.Children.Add(MakeSettingsLabel(label));
            var combo = new ComboBox
            {
                Height = 23,
                FontFamily = interFont,
                FontSize = 9,
                Padding = new Thickness(5, 0, 5, 0),
                BorderBrush = Brush("#DED4CF"),
                Background = Brushes.White,
                ItemsSource = values.ToList(),
                SelectedItem = selectedValue
            };
            if (combo.SelectedIndex < 0 && combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
            combo.IsEnabled = combo.Items.Count > 1 || changed != null;
            combo.SelectionChanged += delegate
            {
                if (combo.SelectedItem != null && changed != null)
                {
                    changed(combo.SelectedItem.ToString());
                    SaveSettings();
                }
            };
            Grid.SetColumn(combo, 1);
            row.Children.Add(combo);
            return row;
        }

        private UIElement MakeVolumeSettingsRow()
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.Children.Add(MakeSettingsLabel("Volume"));
            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                Value = appSettings.Volume,
                Height = 20,
                Foreground = accentBrush
            };
            slider.ValueChanged += delegate
            {
                appSettings.Volume = slider.Value;
                if (media != null) media.Volume = slider.Value;
                SaveSettings();
            };
            Grid.SetColumn(slider, 1);
            row.Children.Add(slider);
            return row;
        }

        private UIElement BuildKoboView()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(29) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var accountRow = new Grid();
            accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            koboAccountStatusText = new TextBlock
            {
                Text = KoboAccountStatus(),
                FontFamily = interFont,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#1A1111"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            accountRow.Children.Add(koboAccountStatusText);
            var connect = MakeCompactActionButton(koboClient == null ? "Connect" : "Sync", true);
            connect.Click += delegate { ConnectKobo(); };
            Grid.SetColumn(connect, 1);
            accountRow.Children.Add(connect);
            var disconnect = MakeCompactActionButton("Disconnect", false);
            disconnect.Margin = new Thickness(6, 0, 0, 0);
            disconnect.IsEnabled = koboSession != null;
            disconnect.Click += delegate { DisconnectKobo(); };
            Grid.SetColumn(disconnect, 2);
            accountRow.Children.Add(disconnect);
            importKoboButton = MakeCompactActionButton("Import selected", false);
            importKoboButton.Margin = new Thickness(6, 0, 0, 0);
            importKoboButton.IsEnabled = false;
            importKoboButton.Click += async delegate { await ImportSelectedKoboBookAsync(); };
            Grid.SetColumn(importKoboButton, 3);
            accountRow.Children.Add(importKoboButton);
            root.Children.Add(accountRow);

            var activityRow = new Grid();
            activityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            activityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            koboActivationCodeText = new TextBlock
            {
                Text = pendingKoboActivation == null ? "Tokens are encrypted with Windows user protection." : "Activation code: " + pendingKoboActivation.Code + " • visit kobo.com/activate",
                FontFamily = interFont,
                FontSize = 8.5,
                Foreground = Brush("#8A7E7A"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            activityRow.Children.Add(koboActivationCodeText);
            completeActivationButton = MakeCompactActionButton("I entered the code", false);
            completeActivationButton.Visibility = pendingKoboActivation == null ? Visibility.Collapsed : Visibility.Visible;
            completeActivationButton.Click += async delegate { await CompleteKoboActivationAsync(); };
            Grid.SetColumn(completeActivationButton, 1);
            activityRow.Children.Add(completeActivationButton);
            Grid.SetRow(activityRow, 1);
            root.Children.Add(activityRow);

            var libraryGrid = new Grid();
            libraryGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            libraryGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            remoteKoboList = new ListBox
            {
                ItemsSource = remoteKoboBooks,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ItemTemplate = BuildRemoteKoboCoverTemplate(),
                ItemContainerStyle = BuildShelfItemStyle(),
                SelectionMode = SelectionMode.Single
            };
            var panelFactory = new FrameworkElementFactory(typeof(StackPanel));
            panelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            remoteKoboList.ItemsPanel = new ItemsPanelTemplate(panelFactory);
            ScrollViewer.SetHorizontalScrollBarVisibility(remoteKoboList, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(remoteKoboList, ScrollBarVisibility.Disabled);
            remoteKoboList.SelectionChanged += delegate { if (importKoboButton != null) importKoboButton.IsEnabled = remoteKoboList.SelectedItem != null; };
            libraryGrid.Children.Add(remoteKoboList);
            var downloadRow = new Grid();
            downloadRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            downloadRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            koboDownloadText = new TextBlock
            {
                Text = remoteKoboBooks.Count == 0 ? "Connect or sync to browse your Kobo audiobooks." : remoteKoboBooks.Count + " Kobo audiobooks",
                FontFamily = interFont,
                FontSize = 8.5,
                Foreground = Brush("#8A7E7A"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            downloadRow.Children.Add(koboDownloadText);
            koboDownloadProgress = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Height = 5,
                Foreground = accentBrush,
                Background = Brush("#E7E0DC"),
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(koboDownloadProgress, 1);
            downloadRow.Children.Add(koboDownloadProgress);
            Grid.SetRow(downloadRow, 1);
            libraryGrid.Children.Add(downloadRow);
            Grid.SetRow(libraryGrid, 2);
            root.Children.Add(libraryGrid);
            return root;
        }

        private DataTemplate BuildRemoteKoboCoverTemplate()
        {
            var template = new DataTemplate(typeof(KoboRemoteBook));
            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.WidthProperty, 66.0);
            var cover = new FrameworkElementFactory(typeof(Border));
            cover.SetValue(Border.WidthProperty, 66.0);
            cover.SetValue(Border.HeightProperty, 88.0);
            cover.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            cover.SetValue(Border.BackgroundProperty, Brush("#DDF3FC"));
            cover.SetValue(Border.ClipToBoundsProperty, true);
            var image = new FrameworkElementFactory(typeof(Image));
            image.SetBinding(Image.SourceProperty, new Binding("CoverUrl"));
            image.SetValue(Image.StretchProperty, Stretch.UniformToFill);
            cover.AppendChild(image);
            stack.AppendChild(cover);
            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new Binding("Title"));
            title.SetValue(TextBlock.WidthProperty, 66.0);
            title.SetValue(TextBlock.FontFamilyProperty, interFont);
            title.SetValue(TextBlock.FontSizeProperty, 8.0);
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            title.SetValue(TextBlock.MarginProperty, new Thickness(0, 3, 0, 0));
            stack.AppendChild(title);
            var author = new FrameworkElementFactory(typeof(TextBlock));
            author.SetBinding(TextBlock.TextProperty, new Binding("Author"));
            author.SetValue(TextBlock.WidthProperty, 66.0);
            author.SetValue(TextBlock.FontFamilyProperty, interFont);
            author.SetValue(TextBlock.FontSizeProperty, 7.0);
            author.SetValue(TextBlock.ForegroundProperty, Brush("#8A7E7A"));
            author.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            stack.AppendChild(author);
            template.VisualTree = stack;
            return template;
        }

        private string KoboAccountStatus()
        {
            if (koboSession == null || String.IsNullOrWhiteSpace(koboSession.AccessToken))
            {
                return "Kobo account not connected";
            }
            return String.IsNullOrWhiteSpace(koboSession.Email) ? "Kobo account connected" : "Connected as " + koboSession.Email;
        }

        private void SaveSettings()
        {
            try
            {
                AppSettingsStore.Save(settingsFile, appSettings);
            }
            catch
            {
                if (statusText != null)
                {
                    statusText.Text = "Kapla could not save settings.";
                }
            }
        }

        private void ApplyLaunchAtStartup()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key == null)
                    {
                        return;
                    }
                    if (appSettings.LaunchAtStartup)
                    {
                        key.SetValue("Kapla", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                    }
                    else
                    {
                        key.DeleteValue("Kapla", false);
                    }
                }
            }
            catch
            {
                appSettings.LaunchAtStartup = false;
                if (statusText != null)
                {
                    statusText.Text = "Windows did not allow the startup setting to change.";
                }
            }
        }

        private void SaveLibraryFoldersFromText()
        {
            if (libraryFoldersTextBox == null)
            {
                return;
            }
            appSettings.LibraryFolders = libraryFoldersTextBox.Text
                .Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => Environment.ExpandEnvironmentVariables(value.Trim()))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            SaveSettings();
        }

        private async Task RescanLibraryFoldersAsync()
        {
            SaveLibraryFoldersFromText();
            statusText.Text = "Scanning audiobook folders…";
            var folders = appSettings.LibraryFolders.Where(Directory.Exists).ToList();
            var paths = await Task.Run(delegate
            {
                var found = new List<string>();
                foreach (var folder in folders)
                {
                    try
                    {
                        found.AddRange(Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                            .Where(IsSupportedAudiobookPath));
                    }
                    catch
                    {
                        // Continue with accessible folders.
                    }
                }
                return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            });
            var added = 0;
            foreach (var path in paths)
            {
                if (allBooks.Any(book => String.Equals(book.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                var metadata = await Task.Run(() => LocalAudiobookMetadata.Read(path));
                var newBook = CreateLocalBook(path, metadata);
                allBooks.Add(newBook);
                added++;
            }
            SaveLibrary();
            RefreshVisibleBooks();
            ShowExpandedView("library");
            statusText.Text = added == 0 ? "Library scan complete; no new audiobooks." : "Added " + added + (added == 1 ? " audiobook." : " audiobooks.");
        }

        private async Task RefreshAllMetadataAsync()
        {
            var localBooks = allBooks.Where(book => !IsKoboBook(book) && File.Exists(book.Path)).ToList();
            statusText.Text = "Refreshing embedded metadata and cover art…";
            var changed = 0;
            foreach (var book in localBooks)
            {
                var updated = await Task.Run(() => RefreshLocalBookMetadata(book, true));
                if (updated)
                {
                    changed++;
                }
            }
            SaveLibrary();
            RefreshVisibleBooks();
            if (currentBook != null)
            {
                UpdateBookDetails();
                LoadCover();
            }
            ShowExpandedView("library");
            statusText.Text = "Refreshed metadata for " + changed + (changed == 1 ? " audiobook." : " audiobooks.");
        }

        private BookEntry CreateLocalBook(string fullPath, LocalAudiobookInfo metadata)
        {
            var title = String.IsNullOrWhiteSpace(metadata.Title) ? Path.GetFileNameWithoutExtension(fullPath) : metadata.Title;
            return new BookEntry
            {
                Path = fullPath,
                Title = title,
                Author = String.IsNullOrWhiteSpace(metadata.Author) ? "Unknown author" : metadata.Author,
                Album = metadata.Album,
                CoverPath = SaveLocalCover(metadata),
                DurationSeconds = metadata.DurationSeconds,
                Chapters = metadata.Chapters,
                Tracks = new List<KoboTrack>
                {
                    new KoboTrack { Path = fullPath, Title = title, DurationSeconds = metadata.DurationSeconds }
                }
            };
        }

        private static bool IsSupportedAudiobookPath(string path)
        {
            var extension = Path.GetExtension(path);
            return String.Equals(extension, ".m4b", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".m4a", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".aac", StringComparison.OrdinalIgnoreCase);
        }

        private static int ParseLeadingInt(string value, int fallback)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }
            var digits = new string(value.TakeWhile(Char.IsDigit).ToArray());
            int parsed;
            return Int32.TryParse(digits, out parsed) && parsed > 0 ? parsed : fallback;
        }

        private static string FormatSpeed(double value)
        {
            return value.ToString("0.0#", CultureInfo.InvariantCulture) + "x";
        }

        private static double ParseSpeed(string value)
        {
            double parsed;
            return Double.TryParse((value ?? String.Empty).TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 1.0;
        }

        private void SetSpeedSelection(double speed)
        {
            if (speedBox == null)
            {
                return;
            }
            var options = new[] { 0.75, 1.0, 1.25, 1.5, 2.0 };
            var index = Enumerable.Range(0, options.Length).OrderBy(value => Math.Abs(options[value] - speed)).First();
            speedBox.SelectedIndex = index;
            ApplySpeed();
        }

        private void UpdateSkipButtonLabels()
        {
            if (rewindButton != null)
            {
                var replacement = MakeTransportButton("skip-back-15.svg", String.Empty, appSettings.RewindSeconds);
                var content = replacement.Content;
                replacement.Content = null;
                rewindButton.Content = content;
                rewindButton.ToolTip = "Skip back " + appSettings.RewindSeconds + " seconds";
            }
            if (forwardButton != null)
            {
                var replacement = MakeTransportButton("skip-forward-15.svg", String.Empty, appSettings.ForwardSeconds);
                var content = replacement.Content;
                replacement.Content = null;
                forwardButton.Content = content;
                forwardButton.ToolTip = "Skip forward " + appSettings.ForwardSeconds + " seconds";
            }
        }

        private static string AccentName(string color)
        {
            if (String.Equals(color, "#75CFFF", StringComparison.OrdinalIgnoreCase)) return "Sky blue";
            if (String.Equals(color, "#8BD8FF", StringComparison.OrdinalIgnoreCase)) return "Frost blue";
            return "Icy blue";
        }

        private static string AccentFromName(string name)
        {
            if (name == "Sky blue") return "#75CFFF";
            if (name == "Frost blue") return "#8BD8FF";
            return "#7DD3FC";
        }

        private void ApplyAccent(string color)
        {
            appSettings.AccentColor = color;
            accentBrush = Brush(color);
            accentSoftBrush = WithOpacity(accentBrush.Color, 0.14);
            SvgIconFactory.AccentColor = accentBrush.Color;
            if (progressFill != null) progressFill.Background = accentBrush;
            if (progressThumb != null) progressThumb.Background = accentBrush;
            if (playerStateText != null) playerStateText.Foreground = Brush("#4D9FC4");
            if (playButton != null && playButton.Content is Border) ((Border)playButton.Content).Background = BuildPlayButtonBrush();
            if (speedButton != null) speedButton.Content = MakeSpeedContent(speedBox == null || speedBox.SelectedItem == null ? "1.0x" : speedBox.SelectedItem.ToString());
            if (koboDownloadProgress != null) koboDownloadProgress.Foreground = accentBrush;
            if (brandIcon != null) brandIcon.Background = BuildBrandBrush();
            Icon = CreateApplicationIcon();
            UpdatePinVisual();
            SetPanelTabState(libraryTabButton, expandedView == "library");
            SetPanelTabState(settingsTabButton, expandedView == "settings");
            SetPanelTabState(koboTabButton, expandedView == "kobo");
            SaveSettings();
        }

        private Brush BuildPlayButtonBrush()
        {
            var lighter = Color.FromRgb(
                (byte)Math.Min(255, accentBrush.Color.R + 20),
                (byte)Math.Min(255, accentBrush.Color.G + 20),
                (byte)Math.Min(255, accentBrush.Color.B + 20));
            return new LinearGradientBrush(lighter, accentBrush.Color, new Point(0.2, 0), new Point(0.8, 1));
        }

        private static SolidColorBrush WithOpacity(Color color, double opacity)
        {
            color.A = (byte)Math.Max(0, Math.Min(255, Math.Round(opacity * 255)));
            return new SolidColorBrush(color);
        }

        private void UpdatePinVisual()
        {
            if (pinButton == null)
            {
                return;
            }
            var surface = pinButton.Content as Border;
            if (surface != null)
            {
                surface.Background = isPinned ? accentSoftBrush : Brushes.Transparent;
                surface.Child = BuildPinIcon(isPinned);
            }
            pinButton.ToolTip = isPinned ? "Unpin Kapla" : "Keep Kapla on top";
        }

        private Button MakeCompactActionButton(string label, bool primary)
        {
            return new Button
            {
                Content = label,
                Height = 26,
                Padding = new Thickness(11, 3, 11, 3),
                BorderThickness = new Thickness(1),
                BorderBrush = primary ? accentBrush : Brush("#18A7DDF7"),
                Background = primary ? BuildPlayButtonBrush() : Brush("#8FFFFFFF"),
                Foreground = primary ? Brush("#17384A") : Brush("#1A1111"),
                FontFamily = interFont,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Template = MakeRoundedButtonTemplate(7)
            };
        }

        private UIElement BuildFigmaPlayerPanel()
        {
            playerSurface = new Border
            {
                Width = 504,
                Height = 212,
                Background = Brushes.Transparent
            };

            var playerCanvas = new Canvas { Width = 504, Height = 212 };
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

            var controls = new Canvas { Width = 330, Height = 212 };
            Canvas.SetLeft(controls, 174);
            playerCanvas.Children.Add(controls);

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
            chapterTitleText.Width = 235;
            chapterTitleText.Height = 13;
            chapterTitleText.TextTrimming = TextTrimming.CharacterEllipsis;
            Canvas.SetLeft(chapterTitleText, 17);
            Canvas.SetTop(chapterTitleText, 3);
            chapterCanvas.Children.Add(chapterTitleText);

            chapterPreviousButton = MakeChapterButton("chevron-left.svg", "Previous chapter");
            chapterPreviousButton.Click += delegate { MoveChapter(-1); };
            Canvas.SetLeft(chapterPreviousButton, 260);
            chapterCanvas.Children.Add(chapterPreviousButton);
            chapterIndexText = FigmaText("—", 10, FontWeights.SemiBold, Brush("#541A1111"));
            chapterIndexText.Width = 22;
            chapterIndexText.Height = 12;
            chapterIndexText.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(chapterIndexText, 284);
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
            progressSlider.PreviewMouseLeftButtonDown += delegate { isDraggingProgress = true; };
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
            var chapterDots = SvgIconFactory.Load("chapter-progress-dots.svg", 18, 4);
            Canvas.SetLeft(chapterDots, 153.5);
            Canvas.SetTop(chapterDots, 129);
            controls.Children.Add(chapterDots);
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
            Canvas.SetLeft(rewindButton, 60.4);
            Canvas.SetTop(rewindButton, 155);
            controls.Children.Add(rewindButton);

            playButton = MakeFigmaPlayButton();
            playButton.Click += delegate { TogglePlay(); };
            Canvas.SetLeft(playButton, 116.8);
            Canvas.SetTop(playButton, 149);
            controls.Children.Add(playButton);

            forwardButton = MakeTransportButton("skip-forward-15.svg", "Skip forward " + appSettings.ForwardSeconds + " seconds", appSettings.ForwardSeconds);
            forwardButton.Click += delegate { Skip(appSettings.ForwardSeconds); };
            Canvas.SetLeft(forwardButton, 185.2);
            Canvas.SetTop(forwardButton, 155);
            controls.Children.Add(forwardButton);

            bookmarkButton = MakeTransportButton("bookmark.svg", "Bookmark current position", (int?)null);
            bookmarkButton.Click += BookmarkButtonOnClick;
            Canvas.SetLeft(bookmarkButton, 241.6);
            Canvas.SetTop(bookmarkButton, 155);
            controls.Children.Add(bookmarkButton);

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

        private static Button MakeHeaderButton(UIElement icon, string tooltip)
        {
            return new Button
            {
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Content = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(13),
                    Background = Brush("#081A1111"),
                    Child = icon
                }
            };
        }

        private static UIElement MakeChevronIcon(bool up)
        {
            var icon = SvgIconFactory.Load("chevron-up.svg", 12, 12);
            if (!up)
            {
                icon.RenderTransformOrigin = new Point(0.5, 0.5);
                icon.RenderTransform = new RotateTransform(180);
            }
            return icon;
        }

        private static UIElement MakePlusIcon()
        {
            var canvas = new Canvas { Width = 12, Height = 12 };
            var horizontal = new Border { Width = 10, Height = 1.5, Background = Brush("#881A1111"), CornerRadius = new CornerRadius(1) };
            Canvas.SetLeft(horizontal, 1);
            Canvas.SetTop(horizontal, 5.25);
            canvas.Children.Add(horizontal);
            var vertical = new Border { Width = 1.5, Height = 10, Background = Brush("#881A1111"), CornerRadius = new CornerRadius(1) };
            Canvas.SetLeft(vertical, 5.25);
            Canvas.SetTop(vertical, 1);
            canvas.Children.Add(vertical);
            return new Viewbox { Width = 12, Height = 12, Child = canvas };
        }

        private Button MakeMicroHeaderButton(UIElement icon, string tooltip)
        {
            return new Button
            {
                Width = 16,
                Height = 16,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Content = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Background = isPinned ? accentSoftBrush : Brushes.Transparent,
                    Child = icon
                }
            };
        }

        private UIElement BuildPinIcon(bool pinned)
        {
            var canvas = new Canvas { Width = 10, Height = 10 };
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M3.2,1.2 L6.8,1.2 L6.1,3.5 L7.8,5.2 L5.6,5.7 L5,8.8 L4.4,5.7 L2.2,5.2 L3.9,3.5 Z"),
                Fill = pinned ? accentBrush : Brush("#741A1111"),
                Stroke = pinned ? accentBrush : Brush("#741A1111"),
                StrokeThickness = 0.45,
                StrokeLineJoin = PenLineJoin.Round
            };
            canvas.Children.Add(path);
            return new Viewbox { Width = 10, Height = 10, Child = canvas };
        }

        private void TogglePin()
        {
            isPinned = !isPinned;
            Topmost = isPinned;
            appSettings.AlwaysOnTopByDefault = isPinned;
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
            SaveSettings();
        }

        private Button MakeChapterButton(string assetName, string tooltip)
        {
            return new Button
            {
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Content = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = Brush("#081A1111"),
                    Child = SvgIconFactory.Load(assetName, 10, 10)
                }
            };
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
                Content = MakeSpeedContent("1.0x")
            };
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
                    Foreground = Brush("#285D78"),
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
            return new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Content = canvas
            };
        }

        private Button MakeFigmaPlayButton()
        {
            return new Button
            {
                Width = 44,
                Height = 44,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "Play or pause",
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

        private IntPtr WindowMessageHook(IntPtr window, int message, IntPtr wordParameter, IntPtr longParameter, ref bool handled)
        {
            if (message != WindowMessageNonClientHitTest || shellSurface == null || rootLayout == null)
            {
                return IntPtr.Zero;
            }

            var packedPoint = longParameter.ToInt64();
            var screenX = unchecked((short)(packedPoint & 0xFFFF));
            var screenY = unchecked((short)((packedPoint >> 16) & 0xFFFF));
            Point clientPoint;
            try
            {
                clientPoint = PointFromScreen(new Point(screenX, screenY));
            }
            catch (InvalidOperationException)
            {
                return IntPtr.Zero;
            }

            if (!IsPointInsideElement(shellSurface, clientPoint) || IsInteractiveElementAt(clientPoint))
            {
                return new IntPtr(HitTestClient);
            }

            handled = true;
            return new IntPtr(HitTestCaption);
        }

        private bool IsPointInsideElement(FrameworkElement element, Point pointInRoot)
        {
            if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                return false;
            }
            try
            {
                var topLeft = element.TransformToAncestor(rootLayout).Transform(new Point(0, 0));
                return new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight)).Contains(pointInRoot);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private bool IsInteractiveElementAt(Point clientPoint)
        {
            var current = InputHitTest(clientPoint) as DependencyObject;
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
                    return true;
                }
                if (ReferenceEquals(current, rootLayout))
                {
                    break;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
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
            var width = 330 * ratio;
            progressFill.Width = width;
            Canvas.SetLeft(progressThumb, Math.Max(0, Math.Min(320, width - 5)));
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
            times.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            times.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            positionText = MakeTimeText("0:00");
            durationText = MakeTimeText("0:00");
            times.Children.Add(positionText);
            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            dots.Children.Add(MakeTimelineDot(true));
            dots.Children.Add(MakeTimelineDot(false));
            dots.Children.Add(MakeTimelineDot(false));
            Grid.SetColumn(dots, 1);
            times.Children.Add(dots);
            Grid.SetColumn(durationText, 2);
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

            var bookmark = MakeGlyphButton("♡", "Bookmark chapter");
            bookmark.Margin = new Thickness(19, 0, 17, 0);
            Grid.SetColumn(bookmark, 4);
            controls.Children.Add(bookmark);

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

        private static Border MakeTimelineDot(bool active)
        {
            return new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = active ? Brush("#7DD3FC") : Brush("#DCEEF6"),
                Margin = new Thickness(2, 0, 2, 0)
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

        private static Border MakeDot()
        {
            return new Border
            {
                Width = 11,
                Height = 11,
                CornerRadius = new CornerRadius(6),
                Background = Brush("#DDD8D5"),
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

        private void LoadWindowPosition()
        {
            try
            {
                if (!appSettings.RememberWindowPosition)
                {
                    return;
                }
                if (!File.Exists(windowPositionFile))
                {
                    return;
                }
                var lines = File.ReadAllLines(windowPositionFile);
                double left;
                double top;
                if (lines.Length < 2
                    || !Double.TryParse(lines[0], NumberStyles.Float, CultureInfo.InvariantCulture, out left)
                    || !Double.TryParse(lines[1], NumberStyles.Float, CultureInfo.InvariantCulture, out top))
                {
                    return;
                }
                var visibleHorizontally = left + 64 >= SystemParameters.VirtualScreenLeft
                    && left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 64;
                var visibleVertically = top + 36 >= SystemParameters.VirtualScreenTop
                    && top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 36;
                if (!visibleHorizontally || !visibleVertically)
                {
                    return;
                }
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
            catch
            {
                // A stale position must never prevent Kapla from opening.
            }
        }

        private void SaveWindowPosition()
        {
            try
            {
                if (!appSettings.RememberWindowPosition)
                {
                    return;
                }
                Directory.CreateDirectory(dataDirectory);
                var collapsedTop = libraryExpanded ? Top + (ExpandedWindowHeight - CollapsedWindowHeight) : Top;
                File.WriteAllLines(windowPositionFile, new[]
                {
                    Left.ToString("R", CultureInfo.InvariantCulture),
                    collapsedTop.ToString("R", CultureInfo.InvariantCulture)
                });
            }
            catch
            {
                // Window position is a convenience and should not interrupt closing.
            }
        }

        private void ShowAddMenu(Button anchor)
        {
            EnsureExpanded("add");
        }

        private void ImportLocalAudiobook()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Add an audiobook to Kapla",
                Filter = "Audiobooks (*.m4b;*.m4a;*.mp3;*.aac)|*.m4b;*.m4a;*.mp3;*.aac|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var fullPath = Path.GetFullPath(dialog.FileName);
            var existing = allBooks.FirstOrDefault(book => String.Equals(book.Path, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                var localMetadata = LocalAudiobookMetadata.Read(fullPath);
                var coverPath = SaveLocalCover(localMetadata);
                existing = new BookEntry
                {
                    Path = fullPath,
                    Title = String.IsNullOrWhiteSpace(localMetadata.Title) ? Path.GetFileNameWithoutExtension(fullPath) : localMetadata.Title,
                    Author = String.IsNullOrWhiteSpace(localMetadata.Author) ? "Unknown author" : localMetadata.Author,
                    Album = localMetadata.Album,
                    CoverPath = coverPath,
                    DurationSeconds = localMetadata.DurationSeconds,
                    Chapters = localMetadata.Chapters,
                    Tracks = new List<KoboTrack>
                    {
                        new KoboTrack { Path = fullPath, Title = Path.GetFileNameWithoutExtension(fullPath) }
                    }
                };
                allBooks.Add(existing);
            }
            RefreshVisibleBooks();
            SaveLibrary();
            if (!libraryExpanded)
            {
                ToggleLibraryExpanded();
            }
            libraryList.SelectedItem = existing;
            statusText.Text = "Added “" + existing.Title + "”.";
        }

        private bool RefreshLocalBookMetadata(BookEntry book, bool force)
        {
            if (book == null || IsKoboBook(book) || String.IsNullOrWhiteSpace(book.Path) || !File.Exists(book.Path))
            {
                return false;
            }
            var missingCover = String.IsNullOrWhiteSpace(book.CoverPath) || !File.Exists(book.CoverPath);
            var needsRefresh = force
                || missingCover
                || String.IsNullOrWhiteSpace(book.Author)
                || String.Equals(book.Author, "Unknown author", StringComparison.OrdinalIgnoreCase)
                || String.IsNullOrWhiteSpace(book.Album)
                || book.DurationSeconds <= 0
                || book.Chapters == null
                || book.Chapters.Count == 0;
            if (!needsRefresh)
            {
                return false;
            }
            var metadata = LocalAudiobookMetadata.Read(book.Path);
            if (!String.IsNullOrWhiteSpace(metadata.Title))
            {
                book.Title = metadata.Title;
            }
            if (!String.IsNullOrWhiteSpace(metadata.Author))
            {
                book.Author = metadata.Author;
            }
            if (!String.IsNullOrWhiteSpace(metadata.Album))
            {
                book.Album = metadata.Album;
            }
            if (metadata.DurationSeconds > 0)
            {
                book.DurationSeconds = metadata.DurationSeconds;
            }
            if (metadata.Chapters != null && metadata.Chapters.Count > 0)
            {
                book.Chapters = metadata.Chapters;
            }
            if (metadata.CoverBytes != null && metadata.CoverBytes.Length > 0 && (force || missingCover))
            {
                book.CoverPath = SaveLocalCover(metadata);
            }
            if (book.Tracks == null || book.Tracks.Count == 0)
            {
                book.Tracks = new List<KoboTrack>
                {
                    new KoboTrack { Path = book.Path, Title = book.Title, DurationSeconds = book.DurationSeconds }
                };
            }
            else if (book.Tracks.Count == 1)
            {
                book.Tracks[0].Title = book.Title;
                if (metadata.DurationSeconds > 0)
                {
                    book.Tracks[0].DurationSeconds = metadata.DurationSeconds;
                }
            }
            return true;
        }

        private string SaveLocalCover(LocalAudiobookInfo metadata)
        {
            if (metadata == null || metadata.CoverBytes == null || metadata.CoverBytes.Length == 0)
            {
                return null;
            }
            try
            {
                var coverDirectory = Path.Combine(dataDirectory, "covers");
                Directory.CreateDirectory(coverDirectory);
                var extension = metadata.CoverExtension == ".png" ? ".png" : ".jpg";
                string key;
                using (var sha = SHA256.Create())
                {
                    key = String.Concat(sha.ComputeHash(metadata.CoverBytes)
                        .Take(12)
                        .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
                }
                var path = Path.Combine(coverDirectory, key + extension);
                if (!File.Exists(path))
                {
                    File.WriteAllBytes(path, metadata.CoverBytes);
                }
                return path;
            }
            catch
            {
                return null;
            }
        }

        private void BookmarkButtonOnClick(object sender, RoutedEventArgs e)
        {
            if (currentBook == null)
            {
                return;
            }
            if (currentBook.Bookmarks == null)
            {
                currentBook.Bookmarks = new List<double>();
            }
            var position = sourceLoaded
                ? currentTrackStartSeconds + media.Position.TotalSeconds
                : currentBook.PositionSeconds;
            currentBook.Bookmarks.Add(Math.Max(0, position));
            currentBook.Bookmarks = currentBook.Bookmarks.OrderBy(value => value).ToList();
            SaveLibrary();
            bookmarkButton.ToolTip = "Bookmarked " + FormatTime(position) + " (" + currentBook.Bookmarks.Count + " total)";
            statusText.Text = "Bookmark saved at " + FormatTime(position) + ".";
        }

        private void SleepTimerButtonOnClick(object sender, RoutedEventArgs e)
        {
            if (sleepTimerEndUtc.HasValue)
            {
                sleepTimerEndUtc = null;
                sleepTimerButton.ToolTip = "Sleep timer";
                statusText.Text = "Sleep timer turned off.";
                return;
            }
            var minutes = Math.Max(1, appSettings.DefaultSleepMinutes);
            sleepTimerEndUtc = DateTime.UtcNow.AddMinutes(minutes);
            sleepTimerButton.ToolTip = "Sleep timer: " + minutes + " minutes • click to cancel";
            statusText.Text = "Sleep timer set for " + minutes + " minutes.";
        }

        private void ShowFigmaPreviewState()
        {
            playerStateText.Text = "ULYSSES";
            titleText.Text = "Joe Speedboot";
            authorText.Text = "Tommy Wieringa";
            chapterRow.Visibility = Visibility.Visible;
            chapterTitleText.Text = "Chapter 3: The Arrival";
            chapterIndexText.Text = "3/18";
            chapterPreviousButton.IsEnabled = true;
            chapterNextButton.IsEnabled = true;
            progressSlider.Maximum = 100;
            progressSlider.Value = 25;
            positionText.Text = "01:23:45";
            durationText.Text = "-04:12:30";
            speedBox.SelectedIndex = 1;
            UpdateProgressVisual();
            var previewCover = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Figma", "artwork.png");
            if (File.Exists(previewCover))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(previewCover, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                coverBorder.Child = BuildCoverImage(bitmap);
            }
        }

        private void ShowLocalMetadataPreview(string path)
        {
            var metadata = LocalAudiobookMetadata.Read(path);
            var coverPath = SaveLocalCover(metadata);
            currentBook = new BookEntry
            {
                Path = path,
                Title = String.IsNullOrWhiteSpace(metadata.Title) ? Path.GetFileNameWithoutExtension(path) : metadata.Title,
                Author = String.IsNullOrWhiteSpace(metadata.Author) ? "Unknown author" : metadata.Author,
                Album = metadata.Album,
                CoverPath = coverPath,
                DurationSeconds = metadata.DurationSeconds,
                Chapters = metadata.Chapters,
                Tracks = new List<KoboTrack>
                {
                    new KoboTrack
                    {
                        Path = path,
                        Title = String.IsNullOrWhiteSpace(metadata.Title) ? Path.GetFileNameWithoutExtension(path) : metadata.Title,
                        DurationSeconds = metadata.DurationSeconds
                    }
                }
            };
            previewBook = currentBook;
            if (!visibleBooks.Contains(currentBook))
            {
                visibleBooks.Insert(0, currentBook);
            }
            if (libraryList != null)
            {
                libraryList.SelectedItem = currentBook;
            }
            PreparePlaybackTracks();
            UpdateBookDetails();
            if (metadata.CoverBytes != null && metadata.CoverBytes.Length > 0)
            {
                using (var stream = new MemoryStream(metadata.CoverBytes))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    coverBorder.Child = BuildCoverImage(bitmap);
                }
            }
            LoadSource(false);
        }

        private void LoadLibrary()
        {
            var metadataChanged = false;
            try
            {
                if (File.Exists(libraryFile))
                {
                    var serializer = new DataContractJsonSerializer(typeof(LibraryStore));
                    using (var stream = File.OpenRead(libraryFile))
                    {
                        var store = serializer.ReadObject(stream) as LibraryStore;
                        if (store != null && store.Books != null)
                        {
                            foreach (var book in store.Books.Where(IsUsableBook))
                            {
                                if (book.Tracks == null)
                                {
                                    book.Tracks = new List<KoboTrack>();
                                }
                                if (book.Chapters == null)
                                {
                                    book.Chapters = new List<KoboChapter>();
                                }
                                if (book.Bookmarks == null)
                                {
                                    book.Bookmarks = new List<double>();
                                }
                                metadataChanged = RefreshLocalBookMetadata(book, false) || metadataChanged;
                                allBooks.Add(book);
                            }
                        }
                    }
                }
            }
            catch
            {
                statusText.Text = "Your saved library could not be read; you can add books again.";
            }

            RefreshVisibleBooks();
            if (metadataChanged)
            {
                SaveLibrary();
            }
            if (visibleBooks.Count > 0)
            {
                var resumeBook = appSettings.ResumeLastAudiobook && !String.IsNullOrWhiteSpace(appSettings.LastBookPath)
                    ? visibleBooks.FirstOrDefault(book => String.Equals(book.Path, appSettings.LastBookPath, StringComparison.OrdinalIgnoreCase))
                    : null;
                libraryList.SelectedItem = resumeBook ?? visibleBooks[0];
            }
        }

        private void LoadKoboSession()
        {
            koboSession = KoboSessionStore.Load(dataDirectory);
            if (koboSession == null || String.IsNullOrWhiteSpace(koboSession.AccessToken))
            {
                return;
            }

            koboClient = new KoboClient(koboSession);
            connectKoboButton.ToolTip = "Add more Kobo audiobooks";
            syncText.Text = "Kobo account connected";
            syncDetailText.Text = "Your encrypted local session can browse Kobo titles and sync positions for linked imports.";
        }

        private async void ConnectKobo()
        {
            EnsureExpanded("kobo");
            if (connectKoboButton != null)
            {
                connectKoboButton.IsEnabled = false;
            }
            statusText.Text = "Connecting to Kobo…";

            try
            {
                if (koboClient == null || koboSession == null || String.IsNullOrWhiteSpace(koboSession.AccessToken))
                {
                    koboSession = KoboClient.CreateNewSession();
                    koboClient = new KoboClient(koboSession);
                    pendingKoboActivation = await koboClient.BeginActivationAsync();
                    try
                    {
                        Clipboard.SetText(pendingKoboActivation.Code);
                    }
                    catch
                    {
                        // Clipboard access is a convenience only.
                    }
                    statusText.Text = "Activation code copied. Visit kobo.com/activate, then confirm here.";
                    ShowExpandedView("kobo");
                    return;
                }
                await SyncKoboLibraryAsync();
            }
            catch (Exception ex)
            {
                statusText.Text = "Kobo connection failed: " + DescribeKoboError(ex);
                if (koboAccountStatusText != null)
                {
                    koboAccountStatusText.Text = "Kobo connection needs attention";
                }
            }
            finally
            {
                if (connectKoboButton != null)
                {
                    connectKoboButton.IsEnabled = true;
                }
            }
        }

        private async Task CompleteKoboActivationAsync()
        {
            if (pendingKoboActivation == null || koboClient == null)
            {
                return;
            }
            if (completeActivationButton != null)
            {
                completeActivationButton.IsEnabled = false;
            }
            statusText.Text = "Waiting for Kobo to confirm this device…";
            try
            {
                await koboClient.CompleteActivationAsync(pendingKoboActivation);
                KoboSessionStore.Save(dataDirectory, koboSession);
                pendingKoboActivation = null;
                statusText.Text = "Kobo connected. Loading your audiobooks…";
                await SyncKoboLibraryAsync();
            }
            catch (Exception ex)
            {
                statusText.Text = "Kobo activation failed: " + DescribeKoboError(ex);
            }
            finally
            {
                ShowExpandedView("kobo");
            }
        }

        private async Task SyncKoboLibraryAsync()
        {
            if (koboClient == null || koboSession == null || String.IsNullOrWhiteSpace(koboSession.AccessToken))
            {
                return;
            }
            statusText.Text = "Syncing your Kobo audiobook library…";
            var books = await koboClient.GetAudiobooksAsync();
            KoboSessionStore.Save(dataDirectory, koboSession);
            UpdateLinkedKoboDetails(books);
            remoteKoboBooks.Clear();
            foreach (var book in books)
            {
                remoteKoboBooks.Add(book);
            }
            statusText.Text = books.Count == 0 ? "Kobo returned no audiobook titles." : "Kobo library synced: " + books.Count + (books.Count == 1 ? " title." : " titles.");
            ShowExpandedView("kobo");
        }

        private async Task ImportSelectedKoboBookAsync()
        {
            var selected = remoteKoboList == null ? null : remoteKoboList.SelectedItem as KoboRemoteBook;
            if (selected == null || koboClient == null)
            {
                return;
            }
            if (importKoboButton != null)
            {
                importKoboButton.IsEnabled = false;
            }
            try
            {
                if (koboDownloadProgress != null)
                {
                    koboDownloadProgress.Visibility = Visibility.Visible;
                    koboDownloadProgress.Value = 0;
                }
                var reporter = new Progress<KoboDownloadProgress>(UpdateIntegratedDownloadProgress);
                await ImportKoboBookAsync(selected, reporter);
                statusText.Text = "Imported “" + selected.Title + "”.";
                ShowExpandedView("library");
            }
            catch (Exception ex)
            {
                statusText.Text = "Import failed: " + DescribeKoboError(ex);
            }
            finally
            {
                if (importKoboButton != null)
                {
                    importKoboButton.IsEnabled = true;
                }
            }
        }

        private void UpdateIntegratedDownloadProgress(KoboDownloadProgress value)
        {
            if (koboDownloadProgress != null)
            {
                koboDownloadProgress.Visibility = Visibility.Visible;
                koboDownloadProgress.Value = Math.Max(0, Math.Min(100, value.Percent));
            }
            if (koboDownloadText != null)
            {
                var track = value.TotalTracks > 0 && value.CurrentTrack > 0
                    ? " • track " + value.CurrentTrack + "/" + value.TotalTracks
                    : String.Empty;
                koboDownloadText.Text = value.Percent + "% • " + value.Stage + track;
            }
            statusText.Text = (String.IsNullOrWhiteSpace(value.Title) ? "Kobo audiobook" : value.Title) + " • " + value.Percent + "%";
        }

        private void DisconnectKobo()
        {
            if (koboClient != null)
            {
                koboClient.Dispose();
            }
            koboClient = null;
            koboSession = null;
            pendingKoboActivation = null;
            remoteKoboBooks.Clear();
            KoboSessionStore.Clear(dataDirectory);
            statusText.Text = "Kobo account data cleared from this PC.";
            ShowExpandedView("kobo");
        }

        private async Task ImportKoboBookAsync(KoboRemoteBook remoteBook, IProgress<KoboDownloadProgress> progress)
        {
            var result = await koboClient.DownloadKoboAudiobookAsync(remoteBook, dataDirectory, progress);
            var existing = allBooks.FirstOrDefault(book => String.Equals(book.KoboRevisionId, remoteBook.RevisionId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Path = result.OutputPath;
                existing.CoverPath = result.CoverPath;
                existing.CoverUrl = result.CoverUrl;
                existing.Author = KoboMetadata.PreferAuthor(result.Author, existing.Author);
                existing.Narrator = result.Narrator;
                existing.Series = result.Series;
                existing.Publisher = result.Publisher;
                existing.ReleaseDate = result.ReleaseDate;
                existing.Description = result.Description;
                existing.Tracks = result.Tracks;
                existing.Chapters = result.Chapters;
                existing.KoboProgressPercent = remoteBook.ProgressPercent;
                existing.Finished = false;
            }
            else
            {
                allBooks.Add(new BookEntry
                {
                    Path = result.OutputPath,
                    Title = remoteBook.Title,
                    Author = KoboMetadata.PreferAuthor(result.Author, remoteBook.Author),
                    Narrator = result.Narrator,
                    Series = result.Series,
                    Publisher = result.Publisher,
                    ReleaseDate = result.ReleaseDate,
                    Description = result.Description,
                    Tracks = result.Tracks,
                    Chapters = result.Chapters,
                    KoboRevisionId = remoteBook.RevisionId,
                    KoboProductId = remoteBook.ProductId,
                    KoboProgressPercent = remoteBook.ProgressPercent,
                    CoverPath = result.CoverPath,
                    CoverUrl = result.CoverUrl
                });
            }
            RefreshVisibleBooks();
            SaveLibrary();
            statusText.Text = "Imported “" + remoteBook.Title + "” from Kobo.";
            var imported = allBooks.FirstOrDefault(book => String.Equals(book.KoboRevisionId, remoteBook.RevisionId, StringComparison.OrdinalIgnoreCase));
            if (imported != null)
            {
                libraryList.SelectedItem = imported;
            }
        }

        private void UpdateLinkedKoboDetails(IList<KoboRemoteBook> remoteBooks)
        {
            var changed = false;
            foreach (var remote in remoteBooks)
            {
                var linked = allBooks.FirstOrDefault(book => String.Equals(book.KoboRevisionId, remote.RevisionId, StringComparison.OrdinalIgnoreCase));
                if (linked == null)
                {
                    continue;
                }

                linked.Author = KoboMetadata.PreferAuthor(remote.Author, linked.Author);
                linked.Narrator = remote.Narrator;
                linked.Series = remote.Series;
                linked.Publisher = remote.Publisher;
                linked.ReleaseDate = remote.ReleaseDate;
                linked.Description = remote.Description;
                linked.CoverUrl = remote.CoverUrl;
                linked.KoboProgressPercent = remote.ProgressPercent;
                changed = true;
            }
            if (changed)
            {
                SaveLibrary();
                RefreshVisibleBooks();
                if (currentBook != null)
                {
                    UpdateBookDetails();
                    LoadCover();
                }
            }
        }

        private static bool IsUsableBook(BookEntry book)
        {
            return book != null && !String.IsNullOrWhiteSpace(book.Path) && File.Exists(book.Path);
        }

        private static bool IsKoboBook(BookEntry book)
        {
            return book != null
                && !String.IsNullOrWhiteSpace(book.KoboRevisionId)
                && !String.IsNullOrWhiteSpace(book.KoboProductId);
        }

        private void SaveLibrary()
        {
            try
            {
                Directory.CreateDirectory(dataDirectory);
                var serializer = new DataContractJsonSerializer(typeof(LibraryStore));
                using (var stream = File.Create(libraryFile))
                {
                    serializer.WriteObject(stream, new LibraryStore { Books = allBooks });
                }
                lastSaveUtc = DateTime.UtcNow;
            }
            catch
            {
                statusText.Text = "Could not save the current position.";
            }
        }

        private void RefreshVisibleBooks()
        {
            var query = searchBox == null ? String.Empty : searchBox.Text.Trim();
            visibleBooks.Clear();
            var sourceBooks = new List<BookEntry>(allBooks);
            if (previewBook != null)
            {
                sourceBooks.RemoveAll(book => String.Equals(book.Path, previewBook.Path, StringComparison.OrdinalIgnoreCase));
                sourceBooks.Insert(0, previewBook);
            }
            var filtered = sourceBooks
                .Where(IsUsableBook)
                .Where(b => query.Length == 0 || (b.Title + " " + b.Author + " " + b.Album).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            IEnumerable<BookEntry> ordered;
            if (String.Equals(appSettings.LibrarySort, "Title", StringComparison.OrdinalIgnoreCase))
            {
                ordered = filtered.OrderBy(book => book.Title, StringComparer.OrdinalIgnoreCase);
            }
            else if (String.Equals(appSettings.LibrarySort, "Author", StringComparison.OrdinalIgnoreCase))
            {
                ordered = filtered.OrderBy(book => book.Author, StringComparer.OrdinalIgnoreCase).ThenBy(book => book.Title, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                ordered = filtered.OrderByDescending(book => book.LastPlayedUtc).ThenBy(book => book.Title, StringComparer.OrdinalIgnoreCase);
            }
            foreach (var book in ordered)
            {
                visibleBooks.Add(book);
            }

            if (libraryList != null && currentBook != null && visibleBooks.Contains(currentBook))
            {
                libraryList.SelectedItem = currentBook;
            }

            if (libraryCount != null)
            {
                libraryCount.Text = visibleBooks.Count + (visibleBooks.Count == 1 ? " audiobook" : " audiobooks");
            }
            if (statusText != null && allBooks.Count > 0)
            {
                statusText.Text = "Positions are saved automatically on this PC.";
            }
        }

        private void LibraryListOnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = libraryList.SelectedItem as BookEntry;
            if (selected == null || selected == currentBook)
            {
                return;
            }

            SaveCurrentPosition();
            currentBook = selected;
            appSettings.LastBookPath = currentBook.Path;
            SaveSettings();
            PreparePlaybackTracks();
            sourceLoaded = false;
            media.Stop();
            media.Source = null;
            UpdateBookDetails();
            LoadCover();
            LoadSource(false);
        }

        private void PreparePlaybackTracks()
        {
            playbackTracks = GetPlaybackTracks(currentBook);
            if (currentBook.Chapters == null || currentBook.Chapters.Count == 0)
            {
                currentBook.Chapters = BuildTrackChapters(playbackTracks);
            }
            UpdateTotalDuration();
            if (appSettings.AutoResume && currentBook.PositionSeconds <= 0 && currentBook.KoboProgressPercent > 0 && currentBook.DurationSeconds > 0)
            {
                currentBook.PositionSeconds = currentBook.DurationSeconds * currentBook.KoboProgressPercent / 100.0;
            }

            var startPosition = appSettings.AutoResume ? currentBook.PositionSeconds : 0;
            currentTrackIndex = FindTrackForPosition(startPosition);
            if (currentBook.Finished)
            {
                currentTrackIndex = 0;
            }
            currentTrackStartSeconds = GetTrackStartSeconds(currentTrackIndex);
            pendingTrackPositionSeconds = currentBook.Finished
                ? 0
                : Math.Max(0, startPosition - currentTrackStartSeconds);
        }

        private static List<KoboChapter> BuildTrackChapters(List<KoboTrack> tracks)
        {
            var chapters = new List<KoboChapter>();
            var offset = 0.0;
            for (var index = 0; index < tracks.Count; index++)
            {
                var duration = Math.Max(0, tracks[index].DurationSeconds);
                chapters.Add(new KoboChapter
                {
                    Title = String.IsNullOrWhiteSpace(tracks[index].Title) ? "Chapter " + (index + 1) : tracks[index].Title,
                    StartSeconds = offset,
                    EndSeconds = offset + duration
                });
                offset += duration;
            }
            return chapters;
        }

        private static List<KoboTrack> GetPlaybackTracks(BookEntry book)
        {
            var tracks = book.Tracks == null
                ? new List<KoboTrack>()
                : book.Tracks.Where(track => track != null && !String.IsNullOrWhiteSpace(track.Path) && File.Exists(track.Path)).ToList();
            if (tracks.Count > 0)
            {
                return tracks;
            }

            var directory = String.IsNullOrWhiteSpace(book.Path) ? null : Path.GetDirectoryName(book.Path);
            if (!String.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                var numbered = Directory.GetFiles(directory, "*.mp3")
                    .Select(path => new { Path = path, Name = Path.GetFileNameWithoutExtension(path) })
                    .Select(value => new { value.Path, Number = ParseTrackNumber(value.Name) })
                    .Where(value => value.Number.HasValue)
                    .OrderBy(value => value.Number.Value)
                    .Select(value => new KoboTrack
                    {
                        Path = value.Path,
                        DurationSeconds = EstimateTrackDuration(value.Path),
                        Title = "Chapter " + (value.Number.Value + 1)
                    })
                    .ToList();
                if (numbered.Count > 0)
                {
                    return numbered;
                }
            }

            return new List<KoboTrack>
            {
                new KoboTrack
                {
                    Path = book.Path,
                    DurationSeconds = book.DurationSeconds,
                    Title = book.Title
                }
            };
        }

        private static int? ParseTrackNumber(string value)
        {
            int number;
            return Int32.TryParse(value, out number) && number >= 0 && number < 10000 ? (int?)number : null;
        }

        private static double EstimateTrackDuration(string path)
        {
            try
            {
                return new FileInfo(path).Length * 8.0 / 96000.0;
            }
            catch
            {
                return 0;
            }
        }

        private void UpdateTotalDuration()
        {
            if (currentBook == null || playbackTracks == null || playbackTracks.Count == 0)
            {
                return;
            }

            var total = PlaybackTimeline.TotalDuration(playbackTracks);
            if (total > 0)
            {
                currentBook.DurationSeconds = total;
                if (currentBook.PositionSeconds > total)
                {
                    currentBook.PositionSeconds = total;
                }
                if (currentBook.Chapters != null && currentBook.Chapters.Count == playbackTracks.Count)
                {
                    // Track durations become authoritative once MediaElement opens a
                    // file. Keep the chapter timeline aligned while preserving titles.
                    PlaybackTimeline.AlignChapters(currentBook.Chapters, playbackTracks);
                }
            }
        }

        private double GetTrackStartSeconds(int trackIndex)
        {
            if (playbackTracks == null)
            {
                return 0;
            }
            return PlaybackTimeline.TrackStart(playbackTracks, trackIndex);
        }

        private int FindTrackForPosition(double position)
        {
            return PlaybackTimeline.FindTrack(playbackTracks, position);
        }

        private void UpdateChapterSelection(double position)
        {
            if (chapterBox == null || currentBook == null)
            {
                return;
            }

            var chapters = currentBook.Chapters ?? new List<KoboChapter>();
            updatingChapter = true;
            if (!Object.ReferenceEquals(chapterBox.ItemsSource, chapters))
            {
                chapterBox.ItemsSource = chapters;
            }
            chapterRow.Visibility = chapters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            chapterBox.IsEnabled = chapters.Count > 0;
            var selectedIndex = -1;
            for (var index = 0; index < chapters.Count; index++)
            {
                var end = chapters[index].EndSeconds > chapters[index].StartSeconds ? chapters[index].EndSeconds : Double.MaxValue;
                if (position >= chapters[index].StartSeconds && position < end)
                {
                    selectedIndex = index;
                    break;
                }
            }
            if (chapterBox.SelectedIndex != selectedIndex)
            {
                chapterBox.SelectedIndex = selectedIndex;
            }
            if (chapterIndexText != null)
            {
                chapterIndexText.Text = selectedIndex >= 0
                    ? (selectedIndex + 1) + "/" + chapters.Count
                    : chapters.Count > 0 ? "—/" + chapters.Count : "—";
            }
            if (chapterTitleText != null)
            {
                chapterTitleText.Text = selectedIndex >= 0
                    ? (String.IsNullOrWhiteSpace(chapters[selectedIndex].Title) ? "Chapter " + (selectedIndex + 1) : chapters[selectedIndex].Title)
                    : "Choose a chapter";
            }
            if (chapterPreviousButton != null)
            {
                chapterPreviousButton.IsEnabled = selectedIndex > 0;
            }
            if (chapterNextButton != null)
            {
                chapterNextButton.IsEnabled = selectedIndex >= 0 && selectedIndex + 1 < chapters.Count;
            }
            updatingChapter = false;
        }

        private void MoveChapter(int direction)
        {
            if (currentBook == null || currentBook.Chapters == null || currentBook.Chapters.Count == 0)
            {
                return;
            }

            var index = chapterBox == null ? 0 : chapterBox.SelectedIndex;
            if (index < 0)
            {
                index = 0;
            }
            var next = Math.Max(0, Math.Min(currentBook.Chapters.Count - 1, index + direction));
            if (playbackTracks != null && playbackTracks.Count == currentBook.Chapters.Count && next < playbackTracks.Count)
            {
                // Kobo's downloaded files are one track per chapter. Target the track
                // boundary directly so corrected media durations cannot make a stale
                // chapter timestamp land in the preceding track.
                SeekToGlobal(GetTrackStartSeconds(next), isPlaying);
                return;
            }
            var chapter = currentBook.Chapters[next];
            SeekToGlobal(chapter.StartSeconds, isPlaying);
        }

        private void ChapterBoxOnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updatingChapter || chapterBox == null)
            {
                return;
            }

            var chapter = chapterBox.SelectedItem as KoboChapter;
            if (chapter != null)
            {
                SeekToGlobal(chapter.StartSeconds, isPlaying);
            }
        }

        private void UpdateBookDetails()
        {
            if (currentBook == null)
            {
                titleText.Text = "Choose an audiobook";
                authorText.Text = "Your selected book will appear here";
                metadataText.Text = String.Empty;
                descriptionText.Text = String.Empty;
                playerStateText.Text = "READY WHEN YOU ARE";
                syncText.Text = "Kobo account";
                syncDetailText.Text = "Connect Kobo to browse your audiobook library.";
                chapterBox.ItemsSource = null;
                chapterBox.IsEnabled = false;
                chapterRow.Visibility = Visibility.Collapsed;
                chapterTitleText.Text = "Choose a chapter";
                if (chapterIndexText != null)
                {
                    chapterIndexText.Text = "—";
                }
                if (chapterPreviousButton != null)
                {
                    chapterPreviousButton.IsEnabled = false;
                }
                if (chapterNextButton != null)
                {
                    chapterNextButton.IsEnabled = false;
                }
                positionText.Text = "0:00";
                durationText.Text = "-0:00";
                progressSlider.Maximum = 1;
                progressSlider.Value = 0;
                UpdateProgressVisual();
                UpdatePlayButtonVisual();
                return;
            }

            titleText.Text = currentBook.Title;
            authorText.Text = currentBook.Author;
            var metadata = new List<string>();
            if (!String.IsNullOrWhiteSpace(currentBook.Narrator))
            {
                metadata.Add("Narrated by " + currentBook.Narrator);
            }
            if (!String.IsNullOrWhiteSpace(currentBook.Series))
            {
                metadata.Add(currentBook.Series);
            }
            if (!String.IsNullOrWhiteSpace(currentBook.Publisher))
            {
                metadata.Add(currentBook.Publisher);
            }
            if (!String.IsNullOrWhiteSpace(currentBook.ReleaseDate))
            {
                metadata.Add(currentBook.ReleaseDate);
            }
            metadataText.Text = String.Join("  •  ", metadata);
            descriptionText.Text = currentBook.Description ?? String.Empty;
            metadataText.Visibility = String.IsNullOrWhiteSpace(metadataText.Text) ? Visibility.Collapsed : Visibility.Visible;
            descriptionText.Visibility = String.IsNullOrWhiteSpace(descriptionText.Text) ? Visibility.Collapsed : Visibility.Visible;
            var collectionLabel = !String.IsNullOrWhiteSpace(currentBook.Series)
                ? currentBook.Series
                : !String.IsNullOrWhiteSpace(currentBook.Album) ? currentBook.Album
                : !String.IsNullOrWhiteSpace(currentBook.Publisher) ? currentBook.Publisher : "KOBO AUDIOBOOK";
            playerStateText.Text = collectionLabel.ToUpperInvariant();
            if (!String.IsNullOrWhiteSpace(currentBook.KoboRevisionId) && koboClient != null)
            {
                syncText.Text = "Kobo linked • " + FormatTime(currentBook.PositionSeconds);
                syncDetailText.Text = "Progress is sent to Kobo as a percentage when the account accepts the beta sync endpoint.";
            }
            else
            {
                syncText.Text = "Kobo audiobook";
                syncDetailText.Text = "Connect Kobo to refresh account progress and audiobook details.";
            }
            positionText.Text = FormatTime(currentBook.PositionSeconds);
            durationText.Text = FormatRemaining(currentBook.DurationSeconds - currentBook.PositionSeconds);
            progressSlider.Maximum = Math.Max(1, currentBook.DurationSeconds);
            progressSlider.Value = Math.Min(progressSlider.Maximum, Math.Max(0, currentBook.PositionSeconds));
            UpdateChapterSelection(currentBook.PositionSeconds);
            UpdateProgressVisual();
            UpdatePlayButtonVisual();
        }

        private void LoadCover()
        {
            coverBorder.Child = BuildEmptyArtwork();

            var coverSource = currentBook == null
                ? null
                : File.Exists(currentBook.CoverPath) ? currentBook.CoverPath : currentBook.CoverUrl;
            if (String.IsNullOrWhiteSpace(coverSource))
            {
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(coverSource, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                coverBorder.Child = BuildCoverImage(bitmap);
            }
            catch
            {
                // Keep the Figma-consistent book placeholder when cover art is unsupported.
            }
        }

        private void ResetCoverPalette()
        {
            SetPalette(Color.FromRgb(125, 211, 252));
        }

        private void ApplyCoverPalette(BitmapSource source)
        {
            if (source == null)
            {
                return;
            }

            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var width = Math.Max(1, converted.PixelWidth);
            var height = Math.Max(1, converted.PixelHeight);
            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
            var bestScore = 0.0;
            var selected = Color.FromRgb(125, 211, 252);
            for (var y = 0; y < height; y += Math.Max(1, height / 40))
            {
                for (var x = 0; x < width; x += Math.Max(1, width / 40))
                {
                    var offset = y * stride + x * 4;
                    var red = pixels[offset + 2];
                    var green = pixels[offset + 1];
                    var blue = pixels[offset];
                    var maximum = Math.Max(red, Math.Max(green, blue)) / 255.0;
                    var minimum = Math.Min(red, Math.Min(green, blue)) / 255.0;
                    var saturation = maximum <= 0 ? 0 : (maximum - minimum) / maximum;
                    var brightness = (maximum + minimum) / 2.0;
                    var score = saturation * (0.35 + (1.0 - Math.Abs(brightness - 0.55)));
                    if (brightness > 0.12 && score > bestScore)
                    {
                        bestScore = score;
                        selected = Color.FromRgb(red, green, blue);
                    }
                }
            }

            if (selected.R * 0.299 + selected.G * 0.587 + selected.B * 0.114 > 190)
            {
                selected = Color.FromRgb((byte)(selected.R * 0.62), (byte)(selected.G * 0.62), (byte)(selected.B * 0.62));
            }
            SetPalette(selected);
        }

        private void SetPalette(Color selected)
        {
            accentBrush = Brush(appSettings.AccentColor);
            Background = Brushes.Transparent;
            if (windowSurface != null)
            {
                windowSurface.Background = Brushes.Transparent;
            }
            rootLayout.Background = Brushes.Transparent;
            librarySurface.Background = Brush("#FDF8F4");
            headerSurface.Background = Brushes.Transparent;
            appCard.Background = Brush("#FDF8F4");
            if (playButton != null)
            {
                var playSurface = playButton.Content as Border;
                if (playSurface != null)
                {
                    playSurface.Background = BuildPlayButtonBrush();
                }
            }
            if (progressSlider != null)
            {
                progressSlider.Foreground = accentBrush;
            }
            if (progressFill != null)
            {
                progressFill.Background = accentBrush;
            }
            if (progressThumb != null)
            {
                progressThumb.Background = accentBrush;
            }
            if (playerStateText != null)
            {
                playerStateText.Foreground = accentBrush;
            }
            if (coverBorder != null)
            {
                coverBorder.Background = Brush("#EFE8E8");
            }
        }

        private static Color Blend(Color color, Color background, double backgroundWeight)
        {
            var foregroundWeight = 1.0 - backgroundWeight;
            return Color.FromRgb(
                (byte)(color.R * foregroundWeight + background.R * backgroundWeight),
                (byte)(color.G * foregroundWeight + background.G * backgroundWeight),
                (byte)(color.B * foregroundWeight + background.B * backgroundWeight));
        }

        private void LoadSource(bool playWhenReady)
        {
            var trackPath = playbackTracks != null && playbackTracks.Count > 0 && currentTrackIndex < playbackTracks.Count
                ? playbackTracks[currentTrackIndex].Path
                : currentBook == null ? null : currentBook.Path;
            if (currentBook == null || String.IsNullOrWhiteSpace(trackPath) || !File.Exists(trackPath))
            {
                return;
            }

            sourceLoaded = false;
            isPlaying = false;
            playWhenSourceReady = playWhenReady;
            media.Source = new Uri(ResolvePlayablePath(trackPath), UriKind.Absolute);
        }

        private void TogglePlay()
        {
            if (currentBook == null)
            {
                statusText.Text = "Choose an audiobook first.";
                return;
            }

            if (!sourceLoaded)
            {
                LoadSource(true);
                return;
            }

            if (isPlaying)
            {
                media.Pause();
                isPlaying = false;
                UpdatePlayButtonVisual();
                SaveCurrentPosition();
            }
            else
            {
                media.Play();
                isPlaying = true;
                UpdatePlayButtonVisual();
            }
        }

        private void PlayCurrent()
        {
            if (currentBook == null)
            {
                return;
            }

            if (!sourceLoaded)
            {
                LoadSource(true);
                return;
            }

            media.Play();
            isPlaying = true;
            UpdatePlayButtonVisual();
        }

        private void Skip(double seconds)
        {
            if (currentBook == null)
            {
                return;
            }

            SeekToGlobal(Math.Max(0, currentBook.PositionSeconds + seconds), isPlaying);
        }

        private void ApplySpeed()
        {
            if (media == null || speedBox == null || speedBox.SelectedIndex < 0)
            {
                return;
            }

            media.SpeedRatio = new[] { 0.75, 1.0, 1.25, 1.5, 2.0 }[speedBox.SelectedIndex];
        }

        private void ProgressSliderOnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateProgressVisual();
            if (!isDraggingProgress)
            {
                return;
            }

            positionText.Text = FormatTime(e.NewValue);
            if (currentBook != null)
            {
                durationText.Text = FormatRemaining(currentBook.DurationSeconds - e.NewValue);
            }
        }

        private void ProgressSliderOnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isDraggingProgress = false;
            SeekToGlobal(progressSlider.Value, isPlaying);
            SaveCurrentPosition();
        }

        private void SeekToGlobal(double target, bool resume)
        {
            if (currentBook == null)
            {
                return;
            }

            var duration = Math.Max(0, currentBook.DurationSeconds);
            target = Math.Max(0, duration > 0 ? Math.Min(duration, target) : target);
            var targetTrack = FindTrackForPosition(target);
            var targetStart = GetTrackStartSeconds(targetTrack);
            var local = Math.Max(0, target - targetStart);
            if (targetTrack == currentTrackIndex && sourceLoaded)
            {
                media.Position = TimeSpan.FromSeconds(local);
                currentBook.PositionSeconds = target;
                currentBook.Finished = false;
                positionText.Text = FormatTime(target);
                progressSlider.Value = target;
                durationText.Text = FormatRemaining(currentBook.DurationSeconds - target);
                UpdateChapterSelection(target);
                SaveCurrentPosition();
                return;
            }

            currentTrackIndex = targetTrack;
            currentTrackStartSeconds = targetStart;
            pendingTrackPositionSeconds = local;
            currentBook.PositionSeconds = target;
            currentBook.Finished = false;
            progressSlider.Value = target;
            positionText.Text = FormatTime(target);
            durationText.Text = FormatRemaining(currentBook.DurationSeconds - target);
            sourceLoaded = false;
            media.Stop();
            media.Source = null;
            LoadSource(resume);
            UpdateChapterSelection(target);
        }

        private void ProgressTimerOnTick(object sender, EventArgs e)
        {
            if (sleepTimerEndUtc.HasValue && DateTime.UtcNow >= sleepTimerEndUtc.Value)
            {
                sleepTimerEndUtc = null;
                if (isPlaying)
                {
                    media.Pause();
                    isPlaying = false;
                    UpdatePlayButtonVisual();
                    SaveCurrentPosition();
                }
                sleepTimerButton.ToolTip = "Sleep timer";
                statusText.Text = "Sleep timer finished.";
            }
            if (currentBook == null || !sourceLoaded || isDraggingProgress)
            {
                return;
            }

            if (currentTrackIndex >= 0 && currentTrackIndex < playbackTracks.Count && media.NaturalDuration.HasTimeSpan)
            {
                playbackTracks[currentTrackIndex].DurationSeconds = media.NaturalDuration.TimeSpan.TotalSeconds;
                UpdateTotalDuration();
                currentTrackStartSeconds = GetTrackStartSeconds(currentTrackIndex);
            }

            var seconds = currentTrackStartSeconds + media.Position.TotalSeconds;
            if (seconds <= 0)
            {
                return;
            }

            currentBook.PositionSeconds = seconds;
            currentBook.LastPlayedUtc = DateTime.UtcNow;
            positionText.Text = FormatTime(seconds);
            if (currentBook.DurationSeconds > 0)
            {
                progressSlider.Maximum = Math.Max(1, currentBook.DurationSeconds);
                progressSlider.Value = Math.Min(progressSlider.Maximum, seconds);
                durationText.Text = FormatRemaining(currentBook.DurationSeconds - seconds);
            }
            UpdateChapterSelection(seconds);

            if (appSettings.RememberPlaybackPosition && (DateTime.UtcNow - lastSaveUtc).TotalSeconds >= 5)
            {
                SaveLibrary();
            }
        }

        private void SaveCurrentPosition()
        {
            if (currentBook == null || media == null || !sourceLoaded || !appSettings.RememberPlaybackPosition)
            {
                return;
            }

            currentBook.PositionSeconds = currentTrackStartSeconds + media.Position.TotalSeconds;
            currentBook.LastPlayedUtc = DateTime.UtcNow;
            SaveLibrary();
            RefreshVisibleBooks();
            ScheduleKoboProgressSync();
        }

        private void ScheduleKoboProgressSync()
        {
            if (koboClient == null || koboSession == null || currentBook == null || String.IsNullOrWhiteSpace(currentBook.KoboRevisionId) || currentBook.DurationSeconds <= 0)
            {
                return;
            }
            if ((DateTime.UtcNow - lastKoboSyncUtc).TotalSeconds < 30)
            {
                return;
            }

            var revisionId = currentBook.KoboRevisionId;
            var position = currentBook.PositionSeconds;
            var duration = currentBook.DurationSeconds;
            lastKoboSyncUtc = DateTime.UtcNow;
            Task.Run(async delegate
            {
                try
                {
                    await koboClient.UpdateProgressAsync(revisionId, position, duration).ConfigureAwait(false);
                    var ignored = Dispatcher.BeginInvoke(new Action(delegate
                    {
                        syncText.Text = "Kobo synced • " + FormatTime(position);
                        KoboSessionStore.Save(dataDirectory, koboSession);
                    }));
                }
                catch
                {
                    var ignored = Dispatcher.BeginInvoke(new Action(delegate
                    {
                        syncText.Text = "Kobo linked • sync pending";
                    }));
                }
            });
        }

        private void MediaOnMediaOpened(object sender, RoutedEventArgs e)
        {
            if (currentBook == null)
            {
                return;
            }
            sourceLoaded = true;
            isPlaying = false;
            ApplySpeed();
            media.Volume = volumeSlider.Value;
            if (currentTrackIndex >= 0 && currentTrackIndex < playbackTracks.Count && media.NaturalDuration.HasTimeSpan)
            {
                playbackTracks[currentTrackIndex].DurationSeconds = media.NaturalDuration.TimeSpan.TotalSeconds;
                UpdateTotalDuration();
                currentTrackStartSeconds = GetTrackStartSeconds(currentTrackIndex);
            }

            var start = Math.Min(
                media.NaturalDuration.HasTimeSpan ? media.NaturalDuration.TimeSpan.TotalSeconds : Double.MaxValue,
                Math.Max(0, pendingTrackPositionSeconds));
            pendingTrackPositionSeconds = 0;
            media.Position = TimeSpan.FromSeconds(start);
            currentBook.PositionSeconds = currentTrackStartSeconds + start;
            currentBook.Finished = false;
            UpdateBookDetails();
            if (playWhenSourceReady)
            {
                media.Play();
                isPlaying = true;
                UpdatePlayButtonVisual();
            }
            playWhenSourceReady = false;
        }

        private void MediaOnMediaEnded(object sender, RoutedEventArgs e)
        {
            if (currentBook == null)
            {
                return;
            }

            if (currentTrackIndex >= 0 && currentTrackIndex < playbackTracks.Count && media.NaturalDuration.HasTimeSpan)
            {
                playbackTracks[currentTrackIndex].DurationSeconds = media.NaturalDuration.TimeSpan.TotalSeconds;
                UpdateTotalDuration();
                currentTrackStartSeconds = GetTrackStartSeconds(currentTrackIndex);
            }

            if (playbackTracks != null && currentTrackIndex + 1 < playbackTracks.Count)
            {
                currentBook.PositionSeconds = currentTrackStartSeconds + playbackTracks[currentTrackIndex].DurationSeconds;
                currentTrackIndex++;
                currentTrackStartSeconds = GetTrackStartSeconds(currentTrackIndex);
                pendingTrackPositionSeconds = 0;
                sourceLoaded = false;
                media.Source = null;
                LoadSource(true);
                return;
            }

            currentBook.PositionSeconds = currentBook.DurationSeconds;
            currentBook.Finished = true;
            isPlaying = false;
            UpdatePlayButtonVisual();
            positionText.Text = FormatTime(currentBook.PositionSeconds);
            durationText.Text = "-0:00";
            progressSlider.Value = progressSlider.Maximum;
            SaveLibrary();
            RefreshVisibleBooks();
            ScheduleKoboProgressSync();
        }

        private void MediaOnMediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            sourceLoaded = false;
            isPlaying = false;
            playWhenSourceReady = false;
            UpdatePlayButtonVisual();
            statusText.Text = "Kapla could not open this audiobook track with the installed Windows audio codecs.";
        }

        private string ResolvePlayablePath(string path)
        {
            if (!String.Equals(Path.GetExtension(path), ".m4b", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
            try
            {
                var linkDirectory = Path.Combine(dataDirectory, "MediaLinks");
                Directory.CreateDirectory(linkDirectory);
                string key;
                using (var sha = SHA256.Create())
                {
                    key = String.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()))
                        .Take(10)
                        .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
                }
                var linkPath = Path.Combine(linkDirectory, key + ".m4a");
                if (File.Exists(linkPath) || CreateHardLink(linkPath, path, IntPtr.Zero))
                {
                    return linkPath;
                }
            }
            catch
            {
                // The original path may still be playable if a hard link is unavailable.
            }
            return path;
        }

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

        private static string FormatTime(double seconds)
        {
            var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
        }

        private static string FormatRemaining(double seconds)
        {
            return "-" + FormatTime(Math.Max(0, seconds));
        }

        private static string DescribeKoboError(Exception exception)
        {
            var root = exception;
            while (root.InnerException != null)
            {
                root = root.InnerException;
            }

            var webException = root as System.Net.WebException;
            if (webException != null)
            {
                if (webException.Status == System.Net.WebExceptionStatus.NameResolutionFailure)
                {
                    return "Windows could not resolve Kobo's server name. Check your internet connection or DNS settings.";
                }
                if (webException.Status == System.Net.WebExceptionStatus.ConnectFailure)
                {
                    return "Windows could not connect to Kobo. Check your firewall, proxy, or VPN settings.";
                }
                if (webException.Status == System.Net.WebExceptionStatus.Timeout)
                {
                    return "Kobo did not respond before the connection timed out.";
                }
            }

            return root.Message;
        }

        private static SolidColorBrush Brush(string color)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(color);
        }
    }
}
