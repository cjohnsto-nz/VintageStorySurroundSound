using System;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using OpenTK.Audio.OpenAL;
using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace SurroundSoundLab;

[HarmonyPatch(typeof(LoadedSoundNative), "createSoundSource")]
internal static class LoadedSoundNativeCreateSoundSourceAuditPatch
{
    public static void Postfix(LoadedSoundNative __instance)
    {
        SoundAuditCollector.Capture(__instance, SoundAuditEventType.SourceCreated);
    }
}

[HarmonyPatch(typeof(LoadedSoundNative), nameof(LoadedSoundNative.Start))]
internal static class LoadedSoundNativeStartAuditPatch
{
    public static void Postfix(LoadedSoundNative __instance)
    {
        SoundAuditCollector.Capture(__instance, SoundAuditEventType.PlaybackStarted);
    }
}

[HarmonyPatch(typeof(LoadedSoundNative), nameof(LoadedSoundNative.Dispose))]
internal static class LoadedSoundNativeDisposeAuditPatch
{
    public static void Postfix(LoadedSoundNative __instance)
    {
        SoundAuditCollector.Capture(__instance, SoundAuditEventType.Disposed);
    }
}

internal static class SoundAuditCollector
{
    private const int DirectChannelsSoft = 0x1033;
    private static readonly ConditionalWeakTable<LoadedSoundNative, TrackedSoundInstance> InstanceIds = new();
    private static long nextInstanceId;

    private static readonly AccessTools.FieldRef<LoadedSoundNative, SoundParams> SoundParamsRef =
        AccessTools.FieldRefAccess<LoadedSoundNative, SoundParams>("soundParams");

    private static readonly AccessTools.FieldRef<LoadedSoundNative, AudioMetaData> SampleRef =
        AccessTools.FieldRefAccess<LoadedSoundNative, AudioMetaData>("sample");

    private static readonly AccessTools.FieldRef<LoadedSoundNative, int> SourceIdRef =
        AccessTools.FieldRefAccess<LoadedSoundNative, int>("sourceId");

    private static readonly AccessTools.FieldRef<LoadedSoundNative, int> BufferIdRef =
        AccessTools.FieldRefAccess<LoadedSoundNative, int>("bufferId");

    public static void Capture(LoadedSoundNative instance, SoundAuditEventType eventType)
    {
        if (!SurroundSoundLabConfigManager.Current.EnableSoundAudit || instance == null)
        {
            return;
        }

        try
        {
            SoundParams soundParams = SoundParamsRef(instance);
            AudioMetaData sample = SampleRef(instance);
            int sourceId = SourceIdRef(instance);
            int bufferId = BufferIdRef(instance);

            int? directChannelsValue = TryReadDirectChannelsValue(sourceId);
            bool usesDirectChannels = directChannelsValue.GetValueOrDefault() != 0;
            bool hasPosition = soundParams?.Position != null;
            bool hasNonZeroPosition = HasNonZeroPosition(soundParams?.Position);
            ListenerSnapshot listener = CaptureListenerSnapshot();

            var auditEvent = new SoundAuditEvent
            {
                InstanceId = GetInstanceId(instance),
                TimestampUtc = DateTime.UtcNow,
                EventType = eventType,
                Location = soundParams?.Location?.ToShortString(),
                SoundType = soundParams?.SoundType ?? default,
                Channels = sample?.Channels ?? 0,
                BitsPerSample = sample?.BitsPerSample ?? 0,
                SampleRate = sample?.Rate ?? 0,
                SourceId = sourceId,
                BufferId = bufferId,
                RelativePosition = soundParams?.RelativePosition ?? false,
                HasPosition = hasPosition,
                HasNonZeroPosition = hasNonZeroPosition,
                PositionX = hasPosition ? soundParams.Position.X : null,
                PositionY = hasPosition ? soundParams.Position.Y : null,
                PositionZ = hasPosition ? soundParams.Position.Z : null,
                ListenerPositionX = listener.HasPosition ? listener.Position.X : null,
                ListenerPositionY = listener.HasPosition ? listener.Position.Y : null,
                ListenerPositionZ = listener.HasPosition ? listener.Position.Z : null,
                ListenerForwardX = listener.HasForward ? listener.Forward.X : null,
                ListenerForwardY = listener.HasForward ? listener.Forward.Y : null,
                ListenerForwardZ = listener.HasForward ? listener.Forward.Z : null,
                ShouldLoop = soundParams?.ShouldLoop ?? false,
                DisposeOnFinish = soundParams?.DisposeOnFinish ?? false,
                Volume = soundParams?.Volume ?? 0f,
                Pitch = soundParams?.Pitch ?? 0f,
                LowPassFilter = soundParams?.LowPassFilter ?? 0f,
                ReverbDecayTime = soundParams?.ReverbDecayTime ?? 0f,
                Range = soundParams?.Range ?? 0f,
                ReferenceDistance = soundParams?.ReferenceDistance ?? 0f,
                RequestedOutputMode = AudioOpenAlInitContextPatch.LastRequestedOutputMode,
                ActualOutputMode = AudioOpenAlInitContextPatch.LastActualOutputMode,
                DirectChannelsValue = directChannelsValue,
                UsesDirectChannels = usesDirectChannels
            };

            ApplySpatialSnapshot(auditEvent, soundParams, listener);

            (auditEvent.RoutingClassification, auditEvent.RoutingExplanation) =
                ClassifyRouting(auditEvent.Channels, auditEvent.RelativePosition, auditEvent.HasNonZeroPosition, auditEvent.HasPosition, usesDirectChannels);

            SurroundSessionLogWriter.AppendSoundAuditEvent(auditEvent);
            SoundAuditSummaryCollector.Record(auditEvent);
        }
        catch
        {
        }
    }

