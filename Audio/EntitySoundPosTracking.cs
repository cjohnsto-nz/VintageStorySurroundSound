using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace SurroundSoundLab;

internal static class EntitySoundPosTrackingController
{
    private static readonly object SyncRoot = new();
    private static readonly List<TrackedEntitySound> TrackedSounds = new();
    private static ICoreClientAPI capi;
    private static long tickListenerId;

    public static int TrackedCount
    {
        get
        {
            lock (SyncRoot)
            {
                return TrackedSounds.Count;
            }
        }
    }

    public static void Initialize(ICoreClientAPI api)
    {
        Dispose();
        capi = api;
        int updateMs = Math.Max(1, SurroundSoundLabConfigManager.Current.EntitySoundPosTrackingUpdateMs);
        tickListenerId = api.Event.RegisterGameTickListener(OnGameTick, updateMs);
        api.Event.OnEntityDespawn += OnEntityDespawn;
    }

    public static void Dispose()
    {
        if (capi != null)
        {
            if (tickListenerId != 0)
            {
                capi.Event.UnregisterGameTickListener(tickListenerId);
                tickListenerId = 0;
            }

            capi.Event.OnEntityDespawn -= OnEntityDespawn;
        }

        lock (SyncRoot)
        {
            TrackedSounds.Clear();
        }

        capi = null;
    }

    public static void Register(ILoadedSound sound, EntitySoundPosTrackingMetadata metadata)
    {
        if (sound == null || metadata == null || !SurroundSoundLabConfigManager.Current.EnableEntitySoundPosTracking)
        {
            return;
        }

        lock (SyncRoot)
        {
            for (int i = 0; i < TrackedSounds.Count; i++)
            {
                if (ReferenceEquals(TrackedSounds[i].Sound, sound))
                {
                    TrackedSounds[i] = CreateTrackedSound(sound, metadata);
                    TryUpdateSound(TrackedSounds[i]);
                    return;
                }
            }

            int maxTracked = Math.Max(1, SurroundSoundLabConfigManager.Current.MaxTrackedEntitySounds);
            while (TrackedSounds.Count >= maxTracked)
            {
                TrackedSounds.RemoveAt(0);
            }

            TrackedSounds.Add(CreateTrackedSound(sound, metadata));
            TryUpdateSound(TrackedSounds[^1]);
        }
    }

    private static void OnGameTick(float deltaTime)
    {
        if (!SurroundSoundLabConfigManager.Current.EnableEntitySoundPosTracking)
        {
            return;
        }

        lock (SyncRoot)
        {
            for (int i = TrackedSounds.Count - 1; i >= 0; i--)
            {
                TrackedEntitySound tracked = TrackedSounds[i];
                if (tracked.Sound == null || tracked.Sound.IsDisposed || tracked.Sound.HasStopped)
                {
                    TrackedSounds.RemoveAt(i);
                    continue;
                }

                if (!TryUpdateSound(tracked))
                {
                    HandleLostEntity(tracked.Sound);
                    TrackedSounds.RemoveAt(i);
                }
            }
        }
    }

    private static void OnEntityDespawn(Entity entity, EntityDespawnData reasonData)
    {
        if (entity == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            for (int i = TrackedSounds.Count - 1; i >= 0; i--)
            {
                TrackedEntitySound tracked = TrackedSounds[i];
                if (tracked.Metadata.EntityId != entity.EntityId)
                {
                    continue;
                }

                HandleLostEntity(tracked.Sound);
                TrackedSounds.RemoveAt(i);
            }
        }
    }

    private static TrackedEntitySound CreateTrackedSound(ILoadedSound sound, EntitySoundPosTrackingMetadata metadata)
    {
        Vec3f listenerPosition = GetListenerPosition();
        Vec3f soundPosition = null;
        if (TryResolveEntity(metadata, out Entity entity))
        {
            soundPosition = BuildTrackedPosition(entity, metadata.AnchorOffset);
        }

        float basePitch = sound.Params?.Pitch ?? 1f;
        float baseVolume = sound.Params?.Volume ?? 1f;
        float initialDistance = soundPosition != null && listenerPosition != null ? Distance(soundPosition, listenerPosition) : 0f;
        var tracked = new TrackedEntitySound(sound, metadata, basePitch, basePitch, baseVolume, initialDistance, soundPosition, listenerPosition, capi?.ElapsedMilliseconds ?? 0);
        ApplyInitialBlockOcclusion(tracked, soundPosition);
        return tracked;
    }

