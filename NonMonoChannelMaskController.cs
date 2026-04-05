using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HarmonyLib;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace SurroundSoundLab;

internal enum SurroundSpeaker
{
    FL,
    FR,
    FC,
    LFE,
    SL,
    SR
}

internal static class NonMonoChannelMaskController
{
    private const int AlDirectChannelsSoft = 0x1033;
    private static readonly object SyncRoot = new();
    private static readonly HashSet<SurroundSpeaker> MutedSpeakers = new();

    internal static string DescribeMutedSpeakers()
    {
        lock (SyncRoot)
        {
            return MutedSpeakers.Count == 0
                ? "none"
                : string.Join(", ", MutedSpeakers.OrderBy(speaker => speaker.ToString(), StringComparer.Ordinal).Select(speaker => speaker.ToString()));
        }
    }

    internal static bool ToggleSpeaker(SurroundSpeaker speaker)
    {
        lock (SyncRoot)
        {
            if (!MutedSpeakers.Add(speaker))
            {
                MutedSpeakers.Remove(speaker);
                return false;
            }

            return true;
        }
    }

    internal static void Clear()
    {
        lock (SyncRoot)
        {
            MutedSpeakers.Clear();
        }
    }

    internal static bool HasAnyMutedSpeakers()
    {
        lock (SyncRoot)
        {
            return MutedSpeakers.Count > 0;
        }
    }

    internal static bool TryPrepareMaskedPcm(LoadedSoundNative instance, out MaskedPcmState state)
    {
        state = null;

        if (instance == null)
        {
            return false;
        }

        AudioMetaData sample = LoadedSoundNativeChannelMaskPatch.SampleRef(instance);
        if (sample?.Pcm == null || sample.Channels <= 1 || sample.BitsPerSample <= 0)
        {
            return false;
        }

        Monitor.Enter(sample);
        try
        {
            byte[] originalPcm = sample.Pcm;
            int originalChannels = sample.Channels;
            bool upmixedStereo = false;

            byte[] workingPcm = originalPcm;
            int workingChannels = originalChannels;

            if (SurroundSoundLabConfigManager.Current.UpmixStereoToSurround
                && TryResolveStereoExpansionTargetChannels(out int targetChannels)
                && originalChannels == 2
                && targetChannels > originalChannels)
            {
                workingPcm = UpmixStereoToTargetChannels(originalPcm, sample.BitsPerSample, targetChannels);
                workingChannels = targetChannels;
                upmixedStereo = true;
            }

            byte[] processedPcm = ApplyMutedSpeakerMask(workingPcm, workingChannels, sample.BitsPerSample);
            if (ReferenceEquals(processedPcm, originalPcm) && workingChannels == originalChannels)
            {
                Monitor.Exit(sample);
                return false;
            }

            sample.Pcm = processedPcm;
            sample.Channels = workingChannels;
            state = new MaskedPcmState(sample, originalPcm, originalChannels, upmixedStereo);
            return true;
        }
        catch
        {
            Monitor.Exit(sample);
            throw;
        }
    }

    internal static void Restore(MaskedPcmState state)
    {
        if (state == null)
        {
            return;
        }

        try
        {
            state.Sample.Pcm = state.OriginalPcm;
            state.Sample.Channels = state.OriginalChannels;
        }
        finally
        {
            Monitor.Exit(state.Sample);
        }
    }

    internal static void ApplyPostCreateSourceProcessing(LoadedSoundNative instance, MaskedPcmState state)
    {
        if (instance == null || state == null || !state.UpmixedStereo)
        {
            return;
        }

        try
        {
            if (!OpenTK.Audio.OpenAL.AL.IsExtensionPresent("AL_SOFT_direct_channels"))
            {
                return;
            }

            int sourceId = LoadedSoundNativeChannelMaskPatch.SourceIdRef(instance);
            if (sourceId == 0)
            {
                return;
            }

            OpenTK.Audio.OpenAL.AL.Source(sourceId, (OpenTK.Audio.OpenAL.ALSourcei)AlDirectChannelsSoft, 1);
        }
        catch
        {
        }
    }

    private static byte[] ApplyMutedSpeakerMask(byte[] originalPcm, int channels, int bitsPerSample)
    {
        int bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample <= 0)
        {
            return originalPcm;
        }

        int[] mutedChannelIndices = ResolveMutedChannelIndices(channels);
        if (mutedChannelIndices.Length == 0)
        {
            return originalPcm;
        }

