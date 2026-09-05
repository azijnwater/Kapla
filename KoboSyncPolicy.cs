using System;
using System.Collections.Generic;
using System.Globalization;

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

        public static int KoboProgressPercent(double positionSeconds, double durationSeconds)
        {
            var percent = ProgressPercent(positionSeconds, durationSeconds);
            return (int)Math.Max(0, Math.Min(100, Math.Round(percent, MidpointRounding.AwayFromZero)));
        }

        public static string PreferredProgressId(string entitlementId, string revisionId)
        {
            return !String.IsNullOrWhiteSpace(entitlementId) ? entitlementId : revisionId;
        }

        // Reading-state PUTs must use the entitlement id. A revision id can
        // identify the title, but Kobo rejects it as the {Ids} value for a
        // progress update (HTTP 400). Keep this separate from the broader
        // migration helper above, which is still useful for matching books.
        public static string ProgressUploadId(string entitlementId)
        {
            return String.IsNullOrWhiteSpace(entitlementId) ? null : entitlementId.Trim();
        }

        public static Dictionary<string, object> AudioTimestampLocation(double positionSeconds)
        {
            var safePosition = Double.IsNaN(positionSeconds) || Double.IsInfinity(positionSeconds)
                ? 0
                : Math.Max(0, positionSeconds);
            return new Dictionary<string, object>
            {
                { "Value", safePosition.ToString("R", CultureInfo.InvariantCulture) },
                { "Type", "AudioTimestamp" },
                { "Source", String.Empty }
            };
        }

        public static string ReadingStatus(double positionSeconds, double durationSeconds)
        {
            if (Double.IsNaN(positionSeconds) || Double.IsInfinity(positionSeconds) || positionSeconds <= 0)
            {
                return "ReadyToRead";
            }
            if (!Double.IsNaN(durationSeconds) && !Double.IsInfinity(durationSeconds)
                && durationSeconds > 0 && positionSeconds >= durationSeconds)
            {
                return "Finished";
            }
            return "Reading";
        }
    }
}
