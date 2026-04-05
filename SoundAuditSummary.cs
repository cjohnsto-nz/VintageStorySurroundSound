using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SurroundSoundLab;

internal static class SoundAuditSummaryCollector
{
    private static readonly object SyncRoot = new();
    private static string sessionFilePath;
    private static int totalAuditEvents;
    private static int sourceCreatedCount;
    private static int playbackStartedCount;
    private static int disposedCount;
    private static int directChannelEventCount;
    private static readonly Dictionary<string, int> routingClassificationCounts = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, int> channelCountCounts = new();
    private static readonly Dictionary<string, SoundAssetAuditAggregate> assets = new(StringComparer.OrdinalIgnoreCase);

    public static string LastSummaryFilePath { get; private set; }

    public static void Reset(string currentSessionFilePath)
    {
        lock (SyncRoot)
        {
            sessionFilePath = currentSessionFilePath;
            totalAuditEvents = 0;
            sourceCreatedCount = 0;
            playbackStartedCount = 0;
            disposedCount = 0;
            directChannelEventCount = 0;
            routingClassificationCounts.Clear();
            channelCountCounts.Clear();
            assets.Clear();
            LastSummaryFilePath = null;
        }
    }

    public static void Record(SoundAuditEvent auditEvent)
    {
        if (auditEvent == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            totalAuditEvents++;
            IncrementCount(routingClassificationCounts, auditEvent.RoutingClassification ?? "Unknown");
            IncrementCount(channelCountCounts, auditEvent.Channels);

            switch (auditEvent.EventType)
            {
                case SoundAuditEventType.SourceCreated:
                    sourceCreatedCount++;
                    break;
                case SoundAuditEventType.PlaybackStarted:
                    playbackStartedCount++;
                    break;
                case SoundAuditEventType.Disposed:
                    disposedCount++;
                    break;
            }

            if (auditEvent.UsesDirectChannels)
            {
                directChannelEventCount++;
            }

            string locationKey = string.IsNullOrWhiteSpace(auditEvent.Location) ? "<null>" : auditEvent.Location;
            if (!assets.TryGetValue(locationKey, out var aggregate))
            {
                aggregate = new SoundAssetAuditAggregate(locationKey);
                assets[locationKey] = aggregate;
            }

            aggregate.Record(auditEvent);
        }
    }

