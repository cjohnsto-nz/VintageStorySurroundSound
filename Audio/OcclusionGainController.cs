using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client;

namespace SurroundSoundLab;

// Keep attenuation out of SoundParams.Volume: vanilla owns that value and
// changes it during fades, including the Bell's zero-volume loop startup.
internal static class OcclusionGainController
{
    private sealed class GainState
    {
        internal volatile float Factor = 1f;
    }

    private static readonly ConditionalWeakTable<ILoadedSound, GainState> Gains = new();

    internal static void SetFactor(ILoadedSound sound, float factor)
    {
        Gains.GetOrCreateValue(sound).Factor = factor;
        sound.SetVolume(); // Reapply current vanilla volume without changing it.
    }

    internal static float GetFactor(ILoadedSound sound) =>
        Gains.TryGetValue(sound, out GainState state) ? state.Factor : 1f;

    internal static void Clear() => Gains.Clear();
}

// Apply attenuation at each gain write, preserving vanilla's category
// calculation, source recreation and fade implementation.
[HarmonyPatch]
internal static class LoadedSoundNativeOcclusionGainPatch
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(LoadedSoundNative), nameof(LoadedSoundNative.SetVolume), Type.EmptyTypes);
        yield return AccessTools.Method(typeof(LoadedSoundNative), nameof(LoadedSoundNative.SetVolume), new[] { typeof(float) });
        yield return AccessTools.Method(typeof(LoadedSoundNative), "createSoundSource");
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getter = AccessTools.PropertyGetter(typeof(LoadedSoundNative), "GlobalVolume");
        MethodInfo apply = AccessTools.Method(typeof(LoadedSoundNativeOcclusionGainPatch), nameof(Apply));
        int matches = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction;
            if (instruction.Calls(getter))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, apply);
                matches++;
            }
        }
        if (matches != 1) throw new InvalidOperationException("Unsupported vanilla sound gain implementation.");
    }

    private static float Apply(float categoryGain, LoadedSoundNative sound)
    {
        return categoryGain * OcclusionGainController.GetFactor(sound);
    }
}
