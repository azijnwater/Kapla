using System;

namespace Kapla
{
    internal static class KoboSyncPolicy
    {
        public static TimeSpan ProgressSyncCooldown
        {
            get { return TimeSpan.FromMinutes(2); }
        }

        public static bool IsMeaningfulProgress(double currentSeconds, double lastQueuedSeconds, double thresholdSeconds)
        {
            if (Double.IsNaN(currentSeconds) || Double.IsInfinity(currentSeconds) || currentSeconds < 0)
            {
                return false;
            }
            if (Double.IsNaN(lastQueuedSeconds) || Double.IsInfinity(lastQueuedSeconds) || lastQueuedSeconds < 0)
            {
                return true;
            }
            var threshold = Double.IsNaN(thresholdSeconds) || Double.IsInfinity(thresholdSeconds)
                ? 1
                : Math.Max(1, thresholdSeconds);
            return Math.Abs(currentSeconds - lastQueuedSeconds) >= threshold;
        }

        public static TimeSpan RetryDelay(int failureCount)
        {
            var bounded = Math.Max(1, Math.Min(6, failureCount));
            return TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, bounded - 1)));
        }

        public static double ProgressPercent(double positionSeconds, double durationSeconds)
        {
            if (Double.IsNaN(positionSeconds) || Double.IsInfinity(positionSeconds)
                || Double.IsNaN(durationSeconds) || Double.IsInfinity(durationSeconds)
                || durationSeconds <= 0)
            {
                return 0;
            }
            return Math.Max(0, Math.Min(100, positionSeconds / durationSeconds * 100));
        }

        public static string PreferredProgressId(string entitlementId, string revisionId)
        {
            return !String.IsNullOrWhiteSpace(entitlementId) ? entitlementId : revisionId;
        }
    }
}
