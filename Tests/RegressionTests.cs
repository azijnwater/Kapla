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
            CheckEqual("absolute position includes prior chapters", 42.5, PlaybackTimeline.AbsolutePosition(tracks, 2, 12.5));
            CheckEqual("negative local position is clamped", 30.0, PlaybackTimeline.AbsolutePosition(tracks, 2, -4));
            CheckEqual("exact chapter boundary maps forward", 1, PlaybackTimeline.FindTrack(tracks, 10));
            CheckEqual("second boundary maps forward", 2, PlaybackTimeline.FindTrack(tracks, 30));
            CheckEqual("end maps to final track", 2, PlaybackTimeline.FindTrack(tracks, 60));
            CheckEqual("negative position maps to first track", 0, PlaybackTimeline.FindTrack(tracks, -10));
            CheckEqual("NaN position maps to first track", 0, PlaybackTimeline.FindTrack(tracks, Double.NaN));
            CheckEqual("missing timeline has zero duration", 0.0, PlaybackTimeline.TotalDuration(null));
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

            var chaptersWithGap = new List<KoboChapter>
            {
                new KoboChapter(),
                null,
                new KoboChapter()
            };
            PlaybackTimeline.AlignChapters(chaptersWithGap, tracks);
            CheckEqual("missing chapter does not shift later chapter", 35.0, chaptersWithGap[2].StartSeconds);

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
            CheckEqual("negative playback position is clamped", 0.0, PlaybackProgress.Calculate(-5, 65, chapters, PlaybackProgress.BookMode).AbsoluteSeconds);
            CheckEqual("past-end playback position is clamped", 65.0, PlaybackProgress.Calculate(100, 65, chapters, PlaybackProgress.BookMode).AbsoluteSeconds);
            CheckEqual("invalid playback values recover safely", 0.0, PlaybackProgress.Calculate(Double.NaN, Double.PositiveInfinity, chapters, PlaybackProgress.BookMode).AbsoluteSeconds);
            CheckEqual("invalid slider value recovers safely", chapterWindow.StartSeconds, PlaybackProgress.ToAbsolute(Double.NaN, chapterWindow));
        }

        private static void SleepTimerTests()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var timer = new SleepTimerState();
            timer.StartDuration(now, TimeSpan.FromSeconds(2));
            Check("duration sleep timer active", timer.IsActive);
            CheckEqual("duration sleep timer keeps selected duration", 2.0, timer.Duration.Value.TotalSeconds);
            Check("duration sleep timer waits", !timer.HasExpired(now.AddSeconds(1), 0));
            Check("duration sleep timer expires", timer.HasExpired(now.AddSeconds(2), 0));
            timer.StartEndOfChapter(120, 90);
            Check("restarting sleep timer replaces previous mode", timer.Mode == SleepTimerMode.EndOfChapter && !timer.ExpiresUtc.HasValue);
            Check("end-of-chapter clears selected duration", !timer.Duration.HasValue);
            Check("end-of-chapter timer waits", !timer.HasExpired(now, 119));
            Check("end-of-chapter timer expires", timer.HasExpired(now, 120));
            timer.Cancel();
            Check("sleep timer cancellation", !timer.IsActive);
            Check("sleep timer cancellation clears duration", !timer.Duration.HasValue);
        }

        private static void KoboSyncPolicyTests()
        {
            Check("first Kobo progress queues", KoboSyncPolicy.IsMeaningfulProgress(10, -1, 30));
            Check("small Kobo progress is debounced", !KoboSyncPolicy.IsMeaningfulProgress(35, 10, 30));
            Check("meaningful Kobo progress queues", KoboSyncPolicy.IsMeaningfulProgress(40, 10, 30));
            Check("meaningful rewind queues", KoboSyncPolicy.IsMeaningfulProgress(60, 100, 30));
            Check("invalid Kobo progress is ignored", !KoboSyncPolicy.IsMeaningfulProgress(Double.NaN, 10, 30));
            Check("negative Kobo progress is ignored", !KoboSyncPolicy.IsMeaningfulProgress(-1, 10, 30));
            Check("Kobo progress threshold has a one-second floor", !KoboSyncPolicy.IsMeaningfulProgress(10.5, 10, 0));
            CheckEqual("Kobo progress keeps fractional position", 12.5, KoboSyncPolicy.ProgressPercent(75, 600));
            CheckEqual("Kobo request uses whole-number progress", 13, KoboSyncPolicy.KoboProgressPercent(75, 600));
            CheckEqual("Kobo request clamps completed progress", 100, KoboSyncPolicy.KoboProgressPercent(700, 600));
            CheckEqual("Kobo progress sync cooldown", TimeSpan.FromMinutes(2), KoboSyncPolicy.ProgressSyncCooldown);
            CheckEqual("first retry delay", TimeSpan.FromSeconds(5), KoboSyncPolicy.RetryDelay(1));
            CheckEqual("nonpositive failure count uses first retry delay", TimeSpan.FromSeconds(5), KoboSyncPolicy.RetryDelay(0));
            CheckEqual("retry delay grows", TimeSpan.FromSeconds(40), KoboSyncPolicy.RetryDelay(4));
            CheckEqual("retry delay is bounded", TimeSpan.FromSeconds(160), KoboSyncPolicy.RetryDelay(20));
            CheckEqual("entitlement id is preferred for progress", "entitlement", KoboSyncPolicy.PreferredProgressId("entitlement", "revision"));
            CheckEqual("revision id remains a migration fallback", "revision", KoboSyncPolicy.PreferredProgressId(null, "revision"));
            CheckEqual("blank entitlement id uses revision fallback", "revision", KoboSyncPolicy.PreferredProgressId("  ", "revision"));
            CheckEqual("progress upload requires entitlement id", "entitlement", KoboSyncPolicy.ProgressUploadId(" entitlement "));
            CheckEqual<string>("progress upload never falls back to revision", null, KoboSyncPolicy.ProgressUploadId(null));
            CheckEqual<string>("blank progress entitlement is not uploaded", null, KoboSyncPolicy.ProgressUploadId("  "));
            var audioLocation = KoboSyncPolicy.AudioTimestampLocation(12.345);
            CheckEqual("Kobo audiobook location type", "AudioTimestamp", Convert.ToString(audioLocation["Type"]));
            CheckEqual("Kobo audiobook location keeps exact seconds", "12.345", Convert.ToString(audioLocation["Value"]));
            CheckEqual("Kobo audiobook location has empty source", String.Empty, Convert.ToString(audioLocation["Source"]));
            CheckEqual("invalid audiobook location is clamped", "0", Convert.ToString(KoboSyncPolicy.AudioTimestampLocation(Double.NaN)["Value"]));
            CheckEqual("tiny positive progress is reading", "Reading", KoboSyncPolicy.ReadingStatus(0.01, 100));
            CheckEqual("zero progress is ready", "ReadyToRead", KoboSyncPolicy.ReadingStatus(0, 100));
            CheckEqual("completed progress is finished", "Finished", KoboSyncPolicy.ReadingStatus(100, 100));
            CheckEqual("rounded 99 percent is not prematurely finished", "Reading", KoboSyncPolicy.ReadingStatus(99.4, 100));
        }

        private static void KoboEndpointSecurityTests()
        {
            var token = "fake-access-token";
            var userKey = "fake-user-key";
            var store = new Uri("https://storeapi.kobo.com/v1/library/sync");
            var auth = new Uri("https://auth.kobobooks.com/ActivateOnWeb");
            var lookalike = new Uri("https://evilkobo.com/content");
            var nestedLookalike = new Uri("https://kobo.com.attacker.example/content");
            var apiLookalike = new Uri("https://api.kobo.com.attacker.example/content");
            var thirdParty = new Uri("https://cdn.example.com/audio.mp3");

            var storeHeaders = KoboEndpointPolicy.BuildCredentialHeaders(store, token, userKey);
            Check("storeapi receives access token", storeHeaders.ContainsKey("Authorization"));
            Check("storeapi receives user key", storeHeaders.ContainsKey("x-kobo-userkey"));
            var userKeyOnlyHeaders = KoboEndpointPolicy.BuildCredentialHeaders(store, null, userKey);
            Check("storeapi supports user-key fallback", userKeyOnlyHeaders.Count == 1 && userKeyOnlyHeaders.ContainsKey("x-kobo-userkey"));
            Check("auth receives no bearer token", !KoboEndpointPolicy.BuildCredentialHeaders(auth, token, userKey).ContainsKey("Authorization"));
            Check("evilkobo receives no credentials", KoboEndpointPolicy.BuildCredentialHeaders(lookalike, token, userKey).Count == 0);
            Check("nested lookalike receives no credentials", KoboEndpointPolicy.BuildCredentialHeaders(nestedLookalike, token, userKey).Count == 0);
            Check("api lookalike receives no credentials", KoboEndpointPolicy.BuildCredentialHeaders(apiLookalike, token, userKey).Count == 0);
            Check("third-party resource receives no credentials", KoboEndpointPolicy.BuildCredentialHeaders(thirdParty, token, userKey).Count == 0);
            Check("HTTP destination receives no bearer token", KoboEndpointPolicy.BuildCredentialHeaders(new Uri("http://storeapi.kobo.com"), token, userKey).Count == 0);
            Check("localhost is rejected", KoboEndpointPolicy.Validate(new Uri("https://localhost/resource"), KoboEndpointKind.Resource) != null);
            Check("loopback is rejected", KoboEndpointPolicy.Validate(new Uri("https://127.0.0.1/resource"), KoboEndpointKind.Resource) != null);
            Check("private IPv4 is rejected", KoboEndpointPolicy.Validate(new Uri("https://192.168.1.10/resource"), KoboEndpointKind.Resource) != null);
            Check("private IPv6 is rejected", KoboEndpointPolicy.Validate(new Uri("https://[fd00::1]/resource"), KoboEndpointKind.Resource) != null);
            Check("unsupported scheme is rejected", KoboEndpointPolicy.Validate(new Uri("file:///resource"), KoboEndpointKind.Resource) != null);
            Check("malformed URI is rejected", KoboEndpointPolicy.Validate(KoboEndpointPolicy.CreateUri("not a URI"), KoboEndpointKind.Resource) != null);
            Check("API lookalike is not a trusted API", KoboEndpointPolicy.Validate(apiLookalike, KoboEndpointKind.Api) != null);
            Check("redirect destination is anonymous", KoboEndpointPolicy.BuildCredentialHeaders(thirdParty, token, userKey).Count == 0);
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
                AppearanceMode = "Dark",
                ShowCoverArtwork = false,
                ProgressDisplayMode = PlaybackProgress.BookMode,
                LibraryFolders = new List<string> { "one", "two" }
            };
            AppSettingsStore.Save(path, settings);
            var serializedSettings = File.ReadAllText(path);
            Check("obsolete window-size setting is not persisted", serializedSettings.IndexOf("RememberWindowSize", StringComparison.OrdinalIgnoreCase) < 0);
            Check("obsolete accent setting is not persisted", serializedSettings.IndexOf("AccentColor", StringComparison.OrdinalIgnoreCase) < 0);
            var loaded = AppSettingsStore.Load(path);
            CheckEqual("settings speed round-trip", 1.5, loaded.DefaultPlaybackSpeed);
            CheckEqual("settings rewind round-trip", 30, loaded.RewindSeconds);
            CheckEqual("settings volume round-trip", 0.42, loaded.Volume);
            CheckEqual("settings folders round-trip", 2, loaded.LibraryFolders.Count);
            CheckEqual("settings theme round-trip", "Dark", loaded.AppearanceMode);
            CheckEqual("settings cover visibility round-trip", false, loaded.ShowCoverArtwork);
            CheckEqual("settings progress mode round-trip", PlaybackProgress.BookMode, loaded.ProgressDisplayMode);
            CheckEqual("settings default library order is stable", "Installation order", new AppSettings().LibrarySort);
            File.WriteAllText(path, "not json");
            var repaired = AppSettingsStore.Load(path);
            CheckEqual("corrupt settings recover default speed", 1.0, repaired.DefaultPlaybackSpeed);
            CheckEqual("corrupt settings recover default rewind", 15, repaired.RewindSeconds);

            var invalidPath = TempPath(root, "invalid-settings.json");
            AppSettingsStore.Save(invalidPath, new AppSettings
            {
                DefaultPlaybackSpeed = 0,
                RewindSeconds = 0,
                ForwardSeconds = -1,
                DefaultSleepMinutes = 0,
                Volume = 2,
                LibraryFolders = null,
                LibrarySort = "Recently played",
                AppearanceMode = "Sepia",
                ProgressDisplayMode = "Unknown"
            });
            var normalized = AppSettingsStore.Load(invalidPath);
            CheckEqual("invalid settings recover default speed", 1.0, normalized.DefaultPlaybackSpeed);
            CheckEqual("invalid settings recover default rewind", 15, normalized.RewindSeconds);
            CheckEqual("invalid settings recover default forward skip", 15, normalized.ForwardSeconds);
            CheckEqual("invalid settings recover default sleep timer", 30, normalized.DefaultSleepMinutes);
            CheckEqual("invalid settings recover default volume", 0.9, normalized.Volume);
            CheckEqual("missing settings folders recover empty collection", 0, normalized.LibraryFolders.Count);
            CheckEqual("invalid settings theme recovers light", "Light", normalized.AppearanceMode);
            CheckEqual("invalid progress mode recovers chapter mode", PlaybackProgress.ChapterMode, normalized.ProgressDisplayMode);
            CheckEqual("legacy recently played order migrates", "Installation order", normalized.LibrarySort);

            var missing = AppSettingsStore.Load(TempPath(root, "missing-settings.json"));
            CheckEqual("missing settings use default speed", 1.0, missing.DefaultPlaybackSpeed);
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
            CheckEqual("book time left", "Time left 0:08", book.TimeLeftText);
            book.Finished = true;
            CheckEqual("finished book time left", "No time left", book.TimeLeftText);
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
                        KoboEntitlementId = "entitlement-id",
                        PositionSeconds = 42,
                        HasLocalPlaybackPosition = true,
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
            Check("library local-position marker round-trip", loaded.Books[0].HasLocalPlaybackPosition);
            CheckEqual("library entitlement round-trip", "entitlement-id", loaded.Books[0].KoboEntitlementId);
            CheckEqual("library chapter round-trip", "Start", loaded.Books[0].Chapters[0].Title);

            string serialized;
            using (var memory = new MemoryStream())
            {
                serializer.WriteObject(memory, store);
                serialized = Encoding.UTF8.GetString(memory.ToArray());
            }
            var entitlementField = ",\"koboEntitlementId\":\"entitlement-id\"";
            var legacyJson = serialized.Replace(entitlementField, String.Empty);
            LibraryStore legacyLoaded;
            using (var memory = new MemoryStream(Encoding.UTF8.GetBytes(legacyJson)))
            {
                legacyLoaded = serializer.ReadObject(memory) as LibraryStore;
            }
            CheckEqual("legacy library without entitlement id loads", 1, legacyLoaded.Books.Count);
            CheckEqual<string>("legacy entitlement defaults safely", null, legacyLoaded.Books[0].KoboEntitlementId);
        }

        private static void KoboCacheTests(string root)
        {
            var book = new KoboRemoteBook
            {
                RevisionId = "cached-revision",
                ProductId = "cached-book",
                Title = "Cached book",
                Author = "Cached author",
                CoverUrl = "https://cdn.example.com/cover.jpg"
            };
            var directory = Path.Combine(root, "KoboBooks", "cached-book");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "000.mp3"), new byte[12000]);
            File.WriteAllBytes(Path.Combine(directory, "001.mp3"), new byte[24000]);
            File.WriteAllBytes(Path.Combine(directory, "cover.jpg"), new byte[] { 1, 2, 3 });
            KoboCachedAudiobook.MarkComplete(book, root);
            var restored = KoboCachedAudiobook.TryRestore(book, root);
            Check("cached Kobo audiobook is restored", restored != null);
            CheckEqual("cached Kobo track count", 2, restored.Tracks.Count);
            CheckEqual("cached Kobo output is first track", "000.mp3", Path.GetFileName(restored.OutputPath));
            CheckEqual("cached Kobo chapter count", 2, restored.Chapters.Count);
            Check("cached Kobo duration is estimated", restored.Tracks.Sum(track => track.DurationSeconds) > 0);

            File.Delete(Path.Combine(directory, ".download-complete"));
            CheckEqual<KoboDownloadResult>("cache without completion marker is rejected", null, KoboCachedAudiobook.TryRestore(book, root));
            KoboCachedAudiobook.MarkComplete(book, root);
            File.Delete(Path.Combine(directory, "001.mp3"));
            File.WriteAllBytes(Path.Combine(directory, "002.mp3"), new byte[24000]);
            CheckEqual<KoboDownloadResult>("incomplete Kobo cache is rejected", null, KoboCachedAudiobook.TryRestore(book, root));

            File.WriteAllBytes(Path.Combine(directory, "001.mp3"), new byte[24000]);
            KoboCachedAudiobook.MarkComplete(new KoboRemoteBook
            {
                RevisionId = "different-revision",
                ProductId = book.ProductId
            }, root);
            CheckEqual<KoboDownloadResult>("cache for a different Kobo revision is rejected", null, KoboCachedAudiobook.TryRestore(book, root));
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
                KoboEndpointSecurityTests();
                SettingsTests(root);
                MetadataTests(root);
                KoboMetadataTests();
                LibraryTests(root);
                KoboCacheTests(root);
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
