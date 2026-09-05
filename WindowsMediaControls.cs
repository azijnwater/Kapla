using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Kapla
{
    internal sealed class WindowsMediaControls : IDisposable
    {
        [ComImport]
        [Guid("DDB0472D-C911-4A1F-86D9-DC3D71A95F5A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        private interface ISystemMediaTransportControlsInterop
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            SystemMediaTransportControls GetForWindow(IntPtr appWindow, [In] ref Guid interfaceId);
        }

        [Flags]
        private enum ExecutionState : uint
        {
            SystemRequired = 0x00000001,
            Continuous = 0x80000000
        }

        [DllImport("kernel32.dll")]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState executionState);

        private readonly Dispatcher dispatcher;
        private SystemMediaTransportControls controls;
        private bool disposed;
        private string displayedBookKey;
        private string displayedChapter;

        public event Action PlayRequested;
        public event Action PauseRequested;
        public event Action SkipBackRequested;
        public event Action SkipForwardRequested;
        public event Action<double> SeekRequested;

        public bool IsAvailable
        {
            get { return controls != null && !disposed; }
        }

        private WindowsMediaControls(Dispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
        }

        public static WindowsMediaControls TryCreate(IntPtr windowHandle, Dispatcher dispatcher)
        {
            var instance = new WindowsMediaControls(dispatcher);
            try
            {
                var factory = WindowsRuntimeMarshal.GetActivationFactory(typeof(SystemMediaTransportControls));
                var interop = (ISystemMediaTransportControlsInterop)factory;
                var interfaceId = typeof(SystemMediaTransportControls).GUID;
                instance.controls = interop.GetForWindow(windowHandle, ref interfaceId);
                instance.controls.IsEnabled = true;
                instance.controls.IsPlayEnabled = true;
                instance.controls.IsPauseEnabled = true;
                instance.controls.IsStopEnabled = true;
                instance.controls.IsPreviousEnabled = true;
                instance.controls.IsNextEnabled = true;
                instance.controls.IsRewindEnabled = true;
                instance.controls.IsFastForwardEnabled = true;
                instance.controls.ButtonPressed += instance.ControlsOnButtonPressed;
                instance.controls.PlaybackPositionChangeRequested += instance.ControlsOnPlaybackPositionChangeRequested;
                instance.controls.PlaybackStatus = MediaPlaybackStatus.Closed;
            }
            catch
            {
                instance.controls = null;
            }
            return instance;
        }

        public void UpdateMetadata(BookEntry book, string chapterTitle)
        {
            if (!IsAvailable)
            {
                return;
            }

            if (book == null)
            {
                displayedBookKey = null;
                displayedChapter = null;
                controls.DisplayUpdater.ClearAll();
                controls.DisplayUpdater.Update();
                controls.PlaybackStatus = MediaPlaybackStatus.Closed;
                return;
            }

            var key = (book.Path ?? String.Empty) + "\n" + (book.Title ?? String.Empty) + "\n" + (book.Author ?? String.Empty)
                + "\n" + (book.CoverPath ?? String.Empty);
            chapterTitle = chapterTitle ?? String.Empty;
            if (String.Equals(key, displayedBookKey, StringComparison.Ordinal)
                && String.Equals(chapterTitle, displayedChapter, StringComparison.Ordinal))
            {
                return;
            }

            var updater = controls.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = String.IsNullOrWhiteSpace(book.Title) ? "Kapla audiobook" : book.Title;
            updater.MusicProperties.Artist = String.IsNullOrWhiteSpace(book.Author) ? "Unknown author" : book.Author;
            updater.MusicProperties.AlbumArtist = updater.MusicProperties.Artist;
            updater.MusicProperties.AlbumTitle = String.IsNullOrWhiteSpace(chapterTitle)
                ? (book.Album ?? String.Empty)
                : chapterTitle;
            updater.Thumbnail = null;
            updater.Update();
            displayedBookKey = key;
            displayedChapter = chapterTitle;
            SetThumbnailAsync(key, book.CoverPath);
        }

        private void SetThumbnailAsync(string bookKey, string coverPath)
        {
            if (String.IsNullOrWhiteSpace(coverPath) || !File.Exists(coverPath))
            {
                return;
            }

            try
            {
                var operation = StorageFile.GetFileFromPathAsync(coverPath);
                operation.Completed = delegate(IAsyncOperation<StorageFile> completed, AsyncStatus status)
                {
                    if (status != AsyncStatus.Completed || disposed)
                    {
                        return;
                    }
                    dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (disposed || controls == null || !String.Equals(displayedBookKey, bookKey, StringComparison.Ordinal))
                        {
                            return;
                        }
                        try
                        {
                            controls.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromFile(completed.GetResults());
                            controls.DisplayUpdater.Update();
                        }
                        catch
                        {
                            // Album artwork is optional; media controls remain usable if it cannot be read.
                        }
                    }));
                };
            }
            catch
            {
                // Album artwork is optional; media controls remain usable if it
                // cannot be read by the Windows Runtime.
            }
        }

        public void UpdatePlaybackState(bool hasBook, bool isPlaying)
        {
            if (IsAvailable)
            {
                controls.IsEnabled = hasBook;
                controls.PlaybackStatus = !hasBook
                    ? MediaPlaybackStatus.Closed
                    : isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
            }

            SetThreadExecutionState(isPlaying
                ? ExecutionState.Continuous | ExecutionState.SystemRequired
                : ExecutionState.Continuous);
        }

        public void UpdateTimeline(double positionSeconds, double durationSeconds)
        {
            if (!IsAvailable || durationSeconds <= 0)
            {
                return;
            }

            var duration = TimeSpan.FromSeconds(Math.Max(0, durationSeconds));
            var position = TimeSpan.FromSeconds(Math.Max(0, Math.Min(durationSeconds, positionSeconds)));
            controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                MinSeekTime = TimeSpan.Zero,
                Position = position,
                MaxSeekTime = duration,
                EndTime = duration
            });
        }

        private void ControlsOnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            Dispatch(delegate
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        Raise(PlayRequested);
                        break;
                    case SystemMediaTransportControlsButton.Pause:
                    case SystemMediaTransportControlsButton.Stop:
                        Raise(PauseRequested);
                        break;
                    case SystemMediaTransportControlsButton.Previous:
                    case SystemMediaTransportControlsButton.Rewind:
                        Raise(SkipBackRequested);
                        break;
                    case SystemMediaTransportControlsButton.Next:
                    case SystemMediaTransportControlsButton.FastForward:
                        Raise(SkipForwardRequested);
                        break;
                }
            });
        }

        private void ControlsOnPlaybackPositionChangeRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
        {
            var seconds = args.RequestedPlaybackPosition.TotalSeconds;
            Dispatch(delegate
            {
                var handler = SeekRequested;
                if (handler != null)
                {
                    handler(seconds);
                }
            });
        }

        private void Dispatch(Action action)
        {
            if (disposed || action == null)
            {
                return;
            }
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.BeginInvoke(action);
            }
        }

        private static void Raise(Action handler)
        {
            if (handler != null)
            {
                handler();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            SetThreadExecutionState(ExecutionState.Continuous);
            if (controls != null)
            {
                controls.ButtonPressed -= ControlsOnButtonPressed;
                controls.PlaybackPositionChangeRequested -= ControlsOnPlaybackPositionChangeRequested;
                controls.PlaybackStatus = MediaPlaybackStatus.Closed;
                controls.IsEnabled = false;
                controls = null;
            }
        }
    }
}
