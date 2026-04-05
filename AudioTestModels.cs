using System;
using System.Collections.Generic;

namespace SurroundSoundLab;

internal enum AudioTestContextType
{
    GameContext,
    LabContext,
    PatchedEnginePath
}

internal sealed class AudioTestResult
{
    public string TestId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string DeviceName { get; set; }
    public AudioTestContextType ContextType { get; set; }
    public string FormatKey { get; set; }
    public string FormatEnumName { get; set; }
    public int FormatEnumValue { get; set; }
    public int ChannelIndex { get; set; }
    public int Channels { get; set; }
    public bool StartedPlayback { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public string AlError { get; set; }
    public string AlcError { get; set; }
    public string SpeakerObserved { get; set; }
}

internal sealed class LabContextProbeResult
{
    public DateTime TimestampUtc { get; set; }
    public string DeviceName { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public Dictionary<string, LabFormatProbeResult> Formats { get; set; } = new();
}

internal sealed class LabFormatProbeResult
{
    public string FormatKey { get; set; }
    public string FormatEnumName { get; set; }
    public int FormatEnumValue { get; set; }
    public int Channels { get; set; }
    public bool Present { get; set; }
    public bool UploadSucceeded { get; set; }
    public bool PlaybackStarted { get; set; }
    public string AlError { get; set; }
    public string AlcError { get; set; }
    public string ErrorMessage { get; set; }
}
