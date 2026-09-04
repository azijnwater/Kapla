using System;

namespace Kapla
{
    internal static class KoboSyncPolicy
    {
        public static bool IsMeaningfulProgress(double currentSeconds, double lastQueuedSeconds, double thresholdSeconds)
        {
            return lastQueuedSeconds < 0 || Math.Abs(currentSeconds - lastQueuedSeconds) >= Math.Max(1, thresholdSeconds);
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