    private static bool TryUpdateSound(TrackedEntitySound tracked)
    {
        if (!TryResolveEntity(tracked.Metadata, out Entity entity))
        {
            return false;
        }

        Vec3f position = BuildTrackedPosition(entity, tracked.Metadata.AnchorOffset);
        tracked.Sound.SetPosition(position);
        ApplyDopplerPitch(tracked, position);
        return true;
    }

    private static bool TryResolveEntity(EntitySoundPosTrackingMetadata metadata, out Entity entity)
    {
        entity = null;
        if (metadata == null)
        {
            return false;
        }

        if (metadata.EntityReference.TryGetTarget(out Entity target) && target?.Pos != null)
        {
            entity = target;
            return true;
        }

        if (capi?.World?.LoadedEntities != null && capi.World.LoadedEntities.TryGetValue(metadata.EntityId, out target) && target?.Pos != null)
        {
            entity = target;
            return true;
        }

        return false;
    }

    private static Vec3f BuildTrackedPosition(Entity entity, Vec3f offset)
    {
        return new Vec3f(
            (float)(entity.Pos.X + offset.X),
            (float)(entity.Pos.InternalY + offset.Y),
            (float)(entity.Pos.Z + offset.Z)
        );
    }

    private static void ApplyDopplerPitch(TrackedEntitySound tracked, Vec3f soundPosition)
    {
        SurroundSoundLabConfig config = SurroundSoundLabConfigManager.Current;
        Vec3f listenerPosition = GetListenerPosition();
        long nowMs = capi?.ElapsedMilliseconds ?? 0;

        if (!config.EnableEntitySoundDoppler
            || tracked.LastSoundPosition == null
            || tracked.LastListenerPosition == null
            || listenerPosition == null
            || nowMs <= tracked.LastUpdateMs)
        {
            tracked.UpdateLastPositions(soundPosition, listenerPosition, nowMs);
            tracked.Sound.SetPitch(tracked.BasePitch);
            tracked.CurrentPitch = tracked.BasePitch;
            return;
        }

        float deltaSeconds = (nowMs - tracked.LastUpdateMs) / 1000f;
        if (deltaSeconds <= 0f)
        {
            return;
        }

        float currentDistance = Distance(soundPosition, listenerPosition);
        if (currentDistance < 0.001f)
        {
            tracked.UpdateLastPositions(soundPosition, listenerPosition, nowMs);
            tracked.Sound.SetPitch(tracked.BasePitch);
            tracked.CurrentPitch = tracked.BasePitch;
            return;
        }

        float speedOfSound = Math.Max(1f, config.EntitySoundDopplerSpeedOfSound);
        float strength = Math.Max(0f, config.EntitySoundDopplerStrength);
        float closingSpeed = (tracked.SmoothedDistance - currentDistance) / deltaSeconds;
        float velocitySmoothingSeconds = Math.Max(0.001f, config.EntitySoundDopplerVelocitySmoothingSeconds);
        float velocityBlend = 1f - (float)Math.Exp(-deltaSeconds / velocitySmoothingSeconds);
        tracked.SmoothedClosingSpeed += (closingSpeed - tracked.SmoothedClosingSpeed) * velocityBlend;

        float deadZone = Math.Max(0f, config.EntitySoundDopplerDeadZoneBlocksPerSecond);
        float smoothedClosingSpeed = Math.Abs(tracked.SmoothedClosingSpeed) < deadZone ? 0f : tracked.SmoothedClosingSpeed;
        float rawFactor = 1f + ((smoothedClosingSpeed * strength) / speedOfSound);
        float minFactor = Math.Clamp(config.EntitySoundDopplerMinPitchFactor, 0.1f, 3f);
        float maxFactor = Math.Clamp(config.EntitySoundDopplerMaxPitchFactor, minFactor, 3f);
        float targetPitch = Math.Clamp(tracked.BasePitch * rawFactor, tracked.BasePitch * minFactor, tracked.BasePitch * maxFactor);
        float pitchSmoothingSeconds = Math.Max(0.001f, config.EntitySoundDopplerPitchSmoothingSeconds);
        float pitchBlend = 1f - (float)Math.Exp(-deltaSeconds / pitchSmoothingSeconds);
        float pitch = tracked.CurrentPitch + ((targetPitch - tracked.CurrentPitch) * pitchBlend);

        tracked.Sound.SetPitch(pitch);
        tracked.CurrentPitch = pitch;
        tracked.SmoothedDistance += (currentDistance - tracked.SmoothedDistance) * velocityBlend;
        tracked.UpdateLastPositions(soundPosition, listenerPosition, nowMs);
    }

