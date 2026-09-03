using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

namespace Kapla
{
    internal sealed class KoboActivation
    {
        public string Code { get; set; }
        public string PollUrl { get; set; }
        public string DeviceId { get; set; }
        public string SerialNumber { get; set; }
    }

    internal sealed class KoboHttpResult
    {
        public object Data { get; set; }
        public HttpResponseMessage Response { get; set; }
    }

    internal sealed class KoboProtectedException : Exception
    {
        public KoboProtectedException(string message) : base(message) { }
    }

    internal static class KoboSessionStore
    {
        private const string FileName = "kobo-session.bin";

        public static void Save(string directory, KoboSession session)
        {
            Directory.CreateDirectory(directory);
            var serializer = new JavaScriptSerializer();
            var plain = Encoding.UTF8.GetBytes(serializer.Serialize(session));
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path.Combine(directory, FileName), encrypted);
        }

        public static KoboSession Load(string directory)
        {
            try
            {
                var path = Path.Combine(directory, FileName);
                if (!File.Exists(path))
                {
                    return null;
                }

                var encrypted = File.ReadAllBytes(path);
                var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return new JavaScriptSerializer().Deserialize<KoboSession>(Encoding.UTF8.GetString(plain));
            }
            catch
            {
                return null;
            }
        }

        public static void Clear(string directory)
        {
            try
            {
                var path = Path.Combine(directory, FileName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Account UI reports the disconnected state even if Windows delays deletion.
            }
        }
    }

    internal sealed class KoboClient : IDisposable
    {
        private const string StoreApi = "https://storeapi.kobo.com";
        private const string AuthApi = "https://auth.kobobooks.com";
        private const string PlatformId = "00000000-0000-0000-0000-000000000373";
        private const string ApplicationVersion = "4.38.23171";
        private const string UserAgent = "Mozilla/5.0 (Linux; U; Android 2.0; en-us;) AppleWebKit/538.1 (KHTML, like Gecko) Version/4.0 Mobile Safari/538.1 (Kobo Touch 0373/4.38.23171)";

        private readonly HttpClient http;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private Dictionary<string, object> resources = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private bool refreshing;

        static KoboClient()
        {
            // The player is built against the inbox .NET Framework compiler. Explicitly
            // selecting TLS 1.2 avoids older framework defaults negotiating TLS 1.0/1.1.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = 8;
        }

        public KoboSession Session { get; private set; }

        public KoboClient(KoboSession session)
        {
            Session = session;
            http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }

        public static KoboSession CreateNewSession()
        {
            return new KoboSession
            {
                DeviceId = RandomHex(64),
                SerialNumber = RandomHex(32)
            };
        }

