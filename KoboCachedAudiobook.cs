using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kapla
{
    internal static class KoboCachedAudiobook
    {
        public static KoboDownloadResult TryRestore(KoboRemoteBook book, string rootDirectory)
        {
            if (book == null || String.IsNullOrWhiteSpace(book.ProductId) || String.IsNullOrWhiteSpace(rootDirectory))
            {
                return null;
            }

            var cacheRoot = Path.GetFullPath(Path.Combine(rootDirectory, "KoboBooks"));
            var directory = Path.GetFullPath(Path.Combine(cacheRoot, SafeDirectoryName(book.ProductId)));
            var cachePrefix = cacheRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!directory.StartsWith(cachePrefix, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(directory))
            {
                return null;
            }

            var numberedTracks = Directory.GetFiles(directory, "*.mp3")
                .Select(path => new { Path = path, Number = ParseTrackNumber(Path.GetFileNameWithoutExtension(path)) })
                .Where(value => value.Number.HasValue && new FileInfo(value.Path).Length > 0)
                .OrderBy(value => value.Number.Value)
                .ToList();
            if (numberedTracks.Count == 0 || numberedTracks[0].Number.Value != 0)
            {
                return null;
            }

            for (var index = 0; index < numberedTracks.Count; index++)
            {
                if (numberedTracks[index].Number.Value != index)
                {
                    return null;
                }
            }

            var result = new KoboDownloadResult
            {
                OutputPath = numberedTracks[0].Path,
                CoverPath = FindCover(directory),
                CoverUrl = book.CoverUrl,
                Author = book.Author,
                Narrator = book.Narrator,
                Series = book.Series,
                Publisher = book.Publisher,
                Description = book.Description,
                ReleaseDate = book.ReleaseDate
            };
            var offset = 0.0;
            for (var index = 0; index < numberedTracks.Count; index++)
            {
                var duration = EstimateDuration(numberedTracks[index].Path);
                var title = "Chapter " + (index + 1);
                result.Tracks.Add(new KoboTrack
                {
                    Path = numberedTracks[index].Path,
                    DurationSeconds = duration,
                    Title = title
                });
                result.Chapters.Add(new KoboChapter
                {
                    Title = title,
                    StartSeconds = offset,
                    EndSeconds = offset + duration
                });
                offset += duration;
            }
            return result;
        }

        private static string SafeDirectoryName(string value)
        {
            var result = String.IsNullOrWhiteSpace(value) ? "kobo-audiobook" : value;
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalid, '_');
            }
            result = result.Trim().TrimEnd('.');
            return result.Length == 0 || result == "." || result == ".." ? "kobo-audiobook" : result;
        }

        private static int? ParseTrackNumber(string value)
        {
            int number;
            return Int32.TryParse(value, out number) && number >= 0 && number < 10000 ? (int?)number : null;
        }

        private static string FindCover(string directory)
        {
            return Directory.GetFiles(directory, "cover.*")
                .FirstOrDefault(path => new[] { ".jpg", ".jpeg", ".png", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        }

        private static double EstimateDuration(string path)
        {
            try
            {
                return Math.Max(0, new FileInfo(path).Length * 8.0 / 96000.0);
            }
            catch
            {
                return 0;
            }
        }
    }
}
