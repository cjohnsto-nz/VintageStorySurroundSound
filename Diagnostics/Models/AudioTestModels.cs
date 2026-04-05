using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

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

internal enum SoundAuditEventType
{
    SourceCreated,
    PlaybackStarted,
    Disposed
}

internal sealed class SoundAuditEvent
{
    public long InstanceId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public SoundAuditEventType EventType { get; set; }
    public string Location { get; set; }
    public EnumSoundType SoundType { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
    public int SampleRate { get; set; }
    public int SourceId { get; set; }
    public int BufferId { get; set; }
    public bool RelativePosition { get; set; }
    public bool HasPosition { get; set; }
    public bool HasNonZeroPosition { get; set; }
    public float? PositionX { get; set; }
    public float? PositionY { get; set; }
    public float? PositionZ { get; set; }
    public float? ListenerPositionX { get; set; }
    public float? ListenerPositionY { get; set; }
    public float? ListenerPositionZ { get; set; }
    public float? ListenerForwardX { get; set; }
    public float? ListenerForwardY { get; set; }
    public float? ListenerForwardZ { get; set; }
    public float? RelativeOffsetX { get; set; }
    public float? RelativeOffsetY { get; set; }
    public float? RelativeOffsetZ { get; set; }
    public float? Distance { get; set; }
    public float? AzimuthDegrees { get; set; }
    public string BearingBucket { get; set; }
    public bool ShouldLoop { get; set; }
    public bool DisposeOnFinish { get; set; }
    public float Volume { get; set; }
    public float Pitch { get; set; }
    public float LowPassFilter { get; set; }
    public float ReverbDecayTime { get; set; }
    public float Range { get; set; }
    public float ReferenceDistance { get; set; }
    public string RequestedOutputMode { get; set; }
    public string ActualOutputMode { get; set; }
    public int? DirectChannelsValue { get; set; }
    public bool UsesDirectChannels { get; set; }
    public string RoutingClassification { get; set; }
    public string RoutingExplanation { get; set; }
}
