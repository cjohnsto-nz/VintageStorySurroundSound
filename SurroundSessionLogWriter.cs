using System;
using System.IO;
using System.Text.Json;

namespace SurroundSoundLab;

internal static class SurroundSessionLogWriter
{
    private static readonly object SyncRoot = new();
    private static string sessionFilePath;

    public static string SessionFilePath => sessionFilePath;

    public static void InitializeSession()
    {
        lock (SyncRoot)
        {
            string logDir = AudioCapabilityReportWriter.GetLogDir();
            Directory.CreateDirectory(logDir);
            sessionFilePath = Path.Combine(logDir, $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl");
            AppendEvent("session-started", new
            {
                timestampUtc = DateTime.UtcNow,
                sessionFilePath
            });
        }
    }

    public static void AppendProbeReport(AudioCapabilityReport report, string source)
    {
        AppendEvent("probe-report", new
        {
            timestampUtc = DateTime.UtcNow,
            source,
            report
        });
    }

    public static void AppendTestRun(AudioTestResult result)
    {
        AppendEvent("channel-test", result);
    }

    public static void AppendSpeakerObservation(string testId, string speakerObserved)
    {
        AppendEvent("speaker-observation", new
        {
            timestampUtc = DateTime.UtcNow,
            testId,
            speakerObserved
        });
    }

    public static void AppendLabProbe(LabContextProbeResult result)
    {
        AppendEvent("lab-context-probe", result);
    }

    private static void AppendEvent(string eventType, object payload)
    {
        lock (SyncRoot)
        {
            if (string.IsNullOrWhiteSpace(sessionFilePath))
            {
                InitializeSession();
            }

            string json = JsonSerializer.Serialize(new
            {
                eventType,
                payload
            });

            File.AppendAllText(sessionFilePath, json + Environment.NewLine);
        }
    }
}
