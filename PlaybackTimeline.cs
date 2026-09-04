using System;
using System.Collections.Generic;
using System.Linq;

namespace Kapla
{
    internal static class PlaybackTimeline
    {
        public static double TotalDuration(IList<KoboTrack> tracks)
        {
            if (tracks == null)
            {
                return 0;
            }
            return tracks.Select(track => DurationOf(track)).Sum();
        }

        public static double TrackStart(IList<KoboTrack> tracks, int trackIndex)
        {
            if (tracks == null || tracks.Count == 0)
            {
                return 0;
            }
            var count = Math.Max(0, Math.Min(trackIndex, tracks.Count));
            var total = 0.0;
            for (var index = 0; index < count; index++)
            {
                total += DurationOf(tracks[index]);
            }
            return total;
        }

        public static int FindTrack(IList<KoboTrack> tracks, double position)
        {
            if (tracks == null || tracks.Count == 0)
            {
                return 0;
            }
            if (Double.IsNaN(position) || Double.IsInfinity(position))
            {
                position = 0;
            }
            position = Math.Max(0, position);
            var offset = 0.0;
            for (var index = 0; index < tracks.Count; index++)
            {
                var duration = DurationOf(tracks[index]);
                // Treat an exact boundary as the beginning of the next track. The
                // small tolerance avoids floating-point drift at chapter edges.
                if (index == tracks.Count - 1 || position < offset + duration - 0.001)
                {
                    return index;
                }
                offset += duration;
            }
            return tracks.Count - 1;
        }

        public static void AlignChapters(IList<KoboChapter> chapters, IList<KoboTrack> tracks)
        {
            if (chapters == null || tracks == null || chapters.Count != tracks.Count)
            {
                return;
            }
            var offset = 0.0;
            for (var index = 0; index < tracks.Count; index++)
            {
                var duration = DurationOf(tracks[index]);
                var chapter = chapters[index];
                if (chapter != null)
                {
                    chapter.StartSeconds = offset;
                    chapter.EndSeconds = offset + duration;
                    if (String.IsNullOrWhiteSpace(chapter.Title))
                    {
                        chapter.Title = tracks[index] == null || String.IsNullOrWhiteSpace(tracks[index].Title)
                            ? "Chapter " + (index + 1)
                            : tracks[index].Title;
                    }
                }
                offset += duration;
            }
        }

        private static double DurationOf(KoboTrack track)
        {
            return track == null || Double.IsNaN(track.DurationSeconds) || Double.IsInfinity(track.DurationSeconds)
                ? 0
                : Math.Max(0, track.DurationSeconds);
        }
    }
}
