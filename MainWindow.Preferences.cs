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

        private void ApplyTheme(bool animate)
        {
            accentBrush = Brush(IsDarkTheme ? "#55B8F6" : "#7DD3FC");
            accentSoftBrush = WithOpacity(accentBrush.Color, IsDarkTheme ? 0.20 : 0.14);
            SvgIconFactory.AccentColor = accentBrush.Color;
            Foreground = IsDarkTheme ? Brush("#F4F0EC") : Brush("#261D1B");
            if (shellSurface != null)
            {
                shellSurface.Background = IsDarkTheme ? Brush("#171A1F") : Brush("#FDF8F4");
                shellSurface.BorderBrush = IsDarkTheme ? Brush("#38414C") : Brush("#101A1111");
            }
            if (appCard != null) appCard.Background = IsDarkTheme ? Brush("#171A1F") : Brush("#FDF8F4");
            if (librarySurface != null) librarySurface.Background = IsDarkTheme ? Brush("#171A1F") : Brush("#FDF8F4");
            ApplyThemeToElement(rootLayout);
            if (progressFill != null) progressFill.Background = accentBrush;
            if (progressThumb != null) progressThumb.Background = accentBrush;
            if (progressSlider != null) progressSlider.Foreground = accentBrush;
            if (playerStateText != null) playerStateText.Foreground = accentBrush;
            if (playButton != null && playButton.Content is Border) ((Border)playButton.Content).Background = BuildPlayButtonBrush();
            if (speedButton != null) speedButton.Content = MakeSpeedContent(speedBox == null || speedBox.SelectedItem == null ? "1.0x" : speedBox.SelectedItem.ToString());
            if (brandIcon != null) brandIcon.Background = BuildBrandBrush();
            if (minimizeButton != null) minimizeButton.Content = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Child = BuildBootstrapIcon("dash-lg.svg", 12, false) };
            if (closeButton != null) closeButton.Content = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Child = BuildBootstrapIcon("x-lg.svg", 11, false) };
            UpdateSyncIcon(currentSyncStatus);
            UpdatePinVisual();
            SetPanelTabState(libraryTabButton, expandedView == "library");
            SetPanelTabState(settingsTabButton, expandedView == "settings");
            SetPanelTabState(koboTabButton, expandedView == "kobo");
            UpdateSettingsCategoryStates();
            UpdateExpandedSyncBadge();
            UpdateSleepTimerUi();
            if (animate) AnimateIn(rootLayout, 210, 0);
            SaveSettings();
        }

        private void ApplyThemeToElement(DependencyObject element)
        {
            ThemePalette.Apply(element, IsDarkTheme);
        }

        private Brush ThemeBrush(Brush value)
        {
            return ThemePalette.Map(value, IsDarkTheme);
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

        private Button MakeCompactActionButton(string label, bool primary, string iconGlyph = null)
        {
            var button = new Button
            {
                Content = String.IsNullOrWhiteSpace(iconGlyph) ? (object)label : MakeIconLabel(iconGlyph, label, primary),
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
            AttachMicroInteraction(button, 1.025);
            return button;
        }

        private void ShowChoiceMenu(Button anchor, IEnumerable<string> values, string selected, Action<string> changed)
        {
            var menu = new ContextMenu
            {
                PlacementTarget = anchor,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                FontFamily = interFont,
                FontSize = 11,
                Background = IsDarkTheme ? Brush("#232830") : Brush("#FDF8F4"),
                Foreground = IsDarkTheme ? Brush("#F4F0EC") : Brush("#261D1B")
            };
            foreach (var value in values)
            {
                var choice = value;
                var item = new MenuItem { Header = choice, IsCheckable = true, IsChecked = choice == selected };
                item.Click += delegate { changed(choice); };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private UIElement MakeIconLabel(string glyph, string label, bool primary)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(MakeButtonIcon(glyph, 10, primary ? Brush("#17384A") : Brush("#1A1111")));
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = interFont,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = primary ? Brush("#17384A") : Brush("#1A1111"),
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        private static TextBlock MakeButtonIcon(string glyph, double size, Brush foreground)
        {
            return new TextBlock
            {
                Text = glyph,
                FontFamily = ButtonIconFont,
                FontSize = size,
                FontWeight = FontWeights.Normal,
                Foreground = foreground,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
        }

        private static FontFamily ResolveButtonIconFont()
        {
            return Fonts.SystemFontFamilies.Any(font =>
                String.Equals(font.Source, "Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase))
                ? new FontFamily("Segoe Fluent Icons")
                : new FontFamily("Segoe MDL2 Assets");
        }
    }
}
