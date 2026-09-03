using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Kapla.Tests
{
    internal static class RegressionTests
    {
        private static int failures;

        private static void Check(string name, bool condition)
        {
            if (condition)
            {
                Console.WriteLine("PASS " + name);
            }
            else
            {
                Console.WriteLine("FAIL " + name);
                failures++;
            }
        }

        private static void CheckEqual<T>(string name, T expected, T actual)
        {
            Check(name + " (expected " + expected + ", actual " + actual + ")", EqualityComparer<T>.Default.Equals(expected, actual));
        }

        private static string TempPath(string root, string name)
        {
            return Path.Combine(root, name);
        }

        private static byte[] Synchsafe(int value)
        {
            return new[]
            {
                (byte)((value >> 21) & 0x7F),
                (byte)((value >> 14) & 0x7F),
                (byte)((value >> 7) & 0x7F),
                (byte)(value & 0x7F)
            };
        }

        private static byte[] Id3TextFrame(string id, string value)
        {
            var text = Encoding.UTF8.GetBytes(value);
            var payload = new byte[text.Length + 1];
            payload[0] = 3;
            Buffer.BlockCopy(text, 0, payload, 1, text.Length);
            var frame = new List<byte>(Encoding.ASCII.GetBytes(id));
            frame.AddRange(Synchsafe(payload.Length));
            frame.Add(0);
            frame.Add(0);
            frame.AddRange(payload);
            return frame.ToArray();
        }

        private static byte[] BuildRichMp3Stub()
        {
            var tag = new List<byte>();
            tag.AddRange(Id3TextFrame("TIT2", "QA audiobook"));
            tag.AddRange(Id3TextFrame("TPE1", "QA narrator"));
            tag.AddRange(Id3TextFrame("TALB", "QA collection"));
            tag.AddRange(Id3TextFrame("TLEN", "123000"));
            var result = new List<byte>(Encoding.ASCII.GetBytes("ID3"));
            result.Add(4);
            result.Add(0);
            result.Add(0);
            result.AddRange(Synchsafe(tag.Count));
            result.AddRange(tag);
            result.AddRange(new byte[64]);
            return result.ToArray();
        }

        private static void TimelineTests()
        {
            var tracks = new List<KoboTrack>
            {
                new KoboTrack { Title = "One", DurationSeconds = 10 },
                new KoboTrack { Title = "Two", DurationSeconds = 20 },
                new KoboTrack { Title = "Three", DurationSeconds = 30 }
            };
            CheckEqual("timeline total", 60.0, PlaybackTimeline.TotalDuration(tracks));
            CheckEqual("track start", 30.0, PlaybackTimeline.TrackStart(tracks, 2));
            CheckEqual("exact chapter boundary maps forward", 1, PlaybackTimeline.FindTrack(tracks, 10));
            CheckEqual("second boundary maps forward", 2, PlaybackTimeline.FindTrack(tracks, 30));
            CheckEqual("end maps to final track", 2, PlaybackTimeline.FindTrack(tracks, 60));
            var chapters = new List<KoboChapter>
            {
                new KoboChapter { Title = "Chapter One", StartSeconds = 0, EndSeconds = 10 },
                new KoboChapter { Title = "Chapter Two", StartSeconds = 10, EndSeconds = 30 },
                new KoboChapter { Title = "Chapter Three", StartSeconds = 30, EndSeconds = 60 }
            };
            tracks[1].DurationSeconds = 25;
            PlaybackTimeline.AlignChapters(chapters, tracks);
            CheckEqual("chapter correction preserves title", "Chapter Two", chapters[1].Title);
            CheckEqual("chapter correction updates third start", 35.0, chapters[2].StartSeconds);
            CheckEqual("chapter correction updates final end", 65.0, chapters[2].EndSeconds);

            var chapterWindow = PlaybackProgress.Calculate(16, 65, chapters, PlaybackProgress.ChapterMode);
            Check("chapter progress selected", chapterWindow.IsChapterRelative);
            CheckEqual("chapter progress start", 10.0, chapterWindow.StartSeconds);
            CheckEqual("chapter progress end", 35.0, chapterWindow.EndSeconds);
            CheckEqual("chapter progress elapsed", 6.0, chapterWindow.ElapsedSeconds);
            CheckEqual("chapter progress remaining", 19.0, chapterWindow.RemainingSeconds);
            CheckEqual("chapter seek remains absolute", 20.0, PlaybackProgress.ToAbsolute(20, chapterWindow));

            var wholeWindow = PlaybackProgress.Calculate(16, 65, chapters, PlaybackProgress.BookMode);
            Check("whole-book progress selected", !wholeWindow.IsChapterRelative);
            CheckEqual("whole-book progress start", 0.0, wholeWindow.StartSeconds);
            CheckEqual("whole-book progress end", 65.0, wholeWindow.EndSeconds);
            CheckEqual("whole-book elapsed", 16.0, wholeWindow.ElapsedSeconds);

            var fallbackWindow = PlaybackProgress.Calculate(16, 65, new List<KoboChapter>(), PlaybackProgress.ChapterMode);
            Check("chapter progress falls back without chapters", !fallbackWindow.IsChapterRelative);
        }

        private static void SleepTimerTests()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var timer = new SleepTimerState();
            timer.StartDuration(now, TimeSpan.FromSeconds(2));
            Check("duration sleep timer active", timer.IsActive);
            Check("duration sleep timer waits", !timer.HasExpired(now.AddSeconds(1), 0));
            Check("duration sleep timer expires", timer.HasExpired(now.AddSeconds(2), 0));
            timer.StartEndOfChapter(120, 90);
            Check("restarting sleep timer replaces previous mode", timer.Mode == SleepTimerMode.EndOfChapter && !timer.ExpiresUtc.HasValue);
            Check("end-of-chapter timer waits", !timer.HasExpired(now, 119));
            Check("end-of-chapter timer expires", timer.HasExpired(now, 120));
            timer.Cancel();
            Check("sleep timer cancellation", !timer.IsActive);
        }

        private static void KoboSyncPolicyTests()
        {
            Check("first Kobo progress queues", KoboSyncPolicy.IsMeaningfulProgress(10, -1, 30));
            Check("small Kobo progress is debounced", !KoboSyncPolicy.IsMeaningfulProgress(35, 10, 30));
            Check("meaningful Kobo progress queues", KoboSyncPolicy.IsMeaningfulProgress(40, 10, 30));
            CheckEqual("first retry delay", TimeSpan.FromSeconds(5), KoboSyncPolicy.RetryDelay(1));
            CheckEqual("retry delay grows", TimeSpan.FromSeconds(40), KoboSyncPolicy.RetryDelay(4));
            CheckEqual("retry delay is bounded", TimeSpan.FromSeconds(160), KoboSyncPolicy.RetryDelay(20));
        }

        private static void SettingsTests(string root)
        {
            var path = TempPath(root, "settings.json");
            var settings = new AppSettings
            {
                DefaultPlaybackSpeed = 1.5,
                RewindSeconds = 30,
                ForwardSeconds = 10,
                Volume = 0.42,
                AccentColor = "#75CFFF",
                AppearanceMode = "Dark",
                ShowCoverArtwork = false,
                ProgressDisplayMode = PlaybackProgress.BookMode,
                RememberWindowSize = true,
                SavedWindowWidth = 777,
                LibraryFolders = new List<string> { "one", "two" }
            };
            AppSettingsStore.Save(path, settings);
            var loaded = AppSettingsStore.Load(path);
            CheckEqual("settings speed round-trip", 1.5, loaded.DefaultPlaybackSpeed);
            CheckEqual("settings rewind round-trip", 30, loaded.RewindSeconds);
            CheckEqual("settings volume round-trip", 0.42, loaded.Volume);
            CheckEqual("settings folders round-trip", 2, loaded.LibraryFolders.Count);
            CheckEqual("settings theme round-trip", "Dark", loaded.AppearanceMode);
            CheckEqual("settings cover visibility round-trip", false, loaded.ShowCoverArtwork);
            CheckEqual("settings progress mode round-trip", PlaybackProgress.BookMode, loaded.ProgressDisplayMode);
            CheckEqual("settings window width round-trip", 777.0, loaded.SavedWindowWidth);
            File.WriteAllText(path, "not json");
            var repaired = AppSettingsStore.Load(path);
            CheckEqual("corrupt settings recover default speed", 1.0, repaired.DefaultPlaybackSpeed);
            CheckEqual("corrupt settings recover default rewind", 15, repaired.RewindSeconds);
        }

        private static void MetadataTests(string root)
        {
            var richPath = TempPath(root, "rich.mp3");
            File.WriteAllBytes(richPath, BuildRichMp3Stub());
            var rich = LocalAudiobookMetadata.Read(richPath);
            CheckEqual("ID3 title", "QA audiobook", rich.Title);
            CheckEqual("ID3 author", "QA narrator", rich.Author);
            CheckEqual("ID3 album", "QA collection", rich.Album);
            CheckEqual("ID3 duration", 123.0, rich.DurationSeconds);
            var missingArtPath = TempPath(root, "missing-art.mp3");
            File.WriteAllBytes(missingArtPath, Encoding.ASCII.GetBytes("not an mp3"));
            var missingArt = LocalAudiobookMetadata.Read(missingArtPath);
            Check("missing artwork has no cover without crashing", missingArt.CoverBytes == null);
            Check("malformed metadata returns chapter collection", missingArt.Chapters != null);
            var book = new BookEntry { Title = "Fallback", PositionSeconds = 12, DurationSeconds = 20 };
            CheckEqual("book progress fallback", "Resume at 0:12", book.ProgressText);
            Check("missing cover source falls back safely", String.IsNullOrWhiteSpace(book.CoverSource));
        }

        private static void KoboMetadataTests()
        {
            var direct = new Dictionary<string, object>
            {
                { "Author", "Tommy Wieringa" }
            };
            CheckEqual("Kobo direct author", "Tommy Wieringa", KoboMetadata.FindAuthor(direct));

            var contributors = new Dictionary<string, object>
            {
                {
                    "Contributors", new object[]
                    {
                        new Dictionary<string, object> { { "Name", "The narrator" }, { "Role", "Narrator" } },
                        new Dictionary<string, object> { { "DisplayName", "Tommy Wieringa" }, { "Role", "Author" } }
                    }
                }
            };
            CheckEqual("Kobo contributor author skips narrator", "Tommy Wieringa", KoboMetadata.FindAuthor(contributors));

            var nested = new Dictionary<string, object>
            {
                {
                    "BookMetadata", new Dictionary<string, object>
                    {
                        { "Creator", new Dictionary<string, object> { { "FirstName", "Tommy" }, { "LastName", "Wieringa" } } }
                    }
                }
            };
            CheckEqual("Kobo nested creator author", "Tommy Wieringa", KoboMetadata.FindAuthor(nested));
            CheckEqual("Kobo author fallback", "Unknown author", KoboMetadata.PreferAuthor("Kobo audiobook", null));
            CheckEqual("Kobo author keeps existing", "Existing author", KoboMetadata.PreferAuthor("Unknown author", "Existing author"));
        }

        private static void LibraryTests(string root)
        {
            var path = TempPath(root, "library.json");
            var store = new LibraryStore
            {
                Books = new List<BookEntry>
                {
                    new BookEntry
                    {
                        Title = "Library book",
                        Author = "Author",
                        PositionSeconds = 42,
                        Tracks = new List<KoboTrack> { new KoboTrack { Path = "book.mp3", DurationSeconds = 100 } },
                        Chapters = new List<KoboChapter> { new KoboChapter { Title = "Start", StartSeconds = 0, EndSeconds = 100 } }
                    }
                }
            };
            var serializer = new DataContractJsonSerializer(typeof(LibraryStore));
            using (var stream = File.Create(path)) serializer.WriteObject(stream, store);
            LibraryStore loaded;
            using (var stream = File.OpenRead(path)) loaded = serializer.ReadObject(stream) as LibraryStore;
            CheckEqual("library book round-trip", 1, loaded.Books.Count);
            CheckEqual("library position round-trip", 42.0, loaded.Books[0].PositionSeconds);
            CheckEqual("library chapter round-trip", "Start", loaded.Books[0].Chapters[0].Title);
        }

        public static int Main(string[] args)
        {
            var root = Path.Combine(Path.GetTempPath(), "KaplaRegressionTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                TimelineTests();
                SleepTimerTests();
                KoboSyncPolicyTests();
                SettingsTests(root);
                MetadataTests(root);
                KoboMetadataTests();
                LibraryTests(root);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
            Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : failures + " TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }
    }
}
