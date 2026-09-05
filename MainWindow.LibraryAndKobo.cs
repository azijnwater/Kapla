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
                var collapsedTop = libraryExpanded ? Top + Math.Max(0, ActualHeight - CollapsedWindowHeight) : Top;
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

        private UIElement BuildSleepTimerView()
        {
            sleepDurationButtons.Clear();
            sleepEndChapterButton = null;

            var root = new Grid { Margin = new Thickness(2, 5, 2, 0) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(FigmaText("Sleep timer", 14, FontWeights.Bold, Brush("#1A1111")));
            sleepRemainingText = FigmaText("Off", 10, FontWeights.SemiBold, accentBrush);
            Grid.SetColumn(sleepRemainingText, 1);
            heading.Children.Add(sleepRemainingText);
            root.Children.Add(heading);

            var description = FigmaText("Pause playback after a set time or when the current chapter ends.", 9.5,
                FontWeights.Normal, Brush("#8A7E7A"));
            description.Margin = new Thickness(0, 4, 0, 10);
            Grid.SetRow(description, 1);
            root.Children.Add(description);

            var presets = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Rows = 2, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var minutes in new[] { 5, 10, 15, 30, 45, 60 })
            {
                var value = minutes;
                var button = MakeSleepDurationButton(value, 38, new Thickness(0, 0, 6, 6));
                button.Click += delegate { StartSleepTimer(value); };
                presets.Children.Add(button);
            }
            sleepEndChapterButton = MakeCompactActionButton("End of chapter", false);
            sleepEndChapterButton.Tag = -1;
            sleepEndChapterButton.Height = 38;
            sleepEndChapterButton.Margin = new Thickness(0, 0, 6, 6);
            sleepEndChapterButton.IsEnabled = CurrentChapterEndSeconds().HasValue;
            sleepEndChapterButton.Click += delegate { StartSleepTimerAtChapterEnd(); };
            presets.Children.Add(sleepEndChapterButton);
            Grid.SetRow(presets, 2);
            root.Children.Add(presets);

            var customArea = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            customArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            customArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            customArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var customMinutes = new TextBox
            {
                Text = appSettings.DefaultSleepMinutes.ToString(CultureInfo.InvariantCulture),
                Width = 70,
                Height = 34,
                Padding = new Thickness(9, 5, 9, 3),
                FontFamily = interFont,
                FontSize = 10,
                Background = Brush("#AFFFFFFF"),
                Foreground = Brush("#1A1111"),
                BorderBrush = Brush("#18A7DDF7"),
                BorderThickness = new Thickness(1),
                ToolTip = "Custom minutes"
            };
            customArea.Children.Add(customMinutes);
            var setCustom = MakeCompactActionButton("Set custom", true);
            setCustom.Width = 116;
            setCustom.Height = 38;
            setCustom.Margin = new Thickness(8, 0, 0, 0);
            setCustom.Click += delegate
            {
                int minutes;
                if (Int32.TryParse(customMinutes.Text, out minutes) && minutes > 0 && minutes <= 1440)
                {
                    StartSleepTimer(minutes);
                }
                else
                {
                    statusText.Text = "Enter a duration from 1 to 1440 minutes.";
                }
            };
            Grid.SetColumn(setCustom, 1);
            customArea.Children.Add(setCustom);
            Grid.SetRow(customArea, 3);
            root.Children.Add(customArea);

            var statusCard = new Border
            {
                Margin = new Thickness(0, 2, 0, 0),
                Padding = new Thickness(14, 11, 14, 11),
                Background = accentSoftBrush,
                BorderBrush = WithOpacity(accentBrush.Color, 0.45),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var statusGrid = new Grid();
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var statusCopy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            statusCopy.Children.Add(FigmaText("Timer status", 9, FontWeights.SemiBold, Brush("#285D78")));
            statusCopy.Children.Add(FigmaText("Your position is saved when playback pauses.", 9.5, FontWeights.Normal, Brush("#6F625E")));
            statusGrid.Children.Add(statusCopy);
            sleepCancelButton = MakeCompactActionButton("Cancel timer", false);
            sleepCancelButton.Width = 128;
            sleepCancelButton.Height = 40;
            sleepCancelButton.Padding = new Thickness(10, 4, 10, 4);
            sleepCancelButton.Template = MakeRoundedButtonTemplate(5);
            sleepCancelButton.IsEnabled = sleepTimer.IsActive;
            sleepCancelButton.Click += delegate { CancelSleepTimer("Sleep timer cancelled."); };
            Grid.SetColumn(sleepCancelButton, 1);
            statusGrid.Children.Add(sleepCancelButton);
            statusCard.Child = statusGrid;
            Grid.SetRow(statusCard, 4);
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(statusCard);
            UpdateSleepTimerUi();
            return root;
        }

        private Button MakeSleepDurationButton(int minutes, double height, Thickness margin)
        {
            var button = MakeCompactActionButton(minutes + " min", false);
            button.Tag = minutes;
            button.Height = height;
            button.Margin = margin;
            sleepDurationButtons.Add(button);
            return button;
        }

        private void SleepTimerButtonOnClick(object sender, RoutedEventArgs e)
        {
            EnsureExpanded("sleep");
        }

        private void StartSleepTimer(int minutes)
        {
            minutes = Math.Max(1, Math.Min(1440, minutes));
            sleepTimer.StartDuration(DateTime.UtcNow, TimeSpan.FromMinutes(minutes));
            appSettings.DefaultSleepMinutes = minutes;
            SaveSettings();
            statusText.Text = "Sleep timer set for " + minutes + " minutes.";
            UpdateSleepTimerUi();
        }

        private void StartSleepTimerAtChapterEnd()
        {
            var end = CurrentChapterEndSeconds();
            var position = CurrentAbsolutePosition();
            if (!end.HasValue || end.Value <= position)
            {
                statusText.Text = "The current chapter has no usable ending.";
                return;
            }
            sleepTimer.StartEndOfChapter(end.Value, position);
            statusText.Text = "Sleep timer set for the end of this chapter.";
            UpdateSleepTimerUi();
        }

        private double? CurrentChapterEndSeconds()
        {
            if (currentBook == null || currentBook.Chapters == null)
            {
                return null;
            }
            var position = CurrentAbsolutePosition();
            var window = PlaybackProgress.Calculate(position, currentBook.DurationSeconds, currentBook.Chapters, PlaybackProgress.ChapterMode);
            return window.IsChapterRelative ? (double?)window.EndSeconds : null;
        }

        private double CurrentAbsolutePosition()
        {
            return currentBook == null ? 0 : sourceLoaded && !applyingResumePosition
                ? GetTrackStartSeconds(currentTrackIndex) + media.Position.TotalSeconds
                : currentBook.PositionSeconds;
        }

        private void CancelSleepTimer(string message)
        {
            sleepTimer.Cancel();
            if (!String.IsNullOrWhiteSpace(message) && statusText != null) statusText.Text = message;
            UpdateSleepTimerUi();
        }

        private void UpdateSleepTimerUi()
        {
            var activeDurationMinutes = sleepTimer.Mode == SleepTimerMode.Duration && sleepTimer.Duration.HasValue
                ? sleepTimer.Duration.Value.TotalMinutes
                : -1;
            foreach (var button in sleepDurationButtons)
            {
                if (button == null)
                {
                    continue;
                }
                var minutes = button.Tag is int ? (int)button.Tag : 0;
                var selected = sleepTimer.IsActive
                    ? activeDurationMinutes > 0 && Math.Abs(activeDurationMinutes - minutes) < 0.01
                    : minutes == appSettings.DefaultSleepMinutes;
                SetSleepTimerChoiceVisual(button, selected);
            }
            if (sleepEndChapterButton != null)
            {
                SetSleepTimerChoiceVisual(sleepEndChapterButton, sleepTimer.Mode == SleepTimerMode.EndOfChapter);
                sleepEndChapterButton.IsEnabled = CurrentChapterEndSeconds().HasValue || sleepTimer.Mode == SleepTimerMode.EndOfChapter;
            }
            if (!sleepTimer.IsActive)
            {
                if (sleepRemainingText != null) sleepRemainingText.Text = "Off";
                if (sleepCancelButton != null) sleepCancelButton.IsEnabled = false;
                UpdateSleepTimerButtonVisual(false, null);
                return;
            }
            var remaining = sleepTimer.Remaining(DateTime.UtcNow, CurrentAbsolutePosition());
            var label = sleepTimer.Mode == SleepTimerMode.EndOfChapter
                ? "End of chapter • " + FormatTime(remaining.TotalSeconds)
                : "Remaining • " + FormatTime(remaining.TotalSeconds);
            if (sleepRemainingText != null) sleepRemainingText.Text = label;
            if (sleepCancelButton != null) sleepCancelButton.IsEnabled = true;
            UpdateSleepTimerButtonVisual(true, label);
        }

        private void SetSleepTimerChoiceVisual(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }
            button.Background = selected
                ? BuildPlayButtonBrush()
                : (IsDarkTheme ? Brush("#202830") : Brush("#8FFFFFFF"));
            button.BorderBrush = selected
                ? accentBrush
                : (IsDarkTheme ? Brush("#44515C") : Brush("#18A7DDF7"));
            button.BorderThickness = selected ? new Thickness(1.5) : new Thickness(1);
            button.Foreground = selected
                ? (IsDarkTheme ? Brush("#E7F7FF") : Brush("#17384A"))
                : (IsDarkTheme ? Brush("#DCE3EA") : Brush("#1A1111"));
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
            syncDetailText.Text = "Your Kobo library and progress sync are ready.";
            SetSyncStatus("Ready", null);
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
                    if (ShowKoboActivationDialog(pendingKoboActivation))
                    {
                        await CompleteKoboActivationAsync();
                    }
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

        private async Task SyncKoboLibraryAsync(bool showKoboView = true)
        {
            if (koboClient == null || koboSession == null || String.IsNullOrWhiteSpace(koboSession.AccessToken))
            {
                return;
            }
            statusText.Text = "Syncing your Kobo audiobook library…";
            var books = await koboClient.GetAudiobooksAsync();
            KoboSessionStore.Save(dataDirectory, koboSession);
            var restored = RestoreCachedKoboBooks(books);
            UpdateLinkedKoboDetails(books);
            remoteKoboBooks.Clear();
            foreach (var book in books)
            {
                remoteKoboBooks.Add(book);
            }
            lastKoboLibraryRefreshUtc = DateTime.UtcNow;
            statusText.Text = books.Count == 0
                ? "Kobo returned no audiobook titles."
                : restored > 0
                    ? "Kobo library synced and " + restored + (restored == 1 ? " existing download was restored." : " existing downloads were restored.")
                    : "Kobo library synced: " + books.Count + (books.Count == 1 ? " title." : " titles.");
            SetSyncStatus("Synced", null);
            if (showKoboView) ShowExpandedView("kobo");
        }

        private int RestoreCachedKoboBooks(IList<KoboRemoteBook> remoteBooks)
        {
            var restored = 0;
            foreach (var remoteBook in remoteBooks)
            {
                if (FindLinkedKoboBook(remoteBook) != null)
                {
                    continue;
                }

                var cached = KoboCachedAudiobook.TryRestore(remoteBook, dataDirectory);
                if (cached == null)
                {
                    continue;
                }

                var duration = cached.Tracks.Sum(track => Math.Max(0, track.DurationSeconds));
                allBooks.Add(new BookEntry
                {
                    Path = cached.OutputPath,
                    Title = remoteBook.Title,
                    Author = KoboMetadata.PreferAuthor(cached.Author, remoteBook.Author),
                    Narrator = cached.Narrator,
                    Series = cached.Series,
                    Publisher = cached.Publisher,
                    ReleaseDate = cached.ReleaseDate,
                    Description = cached.Description,
                    Tracks = cached.Tracks,
                    Chapters = cached.Chapters,
                    KoboRevisionId = remoteBook.RevisionId,
                    KoboEntitlementId = remoteBook.EntitlementId,
                    KoboProductId = remoteBook.ProductId,
                    KoboProgressPercent = remoteBook.ProgressPercent,
                    PositionSeconds = duration * Math.Max(0, Math.Min(100, remoteBook.ProgressPercent)) / 100.0,
                    DurationSeconds = duration,
                    CoverPath = cached.CoverPath,
                    CoverUrl = cached.CoverUrl
                });
                restored++;
            }

            if (restored > 0)
            {
                SaveLibrary();
                RefreshVisibleBooks();
            }
            return restored;
        }

        private async Task ImportSelectedKoboBookAsync()
        {
            var selected = remoteKoboBooks.Where(book => selectedKoboBookIds.Contains(KoboBookKey(book))).ToList();
            if (selected.Count == 0 || koboClient == null)
            {
                return;
            }
            if (importKoboButton != null)
            {
                importKoboButton.IsEnabled = false;
            }
            selectedKoboBookIds.Clear();
            remoteKoboList.SelectedItems.Clear();
            UpdateKoboSelectionControls();
            try
            {
                if (koboDownloadProgress != null)
                {
                    koboDownloadProgress.Visibility = Visibility.Visible;
                    koboDownloadProgress.Value = 0;
                }
                var reporter = new Progress<KoboDownloadProgress>(UpdateIntegratedDownloadProgress);
                var skipped = 0;
                foreach (var book in selected)
                {
                    if (await ImportKoboBookAsync(book, reporter))
                    {
                        skipped++;
                    }
                }
                var importedCount = selected.Count - skipped;
                if (skipped == selected.Count)
                {
                    statusText.Text = selected.Count == 1
                        ? "Already downloaded \"" + selected[0].Title + "\"."
                        : "All selected Kobo audiobooks are already downloaded.";
                }
                else if (skipped > 0)
                {
                    statusText.Text = "Imported " + importedCount + " Kobo audiobook" + (importedCount == 1 ? String.Empty : "s")
                        + "; skipped " + skipped + " already downloaded.";
                }
                else
                {
                    statusText.Text = selected.Count == 1
                        ? "Imported \"" + selected[0].Title + "\"."
                        : "Imported " + selected.Count + " Kobo audiobooks.";
                }
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
                    UpdateKoboSelectionControls();
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
            koboSyncPending = false;
            koboLibrarySyncPending = false;
            remoteKoboBooks.Clear();
            selectedKoboBookIds.Clear();
            KoboSessionStore.Clear(dataDirectory);
            SetSyncStatus("Offline", "Connect Kobo to sync.");
            statusText.Text = "Kobo account data cleared from this PC.";
            ShowExpandedView("kobo");
        }

        private async Task<bool> ImportKoboBookAsync(KoboRemoteBook remoteBook, IProgress<KoboDownloadProgress> progress)
        {
            var existing = FindLinkedKoboBook(remoteBook);
            if (HasCompleteKoboDownload(existing) && IsCurrentKoboRevision(existing, remoteBook))
            {
                if (progress != null)
                {
                    progress.Report(new KoboDownloadProgress
                    {
                        Title = remoteBook.Title,
                        Stage = "Already downloaded",
                        Percent = 100,
                        CurrentTrack = existing.Tracks.Count,
                        TotalTracks = existing.Tracks.Count,
                        Detail = "All tracks are already on this PC"
                    });
                }
                statusText.Text = "Already downloaded \"" + remoteBook.Title + "\".";
                return true;
            }

            var result = await koboClient.DownloadKoboAudiobookAsync(remoteBook, dataDirectory, progress);
            if (existing != null)
            {
                existing.Path = result.OutputPath;
                existing.Title = remoteBook.Title;
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
                existing.KoboRevisionId = remoteBook.RevisionId;
                existing.KoboEntitlementId = remoteBook.EntitlementId;
                existing.KoboProductId = remoteBook.ProductId;
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
                    KoboEntitlementId = remoteBook.EntitlementId,
                    KoboProductId = remoteBook.ProductId,
                    KoboProgressPercent = remoteBook.ProgressPercent,
                    CoverPath = result.CoverPath,
                    CoverUrl = result.CoverUrl
                });
            }
            RefreshVisibleBooks();
            SaveLibrary();
            statusText.Text = "Imported \"" + remoteBook.Title + "\" from Kobo.";
            var imported = FindLinkedKoboBook(remoteBook);
            if (imported != null)
            {
                libraryList.SelectedItem = imported;
            }
            return false;
        }

        private BookEntry FindLinkedKoboBook(KoboRemoteBook remoteBook)
        {
            if (remoteBook == null)
            {
                return null;
            }

            if (!String.IsNullOrWhiteSpace(remoteBook.RevisionId))
            {
                var revisionMatch = allBooks.FirstOrDefault(book => book != null
                    && String.Equals(book.KoboRevisionId, remoteBook.RevisionId, StringComparison.OrdinalIgnoreCase));
                if (revisionMatch != null)
                {
                    return revisionMatch;
                }
            }

            return allBooks.FirstOrDefault(book => book != null
                && !String.IsNullOrWhiteSpace(remoteBook.ProductId)
                && String.Equals(book.KoboProductId, remoteBook.ProductId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsCurrentKoboRevision(BookEntry book, KoboRemoteBook remoteBook)
        {
            return book != null && remoteBook != null
                && (String.IsNullOrWhiteSpace(remoteBook.RevisionId)
                    || String.IsNullOrWhiteSpace(book.KoboRevisionId)
                    || String.Equals(book.KoboRevisionId, remoteBook.RevisionId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasCompleteKoboDownload(BookEntry book)
        {
            if (book == null || book.Tracks == null || book.Tracks.Count == 0)
            {
                return false;
            }

            foreach (var track in book.Tracks)
            {
                if (track == null || String.IsNullOrWhiteSpace(track.Path))
                {
                    return false;
                }

                try
                {
                    if (!File.Exists(track.Path) || new FileInfo(track.Path).Length <= 0)
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }

            return true;
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
                if (!String.IsNullOrWhiteSpace(remote.EntitlementId))
                {
                    linked.KoboEntitlementId = remote.EntitlementId;
                }
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
                // allBooks is maintained in installation order and persisted in that
                // order. Keep the default shelf stable while playback updates
                // LastPlayedUtc, so selecting a book never moves it.
                ordered = filtered;
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
        }

        private void LibraryListOnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = libraryList.SelectedItem as BookEntry;
            if (selected == null || selected == currentBook)
            {
                return;
            }

            SaveCurrentPosition();
            if (sleepTimer.IsActive)
            {
                CancelSleepTimer("Sleep timer cancelled because the audiobook changed.");
            }
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
            if (appSettings.AutoResume && !currentBook.HasLocalPlaybackPosition
                && currentBook.PositionSeconds <= 0 && currentBook.KoboProgressPercent > 0 && currentBook.DurationSeconds > 0)
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
                var isLastBoundary = index == chapters.Count - 1 && Math.Abs(position - end) < 0.001;
                if ((position >= chapters[index].StartSeconds && position < end) || isLastBoundary)
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
            UpdateWindowsMediaMetadata();
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
                currentProgressWindow = null;
                progressSlider.Minimum = 0;
                progressSlider.Maximum = 1;
                progressSlider.Value = 0;
                UpdateProgressVisual();
                UpdatePlayButtonVisual();
                UpdateWindowsMediaMetadata();
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
                syncDetailText.Text = "Your exact local position is sent to Kobo as high-precision progress and listening-time stats.";
            }
            else
            {
                syncText.Text = "Kobo audiobook";
                syncDetailText.Text = "Connect Kobo to refresh account progress and audiobook details.";
            }
            UpdateChapterSelection(currentBook.PositionSeconds);
            UpdateProgressDisplay(currentBook.PositionSeconds);
            UpdatePlayButtonVisual();
            UpdateWindowsMediaMetadata();
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
    }
}
