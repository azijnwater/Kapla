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
        private readonly string dataDirectory;
        private readonly string libraryFile;
        private readonly string windowPositionFile;
        private readonly string settingsFile;
        private readonly List<BookEntry> allBooks = new List<BookEntry>();
        private readonly ObservableCollection<BookEntry> visibleBooks = new ObservableCollection<BookEntry>();
        private readonly ObservableCollection<KoboRemoteBook> remoteKoboBooks = new ObservableCollection<KoboRemoteBook>();
        private readonly HashSet<string> selectedKoboBookIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer progressTimer;
        private readonly DispatcherTimer koboSyncTimer;
        private readonly AppSettings appSettings;
        private readonly SleepTimerState sleepTimer = new SleepTimerState();

        private Grid rootLayout;
        private Grid cardLayout;
        private Canvas headerCanvas;
        private Canvas playerCanvas;
        private Canvas playerControlsCanvas;
        private Border windowSurface;
        private Border shellSurface;
        private Border appCard;
        private Border headerSurface;
        private Border brandIcon;
        private Border librarySurface;
        private ContentControl expandedContentHost;
        private Border playerSurface;
        private Button libraryToggleButton;
        private Button syncButton;
        private Button pinButton;
        private Button minimizeButton;
        private Button closeButton;
        private Button libraryTabButton;
        private Button settingsTabButton;
        private Button koboTabButton;
        private readonly Dictionary<string, Button> settingsCategoryButtons = new Dictionary<string, Button>();
        private Border expandedSyncBadge;
        private Border expandedSyncDot;
        private TextBlock expandedSyncText;
        private ListBox libraryList;
        private ListBox remoteKoboList;
        private System.Windows.Controls.Primitives.ScrollBar koboLibraryScrollBar;
        private ScrollViewer koboLibraryScrollViewer;
        private TextBox koboLibrarySearchBox;
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
        private TextBlock headerSyncText;
        private Viewbox syncIcon;
        private string lastKoboSyncDetail;
        private string currentSyncStatus = "Offline";
        private TextBlock sleepRemainingText;
        private Button sleepCancelButton;
        private readonly List<Button> sleepDurationButtons = new List<Button>();
        private Button sleepEndChapterButton;
        private TextBlock sleepTimerCaption;
        private Button playButton;
        private Button chapterPreviousButton;
        private Button chapterNextButton;
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
        private WindowsMediaControls windowsMediaControls;
        private FontFamily interFont;
        private SolidColorBrush accentBrush = Brush("#7DD3FC");
        private SolidColorBrush accentSoftBrush = Brush("#247DD3FC");

        private BookEntry currentBook;
        private BookEntry previewBook;
        private bool isDraggingProgress;
        private bool sourceLoaded;
        private bool sourceLoadPending;
        private bool applyingResumePosition;
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
        private DateTime lastWindowsTimelineUpdateUtc = DateTime.MinValue;
        private PlaybackProgressWindow currentProgressWindow;
        private DateTime nextKoboSyncAttemptUtc = DateTime.MaxValue;
        private DateTime lastKoboLibraryRefreshUtc = DateTime.MinValue;
        private double lastQueuedKoboPosition = -1;
        private int koboSyncFailures;
        private bool koboSyncPending;
        private bool koboLibrarySyncPending;
        private bool koboSyncInProgress;
        private bool updatingWindowLayout;
        private bool libraryExpanded;
        private bool isPinned;
        private bool closeAfterKoboSync;
        private bool purgingData;
        private string expandedView = "library";
        private string settingsCategory = "General";

        private const double ArtworkWindowWidth = 560;
        private const double CompactWindowWidth = 382;
        private const double CollapsedWindowHeight = 300;
        private const double ExpandedWindowHeight = 636;
        private const double ExpandedPanelHeight = 336;
        private const double ExpandedShellHeight = 636;
        private const string IconAdd = "\uE710";
        private const string IconCheck = "\uE73E";
        private const string IconClose = "\uE8BB";
        private const string IconDownload = "\uE896";
        private const string IconLink = "\uE71B";
        private const string IconRefresh = "\uE72C";
        private const string IconSync = "\uE895";
        private static readonly FontFamily ButtonIconFont = ResolveButtonIconFont();

        private bool IsDarkTheme
        {
            get { return String.Equals(appSettings.AppearanceMode, "Dark", StringComparison.OrdinalIgnoreCase); }
        }

        public MainWindow()
        {
            dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KoboNativePlayer");
            libraryFile = Path.Combine(dataDirectory, "library.json");
            windowPositionFile = Path.Combine(dataDirectory, "window-position.txt");
            settingsFile = Path.Combine(dataDirectory, "settings.json");
            appSettings = AppSettingsStore.Load(settingsFile);
            accentBrush = Brush(IsDarkTheme ? "#55B8F6" : "#7DD3FC");
            accentSoftBrush = WithOpacity(accentBrush.Color, 0.14);
            SvgIconFactory.AccentColor = accentBrush.Color;
            interFont = CreateInterFont();

            Title = "Kapla";
            Width = appSettings.ShowCoverArtwork ? ArtworkWindowWidth : CompactWindowWidth;
            Height = CollapsedWindowHeight;
            MinWidth = Width;
            MaxWidth = Width;
            MinHeight = CollapsedWindowHeight;
            MaxHeight = CollapsedWindowHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Foreground = Brush("#261D1B");
            FontFamily = interFont;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            isPinned = false;
            Topmost = false;
            Icon = CreateApplicationIcon();

            BuildLayout();
            ApplyTheme(false);
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
            Loaded += async delegate
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
                UpdateResponsiveLayout();
                ApplyTheme(false);
                if (koboClient != null)
                {
                    QueueKoboSynchronization(true, true);
                    await ProcessKoboSyncQueueAsync();
                }
            };

            progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            progressTimer.Tick += ProgressTimerOnTick;
            progressTimer.Start();

            koboSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            koboSyncTimer.Tick += async delegate { await ProcessKoboSyncQueueAsync(); };
            koboSyncTimer.Start();

            SizeChanged += delegate { UpdateResponsiveLayout(); };
            Activated += delegate
            {
                if (koboClient != null && (DateTime.UtcNow - lastKoboSyncUtc).TotalMinutes >= 2)
                {
                    QueueKoboSynchronization(true, false);
                }
            };
            NetworkChange.NetworkAvailabilityChanged += NetworkAvailabilityChanged;

            SourceInitialized += MainWindowOnSourceInitialized;
            Closed += MainWindowOnClosed;
            Closing += MainWindowOnClosing;
        }

        private void MainWindowOnSourceInitialized(object sender, EventArgs e)
        {
            windowsMediaControls = WindowsMediaControls.TryCreate(new WindowInteropHelper(this).Handle, Dispatcher);
            windowsMediaControls.PlayRequested += delegate { if (!isPlaying) PlayCurrent(); };
            windowsMediaControls.PauseRequested += PauseCurrent;
            windowsMediaControls.SkipBackRequested += delegate { Skip(-appSettings.RewindSeconds); };
            windowsMediaControls.SkipForwardRequested += delegate { Skip(appSettings.ForwardSeconds); };
            windowsMediaControls.SeekRequested += delegate(double seconds) { SeekToGlobal(seconds, isPlaying); };
            UpdateWindowsMediaMetadata();
            UpdateWindowsMediaPlaybackState();
            UpdateWindowsMediaTimeline();
        }

        private void MainWindowOnClosed(object sender, EventArgs e)
        {
            NetworkChange.NetworkAvailabilityChanged -= NetworkAvailabilityChanged;
            progressTimer.Stop();
            koboSyncTimer.Stop();
            if (windowsMediaControls != null)
            {
                windowsMediaControls.Dispose();
                windowsMediaControls = null;
            }
        }

        private async void MainWindowOnClosing(object sender, CancelEventArgs e)
        {
            if (purgingData)
            {
                if (koboClient != null)
                {
                    koboClient.Dispose();
                    koboClient = null;
                }
                return;
            }
            SaveCurrentPosition();
            SaveLibrary();
            SaveWindowPosition();
            SaveSettings();
            if (koboClient != null && !closeAfterKoboSync)
            {
                e.Cancel = true;
                if (statusText != null) statusText.Text = "Saving your position and syncing Kobo…";
                QueueKoboSynchronization(false, true);
                await ProcessKoboSyncQueueAsync();
                closeAfterKoboSync = true;
                Close();
                return;
            }
            if (koboClient != null)
            {
                koboClient.Dispose();
            }
        }
    }
}
