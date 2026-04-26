using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace SurroundSoundLab;

internal static class SoundOcclusion
{
    private static readonly object SyncRoot = new();
    private static readonly List<EntitySoundOcclusionDebugRay> DebugRays = new();
    private static readonly List<ILoadedSound> ActiveStaticSounds = new();
    private static readonly ConditionalWeakTable<ILoadedSound, SoundOcclusionState> AppliedSounds = new();
    private static ICoreClientAPI capi;
    private static long tickListenerId;

    public static void Initialize(ICoreClientAPI api)
    {
        Dispose();
        capi = api;
        Clear();
        if (!SurroundSoundLabConfigManager.Current.EnableStaticSoundBlockOcclusion)
        {
            return;
        }

        int refreshMs = Math.Max(1, SurroundSoundLabConfigManager.Current.EntitySoundBlockOcclusionRefreshMs);
        tickListenerId = api.Event.RegisterGameTickListener(OnGameTick, refreshMs);
    }

    public static void Dispose()
    {
        if (capi != null && tickListenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }

        Clear();
        capi = null;
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            DebugRays.Clear();
            ActiveStaticSounds.Clear();
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
        if (!ApplyOcclusion(sound, soundPosition, baseVolume, force: true))
        {
            return;
        }
    }

    public static void ApplyInitialOcclusion(ILoadedSound sound)
    {
        if (!SurroundSoundLabConfigManager.Current.EnableStaticSoundBlockOcclusion)
        {
            return;
        }

        SoundParams soundParams = sound?.Params;
        if (!IsEligibleStaticPositionalSound(soundParams))
        {
            return;
        }

        if (!ShouldTrackStaticSound(soundParams))
        {
            return;
        }

        ApplyInitialOcclusion(sound, soundParams.Position, soundParams.Volume);
        RegisterEligibleStaticSound(sound);
    }

    public static bool HasApplied(ILoadedSound sound)
    {
        return sound != null && AppliedSounds.TryGetValue(sound, out _);
    }

    public static void ApplyDynamicOcclusion(ILoadedSound sound, Vec3f soundPosition, float baseVolume)
    {
        ApplyOcclusion(sound, soundPosition, baseVolume, force: false);
    }