        byte[] masked = (byte[])originalPcm.Clone();
        int frameSize = channels * bytesPerSample;
        byte silenceByte = bitsPerSample == 8 ? (byte)128 : (byte)0;

        for (int offset = 0; offset + frameSize <= masked.Length; offset += frameSize)
        {
            foreach (int channelIndex in mutedChannelIndices)
            {
                int sampleOffset = offset + (channelIndex * bytesPerSample);
                for (int i = 0; i < bytesPerSample; i++)
                {
                    masked[sampleOffset + i] = silenceByte;
                }
            }
        }

        return masked;
    }

    private static int[] ResolveMutedChannelIndices(int channels)
    {
        lock (SyncRoot)
        {
            return channels switch
            {
                2 => ResolveTwoChannelIndices(),
                4 => ResolveFourChannelIndices(),
                6 => ResolveSixChannelIndices(),
                7 => ResolveSevenChannelIndices(),
                8 => ResolveEightChannelIndices(),
                _ => Array.Empty<int>()
            };
        }
    }

    private static int[] ResolveTwoChannelIndices()
    {
        var indices = new List<int>(2);
        if (MutedSpeakers.Contains(SurroundSpeaker.FL))
        {
            indices.Add(0);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.FR))
        {
            indices.Add(1);
        }

        return indices.ToArray();
    }

    private static int[] ResolveFourChannelIndices()
    {
        var indices = new List<int>(4);
        if (MutedSpeakers.Contains(SurroundSpeaker.FL))
        {
            indices.Add(0);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.FR))
        {
            indices.Add(1);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.SL))
        {
            indices.Add(2);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.SR))
        {
            indices.Add(3);
        }

        return indices.ToArray();
    }

    private static int[] ResolveSixChannelIndices()
    {
        var indices = new List<int>(6);
        if (MutedSpeakers.Contains(SurroundSpeaker.FL))
        {
            indices.Add(0);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.FR))
        {
            indices.Add(1);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.FC))
        {
            indices.Add(2);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.LFE))
        {
            indices.Add(3);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.SL))
        {
            indices.Add(4);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.SR))
        {
            indices.Add(5);
        }

        return indices.ToArray();
    }

    private static int[] ResolveSevenChannelIndices()
    {
        var indices = new List<int>(7);
        if (MutedSpeakers.Contains(SurroundSpeaker.FL))
        {
            indices.Add(0);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.FR))
        {
            indices.Add(1);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.FC))
        {
            indices.Add(2);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.LFE))
        {
            indices.Add(3);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.SL))
        {
            indices.Add(4);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.SR))
        {
            indices.Add(5);
        }

        return indices.ToArray();
    }

    private static int[] ResolveEightChannelIndices()
    {
        var indices = new List<int>(8);
        if (MutedSpeakers.Contains(SurroundSpeaker.FL))
        {
            indices.Add(0);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.FR))
        {
            indices.Add(1);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.FC))
        {
            indices.Add(2);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.LFE))
        {
            indices.Add(3);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.SL))
        {
            indices.Add(4);
            indices.Add(6);
        }

        if (MutedSpeakers.Contains(SurroundSpeaker.SR))
        {
            indices.Add(5);
            indices.Add(7);
        }

        return indices.ToArray();
    }

    private static bool TryResolveStereoExpansionTargetChannels(out int targetChannels)
    {
        targetChannels = AudioOpenAlInitContextPatch.LastActualOutputMode switch
        {
            "Quad" => 4,
            "5.1" => 6,
            "6.1" => 7,
            "7.1" => 8,
            _ => 0
        };

        if (targetChannels != 0)
        {
            return true;
        }

        targetChannels = SurroundSoundLabConfigManager.Current.OutputMode switch
        {
            SurroundOutputMode.Quad => 4,
            SurroundOutputMode.Surround5Point1 => 6,
            SurroundOutputMode.Surround6Point1 => 7,
            SurroundOutputMode.Surround7Point1 => 8,
            _ => 0
        };

        return targetChannels != 0;
    }

    private static byte[] UpmixStereoToTargetChannels(byte[] originalPcm, int bitsPerSample, int targetChannels)
    {
        int bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample <= 0)
        {
            return originalPcm;
        }

        float gain = DbToLinear(SurroundSoundLabConfigManager.Current.StereoUpmixGainDb);
        int stereoFrameSize = 2 * bytesPerSample;
        int frameCount = originalPcm.Length / stereoFrameSize;
        byte[] expanded = new byte[frameCount * targetChannels * bytesPerSample];
        bool isEightBit = bitsPerSample == 8;
        float[] mapped = new float[8];

        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            int sourceOffset = frameIndex * stereoFrameSize;
            float left = ReadSampleAsFloat(originalPcm, sourceOffset, bytesPerSample, isEightBit) * gain;
            float right = ReadSampleAsFloat(originalPcm, sourceOffset + bytesPerSample, bytesPerSample, isEightBit) * gain;
            float center = (left + right) * 0.5f;

            Array.Clear(mapped, 0, mapped.Length);
            switch (targetChannels)
            {
                case 4:
                    mapped[0] = left;
                    mapped[1] = right;
                    mapped[2] = left;
                    mapped[3] = right;
                    break;
                case 6:
                    mapped[0] = left;
                    mapped[1] = right;
                    mapped[2] = 0f;
                    mapped[3] = 0f;
                    mapped[4] = left;
                    mapped[5] = right;
                    break;
                case 7:
                    mapped[0] = left;
                    mapped[1] = right;
                    mapped[2] = 0f;
                    mapped[3] = 0f;
                    mapped[4] = left;
                    mapped[5] = right;
                    mapped[6] = center;
                    break;
                case 8:
                    mapped[0] = left;
                    mapped[1] = right;
                    mapped[2] = 0f;
                    mapped[3] = 0f;
                    mapped[4] = left;
                    mapped[5] = right;
                    mapped[6] = left;
                    mapped[7] = right;
                    break;
                default:
                    return originalPcm;
            }

            int targetOffset = frameIndex * targetChannels * bytesPerSample;
            for (int channel = 0; channel < targetChannels; channel++)
            {
                WriteFloatSample(expanded, targetOffset + (channel * bytesPerSample), bytesPerSample, isEightBit, mapped[channel]);
            }
        }

        return expanded;
    }

    private static float DbToLinear(float decibels)
    {
        return MathF.Pow(10f, decibels / 20f);
    }

    private static float ReadSampleAsFloat(byte[] data, int offset, int bytesPerSample, bool isEightBit)
    {
        if (isEightBit)
        {
            return (data[offset] - 128) / 127f;
        }

        if (bytesPerSample >= 2)
        {
            short sample = BitConverter.ToInt16(data, offset);
            return sample / 32768f;
        }

        return 0f;
    }

    private static void WriteFloatSample(byte[] target, int offset, int bytesPerSample, bool isEightBit, float value)
    {
        value = Math.Clamp(value, -1f, 1f);

        if (isEightBit)
        {
            int sample = (int)Math.Round((value * 127f) + 128f);
            target[offset] = (byte)Math.Clamp(sample, 0, 255);
            return;
        }

        if (bytesPerSample >= 2)
        {
            short sample = (short)Math.Clamp((int)Math.Round(value * 32767f), short.MinValue, short.MaxValue);
            byte[] bytes = BitConverter.GetBytes(sample);
            target[offset] = bytes[0];
            target[offset + 1] = bytes[1];
        }
    }
}

