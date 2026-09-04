using System;

namespace Kapla
{
    internal static class KoboSyncPolicy
    {
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

        public static string PreferredProgressId(string entitlementId, string revisionId)
        {
            return !String.IsNullOrWhiteSpace(entitlementId) ? entitlementId : revisionId;
        }
    }
}
