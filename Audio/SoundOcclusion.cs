using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client;

namespace SurroundSoundLab;

internal static class SoundOcclusion
{
    private static readonly object SyncRoot = new();
    private static readonly List<EntitySoundOcclusionDebugRay> DebugRays = new();
    private static readonly ConditionalWeakTable<ILoadedSound, AppliedMarker> AppliedSounds = new();
    private static ICoreClientAPI capi;

    public static void Initialize(ICoreClientAPI api)
    {
        capi = api;
        Clear();
    }

    public static void Dispose()
    {
        Clear();
        capi = null;
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            DebugRays.Clear();
        }
    }

    public static List<EntitySoundOcclusionDebugRay> GetDebugRaySnapshot(long nowMs)
    {
        lock (SyncRoot)
        {
            DebugRays.RemoveAll(ray => ray.ExpiresMs <= nowMs);
            return new List<EntitySoundOcclusionDebugRay>(DebugRays);
        }
    }

    public static void ApplyInitialOcclusion(ILoadedSound sound, Vec3f soundPosition, float baseVolume)
    {
        SurroundSoundLabConfig config = SurroundSoundLabConfigManager.Current;
        if (!config.EnableEntitySoundBlockOcclusion || sound == null || soundPosition == null)
        {
            return;
        }

        Vec3f listenerPosition = GetListenerEarPosition();
        if (listenerPosition == null)
        {
            return;
        }

        int maxBlocks = Math.Max(0, config.EntitySoundBlockOcclusionMaxBlocks);
        int occludingBlocks = CountOpaqueBlocksBetween(listenerPosition, soundPosition, maxBlocks);
        RecordDebugRay(listenerPosition, soundPosition, occludingBlocks, maxBlocks);
        float volumeFactor = (float)Math.Pow(Math.Clamp(config.EntitySoundBlockOcclusionVolumePerBlock, 0f, 1f), occludingBlocks);
        float lowPass = (float)Math.Pow(Math.Clamp(config.EntitySoundBlockOcclusionLowPassPerBlock, 0f, 1f), occludingBlocks);
        volumeFactor = Math.Clamp(volumeFactor, Math.Clamp(config.EntitySoundBlockOcclusionMinVolumeFactor, 0f, 1f), 1f);
        lowPass = Math.Clamp(lowPass, Math.Clamp(config.EntitySoundBlockOcclusionMinLowPass, 0f, 1f), 1f);

        sound.SetVolume(baseVolume * volumeFactor);
        sound.SetLowPassfiltering(lowPass);
        MarkApplied(sound);
    }

    public static void ApplyInitialOcclusion(ILoadedSound sound)
    {
        SoundParams soundParams = sound?.Params;
        if (!IsEligibleStaticPositionalSound(soundParams))
        {
            return;
        }

        ApplyInitialOcclusion(sound, soundParams.Position, soundParams.Volume);
    }

    public static bool HasApplied(ILoadedSound sound)
    {
        return sound != null && AppliedSounds.TryGetValue(sound, out _);
    }

    private static void MarkApplied(ILoadedSound sound)
    {
        if (sound == null || AppliedSounds.TryGetValue(sound, out _))
        {
            return;
        }

        AppliedSounds.Add(sound, new AppliedMarker());
    }

    private static bool IsEligibleStaticPositionalSound(SoundParams soundParams)
    {
        if (soundParams?.Position == null || soundParams.RelativePosition)
        {
            return false;
        }

        return soundParams.SoundType != EnumSoundType.Music
            && soundParams.SoundType != EnumSoundType.MusicGlitchunaffected
            && soundParams.SoundType != EnumSoundType.Weather;
    }

    private static Vec3f GetListenerEarPosition()
    {
        Entity listenerEntity = capi?.World?.Player?.Entity;
        if (listenerEntity?.Pos == null)
        {
            return null;
        }

        return new Vec3f(
            (float)(listenerEntity.Pos.X + listenerEntity.LocalEyePos.X),
            (float)(listenerEntity.Pos.InternalY + listenerEntity.LocalEyePos.Y),
            (float)(listenerEntity.Pos.Z + listenerEntity.LocalEyePos.Z)
        );
    }

    private static int CountOpaqueBlocksBetween(Vec3f from, Vec3f to, int maxBlocks)
    {
        if (maxBlocks <= 0 || capi?.World?.BlockAccessor == null)
        {
            return 0;
        }

        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float dz = to.Z - from.Z;
        float distance = (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (distance < 1f)
        {
            return 0;
        }

        const float stepLength = 0.75f;
        int steps = Math.Max(1, (int)Math.Ceiling(distance / stepLength));
        int count = 0;
        int lastX = int.MinValue;
        int lastY = int.MinValue;
        int lastZ = int.MinValue;

        for (int i = 1; i < steps; i++)
        {
            float t = i / (float)steps;
            int x = (int)Math.Floor(from.X + (dx * t));
            int y = (int)Math.Floor(from.Y + (dy * t));
            int z = (int)Math.Floor(from.Z + (dz * t));
            if (x == lastX && y == lastY && z == lastZ)
            {
                continue;
            }

            lastX = x;
            lastY = y;
            lastZ = z;

            Block block = capi.World.BlockAccessor.GetBlock(new BlockPos(x, y, z));
            if (!IsSoundOccludingBlock(block))
            {
                continue;
            }

            count++;
            if (count >= maxBlocks)
            {
                return count;
            }
        }

        return count;
    }

    private static bool IsSoundOccludingBlock(Block block)
    {
        if (block == null || block.BlockMaterial == EnumBlockMaterial.Air)
        {
            return false;
        }

        return block.AllSidesOpaque || block.LightAbsorption >= 24;
    }

    private static void RecordDebugRay(Vec3f from, Vec3f to, int occludingBlocks, int maxBlocks)
    {
        if (!SurroundSoundLabConfigManager.Current.ShowEntitySoundOcclusionDebugRays)
        {
            return;
        }

        lock (SyncRoot)
        {
            long nowMs = capi?.ElapsedMilliseconds ?? 0;
            DebugRays.RemoveAll(ray => ray.ExpiresMs <= nowMs);
            DebugRays.Add(new EntitySoundOcclusionDebugRay(
                new Vec3d(from.X, from.Y, from.Z),
                new Vec3d(to.X, to.Y, to.Z),
                occludingBlocks,
                maxBlocks,
                nowMs + 6000
            ));

            while (DebugRays.Count > 128)
            {
                DebugRays.RemoveAt(0);
            }
        }
    }

    private sealed class AppliedMarker;
}

[HarmonyPatch(typeof(LoadedSoundNative), nameof(LoadedSoundNative.Start))]
internal static class LoadedSoundNativeStartStaticOcclusionPatch
{
    public static void Postfix(LoadedSoundNative __instance)
    {
        if (SoundOcclusion.HasApplied(__instance))
        {
            return;
        }

        SoundOcclusion.ApplyInitialOcclusion(__instance);
    }
}

internal readonly record struct EntitySoundOcclusionDebugRay(Vec3d From, Vec3d To, int OccludingBlocks, int MaxBlocks, long ExpiresMs);
