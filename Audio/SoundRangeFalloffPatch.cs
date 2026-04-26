using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace SurroundSoundLab;

internal static class SoundRangeFalloff
{
    private static readonly ConditionalWeakTable<SoundParams, AppliedMarker> AppliedSoundParams = new();

    public static float ScaleRange(float range)
    {
        float multiplier = SurroundSoundLabConfigManager.Current.SoundRangeMultiplier;
        if (range <= 0f || multiplier <= 0f || multiplier == 1f)
        {
            return range;
        }

        return range * multiplier;
    }

    public static void ApplyToSoundParams(SoundParams soundParams)
    {
        if (soundParams == null || AppliedSoundParams.TryGetValue(soundParams, out _))
        {
            return;
        }

        soundParams.Range = ScaleRange(soundParams.Range);
        AppliedSoundParams.Add(soundParams, new AppliedMarker());
    }

    private sealed class AppliedMarker;
}

[HarmonyPatch(typeof(ClientMain), "PlaySoundAtInternal")]
internal static class ClientMainPlaySoundAtInternalSoundRangePatch
{
    public static void Prefix(ref float range)
    {
        range = SoundRangeFalloff.ScaleRange(range);
    }
}

[HarmonyPatch(typeof(ClientMain), nameof(ClientMain.LoadSound), new Type[] { typeof(SoundParams) })]
internal static class ClientMainLoadSoundRangePatch
{
    public static void Prefix(SoundParams sound)
    {
        SoundRangeFalloff.ApplyToSoundParams(sound);
    }
}
