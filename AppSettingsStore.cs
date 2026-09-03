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
                    if (value.DefaultPlaybackSpeed <= 0)
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
                    if (value.Volume < 0 || value.Volume > 1)
                    {
                        value.Volume = 0.9;
                    }
                    if (String.IsNullOrWhiteSpace(value.AccentColor))
                    {
                        value.AccentColor = "#7DD3FC";
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
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var serializer = new DataContractJsonSerializer(typeof(AppSettings));
            using (var stream = File.Create(path))
            {
                serializer.WriteObject(stream, settings);
            }
        }
    }
}
