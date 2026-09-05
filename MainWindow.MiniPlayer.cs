using System;
using System.Windows;

namespace Kapla
{
    public sealed partial class MainWindow
    {
        private void MainWindowStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                ShowMiniPlayer();
            }
            else
            {
                HideMiniPlayer();
            }
        }

        private void ShowMiniPlayer()
        {
            if (currentBook == null)
            {
                return;
            }

            if (miniPlayer == null)
            {
                miniPlayer = new MiniPlayerWindow(
                    delegate { return currentBook; },
                    delegate { return isPlaying; },
                    delegate { return CurrentAbsolutePosition(); },
                    delegate { return currentBook == null ? 0 : currentBook.DurationSeconds; },
                    TogglePlay,
                    delegate { Skip(-appSettings.RewindSeconds); },
                    delegate { Skip(appSettings.ForwardSeconds); },
                    SeekMiniPlayerPosition,
                    RestoreMainFromMiniPlayer,
                    HideMiniPlayer);
            }

            miniPlayer.ShowAtBottomRight();
        }

        private void HideMiniPlayer()
        {
            if (miniPlayer != null && miniPlayer.IsVisible)
            {
                miniPlayer.Hide();
            }
        }

        private void RestoreMainFromMiniPlayer()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            HideMiniPlayer();
        }

        private void SeekMiniPlayerPosition(double target)
        {
            if (currentBook != null)
            {
                SeekToGlobal(target, isPlaying);
            }
        }

        private void UpdateMiniPlayer()
        {
            if (miniPlayer != null && miniPlayer.IsVisible)
            {
                miniPlayer.Refresh();
            }
        }
    }
}
