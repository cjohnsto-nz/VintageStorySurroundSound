using System;
using System.Diagnostics;
using System.IO;

namespace SurroundSoundLab;

// OpenAL exposes no query that proves Windows selected the Atmos encoder.
// Keep configuration, observed layout and receiver verification separate.
internal sealed class SpatialAudioStatus
{
    public bool Requested { get; init; }
    public bool StartupConfigured { get; init; }
    public bool RuntimeSupported { get; init; }
    public string NativeLibraryPath { get; init; }
    public string NativeLibraryVersion { get; init; }
    public string ConfigPath { get; init; }
    public string ConfigurationSource { get; init; }
    public string LogPath { get; init; }
    public string State { get; init; }
    public string Detail { get; init; }
    public bool HeightStreamStartedAtInitialization { get; init; }
    public bool ReceiverVerified => false;

    public static SpatialAudioStatus Capture(string runtimeVersion, bool hasContext, string actualMode)
    {
        bool requested = SurroundSoundLabConfigManager.Current.OutputMode == SurroundOutputMode.WindowsSpatialAudio;
        var configuration = SpatialStartupConfiguration.Current;
        bool startup = configuration.Configured;
        bool supported = SpatialAudioPolicy.SupportsSpatialBackend(runtimeVersion);
        var result = SpatialAudioPolicy.Evaluate(requested, startup, supported, hasContext, actualMode,
            AudioOpenAlInitContextPatch.LastInitializationFailure, AudioOpenAlInitContextPatch.HeightStreamStartedAtInitialization);
        string nativePath = null;
        string nativeVersion = null;
        try
        {
            using Process process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
            {
                string name = Path.GetFileName(module.FileName);
                if (!name.Equals("OpenAL32.dll", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("soft_oal.dll", StringComparison.OrdinalIgnoreCase)) continue;
                nativePath = module.FileName;
                nativeVersion = module.FileVersionInfo.FileVersion;
                break;
            }
        }
        catch (Exception) { /* Diagnostic failure must not interrupt playback. */ }
        return new SpatialAudioStatus
        {
            Requested = requested, StartupConfigured = startup, RuntimeSupported = supported,
            NativeLibraryPath = nativePath, NativeLibraryVersion = nativeVersion,
            ConfigPath = configuration.Path, ConfigurationSource = configuration.Source,
            LogPath = Environment.GetEnvironmentVariable("ALSOFT_LOGFILE"),
            State = result.State, Detail = result.Detail,
            HeightStreamStartedAtInitialization = AudioOpenAlInitContextPatch.HeightStreamStartedAtInitialization
        };
    }
}