    private static bool ApplyOcclusion(ILoadedSound sound, Vec3f soundPosition, float baseVolume, bool force)
    {
        SurroundSoundLabConfig config = SurroundSoundLabConfigManager.Current;
        if (!config.EnableEntitySoundBlockOcclusion || sound == null || soundPosition == null)
        {
            return false;
        }

        SoundOcclusionState state = AppliedSounds.GetOrCreateValue(sound);
        long nowMs = capi?.ElapsedMilliseconds ?? 0;
        int refreshMs = Math.Max(1, config.EntitySoundBlockOcclusionRefreshMs);
        if (!force && nowMs > 0 && state.LastEvaluationMs > 0 && (nowMs - state.LastEvaluationMs) < refreshMs)
        {
            return false;
        }

        state.BaseVolume = baseVolume;
        state.LastEvaluationMs = nowMs;

        Vec3f listenerPosition = GetListenerEarPosition();
        if (listenerPosition == null)
        {
            return false;
        }

        float distance = soundPosition.DistanceTo(listenerPosition);
        float maxBlocks = Math.Max(0, config.EntitySoundBlockOcclusionMaxBlocks);
        float minDistance = Math.Max(0f, config.EntitySoundBlockOcclusionMinDistance);
        float occlusionUnits = 0f;
        if (distance > minDistance)
        {
            bool listenerInRoom = IsInsideCurrentRoom(listenerPosition);
            bool soundInRoom = IsInsideCurrentRoom(soundPosition);
            occlusionUnits = CountOcclusionUnitsBetween(listenerPosition, soundPosition, maxBlocks);

            if (listenerInRoom != soundInRoom)
            {
                occlusionUnits = Math.Max(occlusionUnits, Math.Max(0f, config.EntitySoundBlockOcclusionMinOutsideRoomUnits));
            }
        }
        float effectiveOcclusionUnits = ShapeOcclusionUnits(occlusionUnits);
        float volumeFactor = (float)Math.Pow(Math.Clamp(config.EntitySoundBlockOcclusionVolumePerBlock, 0f, 1f), effectiveOcclusionUnits);
        float lowPass = (float)Math.Pow(Math.Clamp(config.EntitySoundBlockOcclusionLowPassPerBlock, 0f, 1f), effectiveOcclusionUnits);
        volumeFactor = Math.Clamp(volumeFactor, Math.Clamp(config.EntitySoundBlockOcclusionMinVolumeFactor, 0f, 1f), 1f);
        lowPass = Math.Clamp(lowPass, Math.Clamp(config.EntitySoundBlockOcclusionMinLowPass, 0f, 1f), 1f);
        int debugOccludingBlocks = (int)Math.Ceiling(occlusionUnits);
        RecordDebugRay(listenerPosition, soundPosition, debugOccludingBlocks, (int)Math.Ceiling(maxBlocks), volumeFactor, lowPass);

        sound.SetVolume(baseVolume * volumeFactor);
        sound.SetLowPassfiltering(lowPass);
        return true;
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

    private static void RegisterEligibleStaticSound(ILoadedSound sound)
    {
        if (sound == null || !ShouldTrackStaticSound(sound.Params))
        {
            return;
        }

        lock (SyncRoot)
        {
            if (!ActiveStaticSounds.Contains(sound))
            {
                ActiveStaticSounds.Add(sound);
            }
        }
    }

    private static bool ShouldTrackStaticSound(SoundParams soundParams)
    {
        string path = soundParams?.Location?.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        SurroundSoundLabConfig config = SurroundSoundLabConfigManager.Current;
        if (!config.LimitStaticSoundBlockOcclusionToWhitelist)
        {
            return true;
        }

        List<string> whitelist = config.StaticSoundBlockOcclusionSoundWhitelist;
        if (whitelist == null || whitelist.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < whitelist.Count; i++)
        {
            string token = whitelist[i];
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (path.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void OnGameTick(float deltaTime)
    {
        lock (SyncRoot)
        {
            for (int i = ActiveStaticSounds.Count - 1; i >= 0; i--)
            {
                ILoadedSound sound = ActiveStaticSounds[i];
                if (sound == null || sound.IsDisposed || sound.HasStopped)
                {
                    ActiveStaticSounds.RemoveAt(i);
                    continue;
                }

                SoundParams soundParams = sound.Params;
                if (!IsEligibleStaticPositionalSound(soundParams))
                {
                    ActiveStaticSounds.RemoveAt(i);
                    continue;
                }

                ApplyOcclusion(sound, soundParams.Position, soundParams.Volume, force: false);
            }
        }
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

    private static float CountOcclusionUnitsBetween(Vec3f from, Vec3f to, float maxBlocks)
    {
        if (maxBlocks <= 0f || capi?.World?.BlockAccessor == null)
        {
            return 0f;
        }

        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float dz = to.Z - from.Z;
        float distance = (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (distance < 1f)
        {
            return 0f;
        }

        const float stepLength = 0.25f;
        int steps = Math.Max(1, (int)Math.Ceiling(distance / stepLength));
        float occlusion = 0f;
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

            BlockPos pos = new(x, y, z);
            Block block = capi.World.BlockAccessor.GetBlock(pos);
            float blockOcclusion = GetBlockOcclusionWeight(block);
            if (blockOcclusion <= 0f)
            {
                continue;
            }

            occlusion += blockOcclusion;
            if (occlusion >= maxBlocks)
            {
                return maxBlocks;
            }
        }

        return occlusion;
    }

    private static float ShapeOcclusionUnits(float occlusionUnits)
    {
        return Math.Max(0f, occlusionUnits);
    }

    private static bool IsInSameRoom(Vec3f listenerPosition, Vec3f soundPosition)
    {
        if (listenerPosition == null || soundPosition == null)
        {
            return false;
        }

        return IsInsideCurrentRoom(listenerPosition) && IsInsideCurrentRoom(soundPosition);
    }

    private static bool IsInsideCurrentRoom(Vec3f position)
    {
        return position != null && SystemSoundEngine.RoomLocation.ContainsOrTouches(position);
    }

    private static float GetBlockOcclusionWeight(Block block)
    {
        if (block == null)
        {
            return 0f;
        }

        if (block.Replaceable >= 6000)
        {
            return 0f;
        }

        if (IsOpenableBlock(block))
        {
            return IsOpenBlock(block) ? 0f : 1f;
        }

        return IsSubstantialOccluder(block) ? 1f : 0f;
    }

    private static bool IsOpenableBlock(Block block)
    {
        string path = block.Code?.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string normalizedPath = path.ToLowerInvariant();
        return IsOpenablePath(normalizedPath);
    }

    private static bool IsOpenablePath(string normalizedPath)
    {
        return normalizedPath.Contains("door")
            || normalizedPath.Contains("trapdoor")
            || normalizedPath.Contains("gate");
    }

    private static bool IsOpenBlock(Block block)
    {
        string path = block.Code?.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string normalizedPath = path.ToLowerInvariant();
        return normalizedPath.Contains("opened") || normalizedPath.Contains("open");
    }

    private static bool IsSubstantialOccluder(Block block)
    {
        if (block.BlockMaterial == EnumBlockMaterial.Air
            || block.BlockMaterial == EnumBlockMaterial.Fire
            || block.BlockMaterial == EnumBlockMaterial.Water
            || block.BlockMaterial == EnumBlockMaterial.Lava)
        {
            return false;
        }

        if (block.Replaceable >= 2000)
        {
            return false;
        }

        int solidFaces = 0;
        for (int i = 0; i < BlockFacing.ALLFACES.Length; i++)
        {
            int faceIndex = BlockFacing.ALLFACES[i].Index;
            if (block.SideSolid[faceIndex])
            {
                solidFaces++;
            }
        }

        return solidFaces >= 5;
    }


    private static void RecordDebugRay(Vec3f from, Vec3f to, int occludingBlocks, int maxBlocks, float volumeFactor, float lowPassFactor)
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
                volumeFactor,
                lowPassFactor,
                nowMs + 6000
            ));

            while (DebugRays.Count > 128)
            {
                DebugRays.RemoveAt(0);
            }
        }
    }

    private sealed class SoundOcclusionState
    {
        public float BaseVolume;
        public long LastEvaluationMs;
    }
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

internal readonly record struct EntitySoundOcclusionDebugRay(Vec3d From, Vec3d To, int OccludingBlocks, int MaxBlocks, float VolumeFactor, float LowPassFactor, long ExpiresMs);