        public async Task<KoboActivation> BeginActivationAsync()
        {
            var query = "?pwspid=" + Uri.EscapeDataString(PlatformId)
                + "&wsa=Kobo"
                + "&pwsdid=" + Uri.EscapeDataString(Session.DeviceId)
                + "&pwsav=" + Uri.EscapeDataString(ApplicationVersion)
                + "&pwsdm=" + Uri.EscapeDataString(PlatformId)
                + "&pwspos=3.0.35%2B&pwspov=NA";
            var html = await http.GetStringAsync(AuthApi + "/ActivateOnWeb" + query).ConfigureAwait(false);
            var pollMatch = Regex.Match(html, "data-poll-endpoint\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
            var code = ExtractActivationCode(html);
            if (!pollMatch.Success || String.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException("Kobo's activation page changed and did not provide a device code.");
            }

            return new KoboActivation
            {
                Code = code,
                PollUrl = AuthApi + HttpUtility.HtmlDecode(pollMatch.Groups[1].Value),
                DeviceId = Session.DeviceId,
                SerialNumber = Session.SerialNumber
            };
        }

        public async Task CompleteActivationAsync(KoboActivation activation)
        {
            for (var attempt = 0; attempt < 60; attempt++)
            {
                var response = await http.PostAsync(activation.PollUrl, null).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var payload = json.DeserializeObject(await response.Content.ReadAsStringAsync().ConfigureAwait(false)) as Dictionary<string, object>;
                if (payload == null || !String.Equals(GetString(payload, "Status"), "Complete", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    continue;
                }

                var redirect = GetString(payload, "RedirectUrl");
                var query = ParseQuery(redirect);
                Session.Email = GetValue(query, "email");
                Session.UserId = GetValue(query, "userId");
                Session.UserKey = GetValue(query, "userKey");
                if (String.IsNullOrWhiteSpace(Session.UserKey))
                {
                    throw new InvalidOperationException("Kobo activation completed without a user key.");
                }
                break;
            }

            if (String.IsNullOrWhiteSpace(Session.UserKey))
            {
                throw new TimeoutException("Kobo activation timed out. You can try connecting again.");
            }

            var devicePayload = new Dictionary<string, object>
            {
                { "AffiliateName", "Kobo" },
                { "AppVersion", ApplicationVersion },
                { "ClientKey", Convert.ToBase64String(Encoding.UTF8.GetBytes(PlatformId)) },
                { "DeviceId", Session.DeviceId },
                { "PlatformId", PlatformId },
                { "SerialNumber", Session.SerialNumber },
                { "UserKey", Session.UserKey }
            };
            var auth = await PostJsonAsync(StoreApi + "/v1/auth/device", devicePayload, false).ConfigureAwait(false) as Dictionary<string, object>;
            var tokenType = GetString(auth, "TokenType");
            Session.AccessToken = GetString(auth, "AccessToken");
            Session.RefreshToken = GetString(auth, "RefreshToken");
            if (!String.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase) || String.IsNullOrWhiteSpace(Session.AccessToken))
            {
                throw new InvalidOperationException("Kobo did not return a usable access token.");
            }
        }

        public async Task<List<KoboRemoteBook>> GetAudiobooksAsync()
        {
            await LoadResourcesAsync().ConfigureAwait(false);
            var books = new List<KoboRemoteBook>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var syncToken = String.Empty;
            while (true)
            {
                var url = GetResource("library_sync");
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!String.IsNullOrWhiteSpace(syncToken))
                {
                    request.Headers.Add("x-kobo-synctoken", syncToken);
                }
                var result = await SendAuthorizedAsync(request).ConfigureAwait(false);
                foreach (var item in EnumerateEntitlements(result.Data))
                {
                    var metadata = FindDictionary(item, "AudiobookMetadata") ?? FindDictionary(item, "BookMetadata") ?? item;
                    if (!LooksLikeAudiobook(item, metadata))
                    {
                        continue;
                    }

                    var revisionId = FirstString(metadata, "RevisionId", "Id", "ProductId") ?? FirstString(item, "RevisionId", "Id", "ProductId");
                    if (String.IsNullOrWhiteSpace(revisionId) || !seen.Add(revisionId))
                    {
                        continue;
                    }

                    var remote = new KoboRemoteBook
                    {
                        RevisionId = revisionId,
                        ProductId = FirstString(metadata, "ProductId", "RevisionId", "Id") ?? revisionId,
                        Title = FirstString(metadata, "Title", "Name") ?? "Untitled Kobo audiobook",
                        Author = KoboMetadata.PreferAuthor(KoboMetadata.FindAuthor(metadata), KoboMetadata.FindAuthor(item)),
                        Narrator = FirstString(metadata, "Narrator", "NarratedBy", "Reader", "NarratorName"),
                        Series = FirstString(metadata, "Series", "SeriesName", "SeriesTitle"),
                        Publisher = FirstString(metadata, "Publisher", "PublisherName", "Imprint"),
                        Description = FirstString(metadata, "Description", "Synopsis", "Summary", "FullDescription"),
                        ReleaseDate = FirstString(metadata, "ReleaseDate", "PublicationDate", "PublishedDate"),
                        CoverUrl = ResolveCoverUrl(metadata),
                        ProgressPercent = FindProgressPercent(item),
                        IsProtected = HasProtectedDrm(metadata),
                        Metadata = metadata
                    };
                    books.Add(remote);
                }

                var continuation = result.Response.Headers.Contains("x-kobo-sync")
                    && String.Equals(result.Response.Headers.GetValues("x-kobo-sync").FirstOrDefault(), "continue", StringComparison.OrdinalIgnoreCase);
                if (!continuation || !result.Response.Headers.Contains("x-kobo-synctoken"))
                {
                    break;
                }
                syncToken = result.Response.Headers.GetValues("x-kobo-synctoken").FirstOrDefault();
                if (String.IsNullOrWhiteSpace(syncToken))
                {
                    break;
                }
            }

            return books.OrderBy(book => book.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<string> DownloadPlayableAudiobookAsync(KoboRemoteBook book, string rootDirectory)
        {
            var result = await DownloadKoboAudiobookAsync(book, rootDirectory, null).ConfigureAwait(false);
            return result.OutputPath;
        }

        public async Task<KoboDownloadResult> DownloadKoboAudiobookAsync(KoboRemoteBook book, string rootDirectory, IProgress<KoboDownloadProgress> progress)
        {
            ReportDownload(progress, book.Title, "Preparing download", 1, 0, 0, null);
            await LoadResourcesAsync().ConfigureAwait(false);
            var metadata = book.Metadata ?? new Dictionary<string, object>();
            ReportDownload(progress, book.Title, "Finding the audiobook manifest", 4, 0, 0, null);
            var manifestUrl = FindDownloadUrl(metadata);
            if (String.IsNullOrWhiteSpace(manifestUrl))
            {
                var resource = GetResource("audiobook").Replace("{ProductId}", Uri.EscapeDataString(book.ProductId));
                var product = await GetJsonAsync(resource).ConfigureAwait(false);
                var productDictionary = product as Dictionary<string, object>;
                if (productDictionary != null)
                {
                    metadata = productDictionary;
                }
                manifestUrl = FindDownloadUrl(metadata);
            }

            if (HasProtectedDrm(metadata))
            {
                throw new KoboProtectedException("Kobo marked this audiobook as protected. It must be played in the official Kobo app.");
            }

            book.Author = KoboMetadata.PreferAuthor(KoboMetadata.FindAuthor(metadata), book.Author);
            book.Narrator = FirstString(metadata, "Narrator", "NarratedBy", "Reader", "NarratorName") ?? book.Narrator;
            book.Series = FirstString(metadata, "Series", "SeriesName", "SeriesTitle") ?? book.Series;
            book.Publisher = FirstString(metadata, "Publisher", "PublisherName", "Imprint") ?? book.Publisher;
            book.Description = FirstString(metadata, "Description", "Synopsis", "Summary", "FullDescription") ?? book.Description;
            book.ReleaseDate = FirstString(metadata, "ReleaseDate", "PublicationDate", "PublishedDate") ?? book.ReleaseDate;
            book.CoverUrl = ResolveCoverUrl(metadata) ?? book.CoverUrl;

            if (String.IsNullOrWhiteSpace(manifestUrl))
            {
                throw new InvalidOperationException("Kobo did not return an audiobook download manifest for this title.");
            }

            manifestUrl = NormalizeKoboUrl(manifestUrl);
            ReportDownload(progress, book.Title, "Reading audiobook manifest", 8, 0, 0, null);
            var manifest = await GetJsonAsync(manifestUrl).ConfigureAwait(false);
            var spine = FindList(manifest, "Spine").ToList();
            if (spine.Count == 0)
            {
                throw new InvalidOperationException("Kobo returned an empty audiobook manifest.");
            }

            var directory = Path.Combine(rootDirectory, "KoboBooks", SafeFileName(book.ProductId));
            Directory.CreateDirectory(directory);
            var tracks = new List<KeyValuePair<Dictionary<string, object>, string>>();
            foreach (var item in spine.OfType<Dictionary<string, object>>())
            {
                var url = NormalizeKoboUrl(FirstString(item, "Url", "DownloadUrl"));
                if (String.IsNullOrWhiteSpace(url))
                {
                    continue;
                }
                tracks.Add(new KeyValuePair<Dictionary<string, object>, string>(item, url));
            }

            if (tracks.Count == 0)
            {
                throw new InvalidOperationException("Kobo's audiobook manifest did not contain playable tracks.");
            }

            ReportDownload(progress, book.Title, "Found " + tracks.Count + (tracks.Count == 1 ? " track" : " tracks"), 11, 0, tracks.Count, null);
            var partPaths = new string[tracks.Count];
            var limiter = new SemaphoreSlim(Math.Min(4, tracks.Count));
            var progressLock = new object();
            var completedBytes = new long[tracks.Count];
            var totalBytes = new long[tracks.Count];
            var completedTracks = new bool[tracks.Count];
            var lastPercent = 11;
            Action<int, long, long, bool> reportBytes = (trackIndex, completed, total, isComplete) =>
            {
                if (progress == null)
                {
                    return;
                }

                lock (progressLock)
                {
                    completedBytes[trackIndex] = completed;
                    totalBytes[trackIndex] = Math.Max(0, total);
                    completedTracks[trackIndex] = isComplete;
                    var aggregate = 0.0;
                    for (var offset = 0; offset < tracks.Count; offset++)
                    {
                        if (totalBytes[offset] > 0)
                        {
                            aggregate += Math.Min(1.0, completedBytes[offset] / (double)totalBytes[offset]);
                        }
                        else if (completedTracks[offset])
                        {
                            aggregate += 1.0;
                        }
                    }
                    var percent = DownloadPercent(0, aggregate / Math.Max(1, tracks.Count), 1);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        var detail = FormatBytes(completedBytes.Sum());
                        var allTracksTotal = totalBytes.Sum();
                        if (allTracksTotal > 0)
                        {
                            detail += " of " + FormatBytes(allTracksTotal);
                        }
                        ReportDownload(progress, book.Title, "Downloading " + tracks.Count + " tracks", percent, trackIndex + 1, tracks.Count, detail);
                    }
                }
            };

            var downloads = new List<Task>();
            for (var index = 0; index < tracks.Count; index++)
            {
                var indexCopy = index;
                var item = tracks[index].Key;
                var url = tracks[index].Value;
                var extension = FirstString(item, "FileExtension", "Extension") ?? "mp3";
                var partPath = Path.Combine(directory, index.ToString("000") + "." + SafeFileName(extension).TrimStart('.'));
                partPaths[index] = partPath;
                if (index == 0)
                {
                    ReportDownload(progress, book.Title, "Starting parallel downloads", 12, 1, tracks.Count, "Up to 4 tracks at once");
                }
                downloads.Add(DownloadTrackAsync(url, partPath, indexCopy, tracks.Count, limiter, reportBytes));
            }
            try
            {
                await Task.WhenAll(downloads).ConfigureAwait(false);
            }
            finally
            {
                limiter.Dispose();
            }

            var extensions = partPaths.Select(Path.GetExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (extensions.Count != 1 || !String.Equals(extensions[0], ".mp3", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Kobo returned multiple non-MP3 tracks. This lightweight player can import Kobo MP3 audiobooks, but cannot merge this format yet.");
            }

            var outputPath = partPaths[0];
            ReportDownload(progress, book.Title, "Finalizing Kobo chapters", 93, tracks.Count, tracks.Count, "No extra merge step needed");
            var result = new KoboDownloadResult
            {
                OutputPath = outputPath,
                Author = book.Author,
                CoverUrl = book.CoverUrl,
                Narrator = book.Narrator,
                Series = book.Series,
                Publisher = book.Publisher,
                Description = book.Description,
                ReleaseDate = book.ReleaseDate,
                Chapters = BuildChapters(manifest, tracks, partPaths)
            };
            for (var index = 0; index < partPaths.Length; index++)
            {
                result.Tracks.Add(new KoboTrack
                {
                    Path = partPaths[index],
                    DurationSeconds = EstimateMp3Duration(partPaths[index]),
                    Title = FirstString(tracks[index].Key, "Title", "ChapterTitle", "Name", "Label") ?? "Chapter " + (index + 1)
                });
            }
            ReportDownload(progress, book.Title, "Fetching cover artwork", 96, tracks.Count, tracks.Count, null);
            result.CoverPath = await DownloadCoverAsync(book, directory).ConfigureAwait(false);
            if (result.Chapters.Count == 0)
            {
                result.Chapters = BuildTrackChapters(result.Tracks);
            }
            ReportDownload(progress, book.Title, "Import complete", 100, tracks.Count, tracks.Count, null);
            return result;
        }

        public async Task UpdateProgressAsync(string revisionId, double positionSeconds, double durationSeconds)
        {
            if (String.IsNullOrWhiteSpace(revisionId) || durationSeconds <= 0)
            {
                return;
            }

            await LoadResourcesAsync().ConfigureAwait(false);
            var percent = (int)Math.Round(Math.Max(0, Math.Min(100, positionSeconds / durationSeconds * 100)));
            var stateUrl = GetResource("reading_state").Replace("{Ids}", Uri.EscapeDataString(revisionId));
            var state = new Dictionary<string, object>
            {
                {
                    "ReadingStates", new object[]
                    {
                        new Dictionary<string, object>
                        {
                            {
                                "CurrentBookmark", new Dictionary<string, object>
                                {
                                    { "ProgressPercent", percent },
                                    { "ContentSourceProgressPercent", percent },
                                    { "Location", null }
                                }
                            },
                            { "Statistics", null },
                            {
                                "StatusInfo", new Dictionary<string, object>
                                {
                                    { "Status", percent >= 99 ? "Finished" : "Reading" }
                                }
                            }
                        }
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Put, stateUrl)
            {
                Content = new StringContent(json.Serialize(state), Encoding.UTF8, "application/json")
            };
            await SendAuthorizedAsync(request).ConfigureAwait(false);
        }

        private async Task LoadResourcesAsync()
        {
            if (resources.Count > 0)
            {
                return;
            }

            var data = await GetJsonAsync(StoreApi + "/v1/initialization").ConfigureAwait(false) as Dictionary<string, object>;
            var resourceData = data == null ? null : GetValue(data, "Resources") as Dictionary<string, object>;
            if (resourceData == null)
            {
                throw new InvalidOperationException("Kobo initialization did not contain service URLs.");
            }
            resources = new Dictionary<string, object>(resourceData, StringComparer.OrdinalIgnoreCase);
        }

        private string GetResource(string name)
        {
            var value = resources.FirstOrDefault(pair => String.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
            if (value == null)
            {
                throw new InvalidOperationException("Kobo did not advertise the " + name + " service.");
            }
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private string ResolveCoverUrl(Dictionary<string, object> metadata)
        {
            var direct = FirstString(metadata, "ImageUrl", "CoverUrl", "ThumbnailUrl");
            if (!String.IsNullOrWhiteSpace(direct))
            {
                return NormalizeKoboUrl(direct);
            }

            var imageId = FirstString(metadata, "CoverImageId", "ImageId");
            if (String.IsNullOrWhiteSpace(imageId))
            {
                return null;
            }

            var template = resources.FirstOrDefault(pair => String.Equals(pair.Key, "image_url_quality_template", StringComparison.OrdinalIgnoreCase)).Value;
            var templateText = template == null ? null : Convert.ToString(template, System.Globalization.CultureInfo.InvariantCulture);
            if (String.IsNullOrWhiteSpace(templateText))
            {
                template = resources.FirstOrDefault(pair => String.Equals(pair.Key, "image_url_template", StringComparison.OrdinalIgnoreCase)).Value;
                templateText = template == null ? null : Convert.ToString(template, System.Globalization.CultureInfo.InvariantCulture);
            }
            if (String.IsNullOrWhiteSpace(templateText))
            {
                return null;
            }

            return templateText
                .Replace("{ImageId}", Uri.EscapeDataString(imageId))
                .Replace("{Width}", "180")
                .Replace("{width}", "180")
                .Replace("{Height}", "270")
                .Replace("{height}", "270")
                .Replace("{Quality}", "90")
                .Replace("{quality}", "90")
                .Replace("{IsGreyscale}", "false")
                .Replace("{isGreyscale}", "false");
        }

        private async Task<object> GetJsonAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var result = await SendAuthorizedAsync(request).ConfigureAwait(false);
            return result.Data;
        }

        private async Task<object> PostJsonAsync(string url, object payload, bool authorized)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json.Serialize(payload), Encoding.UTF8, "application/json")
            };
            if (authorized)
            {
                AddAuthHeaders(request);
            }
            var response = await http.SendAsync(request).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "the Kobo service").ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return String.IsNullOrWhiteSpace(text) ? new Dictionary<string, object>() : json.DeserializeObject(text);
        }

        private async Task<KoboHttpResult> SendAuthorizedAsync(HttpRequestMessage request)
        {
            AddAuthHeaders(request);
            var response = await http.SendAsync(request).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshing && !String.IsNullOrWhiteSpace(Session.RefreshToken))
            {
                refreshing = true;
                try
                {
                    await RefreshAsync().ConfigureAwait(false);
                }
                finally
                {
                    refreshing = false;
                }
                var retry = new HttpRequestMessage(request.Method, request.RequestUri);
                if (request.Content != null)
                {
                    var content = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                    retry.Content = new StringContent(content, Encoding.UTF8, "application/json");
                }
                request.Dispose();
                return await SendAuthorizedAsync(retry).ConfigureAwait(false);
            }
            await EnsureSuccessAsync(response, "the Kobo service").ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new KoboHttpResult
            {
                Response = response,
                Data = String.IsNullOrWhiteSpace(text) ? new Dictionary<string, object>() : json.DeserializeObject(text)
            };
        }

        private async Task RefreshAsync()
        {
            var payload = new Dictionary<string, object>
            {
                { "AppVersion", ApplicationVersion },
                { "ClientKey", Convert.ToBase64String(Encoding.UTF8.GetBytes(PlatformId)) },
                { "PlatformId", PlatformId },
                { "RefreshToken", Session.RefreshToken }
            };
            var data = await PostJsonAsync(StoreApi + "/v1/auth/refresh", payload, true).ConfigureAwait(false) as Dictionary<string, object>;
            Session.AccessToken = GetString(data, "AccessToken");
            Session.RefreshToken = GetString(data, "RefreshToken");
            resources.Clear();
        }

        private async Task DownloadTrackAsync(string url, string destination, int trackIndex, int totalTracks, SemaphoreSlim limiter, Action<int, long, long, bool> reportBytes)
        {
            await limiter.WaitAsync().ConfigureAwait(false);
            try
            {
                await DownloadFileAsync(url, destination, (completed, total, isComplete) => reportBytes(trackIndex, completed, total, isComplete)).ConfigureAwait(false);
            }
            finally
            {
                limiter.Release();
            }
        }

        private async Task DownloadFileAsync(string url, string destination, Action<long, long, bool> reportBytes)
        {
            var response = await SendDownloadRequestAsync(url).ConfigureAwait(false);
            using (response)
            {
                await EnsureSuccessAsync(response, "an audiobook track").ConfigureAwait(false);
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, true))
                {
                    var buffer = new byte[256 * 1024];
                    var contentLength = response.Content.Headers.ContentLength ?? 0;
                    if (reportBytes != null)
                    {
                        reportBytes(0, contentLength, false);
                    }
                    long completed = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                        completed += read;
                        if (reportBytes != null)
                        {
                            reportBytes(completed, contentLength, false);
                        }
                    }
                    if (reportBytes != null)
                    {
                        reportBytes(completed, contentLength, true);
                    }
                }
            }
        }

        private async Task<string> DownloadCoverAsync(KoboRemoteBook book, string directory)
        {
            if (String.IsNullOrWhiteSpace(book.CoverUrl))
            {
                return null;
            }

            try
            {
                var extension = Path.GetExtension(new Uri(book.CoverUrl).AbsolutePath);
                if (String.IsNullOrWhiteSpace(extension) || extension.Length > 5)
                {
                    extension = ".jpg";
                }
                var destination = Path.Combine(directory, "cover" + extension.ToLowerInvariant());
                await DownloadFileAsync(NormalizeKoboUrl(book.CoverUrl), destination, null).ConfigureAwait(false);
                return destination;
            }
            catch
            {
                return null;
            }
        }

        private static List<KoboChapter> BuildChapters(object manifest, List<KeyValuePair<Dictionary<string, object>, string>> tracks, string[] partPaths)
        {
            var result = new List<KoboChapter>();
            var rawChapters = FindList(manifest, "Chapters").ToList();
            if (rawChapters.Count == 0)
            {
                rawChapters = FindList(manifest, "TableOfContents").ToList();
            }
            if (rawChapters.Count == 0)
            {
                rawChapters = FindList(manifest, "Toc").ToList();
            }

            var fallbackDurations = partPaths.Select(EstimateMp3Duration).ToList();
            var total = fallbackDurations.Sum();
            for (var index = 0; index < rawChapters.Count; index++)
            {
                var item = rawChapters[index] as Dictionary<string, object>;
                if (item == null)
                {
                    continue;
                }
                var trackIndex = (int)Math.Round(FindNumber(item, "TrackIndex", "SpineIndex", "Track", "Part") ?? index);
                trackIndex = Math.Max(0, Math.Min(tracks.Count - 1, trackIndex));
                var start = FindTime(item, "StartSeconds", "StartTime", "Start", "OffsetSeconds", "Offset");
                if (!start.HasValue)
                {
                    start = fallbackDurations.Take(trackIndex).Sum();
                }
                var duration = FindTime(item, "DurationSeconds", "Duration", "LengthSeconds", "Length");
                var title = FirstString(item, "Title", "Name", "Label", "ChapterTitle") ?? "Chapter " + (index + 1);
                result.Add(new KoboChapter
                {
                    Title = title,
                    StartSeconds = Math.Max(0, start.Value),
                    EndSeconds = Math.Min(total, Math.Max(start.Value, start.Value + (duration ?? 0)))
                });
            }

            result = result.OrderBy(chapter => chapter.StartSeconds).ToList();
            for (var index = 0; index < result.Count; index++)
            {
                if (result[index].EndSeconds <= result[index].StartSeconds)
                {
                    result[index].EndSeconds = index + 1 < result.Count ? result[index + 1].StartSeconds : total;
                }
            }
            return result;
        }

        private static List<KoboChapter> BuildTrackChapters(List<KoboTrack> tracks)
        {
            var chapters = new List<KoboChapter>();
            var offset = 0.0;
            for (var index = 0; index < tracks.Count; index++)
            {
                var duration = Math.Max(0, tracks[index].DurationSeconds);
                chapters.Add(new KoboChapter
                {
                    Title = String.IsNullOrWhiteSpace(tracks[index].Title) ? "Chapter " + (index + 1) : tracks[index].Title,
                    StartSeconds = offset,
                    EndSeconds = offset + duration
                });
                offset += duration;
            }
            return chapters;
        }

        private static double? FindNumber(Dictionary<string, object> dictionary, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = GetValue(dictionary, key);
                double parsed;
                if (value != null && Double.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed > 100000 ? parsed / 1000.0 : parsed;
                }
            }
            return null;
        }

        private static double? FindTime(Dictionary<string, object> dictionary, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = GetValue(dictionary, key);
                if (value == null)
                {
                    continue;
                }
                double number;
                if (Double.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out number))
                {
                    return number > 100000 ? number / 1000.0 : number;
                }
                TimeSpan time;
                if (TimeSpan.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out time))
                {
                    return time.TotalSeconds;
                }
            }
            return null;
        }

        private static double EstimateMp3Duration(string path)
        {
            try
            {
                using (var input = File.OpenRead(path))
                {
                    var offset = SkipId3v2(input);
                    var header = new byte[4];
                    var samples = new List<int>();
                    for (var frame = 0; frame < 80; frame++)
                    {
                        if (input.Read(header, 0, 4) != 4)
                        {
                            break;
                        }
                        int bitrate;
                        int sampleRate;
                        int frameLength;
                        if (!TryReadMp3Frame(header, out bitrate, out sampleRate, out frameLength))
                        {
                            input.Position = input.Position - 3;
                            continue;
                        }
                        samples.Add(bitrate);
                        input.Position = Math.Min(input.Length, input.Position + frameLength - 4);
                    }
                    var averageBitrate = samples.Count == 0 ? 96000 : samples.Average();
                    return Math.Max(0, (input.Length - offset) * 8.0 / averageBitrate);
                }
            }
            catch
            {
                return 0;
            }
        }

        private static bool TryReadMp3Frame(byte[] header, out int bitrate, out int sampleRate, out int frameLength)
        {
            bitrate = 0;
            sampleRate = 0;
            frameLength = 0;
            if (header == null || header.Length < 4 || header[0] != 0xff || (header[1] & 0xe0) != 0xe0)
            {
                return false;
            }
            var version = (header[1] >> 3) & 3;
            var layer = (header[1] >> 1) & 3;
            var bitrateIndex = (header[2] >> 4) & 15;
            var sampleIndex = (header[2] >> 2) & 3;
            if (version == 1 || layer != 1 || bitrateIndex == 0 || bitrateIndex == 15 || sampleIndex == 3)
            {
                return false;
            }
            var mpeg1Bitrates = new[] { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
            var mpeg2Bitrates = new[] { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };
            var sampleRates = version == 3 ? new[] { 44100, 48000, 32000 } : version == 2 ? new[] { 22050, 24000, 16000 } : new[] { 11025, 12000, 8000 };
            bitrate = (version == 3 ? mpeg1Bitrates : mpeg2Bitrates)[bitrateIndex] * 1000;
            sampleRate = sampleRates[sampleIndex];
            frameLength = version == 3 ? 144 * bitrate / sampleRate : 72 * bitrate / sampleRate;
            return frameLength > 0;
        }

        private static int DownloadPercent(int completedTracks, double trackFraction, int totalTracks)
        {
            var count = Math.Max(1, totalTracks);
            var fraction = Math.Max(0, Math.Min(count, completedTracks + Math.Max(0, Math.Min(1, trackFraction))));
            return 12 + (int)Math.Round(fraction / count * 76);
        }

        private static void ReportDownload(IProgress<KoboDownloadProgress> progress, string title, string stage, int percent, int currentTrack, int totalTracks, string detail)
        {
            if (progress == null)
            {
                return;
            }

            progress.Report(new KoboDownloadProgress
            {
                Title = title,
                Stage = stage,
                Percent = Math.Max(0, Math.Min(100, percent)),
                CurrentTrack = currentTrack,
                TotalTracks = totalTracks,
                Detail = detail
            });
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024 * 1024)
            {
                return Math.Max(1, bytes / 1024) + " KB";
            }

            return (bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        }

        private async Task<HttpResponseMessage> SendDownloadRequestAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeaders(request);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            request.Dispose();
            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshing && !String.IsNullOrWhiteSpace(Session.RefreshToken))
            {
                response.Dispose();
                await RefreshAsync().ConfigureAwait(false);
                request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuthHeaders(request);
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                request.Dispose();
            }
            return response;
        }

        private void AddAuthHeaders(HttpRequestMessage request)
        {
            if (!String.IsNullOrWhiteSpace(Session.AccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.AccessToken);
            }
            if (request.RequestUri != null && IsKoboHost(request.RequestUri) && !String.IsNullOrWhiteSpace(Session.UserKey))
            {
                request.Headers.TryAddWithoutValidation("x-kobo-userkey", Session.UserKey);
            }
        }

        private static bool IsKoboHost(Uri uri)
        {
            return uri != null && uri.Host.EndsWith("kobo.com", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeKoboUrl(string url)
        {
            if (String.IsNullOrWhiteSpace(url))
            {
                return url;
            }
            try
            {
                var uri = new Uri(url);
                if (!IsKoboHost(uri) || uri.Host.IndexOf("amazonaws.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return url;
                }
                var builder = new UriBuilder(uri);
                var query = HttpUtility.ParseQueryString(uri.Query);
                query.Remove("b");
                builder.Query = query.ToString();
                return builder.Uri.AbsoluteUri;
            }
            catch
            {
                return url;
            }
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }
            var detail = String.Empty;
            try
            {
                detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                // The status code is still useful if the body cannot be read.
            }
            if (detail.Length > 240)
            {
                detail = detail.Substring(0, 240);
            }
            var message = "Kobo refused " + action + " (HTTP " + (int)response.StatusCode + ").";
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                message += " This title may be protected or unavailable to third-party playback.";
            }
            if (!String.IsNullOrWhiteSpace(detail))
            {
                message += " " + detail.Replace("\r", " ").Replace("\n", " ");
            }
            throw new InvalidOperationException(message);
        }

        private static IEnumerable<Dictionary<string, object>> EnumerateEntitlements(object root)
        {
            if (root == null)
            {
                yield break;
            }

            var rootDictionary = root as Dictionary<string, object>;
            if (rootDictionary != null)
            {
                foreach (var pair in rootDictionary)
                {
                    var list = pair.Value as IEnumerable;
                    if (list == null || pair.Value is string || pair.Value is IDictionary)
                    {
                        continue;
                    }
                    foreach (var item in list)
                    {
                        var dictionary = item as Dictionary<string, object>;
                        if (dictionary != null)
                        {
                            yield return dictionary;
                        }
                    }
                }
                yield break;
            }

            var rootList = root as IEnumerable;
            if (rootList == null || root is string)
            {
                yield break;
            }
            foreach (var item in rootList)
            {
                var dictionary = item as Dictionary<string, object>;
                if (dictionary != null)
                {
                    yield return dictionary;
                }
            }
        }

        private static bool LooksLikeAudiobook(Dictionary<string, object> item, Dictionary<string, object> metadata)
        {
            if (FindDictionary(item, "AudiobookMetadata") != null)
            {
                return true;
            }
            var all = new[] { item, metadata }.Where(value => value != null).ToArray();
            foreach (var dictionary in all)
            {
                foreach (var key in new[] { "IsAudiobook", "Audiobook", "IsAudioBook" })
                {
                    var value = GetValue(dictionary, key);
                    if (value is bool && (bool)value)
                    {
                        return true;
                    }
                }

                foreach (var key in new[] { "ContentType", "ProductType", "BookType", "Type", "Format" })
                {
                    var value = FirstString(dictionary, key);
                    if (!String.IsNullOrWhiteSpace(value) && value.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }

            }
            return false;
        }

        private static bool HasProtectedDrm(Dictionary<string, object> dictionary)
        {
            if (dictionary == null)
            {
                return false;
            }

            var directDrm = FirstString(dictionary, "DRMType", "DrmType", "Drm");
            if (!String.IsNullOrWhiteSpace(directDrm) && (directDrm.IndexOf("KDRM", StringComparison.OrdinalIgnoreCase) >= 0 || directDrm.IndexOf("Adobe", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            foreach (var value in dictionary.Values)
            {
                var child = value as Dictionary<string, object>;
                if (child != null)
                {
                    var drm = FirstString(child, "DRMType", "DrmType", "Drm");
                    if (!String.IsNullOrWhiteSpace(drm) && (drm.IndexOf("KDRM", StringComparison.OrdinalIgnoreCase) >= 0 || drm.IndexOf("Adobe", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return true;
                    }
                    if (HasProtectedDrm(child))
                    {
                        return true;
                    }
                }
                else
                {
                    var list = value as IEnumerable;
                    if (list == null || value is string || value is IDictionary)
                    {
                        continue;
                    }
                    foreach (var item in list)
                    {
                        var itemDictionary = item as Dictionary<string, object>;
                        if (itemDictionary != null && HasProtectedDrm(itemDictionary))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static string FindDownloadUrl(Dictionary<string, object> dictionary)
        {
            var value = FindValue(dictionary, "DownloadUrls") ?? FindValue(dictionary, "ContentUrls");
            var single = value as Dictionary<string, object>;
            if (single != null)
            {
                return FindDownloadUrlEntry(single);
            }
            var list = value as IEnumerable;
            if (list == null || value is string || value is IDictionary)
            {
                return value as string;
            }
            foreach (var item in list.OfType<Dictionary<string, object>>())
            {
                var url = FindDownloadUrlEntry(item);
                if (!String.IsNullOrWhiteSpace(url))
                {
                    return url;
                }
            }
            return null;
        }

        private static string FindDownloadUrlEntry(Dictionary<string, object> item)
        {
            var drm = FirstString(item, "DRMType", "DrmType", "Drm");
            if (!String.IsNullOrWhiteSpace(drm) && (drm.IndexOf("KDRM", StringComparison.OrdinalIgnoreCase) >= 0 || drm.IndexOf("Adobe", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                throw new KoboProtectedException("Kobo marked this audiobook as protected. It must be played in the official Kobo app.");
            }
            return FirstString(item, "DownloadUrl", "Url");
        }

        private static IEnumerable<object> FindList(object root, string key)
        {
            var value = FindValue(root, key);
            var list = value as IEnumerable;
            if (list == null || value is string || value is IDictionary)
            {
                yield break;
            }
            foreach (var item in list)
            {
                yield return item;
            }
        }

        private static object FindValue(object root, string key)
        {
            var dictionary = root as Dictionary<string, object>;
            if (dictionary == null)
            {
                return null;
            }
            var direct = GetValue(dictionary, key);
            if (direct != null)
            {
                return direct;
            }
            foreach (var value in dictionary.Values)
            {
                var nested = FindValue(value, key);
                if (nested != null)
                {
                    return nested;
                }
                var list = value as IEnumerable;
                if (list != null && !(value is string) && !(value is IDictionary))
                {
                    foreach (var item in list)
                    {
                        nested = FindValue(item, key);
                        if (nested != null)
                        {
                            return nested;
                        }
                    }
                }
            }
            return null;
        }

        private static Dictionary<string, object> FindDictionary(Dictionary<string, object> root, string key)
        {
            var value = FindValue(root, key);
            return value as Dictionary<string, object>;
        }

        private static double FindProgressPercent(Dictionary<string, object> root)
        {
            var value = FindValue(root, "ProgressPercent");
            double parsed;
            return value != null && Double.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static string FirstString(Dictionary<string, object> dictionary, params string[] keys)
        {
            if (dictionary == null)
            {
                return null;
            }
            foreach (var key in keys)
            {
                var value = GetValue(dictionary, key);
                if (value is string && !String.IsNullOrWhiteSpace((string)value))
                {
                    return (string)value;
                }

                var values = value as IEnumerable;
                if (values != null && !(value is IDictionary))
                {
                    var strings = values.Cast<object>()
                        .Select(item => Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture))
                        .Where(item => !String.IsNullOrWhiteSpace(item))
                        .ToList();
                    if (strings.Count > 0)
                    {
                        return String.Join(", ", strings);
                    }
                }
            }
            return null;
        }

        private static string GetString(Dictionary<string, object> dictionary, string key)
        {
            var value = dictionary == null ? null : GetValue(dictionary, key);
            return value == null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static object GetValue(Dictionary<string, object> dictionary, string key)
        {
            if (dictionary == null)
            {
                return null;
            }
            var pair = dictionary.FirstOrDefault(value => String.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase));
            return String.IsNullOrEmpty(pair.Key) ? null : pair.Value;
        }

        private static Dictionary<string, string> ParseQuery(string url)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrWhiteSpace(url))
            {
                return result;
            }
            var query = new Uri(url).Query;
            foreach (var pair in query.TrimStart('?').Split('&'))
            {
                var parts = pair.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1].Replace("+", " "));
                }
            }
            return result;
        }

        private static string ExtractActivationCode(string html)
        {
            var patterns = new[]
            {
                @"qrcodegenerator/generate.+?%26code%3D(\d+)",
                @"(?:%26|[?&])code(?:%3D|=)(\d+)",
                @"(?:activationCode|activation_code)[^0-9]{1,12}(\d{4,12})"
            };
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            return null;
        }

        private static string GetValue(Dictionary<string, string> dictionary, string key)
        {
            string value;
            return dictionary != null && dictionary.TryGetValue(key, out value) ? value : null;
        }

        private static string SafeFileName(string value)
        {
            var result = String.IsNullOrWhiteSpace(value) ? "kobo-audiobook" : value;
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalid, '_');
            }
            return result.Trim();
        }

        private static string RandomHex(int length)
        {
            var bytes = new byte[(length + 1) / 2];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            return BitConverter.ToString(bytes).Replace("-", String.Empty).ToLowerInvariant().Substring(0, length);
        }

        private static long SkipId3v2(Stream input)
        {
            var header = new byte[10];
            var read = input.Read(header, 0, header.Length);
            if (read != 10 || header[0] != (byte)'I' || header[1] != (byte)'D' || header[2] != (byte)'3')
            {
                input.Position = 0;
                return 0;
            }
            var size = ((header[6] & 0x7f) << 21) | ((header[7] & 0x7f) << 14) | ((header[8] & 0x7f) << 7) | (header[9] & 0x7f);
            input.Position = Math.Min(input.Length, 10 + size);
            return input.Position;
        }

        public void Dispose()
        {
            http.Dispose();
        }
    }
}
