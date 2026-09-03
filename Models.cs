using System;
using System.Collections.Generic;
using System.IO;

namespace Kapla
{
    [Serializable]
    public sealed class LibraryStore
    {
        public List<BookEntry> Books { get; set; }

        public LibraryStore()
        {
            Books = new List<BookEntry>();
        }
    }

    [Serializable]
    public sealed class BookEntry
    {
        public string Path { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Album { get; set; }
        public string CoverPath { get; set; }
        public string CoverUrl { get; set; }
        public string KoboRevisionId { get; set; }
        public string KoboProductId { get; set; }
        public double KoboProgressPercent { get; set; }
        public double PositionSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public bool Finished { get; set; }
        public DateTime LastPlayedUtc { get; set; }
        public string Narrator { get; set; }
        public string Series { get; set; }
        public string Publisher { get; set; }
        public string ReleaseDate { get; set; }
        public string Description { get; set; }
        public List<KoboTrack> Tracks { get; set; }
        public List<KoboChapter> Chapters { get; set; }
        public List<double> Bookmarks { get; set; }

        public BookEntry()
        {
            Title = "Untitled audiobook";
            Author = "Unknown author";
            Tracks = new List<KoboTrack>();
            Chapters = new List<KoboChapter>();
            Bookmarks = new List<double>();
            LastPlayedUtc = DateTime.UtcNow;
        }

        public string ProgressText
        {
            get
            {
                if (Finished)
                {
                    return "Finished";
                }

                if (PositionSeconds > 0)
                {
                    return "Resume at " + FormatTime(PositionSeconds);
                }

                return "Not started";
            }
        }

        public string CoverSource
        {
            get
            {
                return !String.IsNullOrWhiteSpace(CoverPath) && File.Exists(CoverPath) ? CoverPath : CoverUrl;
            }
        }

        private static string FormatTime(double seconds)
        {
            var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (value.TotalHours >= 1)
            {
                return value.ToString(@"h\:mm\:ss");
            }

            return value.ToString(@"m\:ss");
        }
    }

    [Serializable]
    public sealed class AppSettings
    {
        public bool LaunchAtStartup { get; set; }
        public bool RememberWindowPosition { get; set; }
        public bool AlwaysOnTopByDefault { get; set; }
        public bool ResumeLastAudiobook { get; set; }
        public bool MinimizeOnClose { get; set; }
        public double DefaultPlaybackSpeed { get; set; }
        public int RewindSeconds { get; set; }
        public int ForwardSeconds { get; set; }
        public bool AutoResume { get; set; }
        public bool RememberPlaybackPosition { get; set; }
        public int DefaultSleepMinutes { get; set; }
        public double Volume { get; set; }
        public List<string> LibraryFolders { get; set; }
        public string LibrarySort { get; set; }
        public string PreferredMetadataSource { get; set; }
        public string AppearanceMode { get; set; }
        public string AccentColor { get; set; }
        public bool AnimationsEnabled { get; set; }
        public bool ReduceMotion { get; set; }
        public string LastBookPath { get; set; }

        public AppSettings()
        {
            RememberWindowPosition = true;
            ResumeLastAudiobook = true;
            DefaultPlaybackSpeed = 1.0;
            RewindSeconds = 15;
            ForwardSeconds = 15;
            AutoResume = true;
            RememberPlaybackPosition = true;
            DefaultSleepMinutes = 30;
            Volume = 0.9;
            LibraryFolders = new List<string>();
            LibrarySort = "Recently played";
            PreferredMetadataSource = "Embedded metadata first";
            AppearanceMode = "Light";
            AccentColor = "#7DD3FC";
            AnimationsEnabled = true;
        }
    }

    [Serializable]
    public sealed class KoboSession
    {
        public string Email { get; set; }
        public string UserId { get; set; }
        public string UserKey { get; set; }
        public string DeviceId { get; set; }
        public string SerialNumber { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
    }

    public sealed class KoboRemoteBook
    {
        public string RevisionId { get; set; }
        public string ProductId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Narrator { get; set; }
        public string Series { get; set; }
        public string Publisher { get; set; }
        public string Description { get; set; }
        public string ReleaseDate { get; set; }
        public string CoverUrl { get; set; }
        public double ProgressPercent { get; set; }
        public bool IsProtected { get; set; }
        internal Dictionary<string, object> Metadata { get; set; }
        public string StatusText
        {
            get
            {
                if (IsProtected)
                {
                    return "Protected • use Kobo app";
                }

                return ProgressPercent > 0 ? "Kobo library • " + Math.Round(ProgressPercent) + "%" : "Kobo library";
            }
        }

        public string DetailText
        {
            get
            {
                var details = new List<string>();
                if (!String.IsNullOrWhiteSpace(Narrator))
                {
                    details.Add("Narrated by " + Narrator);
                }
                if (!String.IsNullOrWhiteSpace(Series))
                {
                    details.Add(Series);
                }
                if (!String.IsNullOrWhiteSpace(Publisher))
                {
                    details.Add(Publisher);
                }
                return String.Join("  •  ", details);
            }
        }
    }

    public sealed class KoboDownloadProgress
    {
        public string Title { get; set; }
        public string Stage { get; set; }
        public int Percent { get; set; }
        public int CurrentTrack { get; set; }
        public int TotalTracks { get; set; }
        public string Detail { get; set; }
    }

    [Serializable]
    public sealed class KoboTrack
    {
        public string Path { get; set; }
        public double DurationSeconds { get; set; }
        public string Title { get; set; }
    }

    [Serializable]
    public sealed class KoboChapter
    {
        public string Title { get; set; }
        public double StartSeconds { get; set; }
        public double EndSeconds { get; set; }

        public string DisplayText
        {
            get
            {
                return FormatTime(StartSeconds) + "  " + (String.IsNullOrWhiteSpace(Title) ? "Chapter" : Title);
            }
        }

        private static string FormatTime(double seconds)
        {
            var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
        }
    }

    public sealed class KoboDownloadResult
    {
        public string OutputPath { get; set; }
        public string CoverPath { get; set; }
        public string CoverUrl { get; set; }
        public string Author { get; set; }
        public string Narrator { get; set; }
        public string Series { get; set; }
        public string Publisher { get; set; }
        public string Description { get; set; }
        public string ReleaseDate { get; set; }
        public List<KoboTrack> Tracks { get; set; }
        public List<KoboChapter> Chapters { get; set; }

        public KoboDownloadResult()
        {
            Tracks = new List<KoboTrack>();
            Chapters = new List<KoboChapter>();
        }
    }
}
