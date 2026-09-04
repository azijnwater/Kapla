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
            ApplyTheme(false);
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
            sourceLoadPending = true;
            playWhenSourceReady = playWhenReady;
            media.Source = new Uri(ResolvePlayablePath(trackPath), UriKind.Absolute);
            UpdateWindowsMediaPlaybackState();
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
                playWhenSourceReady = true;
                if (!sourceLoadPending)
                {
                    LoadSource(true);
                }
                return;
            }

            if (isPlaying)
            {
                PauseCurrent();
            }
            else
            {
                media.Play();
                isPlaying = true;
                UpdatePlayButtonVisual();
            }
        }

        private void PauseCurrent()
        {
            if (media == null || !isPlaying)
            {
                UpdateWindowsMediaPlaybackState();
                return;
            }
            media.Pause();
            isPlaying = false;
            UpdatePlayButtonVisual();
            SaveCurrentPosition();
            FlushCurrentProgressToKobo();
            UpdateWindowsMediaTimeline();
        }

        private async void FlushCurrentProgressToKobo()
        {
            QueueKoboSynchronization(false, true);
            await ProcessKoboSyncQueueAsync();
        }

        private void PlayCurrent()
        {
            if (currentBook == null)
            {
                return;
            }

            if (!sourceLoaded)
            {
                playWhenSourceReady = true;
                if (!sourceLoadPending)
                {
                    LoadSource(true);
                }
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

            SeekToGlobal(Math.Max(0, CurrentAbsolutePosition() + seconds), isPlaying);
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

            var preview = PlaybackProgress.Calculate(e.NewValue, currentBook == null ? 0 : currentBook.DurationSeconds,
                currentBook == null ? null : currentBook.Chapters, appSettings.ProgressDisplayMode);
            positionText.Text = FormatTime(preview.ElapsedSeconds);
            durationText.Text = FormatRemaining(preview.RemainingSeconds);
        }

        private void ProgressSliderOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var slider = sender as Slider;
            if (slider == null || e.ChangedButton != MouseButton.Left)
            {
                return;
            }
            isDraggingProgress = true;
            SetSliderFromPointer(slider, e.GetPosition(slider));
            slider.CaptureMouse();
            e.Handled = true;
        }

        private void ProgressSliderOnMouseMove(object sender, MouseEventArgs e)
        {
            var slider = sender as Slider;
            if (!isDraggingProgress || slider == null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            SetSliderFromPointer(slider, e.GetPosition(slider));
            e.Handled = true;
        }

        private static void SetSliderFromPointer(Slider slider, Point point)
        {
            var width = Math.Max(1, slider.ActualWidth);
            var ratio = Math.Max(0, Math.Min(1, point.X / width));
            slider.Value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
        }

        private void ProgressSliderOnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var slider = sender as Slider;
            if (slider != null)
            {
                SetSliderFromPointer(slider, e.GetPosition(slider));
                slider.ReleaseMouseCapture();
            }
            isDraggingProgress = false;
            SeekToGlobal(PlaybackProgress.ToAbsolute(progressSlider.Value, currentProgressWindow), isPlaying);
            SaveCurrentPosition();
            e.Handled = true;
        }

        private void UpdateProgressDisplay(double absoluteSeconds)
        {
            if (progressSlider == null || currentBook == null)
            {
                return;
            }
            currentProgressWindow = PlaybackProgress.Calculate(absoluteSeconds, currentBook.DurationSeconds,
                currentBook.Chapters, appSettings.ProgressDisplayMode);
            progressSlider.Minimum = currentProgressWindow.StartSeconds;
            progressSlider.Maximum = Math.Max(currentProgressWindow.StartSeconds + 0.001, currentProgressWindow.EndSeconds);
            progressSlider.Value = Math.Max(progressSlider.Minimum, Math.Min(progressSlider.Maximum, absoluteSeconds));
            positionText.Text = FormatTime(currentProgressWindow.ElapsedSeconds);
            durationText.Text = FormatRemaining(currentProgressWindow.RemainingSeconds);
            UpdateProgressVisual();
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
                currentBook.HasLocalPlaybackPosition = true;
                currentBook.Finished = false;
                UpdateChapterSelection(target);
                UpdateProgressDisplay(target);
                SaveCurrentPosition();
                UpdateWindowsMediaTimeline();
                return;
            }

            currentTrackIndex = targetTrack;
            currentTrackStartSeconds = targetStart;
            pendingTrackPositionSeconds = local;
            currentBook.PositionSeconds = target;
            currentBook.HasLocalPlaybackPosition = true;
            currentBook.Finished = false;
            UpdateChapterSelection(target);
            UpdateProgressDisplay(target);
            // Persist the requested global position before replacing the media
            // source. Loading a different chapter is asynchronous, so waiting
            // for MediaOpened can otherwise lose a seek if the window closes
            // while the new track is still opening.
            SaveLibrary();
            sourceLoaded = false;
            media.Stop();
            media.Source = null;
            LoadSource(resume);
            UpdateWindowsMediaTimeline();
        }

        private void ProgressTimerOnTick(object sender, EventArgs e)
        {
            if (sleepTimer.IsActive && sleepTimer.HasExpired(DateTime.UtcNow, CurrentAbsolutePosition()))
            {
                if (isPlaying)
                {
                    media.Pause();
                    isPlaying = false;
                    UpdatePlayButtonVisual();
                    SaveCurrentPosition();
                }
                CancelSleepTimer("Sleep timer finished.");
            }
            else if (sleepTimer.IsActive)
            {
                UpdateSleepTimerUi();
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

            // Always derive the chapter offset from the current track list. The
            // cached value can be stale while a media source is being replaced;
            // persisting it in that window turns a global position into a
            // chapter-relative position (for example, chapter 10 becomes 0:01).
            currentTrackStartSeconds = GetTrackStartSeconds(currentTrackIndex);
            var seconds = PlaybackTimeline.AbsolutePosition(playbackTracks, currentTrackIndex, media.Position.TotalSeconds);
            if (seconds <= 0)
            {
                return;
            }

            currentBook.PositionSeconds = seconds;
            currentBook.HasLocalPlaybackPosition = true;
            currentBook.LastPlayedUtc = DateTime.UtcNow;
            if ((DateTime.UtcNow - lastWindowsTimelineUpdateUtc).TotalSeconds >= 5)
            {
                UpdateWindowsMediaTimeline();
            }
            if (currentBook.DurationSeconds > 0)
            {
                UpdateChapterSelection(seconds);
                UpdateProgressDisplay(seconds);
            }

            if (appSettings.RememberPlaybackPosition && (DateTime.UtcNow - lastSaveUtc).TotalSeconds >= 5)
            {
                SaveLibrary();
            }
            if (!String.IsNullOrWhiteSpace(currentBook.KoboRevisionId)
                && KoboSyncPolicy.IsMeaningfulProgress(seconds, lastQueuedKoboPosition, 30))
            {
                lastQueuedKoboPosition = seconds;
                QueueKoboSynchronization(false, false);
            }
        }

        private void SaveCurrentPosition()
        {
            if (currentBook == null || media == null || !sourceLoaded || !appSettings.RememberPlaybackPosition)
            {
                return;
            }

            var mediaSeconds = media.Position.TotalSeconds;
            var trackStart = GetTrackStartSeconds(currentTrackIndex);
            if (!Double.IsNaN(mediaSeconds) && !Double.IsInfinity(mediaSeconds) && mediaSeconds >= 0)
            {
                currentTrackStartSeconds = trackStart;
                // A paused MediaElement may report zero for a frame while the
                // book already contains the last position. Keep that position
                // rather than replacing it with the beginning of the track.
                if (mediaSeconds > 0.25 || currentBook.PositionSeconds <= trackStart + 0.25)
                {
                    currentBook.PositionSeconds = PlaybackTimeline.AbsolutePosition(playbackTracks, currentTrackIndex, mediaSeconds);
                }
            }
            currentBook.HasLocalPlaybackPosition = true;
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
            QueueKoboSynchronization(false, false);
        }

        private async void SyncButtonOnClick(object sender, RoutedEventArgs e)
        {
            if (koboClient == null || koboSession == null || String.IsNullOrWhiteSpace(koboSession.AccessToken))
            {
                ConnectKobo();
                return;
            }
            if (koboSyncInProgress)
            {
                return;
            }
            QueueKoboSynchronization(true, true);
            await ProcessKoboSyncQueueAsync();
        }

        private void QueueKoboSynchronization(bool includeLibrary, bool immediate)
        {
            if (koboClient == null || koboSession == null || String.IsNullOrWhiteSpace(koboSession.AccessToken))
            {
                return;
            }
            koboSyncPending = true;
            koboLibrarySyncPending = koboLibrarySyncPending || includeLibrary;
            var requested = immediate ? DateTime.UtcNow : DateTime.UtcNow.AddSeconds(20);
            if (nextKoboSyncAttemptUtc == DateTime.MaxValue || requested < nextKoboSyncAttemptUtc)
            {
                nextKoboSyncAttemptUtc = requested;
            }
            SetSyncStatus(NetworkInterface.GetIsNetworkAvailable() ? "Sync pending" : "Offline", null);
        }

        private async Task ProcessKoboSyncQueueAsync()
        {
            if (koboClient != null && (DateTime.UtcNow - lastKoboLibraryRefreshUtc).TotalMinutes >= 10)
            {
                QueueKoboSynchronization(true, false);
            }
            if (koboSyncInProgress)
            {
                while (koboSyncInProgress)
                {
                    await Task.Delay(50);
                }
            }
            if (!koboSyncPending || DateTime.UtcNow < nextKoboSyncAttemptUtc || koboClient == null || koboSession == null)
            {
                return;
            }
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                SetSyncStatus("Offline", "Progress is saved locally and will retry when the network returns.");
                nextKoboSyncAttemptUtc = DateTime.UtcNow.AddSeconds(15);
                return;
            }

            koboSyncInProgress = true;
            var refreshLibrary = koboLibrarySyncPending;
            koboSyncPending = false;
            koboLibrarySyncPending = false;
            var syncBook = currentBook;
            var position = currentBook == null ? 0 : currentBook.PositionSeconds;
            var duration = currentBook == null ? 0 : currentBook.DurationSeconds;
            SetSyncStatus("Syncing", null);
            try
            {
                if (refreshLibrary)
                {
                    await SyncKoboLibraryAsync(false);
                }
                var progressId = syncBook == null
                    ? null
                    : KoboSyncPolicy.PreferredProgressId(syncBook.KoboEntitlementId, syncBook.KoboRevisionId);
                if (!String.IsNullOrWhiteSpace(progressId) && duration > 0)
                {
                    try
                    {
                        await koboClient.UpdateProgressAsync(progressId, position, duration);
                    }
                    catch (Exception ex)
                    {
                        koboSyncFailures = Math.Min(6, koboSyncFailures + 1);
                        koboSyncPending = true;
                        koboLibrarySyncPending = false;
                        nextKoboSyncAttemptUtc = DateTime.UtcNow.Add(KoboSyncPolicy.RetryDelay(koboSyncFailures));
                        var progressDetail = DescribeKoboError(ex);
                        statusText.Text = (refreshLibrary ? "Kobo library synced, but " : String.Empty)
                            + "progress sync failed: " + progressDetail;
                        SetSyncStatus("Sync issue", progressDetail);
                        return;
                    }
                }
                KoboSessionStore.Save(dataDirectory, koboSession);
                lastKoboSyncUtc = DateTime.UtcNow;
                koboSyncFailures = 0;
                nextKoboSyncAttemptUtc = DateTime.MaxValue;
                SetSyncStatus("Synced", String.IsNullOrWhiteSpace(progressId) ? null : FormatTime(position));
            }
            catch (Exception ex)
            {
                koboSyncFailures = Math.Min(6, koboSyncFailures + 1);
                var delay = KoboSyncPolicy.RetryDelay(koboSyncFailures);
                koboSyncPending = true;
                koboLibrarySyncPending = refreshLibrary;
                nextKoboSyncAttemptUtc = DateTime.UtcNow.Add(delay);
                var detail = DescribeKoboError(ex);
                statusText.Text = "Kobo sync failed: " + detail;
                SetSyncStatus(NetworkInterface.GetIsNetworkAvailable() ? "Sync error" : "Offline", detail);
            }
            finally
            {
                koboSyncInProgress = false;
                var finalStatus = headerSyncText == null || String.IsNullOrWhiteSpace(headerSyncText.Text)
                    ? "Ready"
                    : headerSyncText.Text;
                SetSyncStatus(finalStatus, null);
            }
        }

        private void SetSyncStatus(string status, string detail)
        {
            currentSyncStatus = String.IsNullOrWhiteSpace(status) ? "Offline" : status;
            if (headerSyncText != null) headerSyncText.Text = koboClient == null ? String.Empty : status;
            if (syncText != null) syncText.Text = koboClient == null ? "Kobo account" : "Kobo " + status.ToLowerInvariant();
            if (String.Equals(status, "Synced", StringComparison.OrdinalIgnoreCase))
            {
                lastKoboSyncDetail = null;
                var successDetail = String.IsNullOrWhiteSpace(detail)
                    ? "Kobo library and progress are synced."
                    : "Progress synced at " + detail + ".";
                if (syncDetailText != null) syncDetailText.Text = successDetail;
                if (koboActivationCodeText != null && pendingKoboActivation == null) koboActivationCodeText.Text = successDetail;
            }
            else if (!String.IsNullOrWhiteSpace(detail))
            {
                lastKoboSyncDetail = detail;
                if (syncDetailText != null) syncDetailText.Text = detail;
                if (koboActivationCodeText != null && pendingKoboActivation == null) koboActivationCodeText.Text = detail;
            }
            if (syncButton != null)
            {
                var connected = koboClient != null && koboSession != null && !String.IsNullOrWhiteSpace(koboSession.AccessToken);
                syncButton.IsEnabled = !koboSyncInProgress;
                syncButton.ToolTip = connected
                    ? (String.Equals(status, "Offline", StringComparison.OrdinalIgnoreCase) ? "Kobo sync unavailable offline" : "Sync Kobo now")
                    : "Connect Kobo";
            }
            UpdateSyncIcon(status);
            UpdateExpandedSyncBadge();
        }

        private void UpdateSyncIcon(string status)
        {
            if (syncButton == null)
            {
                return;
            }
            var isError = !String.IsNullOrWhiteSpace(status) && status.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0;
            var color = isError
                ? (Color)ColorConverter.ConvertFromString("#C36B6B")
                : HeaderIconColor(false);
            var fileName = String.Equals(status, "Synced", StringComparison.OrdinalIgnoreCase)
                ? "check-lg.svg"
                : "arrow-repeat.svg";
            syncIcon = SvgIconFactory.LoadTinted("BootstrapIcons", fileName, 12, 12, color);
            syncIcon.RenderTransformOrigin = new Point(0.5, 0.5);
            var rotation = new RotateTransform(0);
            syncIcon.RenderTransform = rotation;
            if (String.Equals(status, "Syncing", StringComparison.OrdinalIgnoreCase)
                && appSettings.AnimationsEnabled && !appSettings.ReduceMotion)
            {
                rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                });
            }
            var surface = syncButton.Content as Border;
            if (surface != null)
            {
                surface.Child = syncIcon;
            }
        }

        private void NetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (e.IsAvailable)
                {
                    QueueKoboSynchronization(true, true);
                }
                else
                {
                    SetSyncStatus("Offline", "Progress is saved locally.");
                }
            }));
        }

        private void MediaOnMediaOpened(object sender, RoutedEventArgs e)
        {
            var openedBook = currentBook;
            var openedTrack = currentTrackIndex;
            if (openedBook == null)
            {
                return;
            }
            sourceLoaded = false;
            applyingResumePosition = true;
            isPlaying = false;
            ApplySpeed();
            media.Volume = volumeSlider.Value;
            var shouldPlay = playWhenSourceReady;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (currentBook != openedBook || currentTrackIndex != openedTrack || media == null)
                {
                    applyingResumePosition = false;
                    sourceLoadPending = false;
                    return;
                }
                if (openedTrack >= 0 && openedTrack < playbackTracks.Count && media.NaturalDuration.HasTimeSpan)
                {
                    playbackTracks[openedTrack].DurationSeconds = media.NaturalDuration.TimeSpan.TotalSeconds;
                    UpdateTotalDuration();
                }
                currentTrackStartSeconds = GetTrackStartSeconds(openedTrack);
                var start = Math.Min(
                    media.NaturalDuration.HasTimeSpan ? media.NaturalDuration.TimeSpan.TotalSeconds : Double.MaxValue,
                    Math.Max(0, pendingTrackPositionSeconds));
                currentBook.PositionSeconds = currentTrackStartSeconds + start;
                currentBook.Finished = false;
                UpdateBookDetails();
                media.Position = TimeSpan.FromSeconds(start);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (currentBook != openedBook || currentTrackIndex != openedTrack || media == null)
                    {
                        applyingResumePosition = false;
                        sourceLoadPending = false;
                        return;
                    }
                    media.Position = TimeSpan.FromSeconds(start);
                    pendingTrackPositionSeconds = 0;
                    currentTrackStartSeconds = GetTrackStartSeconds(openedTrack);
                    currentBook.PositionSeconds = currentTrackStartSeconds + start;
                    applyingResumePosition = false;
                    sourceLoaded = true;
                    sourceLoadPending = false;
                    if (shouldPlay || playWhenSourceReady)
                    {
                        media.Play();
                        isPlaying = true;
                        UpdatePlayButtonVisual();
                    }
                    playWhenSourceReady = false;
                    UpdateWindowsMediaTimeline();
                }), DispatcherPriority.ContextIdle);
            }), DispatcherPriority.Loaded);
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
            UpdateChapterSelection(currentBook.PositionSeconds);
            UpdateProgressDisplay(currentBook.PositionSeconds);
            SaveLibrary();
            RefreshVisibleBooks();
            QueueKoboSynchronization(false, true);
        }

        private void MediaOnMediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            sourceLoaded = false;
            sourceLoadPending = false;
            isPlaying = false;
            playWhenSourceReady = false;
            UpdatePlayButtonVisual();
            UpdateWindowsMediaTimeline();
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