    private static long GetInstanceId(LoadedSoundNative instance)
    {
        return InstanceIds.GetValue(instance, _ => new TrackedSoundInstance
        {
            Id = Interlocked.Increment(ref nextInstanceId)
        }).Id;
    }

    private static int? TryReadDirectChannelsValue(int sourceId)
    {
        if (sourceId == 0)
        {
            return null;
        }

        try
        {
            if (!AL.IsExtensionPresent("AL_SOFT_direct_channels"))
            {
                return null;
            }

            AL.GetSource(sourceId, (ALGetSourcei)DirectChannelsSoft, out int value);
            return value;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasNonZeroPosition(Vec3f position)
    {
        if (position == null)
        {
            return false;
        }

        return Math.Abs(position.X) > 0.001f || Math.Abs(position.Y) > 0.001f || Math.Abs(position.Z) > 0.001f;
    }

    private static ListenerSnapshot CaptureListenerSnapshot()
    {
        try
        {
            Vector3 position = AL.GetListener(ALListener3f.Position);
            AL.GetListener(ALListenerfv.Orientation, out Vector3 forward, out Vector3 up);

            return new ListenerSnapshot
            {
                HasPosition = true,
                HasForward = true,
                Position = position,
                Forward = forward
            };
        }
        catch
        {
            return default;
        }
    }

    private static void ApplySpatialSnapshot(SoundAuditEvent auditEvent, SoundParams soundParams, ListenerSnapshot listener)
    {
        if (auditEvent == null || soundParams?.Position == null)
        {
            return;
        }

        float offsetX;
        float offsetY;
        float offsetZ;

        if (soundParams.RelativePosition)
        {
            offsetX = soundParams.Position.X;
            offsetY = soundParams.Position.Y;
            offsetZ = soundParams.Position.Z;
            auditEvent.RelativeOffsetX = offsetX;
            auditEvent.RelativeOffsetY = offsetY;
            auditEvent.RelativeOffsetZ = offsetZ;
            auditEvent.Distance = MathF.Sqrt((offsetX * offsetX) + (offsetY * offsetY) + (offsetZ * offsetZ));
            auditEvent.BearingBucket = "ListenerRelative";
            return;
        }

        if (!listener.HasPosition)
        {
            return;
        }

        offsetX = soundParams.Position.X - listener.Position.X;
        offsetY = soundParams.Position.Y - listener.Position.Y;
        offsetZ = soundParams.Position.Z - listener.Position.Z;

        auditEvent.RelativeOffsetX = offsetX;
        auditEvent.RelativeOffsetY = offsetY;
        auditEvent.RelativeOffsetZ = offsetZ;
        auditEvent.Distance = MathF.Sqrt((offsetX * offsetX) + (offsetY * offsetY) + (offsetZ * offsetZ));

        if (!listener.HasForward)
        {
            return;
        }

        if (TryComputeAzimuth(offsetX, offsetZ, listener.Forward, out float azimuthDegrees))
        {
            auditEvent.AzimuthDegrees = azimuthDegrees;
            auditEvent.BearingBucket = ClassifyBearingBucket(azimuthDegrees, auditEvent.Distance.GetValueOrDefault());
        }
    }

    private static bool TryComputeAzimuth(float offsetX, float offsetZ, Vector3 listenerForward, out float azimuthDegrees)
    {
        azimuthDegrees = 0f;

        float planarDistance = MathF.Sqrt((offsetX * offsetX) + (offsetZ * offsetZ));
        if (planarDistance < 0.001f)
        {
            return false;
        }

        float forwardX = listenerForward.X;
        float forwardZ = listenerForward.Z;
        float forwardPlanarLength = MathF.Sqrt((forwardX * forwardX) + (forwardZ * forwardZ));
        if (forwardPlanarLength < 0.001f)
        {
            return false;
        }

        forwardX /= forwardPlanarLength;
        forwardZ /= forwardPlanarLength;

        float rightX = forwardZ;
        float rightZ = -forwardX;

        float dirX = offsetX / planarDistance;
        float dirZ = offsetZ / planarDistance;

        float forwardDot = (dirX * forwardX) + (dirZ * forwardZ);
        float rightDot = (dirX * rightX) + (dirZ * rightZ);

        azimuthDegrees = MathF.Atan2(rightDot, forwardDot) * (180f / MathF.PI);
        return true;
    }

    private static string ClassifyBearingBucket(float azimuthDegrees, float distance)
    {
        if (distance < 0.25f)
        {
            return "Center";
        }

        float normalized = azimuthDegrees;
        if (normalized < 0f)
        {
            normalized += 360f;
        }

        return normalized switch
        {
            >= 337.5f or < 22.5f => "Front",
            >= 22.5f and < 67.5f => "FrontRight",
            >= 67.5f and < 112.5f => "Right",
            >= 112.5f and < 157.5f => "BackRight",
            >= 157.5f and < 202.5f => "Back",
            >= 202.5f and < 247.5f => "BackLeft",
            >= 247.5f and < 292.5f => "Left",
            _ => "FrontLeft"
        };
    }

    private static (string Classification, string Explanation) ClassifyRouting(int channels, bool relativePosition, bool hasNonZeroPosition, bool hasPosition, bool usesDirectChannels)
    {
        if (channels <= 1)
        {
            if (relativePosition || hasNonZeroPosition)
            {
                return ("PositionalMono", "Mono source with positional flags; OpenAL should spatialize it across the active speaker layout.");
            }

            if (hasPosition)
            {
                return ("MonoOriginOrListenerSpace", "Mono source with a default/origin position; routing depends on listener position and game semantics.");
            }

            return ("MonoUnknown", "Mono source without explicit positioning information.");
        }

        if (usesDirectChannels)
        {
            return (channels == 2 ? "DirectStereoBed" : $"Direct{channels}chBed", "Non-mono source with AL_DIRECT_CHANNELS_SOFT enabled; channels should map directly to matching speakers.");
        }

        if (channels == 2)
        {
            if (relativePosition || hasNonZeroPosition)
            {
                return ("StereoWithPositionalFlags", "Stereo source with positional flags; the engine considers this a mismatch and behavior may be virtualization, attenuation issues, or partial spatialization.");
            }

            return ("StereoBedOrEngineManagedStereo", "Stereo source without direct channels; likely treated as conventional stereo rather than a positional bed.");
        }

        if (relativePosition || hasNonZeroPosition)
        {
            return ("MultichannelWithPositionalFlags", "Multichannel source carrying positional flags without direct-channel routing; behavior is engine/runtime dependent and should be reviewed.");
        }

        return ("MultichannelWithoutDirectChannels", "Multichannel source without AL_DIRECT_CHANNELS_SOFT; speaker mapping may depend on OpenAL default behavior instead of explicit routing.");
    }

    private sealed class TrackedSoundInstance
    {
        public long Id { get; init; }
    }

    private struct ListenerSnapshot
    {
        public bool HasPosition { get; init; }
        public bool HasForward { get; init; }
        public Vector3 Position { get; init; }
        public Vector3 Forward { get; init; }
    }
}
