using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kapla
{
    internal sealed class LocalAudiobookInfo
    {
        public string Title { get; set; }
        public string Author { get; set; }
        public string Album { get; set; }
        public double DurationSeconds { get; set; }
        public byte[] CoverBytes { get; set; }
        public string CoverExtension { get; set; }
        public List<KoboChapter> Chapters { get; set; }

        public LocalAudiobookInfo()
        {
            Chapters = new List<KoboChapter>();
        }
    }

    internal static class LocalAudiobookMetadata
    {
        public static LocalAudiobookInfo Read(string path)
        {
            var result = new LocalAudiobookInfo();
            try
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".mp3" || extension == ".aac")
                {
                    ReadId3(path, result);
                }
                else if (extension == ".m4b" || extension == ".m4a")
                {
                    ReadMp4(path, result);
                }
            }
            catch
            {
                // Unsupported or malformed metadata must not prevent playback.
            }
            NormalizeChapters(result.Chapters);
            return result;
        }

        private static void ReadId3(string path, LocalAudiobookInfo result)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 10 || Encoding.ASCII.GetString(reader.ReadBytes(3)) != "ID3")
                {
                    return;
                }
                var version = reader.ReadByte();
                reader.ReadByte();
                reader.ReadByte();
                var tagSize = ReadSynchsafe(reader.ReadBytes(4), 0);
                var data = reader.ReadBytes(Math.Min(tagSize, 32 * 1024 * 1024));
                ParseId3Frames(data, version, result, false);
            }
        }

        private static void ParseId3Frames(byte[] data, int version, LocalAudiobookInfo result, bool nested)
        {
            var offset = 0;
            while (offset + 10 <= data.Length)
            {
                var id = Encoding.ASCII.GetString(data, offset, 4);
                if (id.Trim('\0', ' ').Length == 0)
                {
                    break;
                }
                var size = version >= 4 ? ReadSynchsafe(data, offset + 4) : ReadInt32BigEndian(data, offset + 4);
                if (size <= 0 || offset + 10 + size > data.Length)
                {
                    break;
                }
                var payloadOffset = offset + 10;
                if (id == "TIT2" && !nested && String.IsNullOrWhiteSpace(result.Title))
                {
                    result.Title = ReadId3Text(data, payloadOffset, size);
                }
                else if ((id == "TPE1" || id == "TPE2") && !nested && String.IsNullOrWhiteSpace(result.Author))
                {
                    result.Author = ReadId3Text(data, payloadOffset, size);
                }
                else if (id == "TALB" && !nested && String.IsNullOrWhiteSpace(result.Album))
                {
                    result.Album = ReadId3Text(data, payloadOffset, size);
                }
                else if (id == "TLEN" && !nested && result.DurationSeconds <= 0)
                {
                    double milliseconds;
                    if (Double.TryParse(ReadId3Text(data, payloadOffset, size), out milliseconds))
                    {
                        result.DurationSeconds = Math.Max(0, milliseconds / 1000.0);
                    }
                }
                else if (id == "APIC" && !nested && result.CoverBytes == null)
                {
                    ReadAttachedPicture(data, payloadOffset, size, result);
                }
                else if (id == "CHAP" && !nested)
                {
                    ReadId3Chapter(data, payloadOffset, size, version, result);
                }
                offset += 10 + size;
            }
        }

        private static void ReadId3Chapter(byte[] data, int offset, int size, int version, LocalAudiobookInfo result)
        {
            var end = offset + size;
            var cursor = offset;
            while (cursor < end && data[cursor] != 0)
            {
                cursor++;
            }
            cursor++;
            if (cursor + 16 > end)
            {
                return;
            }
            var startMilliseconds = ReadUInt32BigEndian(data, cursor);
            var endMilliseconds = ReadUInt32BigEndian(data, cursor + 4);
            cursor += 16;
            var title = String.Empty;
            while (cursor + 10 <= end)
            {
                var id = Encoding.ASCII.GetString(data, cursor, 4);
                var frameSize = version >= 4 ? ReadSynchsafe(data, cursor + 4) : ReadInt32BigEndian(data, cursor + 4);
                if (frameSize <= 0 || cursor + 10 + frameSize > end)
                {
                    break;
                }
                if (id == "TIT2")
                {
                    title = ReadId3Text(data, cursor + 10, frameSize);
                    break;
                }
                cursor += 10 + frameSize;
            }
            result.Chapters.Add(new KoboChapter
            {
                Title = String.IsNullOrWhiteSpace(title) ? "Chapter " + (result.Chapters.Count + 1) : title,
                StartSeconds = startMilliseconds / 1000.0,
                EndSeconds = endMilliseconds == UInt32.MaxValue ? 0 : endMilliseconds / 1000.0
            });
        }

        private static void ReadAttachedPicture(byte[] data, int offset, int size, LocalAudiobookInfo result)
        {
            var end = offset + size;
            if (offset >= end)
            {
                return;
            }
            var encoding = data[offset++];
            var mimeEnd = Array.IndexOf(data, (byte)0, offset, end - offset);
            if (mimeEnd < 0 || mimeEnd + 2 >= end)
            {
                return;
            }
            var mime = Encoding.ASCII.GetString(data, offset, mimeEnd - offset);
            offset = mimeEnd + 2;
            var terminatorLength = encoding == 1 || encoding == 2 ? 2 : 1;
            while (offset + terminatorLength <= end)
            {
                if (data[offset] == 0 && (terminatorLength == 1 || data[offset + 1] == 0))
                {
                    offset += terminatorLength;
                    break;
                }
                offset += terminatorLength;
            }
            if (offset >= end)
            {
                return;
            }
            result.CoverBytes = data.Skip(offset).Take(end - offset).ToArray();
            result.CoverExtension = mime.IndexOf("png", StringComparison.OrdinalIgnoreCase) >= 0 ? ".png" : ".jpg";
        }

        private static string ReadId3Text(byte[] data, int offset, int size)
        {
            if (size <= 1 || offset + size > data.Length)
            {
                return String.Empty;
            }
            var encoding = data[offset];
            var count = size - 1;
            string value;
            if (encoding == 1)
            {
                value = Encoding.Unicode.GetString(data, offset + 1, count);
            }
            else if (encoding == 2)
            {
                var bytes = data.Skip(offset + 1).Take(count).ToArray();
                for (var index = 0; index + 1 < bytes.Length; index += 2)
                {
                    var swap = bytes[index];
                    bytes[index] = bytes[index + 1];
                    bytes[index + 1] = swap;
                }
                value = Encoding.Unicode.GetString(bytes);
            }
            else if (encoding == 3)
            {
                value = Encoding.UTF8.GetString(data, offset + 1, count);
            }
            else
            {
                value = Encoding.GetEncoding(28591).GetString(data, offset + 1, count);
            }
            return value.Trim('\0', ' ', '\r', '\n');
        }

        private static void ReadMp4(string path, LocalAudiobookInfo result)
        {
            byte[] data;
            using (var stream = File.OpenRead(path))
            {
                const int windowSize = 32 * 1024 * 1024;
                if (stream.Length <= windowSize * 2L)
                {
                    data = ReadBytes(stream, (int)stream.Length);
                }
                else
                {
                    var first = ReadBytes(stream, windowSize);
                    stream.Position = stream.Length - windowSize;
                    var last = ReadBytes(stream, windowSize);
                    data = new byte[first.Length + last.Length];
                    Buffer.BlockCopy(first, 0, data, 0, first.Length);
                    Buffer.BlockCopy(last, 0, data, first.Length, last.Length);
                }
            }
            result.Title = ReadMp4Text(data, new byte[] { 0xA9, (byte)'n', (byte)'a', (byte)'m' });
            result.Author = ReadMp4Text(data, new byte[] { 0xA9, (byte)'A', (byte)'R', (byte)'T' });
            result.Album = ReadMp4Text(data, new byte[] { 0xA9, (byte)'a', (byte)'l', (byte)'b' });
            result.DurationSeconds = ReadMp4Duration(data);
            ReadMp4Cover(data, result);
            ReadMp4Chapters(data, result);
        }

        private static byte[] ReadBytes(Stream stream, int count)
        {
            var data = new byte[count];
            var read = 0;
            while (read < count)
            {
                var current = stream.Read(data, read, count - read);
                if (current <= 0)
                {
                    break;
                }
                read += current;
            }
            if (read == count)
            {
                return data;
            }
            return data.Take(read).ToArray();
        }

        private static string ReadMp4Text(byte[] data, byte[] key)
        {
            var keyIndex = IndexOf(data, key, 0);
            if (keyIndex < 4)
            {
                return String.Empty;
            }
            var parentSize = ReadInt32BigEndian(data, keyIndex - 4);
            var limit = Math.Min(data.Length, keyIndex - 4 + Math.Max(8, parentSize));
            var dataIndex = IndexOf(data, Encoding.ASCII.GetBytes("data"), keyIndex + 4, limit);
            if (dataIndex < 4 || dataIndex + 12 > limit)
            {
                return String.Empty;
            }
            var atomSize = ReadInt32BigEndian(data, dataIndex - 4);
            var contentStart = dataIndex + 12;
            var contentEnd = Math.Min(limit, dataIndex - 4 + atomSize);
            return contentEnd > contentStart
                ? Encoding.UTF8.GetString(data, contentStart, contentEnd - contentStart).Trim('\0', ' ', '\r', '\n')
                : String.Empty;
        }

        private static void ReadMp4Cover(byte[] data, LocalAudiobookInfo result)
        {
            var keyIndex = IndexOf(data, Encoding.ASCII.GetBytes("covr"), 0);
            if (keyIndex < 4)
            {
                return;
            }
            var parentSize = ReadInt32BigEndian(data, keyIndex - 4);
            var limit = Math.Min(data.Length, keyIndex - 4 + Math.Max(8, parentSize));
            var dataIndex = IndexOf(data, Encoding.ASCII.GetBytes("data"), keyIndex + 4, limit);
            if (dataIndex < 4 || dataIndex + 12 > limit)
            {
                return;
            }
            var atomSize = ReadInt32BigEndian(data, dataIndex - 4);
            var contentStart = dataIndex + 12;
            var contentEnd = Math.Min(limit, dataIndex - 4 + atomSize);
            if (contentEnd <= contentStart)
            {
                return;
            }
            result.CoverBytes = data.Skip(contentStart).Take(contentEnd - contentStart).ToArray();
            result.CoverExtension = result.CoverBytes.Length > 8
                && result.CoverBytes[0] == 0x89 && result.CoverBytes[1] == 0x50 ? ".png" : ".jpg";
        }

        private static void ReadMp4Chapters(byte[] data, LocalAudiobookInfo result)
        {
            var typeIndex = IndexOf(data, Encoding.ASCII.GetBytes("chpl"), 0);
            if (typeIndex < 4)
            {
                return;
            }
            var atomSize = ReadInt32BigEndian(data, typeIndex - 4);
            var end = Math.Min(data.Length, typeIndex - 4 + atomSize);
            foreach (var headerSize in new[] { 9, 5 })
            {
                var cursor = typeIndex + 4 + headerSize;
                if (cursor > end)
                {
                    continue;
                }
                var countOffset = cursor - 1;
                var chapterCount = data[countOffset];
                var chapters = new List<KoboChapter>();
                for (var index = 0; index < chapterCount && cursor + 9 <= end; index++)
                {
                    var start = ReadUInt64BigEndian(data, cursor) / 10000000.0;
                    cursor += 8;
                    var titleLength = data[cursor++];
                    if (cursor + titleLength > end)
                    {
                        chapters.Clear();
                        break;
                    }
                    var title = Encoding.UTF8.GetString(data, cursor, titleLength);
                    cursor += titleLength;
                    chapters.Add(new KoboChapter
                    {
                        Title = String.IsNullOrWhiteSpace(title) ? "Chapter " + (index + 1) : title,
                        StartSeconds = start
                    });
                }
                if (chapters.Count > 0)
                {
                    result.Chapters = chapters;
                    return;
                }
            }
        }

        private static double ReadMp4Duration(byte[] data)
        {
            var typeIndex = IndexOf(data, Encoding.ASCII.GetBytes("mvhd"), 0);
            if (typeIndex < 4 || typeIndex + 8 >= data.Length)
            {
                return 0;
            }
            var payload = typeIndex + 4;
            var version = data[payload];
            if (version == 1)
            {
                if (payload + 32 > data.Length)
                {
                    return 0;
                }
                var timescale = ReadUInt32BigEndian(data, payload + 20);
                var duration = ReadUInt64BigEndian(data, payload + 24);
                return timescale == 0 ? 0 : duration / (double)timescale;
            }
            if (payload + 20 > data.Length)
            {
                return 0;
            }
            var scale = ReadUInt32BigEndian(data, payload + 12);
            var value = ReadUInt32BigEndian(data, payload + 16);
            return scale == 0 ? 0 : value / (double)scale;
        }

        private static void NormalizeChapters(List<KoboChapter> chapters)
        {
            chapters.Sort((left, right) => left.StartSeconds.CompareTo(right.StartSeconds));
            for (var index = 0; index < chapters.Count - 1; index++)
            {
                if (chapters[index].EndSeconds <= chapters[index].StartSeconds)
                {
                    chapters[index].EndSeconds = chapters[index + 1].StartSeconds;
                }
            }
        }

        private static int IndexOf(byte[] data, byte[] pattern, int start, int limit = Int32.MaxValue)
        {
            limit = Math.Min(data.Length, limit);
            for (var index = Math.Max(0, start); index + pattern.Length <= limit; index++)
            {
                var matches = true;
                for (var part = 0; part < pattern.Length; part++)
                {
                    if (data[index + part] != pattern[part])
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                {
                    return index;
                }
            }
            return -1;
        }

        private static int ReadSynchsafe(byte[] data, int offset)
        {
            return ((data[offset] & 0x7F) << 21)
                | ((data[offset + 1] & 0x7F) << 14)
                | ((data[offset + 2] & 0x7F) << 7)
                | (data[offset + 3] & 0x7F);
        }

        private static int ReadInt32BigEndian(byte[] data, int offset)
        {
            if (offset < 0 || offset + 4 > data.Length)
            {
                return 0;
            }
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        private static uint ReadUInt32BigEndian(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
        }

        private static ulong ReadUInt64BigEndian(byte[] data, int offset)
        {
            ulong value = 0;
            for (var index = 0; index < 8; index++)
            {
                value = (value << 8) | data[offset + index];
            }
            return value;
        }
    }
}