[HarmonyPatch(typeof(LoadedSoundNative), "createSoundSource")]
internal static class LoadedSoundNativeChannelMaskPatch
{
    internal static readonly AccessTools.FieldRef<LoadedSoundNative, AudioMetaData> SampleRef =
        AccessTools.FieldRefAccess<LoadedSoundNative, AudioMetaData>("sample");

    internal static readonly AccessTools.FieldRef<LoadedSoundNative, int> SourceIdRef =
        AccessTools.FieldRefAccess<LoadedSoundNative, int>("sourceId");

    public static void Prefix(LoadedSoundNative __instance, out MaskedPcmState __state)
    {
        NonMonoChannelMaskController.TryPrepareMaskedPcm(__instance, out __state);
    }

    public static void Postfix(LoadedSoundNative __instance, MaskedPcmState __state)
    {
        NonMonoChannelMaskController.ApplyPostCreateSourceProcessing(__instance, __state);
    }

    public static Exception Finalizer(Exception __exception, MaskedPcmState __state)
    {
        NonMonoChannelMaskController.Restore(__state);
        return __exception;
    }
}

internal sealed class MaskedPcmState
{
    public MaskedPcmState(AudioMetaData sample, byte[] originalPcm, int originalChannels, bool upmixedStereo)
    {
        Sample = sample;
        OriginalPcm = originalPcm;
        OriginalChannels = originalChannels;
        UpmixedStereo = upmixedStereo;
    }

    public AudioMetaData Sample { get; }
    public byte[] OriginalPcm { get; }
    public int OriginalChannels { get; }
    public bool UpmixedStereo { get; }
}
