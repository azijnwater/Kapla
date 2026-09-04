using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace Kapla
{
    internal static class AppSettingsStore
    {
        public static AppSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new AppSettings();
                }
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                using (var stream = File.OpenRead(path))
                {
                    var value = serializer.ReadObject(stream) as AppSettings;
                    if (value == null)
                    {
                        return new AppSettings();
                    }
                    if (value.LibraryFolders == null)
                    {
                        value.LibraryFolders = new System.Collections.Generic.List<string>();
                    }
                    if (value.DefaultPlaybackSpeed <= 0 || Double.IsNaN(value.DefaultPlaybackSpeed) || Double.IsInfinity(value.DefaultPlaybackSpeed))
                    {
                        value.DefaultPlaybackSpeed = 1.0;
                    }
                    if (value.RewindSeconds <= 0)
                    {
                        value.RewindSeconds = 15;
                    }
                    if (value.ForwardSeconds <= 0)
                    {
                        value.ForwardSeconds = 15;
                    }
                    if (value.DefaultSleepMinutes <= 0)
                    {
                        value.DefaultSleepMinutes = 30;
                    }
                    if (value.Volume < 0 || value.Volume > 1 || Double.IsNaN(value.Volume) || Double.IsInfinity(value.Volume))
                    {
                        value.Volume = 0.9;
                    }
                    if (String.IsNullOrWhiteSpace(value.ProgressDisplayMode))
                    {
                        value.ProgressDisplayMode = PlaybackProgress.ChapterMode;
                    }
                    if (!String.Equals(value.AppearanceMode, "Dark", StringComparison.OrdinalIgnoreCase)
                        && !String.Equals(value.AppearanceMode, "Light", StringComparison.OrdinalIgnoreCase))
                    {
                        value.AppearanceMode = "Light";
                    }
                    if (!String.Equals(value.ProgressDisplayMode, PlaybackProgress.ChapterMode, StringComparison.OrdinalIgnoreCase)
                        && !String.Equals(value.ProgressDisplayMode, PlaybackProgress.BookMode, StringComparison.OrdinalIgnoreCase))
                    {
                        value.ProgressDisplayMode = PlaybackProgress.ChapterMode;
                    }
                    return value;
                }
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static void Save(string path, AppSettings settings)
        {
            var directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var serializer = new DataContractJsonSerializer(typeof(AppSettings));
            using (var stream = File.Create(path))
            {
                serializer.WriteObject(stream, settings);
            }
        }
    }
}