    public static SoundAuditSessionSummary BuildCurrentSummary()
    {
        lock (SyncRoot)
        {
            return new SoundAuditSessionSummary
            {
                GeneratedAtUtc = DateTime.UtcNow,
                SessionFilePath = sessionFilePath,
                TotalAuditEvents = totalAuditEvents,
                SourceCreatedCount = sourceCreatedCount,
                PlaybackStartedCount = playbackStartedCount,
                DisposedCount = disposedCount,
                DirectChannelEventCount = directChannelEventCount,
                DistinctAssets = assets.Count,
                RoutingClassificationCounts = routingClassificationCounts
                    .OrderByDescending(pair => pair.Value)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                ChannelCountCounts = channelCountCounts
                    .OrderBy(pair => pair.Key)
                    .ToDictionary(pair => pair.Key.ToString(), pair => pair.Value, StringComparer.Ordinal),
                Assets = assets.Values
                    .Select(aggregate => aggregate.ToSummary())
                    .OrderByDescending(summary => summary.PlaybackStartedCount)
                    .ThenBy(summary => summary.Location, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                SuspiciousAssets = assets.Values
                    .Select(aggregate => aggregate.ToSummary())
                    .Where(summary => summary.IsSuspicious)
                    .OrderByDescending(summary => summary.PlaybackStartedCount)
                    .ThenBy(summary => summary.Location, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }
    }

    public static string WriteCurrentSummary()
    {
        SoundAuditSessionSummary summary = BuildCurrentSummary();
        string logDir = AudioCapabilityReportWriter.GetLogDir();
        Directory.CreateDirectory(logDir);

        string filePath = Path.Combine(logDir, $"sound-audit-summary-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(filePath, json);

        lock (SyncRoot)
        {
            LastSummaryFilePath = filePath;
        }

        return filePath;
    }

    private static void IncrementCount(Dictionary<string, int> dictionary, string key)
    {
        dictionary.TryGetValue(key, out int current);
        dictionary[key] = current + 1;
    }

    private static void IncrementCount(Dictionary<int, int> dictionary, int key)
    {
        dictionary.TryGetValue(key, out int current);
        dictionary[key] = current + 1;
    }

    private sealed class SoundAssetAuditAggregate
    {
        private readonly HashSet<int> channelsSeen = new();
        private readonly HashSet<string> requestedOutputModes = new(StringComparer.Ordinal);
        private readonly HashSet<string> actualOutputModes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> routingCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> bearingCounts = new(StringComparer.Ordinal);
        private float minDistance = float.MaxValue;
        private float maxDistance = 0f;
        private double totalDistance;
        private int distanceSampleCount;

        public SoundAssetAuditAggregate(string location)
        {
            Location = location;
        }

        public string Location { get; }
        public string SoundType { get; private set; } = "Unknown";
        public int SourceCreatedCount { get; private set; }
        public int PlaybackStartedCount { get; private set; }
        public int DisposedCount { get; private set; }
        public bool UsesDirectChannelsEver { get; private set; }
        public bool RelativePositionEver { get; private set; }
        public bool HasNonZeroPositionEver { get; private set; }
        public bool HasPositionEver { get; private set; }

        public void Record(SoundAuditEvent auditEvent)
        {
            SoundType = auditEvent.SoundType.ToString();
            channelsSeen.Add(auditEvent.Channels);
            RelativePositionEver |= auditEvent.RelativePosition;
            HasPositionEver |= auditEvent.HasPosition;
            HasNonZeroPositionEver |= auditEvent.HasNonZeroPosition;
            UsesDirectChannelsEver |= auditEvent.UsesDirectChannels;

            if (!string.IsNullOrWhiteSpace(auditEvent.RequestedOutputMode))
            {
                requestedOutputModes.Add(auditEvent.RequestedOutputMode);
            }

            if (!string.IsNullOrWhiteSpace(auditEvent.ActualOutputMode))
            {
                actualOutputModes.Add(auditEvent.ActualOutputMode);
            }

            if (!string.IsNullOrWhiteSpace(auditEvent.RoutingClassification))
            {
                routingCounts.TryGetValue(auditEvent.RoutingClassification, out int current);
                routingCounts[auditEvent.RoutingClassification] = current + 1;
            }

            if (auditEvent.EventType == SoundAuditEventType.PlaybackStarted)
            {
                if (!string.IsNullOrWhiteSpace(auditEvent.BearingBucket))
                {
                    bearingCounts.TryGetValue(auditEvent.BearingBucket, out int currentBearing);
                    bearingCounts[auditEvent.BearingBucket] = currentBearing + 1;
                }

                if (auditEvent.Distance.HasValue)
                {
                    float distance = auditEvent.Distance.Value;
                    minDistance = Math.Min(minDistance, distance);
                    maxDistance = Math.Max(maxDistance, distance);
                    totalDistance += distance;
                    distanceSampleCount++;
                }
            }

            switch (auditEvent.EventType)
            {
                case SoundAuditEventType.SourceCreated:
                    SourceCreatedCount++;
                    break;
                case SoundAuditEventType.PlaybackStarted:
                    PlaybackStartedCount++;
                    break;
                case SoundAuditEventType.Disposed:
                    DisposedCount++;
                    break;
            }
        }

        public SoundAssetAuditSummary ToSummary()
        {
            var routingClasses = routingCounts.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
            bool stereoWithPositionalFlags = routingClasses.Contains("StereoWithPositionalFlags", StringComparer.Ordinal);
            bool multichannelWithoutDirect = routingClasses.Any(key => key.StartsWith("Multichannel", StringComparison.Ordinal)) && !UsesDirectChannelsEver;

            return new SoundAssetAuditSummary
            {
                Location = Location,
                SoundType = SoundType,
                SourceCreatedCount = SourceCreatedCount,
                PlaybackStartedCount = PlaybackStartedCount,
                DisposedCount = DisposedCount,
                ChannelsSeen = channelsSeen.OrderBy(channel => channel).ToList(),
                RoutingCounts = routingCounts.OrderByDescending(pair => pair.Value)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                RequestedOutputModes = requestedOutputModes.OrderBy(mode => mode, StringComparer.Ordinal).ToList(),
                ActualOutputModes = actualOutputModes.OrderBy(mode => mode, StringComparer.Ordinal).ToList(),
                UsesDirectChannelsEver = UsesDirectChannelsEver,
                RelativePositionEver = RelativePositionEver,
                HasPositionEver = HasPositionEver,
                HasNonZeroPositionEver = HasNonZeroPositionEver,
                BearingCounts = bearingCounts.OrderByDescending(pair => pair.Value)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                MinDistance = distanceSampleCount > 0 ? minDistance : null,
                MaxDistance = distanceSampleCount > 0 ? maxDistance : null,
                AverageDistance = distanceSampleCount > 0 ? (float?)(totalDistance / distanceSampleCount) : null,
                IsSuspicious = stereoWithPositionalFlags || multichannelWithoutDirect,
                SuspicionReasons = BuildSuspicionReasons(stereoWithPositionalFlags, multichannelWithoutDirect)
            };
        }

        private static List<string> BuildSuspicionReasons(bool stereoWithPositionalFlags, bool multichannelWithoutDirect)
        {
            var reasons = new List<string>();
            if (stereoWithPositionalFlags)
            {
                reasons.Add("Stereo source used with positional flags.");
            }

            if (multichannelWithoutDirect)
            {
                reasons.Add("Multichannel source observed without direct-channel routing.");
            }

            return reasons;
        }
    }
}

internal sealed class SoundAuditSessionSummary
{
    public DateTime GeneratedAtUtc { get; set; }
    public string SessionFilePath { get; set; }
    public int TotalAuditEvents { get; set; }
    public int SourceCreatedCount { get; set; }
    public int PlaybackStartedCount { get; set; }
    public int DisposedCount { get; set; }
    public int DirectChannelEventCount { get; set; }
    public int DistinctAssets { get; set; }
    public Dictionary<string, int> RoutingClassificationCounts { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> ChannelCountCounts { get; set; } = new(StringComparer.Ordinal);
    public List<SoundAssetAuditSummary> Assets { get; set; } = new();
    public List<SoundAssetAuditSummary> SuspiciousAssets { get; set; } = new();
}

internal sealed class SoundAssetAuditSummary
{
    public string Location { get; set; }
    public string SoundType { get; set; }
    public int SourceCreatedCount { get; set; }
    public int PlaybackStartedCount { get; set; }
    public int DisposedCount { get; set; }
    public List<int> ChannelsSeen { get; set; } = new();
    public Dictionary<string, int> RoutingCounts { get; set; } = new(StringComparer.Ordinal);
    public List<string> RequestedOutputModes { get; set; } = new();
    public List<string> ActualOutputModes { get; set; } = new();
    public bool UsesDirectChannelsEver { get; set; }
    public bool RelativePositionEver { get; set; }
    public bool HasPositionEver { get; set; }
    public bool HasNonZeroPositionEver { get; set; }
    public Dictionary<string, int> BearingCounts { get; set; } = new(StringComparer.Ordinal);
    public float? MinDistance { get; set; }
    public float? MaxDistance { get; set; }
    public float? AverageDistance { get; set; }
    public bool IsSuspicious { get; set; }
    public List<string> SuspicionReasons { get; set; } = new();
}
