using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Kapla
{
    public sealed partial class MainWindow : Window
    {
        private const string KoboAudiobookStorePage = "https://www.kobo.com/ww/en/audiobooks";

        private UIElement BuildKoboView()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(25) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(47) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new StackPanel();
            title.Children.Add(new TextBlock
            {
                Text = "KOBO INTEGRATION",
                FontFamily = interFont,
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = accentBrush
            });
            title.Children.Add(new TextBlock
            {
                Text = "Cloud synchronization",
                FontFamily = interFont,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#1A1111"),
                Margin = new Thickness(0, -1, 0, 0)
            });
            titleRow.Children.Add(title);
            var browse = MakeCompactActionButton("Browse Kobo store", false);
            browse.Height = 23;
            browse.Padding = new Thickness(9, 2, 9, 2);
            browse.ToolTip = "Browse audiobooks on Kobo.com, then sync purchases back to Kapla";
            browse.Click += delegate { OpenKoboAudiobookStore(); };
            Grid.SetColumn(browse, 1);
            titleRow.Children.Add(browse);
            root.Children.Add(titleRow);

            var accountRow = new Grid { Margin = new Thickness(10, 5, 10, 5) };
            accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            accountRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            accountRow.Children.Add(new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = IsDarkTheme ? Brush("#30363D") : Brush("#DDF3FC"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "K",
                    FontFamily = interFont,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = accentBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });

            koboAccountStatusText = new TextBlock
            {
                Text = KoboAccountStatus(),
                FontFamily = interFont,
                FontSize = 8.5,
                Foreground = Brush("#8A7E7A"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var accountSummary = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            accountSummary.Children.Add(new TextBlock
            {
                Text = KoboAccountName(),
                FontFamily = interFont,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#1A1111"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            accountSummary.Children.Add(koboAccountStatusText);
            Grid.SetColumn(accountSummary, 1);
            accountRow.Children.Add(accountSummary);

            var connect = MakeKoboDashboardButton(koboClient == null ? "Connect Kobo" : "Sync library", true, "arrow-repeat.svg");
            connect.VerticalAlignment = VerticalAlignment.Top;
            connect.Click += delegate { ConnectKobo(); };
            Grid.SetColumn(connect, 2);
            accountRow.Children.Add(connect);
            var disconnect = MakeKoboDashboardButton("Disconnect", false, "x-lg.svg");
            disconnect.VerticalAlignment = VerticalAlignment.Top;
            disconnect.Margin = new Thickness(6, 0, 0, 0);
            disconnect.IsEnabled = koboSession != null;
            disconnect.Visibility = koboSession == null ? Visibility.Collapsed : Visibility.Visible;
            disconnect.Click += delegate { DisconnectKobo(); };
            Grid.SetColumn(disconnect, 3);
            accountRow.Children.Add(disconnect);

            var accountCard = new Border
            {
                Background = IsDarkTheme ? Brush("#161B22") : Brush("#FFFFFF"),
                BorderBrush = IsDarkTheme ? Brush("#30363D") : Brush("#E8DDD7"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = accountRow
            };
            Grid.SetRow(accountCard, 1);
            root.Children.Add(accountCard);

            var libraryGrid = new Grid();
            libraryGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(31) });
            libraryGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            // Keep the scrollbar in its own compact lane. The old fixed rows (14 + 9)
            // took enough height from the list to clip the final status line on smaller
            // expanded windows, even though the scrollbar itself is only a few pixels tall.
            libraryGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            libraryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var libraryHeading = new Grid { Margin = new Thickness(9, 0, 7, 0) };
            libraryHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            libraryHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            libraryHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var headingStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            headingStack.Children.Add(new TextBlock
            {
                Text = "Kobo Cloud Library",
                FontFamily = interFont,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#1A1111")
            });
            koboDownloadText = new TextBlock
            {
                Text = remoteKoboBooks.Count + (remoteKoboBooks.Count == 1 ? " item" : " items"),
                FontFamily = interFont,
                FontSize = 8,
                Foreground = Brush("#8A7E7A"),
                Margin = new Thickness(7, 1, 0, 0)
            };
            headingStack.Children.Add(koboDownloadText);
            libraryHeading.Children.Add(headingStack);

            var searchHost = new Grid
            {
                Width = 132,
                Height = 21,
                Margin = new Thickness(10, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            koboLibrarySearchBox = new TextBox
            {
                FontFamily = interFont,
                FontSize = 8.5,
                Padding = new Thickness(7, 2, 7, 2),
                Background = IsDarkTheme ? Brush("#0D1117") : Brush("#FAF8F6"),
                Foreground = IsDarkTheme ? Brush("#F0F6FC") : Brush("#1A1111"),
                BorderBrush = IsDarkTheme ? Brush("#30363D") : Brush("#E8DDD7"),
                BorderThickness = new Thickness(1)
            };
            var searchHint = new TextBlock
            {
                Text = "Search cloud library",
                FontFamily = interFont,
                FontSize = 8,
                Foreground = Brush("#9A908C"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            koboLibrarySearchBox.TextChanged += delegate
            {
                searchHint.Visibility = String.IsNullOrEmpty(koboLibrarySearchBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                FilterKoboCloudLibrary();
            };
            searchHost.Children.Add(koboLibrarySearchBox);
            searchHost.Children.Add(searchHint);
            Grid.SetColumn(searchHost, 1);
            libraryHeading.Children.Add(searchHost);

            importKoboButton = MakeCompactActionButton("Download selected", true, IconDownload);
            importKoboButton.Height = 22;
            importKoboButton.Padding = new Thickness(8, 2, 8, 2);
            importKoboButton.IsEnabled = false;
            importKoboButton.Visibility = pendingKoboActivation == null && koboClient != null ? Visibility.Visible : Visibility.Collapsed;
            importKoboButton.Click += async delegate { await ImportSelectedKoboBookAsync(); };
            Grid.SetColumn(importKoboButton, 2);
            libraryHeading.Children.Add(importKoboButton);
            libraryGrid.Children.Add(libraryHeading);

            remoteKoboList = new ListBox
            {
                ItemsSource = FilteredKoboBooks(String.Empty),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Top,
                ItemTemplate = BuildRemoteKoboCoverTemplate(),
                ItemContainerStyle = BuildShelfItemStyle(),
                SelectionMode = SelectionMode.Multiple
            };
            var panelFactory = new FrameworkElementFactory(typeof(StackPanel));
            panelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            remoteKoboList.ItemsPanel = new ItemsPanelTemplate(panelFactory);
            ScrollViewer.SetHorizontalScrollBarVisibility(remoteKoboList, ScrollBarVisibility.Hidden);
            ScrollViewer.SetVerticalScrollBarVisibility(remoteKoboList, ScrollBarVisibility.Disabled);
            remoteKoboList.Loaded += delegate { AttachKoboLibraryScrollBar(); RestoreVisibleKoboSelections(); };
            remoteKoboList.PreviewMouseLeftButtonDown += RemoteKoboListOnPreviewMouseLeftButtonDown;
            remoteKoboList.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs args)
            {
                if (koboLibraryScrollViewer == null) return;
                koboLibraryScrollViewer.ScrollToHorizontalOffset(koboLibraryScrollViewer.HorizontalOffset - args.Delta);
                args.Handled = true;
            };
            Grid.SetRow(remoteKoboList, 1);
            libraryGrid.Children.Add(remoteKoboList);

            koboLibraryScrollBar = new System.Windows.Controls.Primitives.ScrollBar
            {
                Orientation = Orientation.Horizontal,
                Height = 5,
                Minimum = 0,
                SmallChange = 70,
                LargeChange = 280,
                Margin = new Thickness(9, 2, 9, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            koboLibraryScrollBar.ValueChanged += delegate
            {
                if (koboLibraryScrollViewer != null
                    && Math.Abs(koboLibraryScrollViewer.HorizontalOffset - koboLibraryScrollBar.Value) > 0.5)
                {
                    koboLibraryScrollViewer.ScrollToHorizontalOffset(koboLibraryScrollBar.Value);
                }
            };
            Grid.SetRow(koboLibraryScrollBar, 2);
            libraryGrid.Children.Add(koboLibraryScrollBar);

            koboActivationCodeText = new TextBlock
            {
                Text = pendingKoboActivation != null
                    ? "Activation code copied - finish connecting in your browser."
                    : remoteKoboBooks.Count == 0
                        ? "Connect or sync to browse your Kobo audiobooks."
                        : String.Empty,
                FontFamily = interFont,
                FontSize = 8.5,
                Foreground = Brush("#8A7E7A"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            Grid.SetRow(koboActivationCodeText, 1);
            libraryGrid.Children.Add(koboActivationCodeText);

            completeActivationButton = MakeCompactActionButton("I've connected", true, IconCheck);
            completeActivationButton.Height = 22;
            completeActivationButton.HorizontalAlignment = HorizontalAlignment.Right;
            completeActivationButton.VerticalAlignment = VerticalAlignment.Center;
            completeActivationButton.Margin = new Thickness(0, 0, 7, 0);
            completeActivationButton.Visibility = pendingKoboActivation == null ? Visibility.Collapsed : Visibility.Visible;
            completeActivationButton.Click += async delegate { await CompleteKoboActivationAsync(); };
            Grid.SetRow(completeActivationButton, 1);
            libraryGrid.Children.Add(completeActivationButton);

            koboDownloadProgress = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Height = 4,
                Foreground = accentBrush,
                Background = Brush("#E7E0DC"),
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8, 0, 8, 2)
            };
            Grid.SetRow(koboDownloadProgress, 3);
            libraryGrid.Children.Add(koboDownloadProgress);
            UpdateKoboSelectionControls();

            var libraryCard = new Border
            {
                Background = IsDarkTheme ? Brush("#161B22") : Brush("#FFFFFF"),
                BorderBrush = IsDarkTheme ? Brush("#30363D") : Brush("#E8DDD7"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 4, 0, 0),
                Child = libraryGrid
            };
            Grid.SetRow(libraryCard, 2);
            root.Children.Add(libraryCard);
            return root;
        }

        private void RemoteKoboListOnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
        {
            var source = args.OriginalSource as DependencyObject;
            var item = source == null || remoteKoboList == null
                ? null
                : ItemsControl.ContainerFromElement(remoteKoboList, source) as ListBoxItem;
            if (item == null)
            {
                return;
            }

            var book = item.DataContext as KoboRemoteBook;
            var key = KoboBookKey(book);
            if (!String.IsNullOrWhiteSpace(key) && selectedKoboBookIds.Contains(key))
            {
                selectedKoboBookIds.Remove(key);
                item.IsSelected = false;
            }
            else if (!String.IsNullOrWhiteSpace(key))
            {
                selectedKoboBookIds.Add(key);
                item.IsSelected = true;
            }
            item.Focus();
            UpdateKoboSelectionControls();
            args.Handled = true;
        }

        private void UpdateKoboSelectionControls()
        {
            if (importKoboButton == null || remoteKoboList == null)
            {
                return;
            }
            var count = selectedKoboBookIds.Count;
            importKoboButton.IsEnabled = count > 0;
            importKoboButton.Content = MakeIconLabel(
                IconDownload,
                count == 0 ? "Download selected" : "Download selected (" + count + ")",
                true);
        }

        private Button MakeKoboDashboardButton(string label, bool primary, string iconFile)
        {
            var foreground = primary ? Brush("#17384A") : (IsDarkTheme ? Brush("#F0F6FC") : Brush("#1A1111"));
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(SvgIconFactory.LoadTinted("BootstrapIcons", iconFile, 11, 11,
                ((SolidColorBrush)foreground).Color));
            content.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = interFont,
                FontSize = 8.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = foreground,
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            var button = MakeCompactActionButton(label, primary);
            button.Content = content;
            button.Height = 24;
            button.Padding = new Thickness(8, 2, 8, 2);
            return button;
        }

        private List<KoboRemoteBook> FilteredKoboBooks(string query)
        {
            if (String.IsNullOrWhiteSpace(query)) return remoteKoboBooks.ToList();
            query = query.Trim();
            return remoteKoboBooks.Where(book =>
                (!String.IsNullOrWhiteSpace(book.Title) && book.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                || (!String.IsNullOrWhiteSpace(book.Author) && book.Author.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        }

        private void FilterKoboCloudLibrary()
        {
            if (remoteKoboList == null) return;
            remoteKoboList.ItemsSource = FilteredKoboBooks(koboLibrarySearchBox == null ? null : koboLibrarySearchBox.Text);
            remoteKoboList.Dispatcher.BeginInvoke(new Action(delegate
            {
                RestoreVisibleKoboSelections();
                AttachKoboLibraryScrollBar();
            }));
        }

        private void RestoreVisibleKoboSelections()
        {
            if (remoteKoboList == null) return;
            remoteKoboList.SelectedItems.Clear();
            foreach (var book in remoteKoboList.Items.Cast<KoboRemoteBook>())
            {
                if (selectedKoboBookIds.Contains(KoboBookKey(book))) remoteKoboList.SelectedItems.Add(book);
            }
            UpdateKoboSelectionControls();
        }

        private void AttachKoboLibraryScrollBar()
        {
            if (remoteKoboList == null || koboLibraryScrollBar == null) return;
            var viewer = FindVisualChild<ScrollViewer>(remoteKoboList);
            if (viewer == null) return;
            if (!ReferenceEquals(koboLibraryScrollViewer, viewer))
            {
                koboLibraryScrollViewer = viewer;
                viewer.ScrollChanged += delegate
                {
                    koboLibraryScrollBar.Maximum = Math.Max(0, viewer.ScrollableWidth);
                    koboLibraryScrollBar.ViewportSize = viewer.ViewportWidth;
                    koboLibraryScrollBar.Value = Math.Min(koboLibraryScrollBar.Maximum, viewer.HorizontalOffset);
                    koboLibraryScrollBar.Visibility = viewer.ScrollableWidth > 0 ? Visibility.Visible : Visibility.Hidden;
                };
            }
            koboLibraryScrollBar.Maximum = Math.Max(0, viewer.ScrollableWidth);
            koboLibraryScrollBar.ViewportSize = viewer.ViewportWidth;
            koboLibraryScrollBar.Value = Math.Min(koboLibraryScrollBar.Maximum, viewer.HorizontalOffset);
            koboLibraryScrollBar.Visibility = viewer.ScrollableWidth > 0 ? Visibility.Visible : Visibility.Hidden;
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                var match = child as T;
                if (match != null) return match;
                match = FindVisualChild<T>(child);
                if (match != null) return match;
            }
            return null;
        }

        private static string KoboBookKey(KoboRemoteBook book)
        {
            if (book == null) return String.Empty;
            if (!String.IsNullOrWhiteSpace(book.EntitlementId)) return "e:" + book.EntitlementId;
            if (!String.IsNullOrWhiteSpace(book.RevisionId)) return "r:" + book.RevisionId;
            if (!String.IsNullOrWhiteSpace(book.ProductId)) return "p:" + book.ProductId;
            return "t:" + (book.Title ?? String.Empty) + "|" + (book.Author ?? String.Empty);
        }

        private DataTemplate BuildRemoteKoboCoverTemplate()
        {
            var template = new DataTemplate(typeof(KoboRemoteBook));
            var root = new FrameworkElementFactory(typeof(Grid));
            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.WidthProperty, 110.0);
            stack.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            var cover = new FrameworkElementFactory(typeof(Border));
            cover.SetValue(Border.WidthProperty, 100.0);
            cover.SetValue(Border.HeightProperty, 70.0);
            cover.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cover.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            cover.SetValue(Border.BackgroundProperty, Brush("#DDF3FC"));
            cover.SetValue(Border.ClipToBoundsProperty, true);
            var coverLayers = new FrameworkElementFactory(typeof(Grid));
            var placeholder = new FrameworkElementFactory(typeof(TextBlock));
            placeholder.SetValue(TextBlock.TextProperty, "K");
            placeholder.SetValue(TextBlock.FontFamilyProperty, interFont);
            placeholder.SetValue(TextBlock.FontSizeProperty, 16.0);
            placeholder.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            placeholder.SetValue(TextBlock.ForegroundProperty, Brush("#5FAED2"));
            placeholder.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            placeholder.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            coverLayers.AppendChild(placeholder);
            var image = new FrameworkElementFactory(typeof(Image));
            image.SetBinding(Image.SourceProperty, new Binding("CoverUrl"));
            image.SetValue(Image.StretchProperty, Stretch.UniformToFill);
            image.SetValue(Image.WidthProperty, 100.0);
            image.SetValue(Image.HeightProperty, 70.0);
            coverLayers.AppendChild(image);
            cover.AppendChild(coverLayers);
            stack.AppendChild(cover);

            var meta = new FrameworkElementFactory(typeof(StackPanel));
            meta.SetValue(FrameworkElement.WidthProperty, 110.0);
            meta.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0));
            var bookTitle = new FrameworkElementFactory(typeof(TextBlock));
            bookTitle.SetBinding(TextBlock.TextProperty, new Binding("Title"));
            bookTitle.SetValue(TextBlock.WidthProperty, 110.0);
            bookTitle.SetValue(TextBlock.FontFamilyProperty, interFont);
            bookTitle.SetValue(TextBlock.FontSizeProperty, 9.0);
            bookTitle.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            bookTitle.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            meta.AppendChild(bookTitle);
            var author = new FrameworkElementFactory(typeof(TextBlock));
            author.SetBinding(TextBlock.TextProperty, new Binding("Author"));
            author.SetValue(TextBlock.WidthProperty, 110.0);
            author.SetValue(TextBlock.FontFamilyProperty, interFont);
            author.SetValue(TextBlock.FontSizeProperty, 8.0);
            author.SetValue(TextBlock.ForegroundProperty, Brush("#8A7E7A"));
            author.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            meta.AppendChild(author);
            var status = new FrameworkElementFactory(typeof(TextBlock));
            status.SetBinding(TextBlock.TextProperty, new Binding("StatusText"));
            status.SetValue(TextBlock.WidthProperty, 110.0);
            status.SetValue(TextBlock.FontFamilyProperty, interFont);
            status.SetValue(TextBlock.FontSizeProperty, 7.5);
            status.SetValue(TextBlock.ForegroundProperty, accentBrush);
            status.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            status.SetValue(TextBlock.MarginProperty, new Thickness(0, 1, 0, 0));
            meta.AppendChild(status);
            stack.AppendChild(meta);
            root.AppendChild(stack);

            var selectedBadge = new FrameworkElementFactory(typeof(Border));
            selectedBadge.SetValue(Border.WidthProperty, 18.0);
            selectedBadge.SetValue(Border.HeightProperty, 18.0);
            selectedBadge.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            selectedBadge.SetValue(Border.BackgroundProperty, accentBrush);
            selectedBadge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            selectedBadge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            selectedBadge.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 4, 0));
            selectedBadge.SetBinding(UIElement.VisibilityProperty, new Binding("IsSelected")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1),
                Converter = new BooleanToVisibilityConverter()
            });
            var check = new FrameworkElementFactory(typeof(TextBlock));
            check.SetValue(TextBlock.TextProperty, "✓");
            check.SetValue(TextBlock.FontFamilyProperty, interFont);
            check.SetValue(TextBlock.FontSizeProperty, 11.0);
            check.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            check.SetValue(TextBlock.ForegroundProperty, Brush("#17384A"));
            check.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            selectedBadge.AppendChild(check);
            root.AppendChild(selectedBadge);

            template.VisualTree = root;
            return template;
        }

        private string KoboAccountStatus()
        {
            return koboSession == null || String.IsNullOrWhiteSpace(koboSession.AccessToken)
                ? "Not connected"
                : "Kobo Account Connected";
        }

        private void OpenKoboAudiobookStore()
        {
            try
            {
                Process.Start(new ProcessStartInfo(KoboAudiobookStorePage) { UseShellExecute = true });
                if (statusText != null)
                {
                    statusText.Text = "Browse or buy on Kobo.com, then choose Sync library to refresh Kapla.";
                }
            }
            catch
            {
                if (statusText != null)
                {
                    statusText.Text = "The Kobo store could not be opened in your browser.";
                }
            }
        }

        private string KoboAccountName()
        {
            return koboSession == null || String.IsNullOrWhiteSpace(koboSession.Email)
                ? "Kobo account"
                : koboSession.Email;
        }
    }
}
