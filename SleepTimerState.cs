using System;

namespace Kapla
{
    internal enum SleepTimerMode
    {
        Off,
        Duration,
        EndOfChapter
    }

    internal sealed class SleepTimerState
    {
        public SleepTimerMode Mode { get; private set; }
        public DateTime? ExpiresUtc { get; private set; }
        public double? ChapterEndSeconds { get; private set; }
        public bool IsActive { get { return Mode != SleepTimerMode.Off; } }

        public void StartDuration(DateTime nowUtc, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException("duration");
            }
            Mode = SleepTimerMode.Duration;
            ExpiresUtc = nowUtc.Add(duration);
            ChapterEndSeconds = null;
        }

        public void StartEndOfChapter(double chapterEndSeconds, double currentPositionSeconds)
        {
            if (chapterEndSeconds <= currentPositionSeconds)
            {
                throw new ArgumentOutOfRangeException("chapterEndSeconds");
            }
            Mode = SleepTimerMode.EndOfChapter;
            ChapterEndSeconds = chapterEndSeconds;
            ExpiresUtc = null;
        }

        public bool HasExpired(DateTime nowUtc, double currentPositionSeconds)
        {
            return Mode == SleepTimerMode.Duration && ExpiresUtc.HasValue && nowUtc >= ExpiresUtc.Value
                || Mode == SleepTimerMode.EndOfChapter && ChapterEndSeconds.HasValue && currentPositionSeconds >= ChapterEndSeconds.Value - 0.05;
        }

        public TimeSpan Remaining(DateTime nowUtc, double currentPositionSeconds)
        {
            if (Mode == SleepTimerMode.Duration && ExpiresUtc.HasValue)
            {
                return ExpiresUtc.Value > nowUtc ? ExpiresUtc.Value - nowUtc : TimeSpan.Zero;
            }
            if (Mode == SleepTimerMode.EndOfChapter && ChapterEndSeconds.HasValue)
            {
                return TimeSpan.FromSeconds(Math.Max(0, ChapterEndSeconds.Value - currentPositionSeconds));
            }
            return TimeSpan.Zero;
        }

        public void Cancel()
        {
            Mode = SleepTimerMode.Off;
            ExpiresUtc = null;
            ChapterEndSeconds = null;
        }
    }
}