    private static Vec3f GetListenerPosition()
    {
        Entity listenerEntity = capi?.World?.Player?.Entity;
        if (listenerEntity?.Pos == null)
        {
            return null;
        }

        return new Vec3f((float)listenerEntity.Pos.X, (float)listenerEntity.Pos.InternalY, (float)listenerEntity.Pos.Z);
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

    private static void ApplyInitialBlockOcclusion(TrackedEntitySound tracked, Vec3f soundPosition)
    {
        SoundOcclusion.ApplyInitialOcclusion(tracked.Sound, soundPosition, tracked.BaseVolume);
    }

    private static float Distance(Vec3f first, Vec3f second)
    {
        float dx = first.X - second.X;
        float dy = first.Y - second.Y;
        float dz = first.Z - second.Z;
        return (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static void HandleLostEntity(ILoadedSound sound)
    {
        if (sound == null)
        {
            return;
        }

        if (sound.Params?.ShouldLoop == true)
        {
            if (SurroundSoundLabConfigManager.Current.StopLoopingEntitySoundsOnDespawn)
            {
                sound.FadeOutAndStop(0.15f);
            }

            return;
        }

        if (!SurroundSoundLabConfigManager.Current.FreezeOneShotEntitySoundsOnDespawn)
        {
            sound.FadeOutAndStop(0.05f);
        }
    }

    private sealed class TrackedEntitySound
    {
        public TrackedEntitySound(ILoadedSound sound, EntitySoundPosTrackingMetadata metadata, float basePitch, float currentPitch, float baseVolume, float smoothedDistance, Vec3f lastSoundPosition, Vec3f lastListenerPosition, long lastUpdateMs)
        {
            Sound = sound;
            Metadata = metadata;
            BasePitch = basePitch;
            CurrentPitch = currentPitch;
            BaseVolume = baseVolume;
            SmoothedDistance = smoothedDistance;
            LastSoundPosition = lastSoundPosition;
            LastListenerPosition = lastListenerPosition;
            LastUpdateMs = lastUpdateMs;
        }

        public ILoadedSound Sound { get; }
        public EntitySoundPosTrackingMetadata Metadata { get; }
        public float BasePitch { get; }
        public float CurrentPitch { get; set; }
        public float BaseVolume { get; }
        public float SmoothedDistance { get; set; }
        public float SmoothedClosingSpeed { get; set; }
        public Vec3f LastSoundPosition { get; private set; }
        public Vec3f LastListenerPosition { get; private set; }
        public long LastUpdateMs { get; private set; }

        public void UpdateLastPositions(Vec3f soundPosition, Vec3f listenerPosition, long updateMs)
        {
            LastSoundPosition = soundPosition;
            LastListenerPosition = listenerPosition;
            LastUpdateMs = updateMs;
        }
    }
}

internal sealed class EntitySoundPosTrackingMetadata
{
    public EntitySoundPosTrackingMetadata(Entity entity, Vec3f anchorOffset, bool inferred)
    {
        EntityId = entity.EntityId;
        EntityReference = new WeakReference<Entity>(entity);
        AnchorOffset = anchorOffset;
        Inferred = inferred;
    }

    public long EntityId { get; }
    public WeakReference<Entity> EntityReference { get; }
    public Vec3f AnchorOffset { get; }
    public bool Inferred { get; }
}

internal static class EntitySoundPosTrackingMetadataStore
{
    private static readonly ConditionalWeakTable<SoundParams, EntitySoundPosTrackingMetadata> MetadataByParams = new();

    public static void Attach(SoundParams soundParams, EntitySoundPosTrackingMetadata metadata)
    {
        if (soundParams == null || metadata == null)
        {
            return;
        }

        MetadataByParams.Remove(soundParams);
        MetadataByParams.Add(soundParams, metadata);
    }

    public static bool TryGet(SoundParams soundParams, out EntitySoundPosTrackingMetadata metadata)
    {
        metadata = null;
        return soundParams != null && MetadataByParams.TryGetValue(soundParams, out metadata);
    }
}

internal static class EntitySoundPosTrackingPlayback
{
    private static readonly AccessTools.FieldRef<ClientMain, Queue<ILoadedSound>> ActiveSoundsRef =
        AccessTools.FieldRefAccess<ClientMain, Queue<ILoadedSound>>("ActiveSounds");

    private static readonly MethodInfo StartPlayingMethod =
        AccessTools.Method(typeof(ClientMain), "StartPlaying", new[] { typeof(AudioData), typeof(SoundParams), typeof(AssetLocation) });

    public static bool TryPlayTrackedSoundAttributes(ClientMain game, SoundAttributes sound, Entity entity, float volumeMultiplier, out int durationMs)
    {
        durationMs = 0;
        if (!ShouldUseDefinitePosTracking(entity) || sound.Location == null)
        {
            return false;
        }

        Vec3f offset = ResolveEntityAnchorOffset(entity);
        Vec3f position = BuildPosition(entity, offset);
        var metadata = new EntitySoundPosTrackingMetadata(entity, offset, inferred: false);

        durationMs = PlayAt(game, sound.Location, position.X, position.Y, position.Z, sound.Volume.nextFloat(volumeMultiplier), sound.Pitch.nextFloat(), sound.Range, sound.Type, metadata);
        return true;
    }

    public static bool TryPlayTrackedAsset(ClientMain game, AssetLocation location, Entity entity, bool randomizePitch, float range, float volume)
    {
        if (!ShouldUseDefinitePosTracking(entity))
        {
            return false;
        }

        float pitch = randomizePitch ? game.RandomPitch() : 1f;
        return TryPlayTrackedAsset(game, location, entity, pitch, range, volume);
    }

    public static bool TryPlayTrackedAsset(ClientMain game, AssetLocation location, Entity entity, float pitch, float range, float volume)
    {
        if (!ShouldUseDefinitePosTracking(entity))
        {
            return false;
        }

        Vec3f offset = ResolveEntityAnchorOffset(entity);
        Vec3f position = BuildPosition(entity, offset);
        var metadata = new EntitySoundPosTrackingMetadata(entity, offset, inferred: false);
        PlayAt(game, location, position.X, position.Y, position.Z, volume, pitch, range, EnumSoundType.Sound, metadata);
        return true;
    }

    public static bool TryPlayInferredCoordinateSound(ClientMain game, AssetLocation location, double x, double y, double z, EnumSoundType soundType, float pitch, float range, float volume)
    {
        if (!SurroundSoundLabConfigManager.Current.EnableEntitySoundPosTracking
            || !SurroundSoundLabConfigManager.Current.EnableEntitySoundPosTrackingInference
            || !IsInferenceEligible(location, soundType))
        {
            return false;
        }

        var position = new Vec3f((float)x, (float)y, (float)z);
        if (!TryInferMetadata(game, position, out EntitySoundPosTrackingMetadata metadata))
        {
            return false;
        }

        PlayAt(game, location, x, y, z, volume, pitch, range, soundType, metadata);
        return true;
    }

    public static void TryAttachInferredMetadata(ClientMain game, SoundParams soundParams)
    {
        if (!SurroundSoundLabConfigManager.Current.EnableEntitySoundPosTracking
            || !SurroundSoundLabConfigManager.Current.EnableEntitySoundPosTrackingInference
            || soundParams?.Position == null
            || soundParams.RelativePosition
            || !IsInferenceEligible(soundParams.Location, soundParams.SoundType))
        {
            return;
        }

        if (TryInferMetadata(game, soundParams.Position, out EntitySoundPosTrackingMetadata metadata))
        {
            EntitySoundPosTrackingMetadataStore.Attach(soundParams, metadata);
        }
    }

    private static int PlayAt(ClientMain game, AssetLocation location, double x, double y, double z, float volume, float pitch, float range, EnumSoundType soundType, EntitySoundPosTrackingMetadata metadata)
    {
        if (Environment.CurrentManagedThreadId != RuntimeEnv.MainThreadId)
        {
            throw new InvalidOperationException("Cannot call PlaySound outside the main thread, it is not thread safe");
        }

        if (location == null || ClientSettings.SoundLevel == 0)
        {
            return 0;
        }

        Queue<ILoadedSound> activeSounds = ActiveSoundsRef(game);
        if (activeSounds != null && activeSounds.Count >= 250)
        {
            return 0;
        }

        AssetLocation resolvedLocation = game.ResolveSoundPath(location).Clone().WithPathAppendixOnce(".ogg");
        if (resolvedLocation == null)
        {
            return 0;
        }

        float scaledRange = SoundRangeFalloff.ScaleRange(range);
        if (game.Player?.Entity?.Pos != null && game.Player.Entity.Pos.SquareDistanceTo(x, y, z) > scaledRange * scaledRange)
        {
            return 0;
        }

        var soundParams = new SoundParams(resolvedLocation)
        {
            Position = new Vec3f((float)x, (float)y, (float)z),
            RelativePosition = false,
            Range = scaledRange,
            SoundType = soundType,
            Pitch = pitch
        };
        soundParams.Volume *= volume;
        EntitySoundPosTrackingMetadataStore.Attach(soundParams, metadata);

        if (!ScreenManager.soundAudioData.TryGetValue(resolvedLocation, out AudioData audioData) || audioData == null)
        {
            game.Platform.Logger.Warning("Audio File not found: {0}", resolvedLocation);
            return 0;
        }

        int loadResult = audioData.Load_Async(new MainThreadAction(game, () => InvokeStartPlaying(game, audioData, soundParams, resolvedLocation), "playSound"));
        return loadResult >= 0 ? loadResult : 500;
    }

    private static int InvokeStartPlaying(ClientMain game, AudioData audioData, SoundParams soundParams, AssetLocation location)
    {
        if (StartPlayingMethod == null)
        {
            return 0;
        }

        return (int)(StartPlayingMethod.Invoke(game, new object[] { audioData, soundParams, location }) ?? 0);
    }

    private static bool ShouldUseDefinitePosTracking(Entity entity)
    {
        return SurroundSoundLabConfigManager.Current.EnableEntitySoundPosTracking && entity?.Pos != null;
    }

    private static Vec3f ResolveEntityAnchorOffset(Entity entity)
    {
        float yOffset = 0f;
        if (entity.SelectionBox != null)
        {
            yOffset = entity.SelectionBox.Y2 / 2f;
        }
        else if (entity.Properties?.CollisionBoxSize != null)
        {
            yOffset = entity.Properties.CollisionBoxSize.Y / 2f;
        }

        return new Vec3f(0f, yOffset, 0f);
    }

    private static Vec3f BuildPosition(Entity entity, Vec3f offset)
    {
        return new Vec3f(
            (float)(entity.Pos.X + offset.X),
            (float)(entity.Pos.InternalY + offset.Y),
            (float)(entity.Pos.Z + offset.Z)
        );
    }

    private static bool TryInferMetadata(ClientMain game, Vec3f soundPosition, out EntitySoundPosTrackingMetadata metadata)
    {
        metadata = null;
        if (game?.LoadedEntities == null || soundPosition == null)
        {
            return false;
        }

        float maxDistance = Math.Max(0.1f, SurroundSoundLabConfigManager.Current.EntitySoundPosTrackingInferenceMaxDistance);
        double maxDistanceSq = maxDistance * maxDistance;
        double bestDistanceSq = double.MaxValue;
        double secondDistanceSq = double.MaxValue;
        Entity bestEntity = null;

        foreach (Entity entity in game.LoadedEntities.Values)
        {
            if (entity?.Pos == null || entity == game.EntityPlayer)
            {
                continue;
            }

            double dx = soundPosition.X - entity.Pos.X;
            double dy = soundPosition.Y - entity.Pos.InternalY;
            double dz = soundPosition.Z - entity.Pos.Z;
            double distanceSq = (dx * dx) + (dy * dy) + (dz * dz);
            if (distanceSq > maxDistanceSq)
            {
                continue;
            }

            if (distanceSq < bestDistanceSq)
            {
                secondDistanceSq = bestDistanceSq;
                bestDistanceSq = distanceSq;
                bestEntity = entity;
            }
            else if (distanceSq < secondDistanceSq)
            {
                secondDistanceSq = distanceSq;
            }
        }

        if (bestEntity == null)
        {
            return false;
        }

        double minimumSeparationSq = Math.Min(maxDistanceSq, 0.75f * 0.75f);
        if (secondDistanceSq < double.MaxValue && secondDistanceSq - bestDistanceSq < minimumSeparationSq)
        {
            return false;
        }

        var offset = new Vec3f(
            (float)(soundPosition.X - bestEntity.Pos.X),
            (float)(soundPosition.Y - bestEntity.Pos.InternalY),
            (float)(soundPosition.Z - bestEntity.Pos.Z)
        );
        metadata = new EntitySoundPosTrackingMetadata(bestEntity, offset, inferred: true);
        return true;
    }

    private static bool IsInferenceEligible(AssetLocation location, EnumSoundType soundType)
    {
        if (soundType == EnumSoundType.Entity)
        {
            return true;
        }

        string path = location?.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.Contains("creature", StringComparison.OrdinalIgnoreCase)
            || path.Contains("entity", StringComparison.OrdinalIgnoreCase)
            || path.Contains("voice", StringComparison.OrdinalIgnoreCase);
    }
}

[HarmonyPatch(typeof(ClientMain), nameof(ClientMain.PlaySoundAt), new Type[] { typeof(SoundAttributes), typeof(Entity), typeof(IPlayer), typeof(float) })]
internal static class ClientMainPlaySoundAtSoundAttributesEntityPosTrackingPatch
{
    public static bool Prefix(ClientMain __instance, SoundAttributes sound, Entity atEntity, float volumeMultiplier, ref int __result)
    {
        if (!EntitySoundPosTrackingPlayback.TryPlayTrackedSoundAttributes(__instance, sound, atEntity, volumeMultiplier, out int durationMs))
        {
            return true;
        }

        __result = durationMs;
        return false;
    }
}

[HarmonyPatch(typeof(ClientMain), nameof(ClientMain.PlaySoundAt), new Type[] { typeof(AssetLocation), typeof(Entity), typeof(IPlayer), typeof(bool), typeof(float), typeof(float) })]
internal static class ClientMainPlaySoundAtAssetEntityRandomPitchPosTrackingPatch
{
    public static bool Prefix(ClientMain __instance, AssetLocation location, Entity atEntity, bool randomizePitch, float range, float volume)
    {
        return !EntitySoundPosTrackingPlayback.TryPlayTrackedAsset(__instance, location, atEntity, randomizePitch, range, volume);
    }
}

[HarmonyPatch(typeof(ClientMain), nameof(ClientMain.PlaySoundAt), new Type[] { typeof(AssetLocation), typeof(Entity), typeof(IPlayer), typeof(float), typeof(float), typeof(float) })]
internal static class ClientMainPlaySoundAtAssetEntityPitchPosTrackingPatch
{
    public static bool Prefix(ClientMain __instance, AssetLocation location, Entity atEntity, float pitch, float range, float volume)
    {
        return !EntitySoundPosTrackingPlayback.TryPlayTrackedAsset(__instance, location, atEntity, pitch, range, volume);
    }
}

[HarmonyPatch(typeof(ClientMain), nameof(ClientMain.PlaySoundAt), new Type[] { typeof(AssetLocation), typeof(double), typeof(double), typeof(double), typeof(IPlayer), typeof(EnumSoundType), typeof(float), typeof(float), typeof(float) })]
internal static class ClientMainPlaySoundAtCoordinateTypedPosTrackingInferencePatch
{
    public static bool Prefix(ClientMain __instance, AssetLocation location, double posx, double posy, double posz, EnumSoundType soundType, float pitch, float range, float volume)
    {
        return !EntitySoundPosTrackingPlayback.TryPlayInferredCoordinateSound(__instance, location, posx, posy, posz, soundType, pitch, range, volume);
    }
}

[HarmonyPatch(typeof(ClientMain), nameof(ClientMain.LoadSound), new Type[] { typeof(SoundParams) })]
internal static class ClientMainLoadSoundPosTrackingInferencePatch
{
    public static void Prefix(ClientMain __instance, SoundParams sound)
    {
        EntitySoundPosTrackingPlayback.TryAttachInferredMetadata(__instance, sound);
    }
}

[HarmonyPatch(typeof(LoadedSoundNative), nameof(LoadedSoundNative.Start))]
internal static class LoadedSoundNativeStartPosTrackingPatch
{
    private static readonly AccessTools.FieldRef<LoadedSoundNative, SoundParams> SoundParamsRef =
        AccessTools.FieldRefAccess<LoadedSoundNative, SoundParams>("soundParams");

    public static void Postfix(LoadedSoundNative __instance)
    {
        if (__instance == null)
        {
            return;
        }

        SoundParams soundParams = SoundParamsRef(__instance);
        if (EntitySoundPosTrackingMetadataStore.TryGet(soundParams, out EntitySoundPosTrackingMetadata metadata))
        {
            EntitySoundPosTrackingController.Register(__instance, metadata);
        }
    }
}
