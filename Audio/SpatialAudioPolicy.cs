using System;
using System.Text.RegularExpressions;

namespace SurroundSoundLab;

internal static class SpatialAudioPolicy
{
    internal static bool SupportsSpatialBackend(string version)
    {
        Match match = Regex.Match(version ?? "", @"ALSOFT\s+(\d+\.\d+\.\d+)");
        return match.Success && Version.TryParse(match.Groups[1].Value, out Version parsed)
            && parsed >= new Version(1, 24, 0);
    }

    internal static (string State, string Detail) Evaluate(bool requested, bool startupConfigured,
        bool runtimeSupported, bool hasContext, string actualMode, string initializationFailure, bool streamActivated = false)
    {
        if (!hasContext)
            return ("Unavailable", initializationFailure ?? "No active audio context.");
        if (!requested)
            return ("NotRequested", "Windows Spatial Audio mode is not selected.");
        if (!runtimeSupported)
            return ("SetupRequired", "Install the compatible spatial OpenAL runtime, then restart the game.");
        if (!startupConfigured)
            return ("RestartRequired", "Install the game-local spatial configuration and fully restart, or use the diagnostic launcher.");
        if (initializationFailure != null)
            return ("Fallback", initializationFailure);
        if (actualMode is "Stereo" or "Stereo Basic" or "Stereo HRTF" or "Mono" or "Quad" or "5.1" or "6.1" or "7.1")
            return ("HeightUnavailable", $"OpenAL reports {actualMode}; the requested 7.1.4 layout was not established.");
        if (streamActivated)
            return ("StreamActive", "7.1.4 Windows stream started at initialization. Verify Atmos input and overhead playback on the receiver.");
        return ("Unverified", "Spatial startup settings detected. Verify height playback and receiver format; no native activation log is available for this context.");
    }
}
