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
        private UIElement BuildSettingsView()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var categories = new StackPanel { Orientation = Orientation.Horizontal };
            var content = new ContentControl { Margin = new Thickness(1, 5, 1, 0) };
            Grid.SetRow(content, 1);
            root.Children.Add(categories);
            root.Children.Add(content);

            settingsCategoryButtons.Clear();
            Action<string> activate = null;
            activate = delegate(string category)
            {
                settingsCategory = category;
                content.Content = BuildSettingsCategoryContent(category);
                ApplyThemeToElement(content.Content as DependencyObject);
                UpdateSettingsCategoryStates();
            };

            foreach (var name in new[] { "General", "Playback", "Library", "Appearance" })
            {
                var categoryName = name;
                var button = MakeSettingsCategoryButton(categoryName);
                button.Click += delegate { activate(categoryName); };
                settingsCategoryButtons[categoryName] = button;
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
                Height = 22,
                MinWidth = 54,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(8, 1, 8, 1),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brush("#8A7E7A"),
                FontFamily = interFont,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Template = MakeRoundedButtonTemplate(6)
            };
            System.Windows.Automation.AutomationProperties.SetName(button, "Settings " + label);
            return button;
        }

        private void SetSettingsCategoryState(Button button, bool active)
        {
            button.Background = active
                ? WithOpacity(accentBrush.Color, IsDarkTheme ? 0.34 : 0.24)
                : Brushes.Transparent;
            button.BorderBrush = active ? accentBrush : Brushes.Transparent;
            button.BorderThickness = active ? new Thickness(1.5) : new Thickness(1.25);
            button.Foreground = active
                ? (IsDarkTheme ? Brush("#BCE8FF") : Brush("#17384A"))
                : (IsDarkTheme ? Brush("#AAB3BD") : Brush("#8A7E7A"));
        }

        private void UpdateSettingsCategoryStates()
        {
            foreach (var entry in settingsCategoryButtons)
            {
                SetSettingsCategoryState(entry.Value,
                    String.Equals(entry.Key, settingsCategory, StringComparison.Ordinal));
            }
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

            var right = new StackPanel();
            right.Children.Add(MakeSettingsSectionLabel("STARTUP"));
            right.Children.Add(MakeCompactSettingsCheck("Launch Kapla at startup", appSettings.LaunchAtStartup, value =>
            {
                appSettings.LaunchAtStartup = value;
                ApplyLaunchAtStartup();
            }));
            right.Children.Add(MakeCompactSettingsCheck("Resume last audiobook", appSettings.ResumeLastAudiobook, value => appSettings.ResumeLastAudiobook = value));
            right.Children.Add(MakeSettingsSectionLabel("DATA"));
            right.Children.Add(new TextBlock
            {
                Text = "Remove your local library, downloads, saved progress, settings, and Kobo connection.",
                FontFamily = interFont,
                FontSize = 8.5,
                Foreground = Brush("#8A7E7A"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 5)
            });
            var purge = MakeCompactActionButton("Purge all local data", false);
            var purgeContent = new StackPanel { Orientation = Orientation.Horizontal };
            purgeContent.Children.Add(BuildBootstrapIcon("x-lg.svg", 10, false));
            purgeContent.Children.Add(new TextBlock
            {
                Text = "Purge all local data",
                FontFamily = interFont,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#B04C4C"),
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            purge.Content = purgeContent;
            purge.Height = 24;
            purge.HorizontalAlignment = HorizontalAlignment.Left;
            purge.Foreground = Brush("#B04C4C");
            purge.ToolTip = "Permanently remove Kapla's local data from this PC";
            purge.Click += delegate { PurgeLocalData(); };
            right.Children.Add(purge);
            return MakeSettingsColumns(left, right);
        }

        private void PurgeLocalData()
        {
            var answer = MessageBox.Show(this,
                "This permanently deletes Kapla's local library, downloaded Kobo audiobooks, saved progress, settings, and account connection from this PC. It cannot be undone. Continue?",
                "Purge all local data",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            try
            {
                purgingData = true;
                if (media != null)
                {
                    media.Stop();
                    media.Close();
                    media.Source = null;
                }
                if (koboClient != null)
                {
                    koboClient.Dispose();
                    koboClient = null;
                }
                koboSession = null;
                pendingKoboActivation = null;
                if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
                allBooks.Clear();
                visibleBooks.Clear();
                remoteKoboBooks.Clear();
                selectedKoboBookIds.Clear();
                currentBook = null;
                previewBook = null;
                appSettings.LaunchAtStartup = false;
                ApplyLaunchAtStartup();
                MessageBox.Show(this,
                    "All Kapla data has been removed. Kapla will now close; reopen it to start as a new user.",
                    "Kapla data purged",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                purgingData = false;
                MessageBox.Show(this,
                    "Kapla could not remove all local data. Close the app and try again.\n\n" + ex.Message,
                    "Purge failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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
            right.Children.Add(MakeCompactSettingsValue("Progress", new[] { PlaybackProgress.ChapterMode, PlaybackProgress.BookMode }, appSettings.ProgressDisplayMode, value =>
            {
                appSettings.ProgressDisplayMode = value;
                if (currentBook != null) UpdateProgressDisplay(CurrentAbsolutePosition());
            }));
            right.Children.Add(BuildSleepTimerSettingsControl());
            right.Children.Add(MakeCompactVolumeRow());
            return MakeSettingsColumns(left, right);
        }

        private UIElement BuildSleepTimerSettingsControl()
        {
            var root = new StackPanel { Margin = new Thickness(0, 3, 0, 2) };
            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(MakeSettingsLabel("Sleep timer"));
            sleepRemainingText = FigmaText("Off", 9.5, FontWeights.SemiBold, accentBrush);
            Grid.SetColumn(sleepRemainingText, 1);
            heading.Children.Add(sleepRemainingText);
            root.Children.Add(heading);

            var presets = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            foreach (var minutes in new[] { 5, 15, 30, 60 })
            {
                var value = minutes;
                var button = MakeCompactActionButton(value + " min", false);
                button.Height = 22;
                button.Padding = new Thickness(7, 2, 7, 2);
                button.Margin = new Thickness(0, 0, 4, 4);
                button.Click += delegate { StartSleepTimer(value); };
                presets.Children.Add(button);
            }
            var end = MakeCompactActionButton("End chapter", false);
            end.Height = 22;
            end.Padding = new Thickness(7, 2, 7, 2);
            end.Margin = new Thickness(0, 0, 4, 4);
            end.IsEnabled = CurrentChapterEndSeconds().HasValue;
            end.Click += delegate { StartSleepTimerAtChapterEnd(); };
            presets.Children.Add(end);
            sleepCancelButton = MakeCompactActionButton("Cancel", false);
            sleepCancelButton.Height = 22;
            sleepCancelButton.Padding = new Thickness(7, 2, 7, 2);
            sleepCancelButton.Margin = new Thickness(0, 0, 4, 4);
            sleepCancelButton.IsEnabled = sleepTimer.IsActive;
            sleepCancelButton.Click += delegate { CancelSleepTimer("Sleep timer cancelled."); };
            presets.Children.Add(sleepCancelButton);
            root.Children.Add(presets);
            return root;
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
                FontSize = 9.5,
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
            var rescan = MakeCompactActionButton("Rescan library folders", true, IconRefresh);
            rescan.HorizontalAlignment = HorizontalAlignment.Left;
            rescan.Click += async delegate { await RescanLibraryFoldersAsync(); };
            actions.Children.Add(rescan);
            var refresh = MakeCompactActionButton("Refresh metadata + covers", false, IconSync);
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
            left.Children.Add(MakeSettingsSectionLabel("APPEARANCE"));
            left.Children.Add(MakeCompactSettingsValue("Theme", new[] { "Light", "Dark" }, appSettings.AppearanceMode, value =>
            {
                appSettings.AppearanceMode = value;
                ApplyTheme(true);
            }));
            left.Children.Add(MakeCompactSettingsCheck("Show cover artwork", appSettings.ShowCoverArtwork, value =>
            {
                appSettings.ShowCoverArtwork = value;
                ApplyCoverVisibility(true);
                UpdateResponsiveLayout();
            }));

            var right = new StackPanel();
            right.Children.Add(MakeSettingsSectionLabel("MOTION"));
            right.Children.Add(MakeCompactSettingsCheck("Animations", appSettings.AnimationsEnabled, value => appSettings.AnimationsEnabled = value));
            right.Children.Add(MakeCompactSettingsCheck("Reduce motion", appSettings.ReduceMotion, value => appSettings.ReduceMotion = value));
            return MakeSettingsColumns(left, right);
        }

        private Button MakeCompactSettingsCheck(string label, bool current, Action<bool> changed)
        {
            var state = current;
            var button = new Button
            {
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontFamily = interFont,
                FontSize = 9.5,
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
                FontSize = 9.5,
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
                Height = 23,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(7, 1, 6, 1),
                FontFamily = interFont,
                FontSize = 9.5,
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
                FontSize = 9.5,
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
                FontSize = 9.5,
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
                FontSize = 9.5,
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
                FontSize = 9.5,
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
                FontSize = 9.5,
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
    }
}
