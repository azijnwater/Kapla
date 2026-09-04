using System;
using System.Collections.Generic;

namespace Kapla
{
    internal sealed class PlaybackProgressWindow
    {
        public double StartSeconds { get; set; }
        public double EndSeconds { get; set; }
        public double AbsoluteSeconds { get; set; }
        public double ElapsedSeconds { get { return Math.Max(0, AbsoluteSeconds - StartSeconds); } }
        public double RemainingSeconds { get { return Math.Max(0, EndSeconds - AbsoluteSeconds); } }
        public double DurationSeconds { get { return Math.Max(0, EndSeconds - StartSeconds); } }
        public int ChapterIndex { get; set; }
        public bool IsChapterRelative { get; set; }
    }

    internal static class PlaybackProgress
    {
        public const string ChapterMode = "Chapter progress";
        public const string BookMode = "Whole audiobook progress";

        public static PlaybackProgressWindow Calculate(double absoluteSeconds, double bookDurationSeconds, IList<KoboChapter> chapters, string mode)
        {
            var bookEnd = FiniteNonNegative(bookDurationSeconds);
            var requestedAbsolute = FiniteNonNegative(absoluteSeconds);
            var absolute = bookEnd > 0 ? Math.Min(bookEnd, requestedAbsolute) : requestedAbsolute;
            var result = new PlaybackProgressWindow
            {
                StartSeconds = 0,
                EndSeconds = Math.Max(1, bookEnd),
                AbsoluteSeconds = absolute,
                ChapterIndex = -1,
                IsChapterRelative = false
            };

            if (!String.Equals(mode, ChapterMode, StringComparison.OrdinalIgnoreCase) || chapters == null)
            {
                return result;
            }

            for (var index = 0; index < chapters.Count; index++)
            {
                var chapter = chapters[index];
                if (chapter == null || chapter.StartSeconds < 0 || chapter.EndSeconds <= chapter.StartSeconds)
                {
                    continue;
                }
                var isLastBoundary = index == chapters.Count - 1 && Math.Abs(absolute - chapter.EndSeconds) < 0.001;
                if ((absolute >= chapter.StartSeconds && absolute < chapter.EndSeconds) || isLastBoundary)
                {
                    result.StartSeconds = chapter.StartSeconds;
                    result.EndSeconds = chapter.EndSeconds;
                    result.AbsoluteSeconds = Math.Max(chapter.StartSeconds, Math.Min(chapter.EndSeconds, absolute));
                    result.ChapterIndex = index;
                    result.IsChapterRelative = true;
                    return result;
                }
            }
            return result;
        }

        public static double ToAbsolute(double sliderValue, PlaybackProgressWindow window)
        {
            var value = FiniteNonNegative(sliderValue);
            if (window == null)
            {
                return value;
            }
            var start = FiniteNonNegative(window.StartSeconds);
            var end = Math.Max(start, FiniteNonNegative(window.EndSeconds));
            return Math.Max(start, Math.Min(end, value));
        }

        private static double FiniteNonNegative(double value)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value) ? 0 : Math.Max(0, value);
        }
    }
}
