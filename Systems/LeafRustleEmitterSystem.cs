using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SurroundSoundLab;

internal sealed class LeafRustleEmitterSystem : IDisposable
{
    private const long LeafEmitterLifetimeMs = 10000;
    private const int MaxActiveLeafEmitters = 100;
    private const int MaxActiveEmittersPerPool = 25;
    private const long PoolCycleMs = 2500;
    private const double ImmediateRingMaxDistance = 2.0;
    private const double NearRingMaxDistance = 5.0;
    private const double MidRingMaxDistance = 20.0;
    private const double FarRingMaxDistance = 52.0;
    private const double AheadPreloadDistance = 25.0;
    private const double AheadPreloadRadius = 16.0;

    private static readonly AssetLocation[] BrightRustleAliases =
    {
        CustomSoundRegistry.LeafRustleOneAlias,
        CustomSoundRegistry.LeafRustleTwoAlias
    };

    private static readonly AssetLocation[] SoftRustleAliases =
    {
        CustomSoundRegistry.LeafRustleThreeAlias,
        CustomSoundRegistry.LeafRustleFourAlias
    };

    private readonly ICoreClientAPI capi;
    private readonly Random random = new();
    private readonly Dictionary<long, long> recentLeafTriggers = new();
    private readonly Dictionary<long, ActiveLeafEmitterState> activeLeafEmitters = new();
    private readonly List<DebugEmitter> activeEmitters = new();
    private readonly object activeEmittersLock = new();
    private long tickListenerId;
    private Vec3d lastPlayerPos;
    private bool hasLastPlayerPos;
    private bool hadLeafPresenceLastTick;

    public LeafRustleEmitterSystem(ICoreClientAPI capi)
    {
        this.capi = capi;
        tickListenerId = capi.Event.RegisterGameTickListener(OnGameTick, 100);
    }

    public void Dispose()
    {
        if (tickListenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }
    }

    public List<LeafRustleEmitterVisual> GetActiveEmittersSnapshot(long nowMs)
    {
        lock (activeEmittersLock)
        {
            activeEmitters.RemoveAll(emitter => emitter.ExpiresMs <= nowMs);
            var snapshot = new List<LeafRustleEmitterVisual>(activeEmitters.Count);
            foreach (var emitter in activeEmitters)
            {
                snapshot.Add(new LeafRustleEmitterVisual(
                    new Vec3d(emitter.Position.X, emitter.Position.Y, emitter.Position.Z),
                    emitter.ExpiresMs,
                    emitter.Ring,
                    emitter.Volume
                ));
            }

            return snapshot;
        }
    }

    public int GetActiveLeafEmitterCount(long nowMs)
    {
        CleanupCooldowns(nowMs);
        return activeLeafEmitters.Count;
    }

    private void OnGameTick(float deltaTime)
    {
        if (!SurroundSoundLabConfigManager.Current.EnableExperimentalLeafRustleEmitters)
        {
            return;
        }

        Entity playerEntity = capi.World?.Player?.Entity;
        if (playerEntity?.Pos == null)
        {
            return;
        }

        Vec3d currentPos = playerEntity.Pos.XYZ;
        if (!hasLastPlayerPos)
        {
            lastPlayerPos = new Vec3d(currentPos.X, currentPos.Y, currentPos.Z);
            hasLastPlayerPos = true;
            return;
        }

        double deltaX = currentPos.X - lastPlayerPos.X;
        double deltaY = currentPos.Y - lastPlayerPos.Y;
        double deltaZ = currentPos.Z - lastPlayerPos.Z;
        double horizontalMovement = Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        lastPlayerPos.Set(currentPos);
        float windExposure = GetWindExposure();

        if (windExposure < 0.08f)
        {
            hadLeafPresenceLastTick = false;
            CleanupCooldowns(capi.ElapsedMilliseconds);
            return;
        }

        long nowMs = capi.ElapsedMilliseconds;
        if (activeLeafEmitters.Count >= MaxActiveLeafEmitters)
        {
            CleanupCooldowns(nowMs);
            return;
        }

        Vec3d motion = new(deltaX, deltaY, deltaZ);
        CullOutOfRangeEmitters(playerEntity.Pos, motion, horizontalMovement, nowMs);
        if (TryEmitFromNearbyLeafBlock(playerEntity.Pos, motion, horizontalMovement, windExposure, nowMs, out int nearCount, out int farCount))
        {
            hadLeafPresenceLastTick = true;
        }
        else
        {
            hadLeafPresenceLastTick = nearCount > 0 || farCount > 0;
        }

        CleanupCooldowns(nowMs);
    }

    private void CullOutOfRangeEmitters(EntityPos playerPos, Vec3d motion, double horizontalMovement, long nowMs)
    {
        if (activeLeafEmitters.Count == 0)
        {
            return;
        }

        bool isMoving = horizontalMovement > 0.02;
        double motionLength = Math.Max(0.0001, Math.Sqrt((motion.X * motion.X) + (motion.Z * motion.Z)));
        GetFacingVector(playerPos, motion, isMoving, motionLength, out double motionNormX, out double motionNormZ);
        float movementFactor = GameMath.Clamp((float)(horizontalMovement / 0.2), 0f, 1f);
        Vec3d lookAheadCenter = new(
            playerPos.X + (motionNormX * (12.0 + (movementFactor * 24.0))),
            playerPos.Y,
            playerPos.Z + (motionNormZ * (12.0 + (movementFactor * 24.0)))
        );

        var expired = new List<long>();
        foreach (var pair in activeLeafEmitters)
        {
            if (pair.Value.ExpiresMs <= nowMs)
            {
                expired.Add(pair.Key);
                continue;
            }

            DecodeKey(pair.Key, out int x, out int y, out int z);
            double cx = x + 0.5;
            double cy = y + 0.5;
            double cz = z + 0.5;

            double relX = cx - playerPos.X;
            double relY = cy - playerPos.Y;
            double relZ = cz - playerPos.Z;
            double playerDistance = Math.Sqrt((relX * relX) + (relY * relY) + (relZ * relZ));

            double aheadX = cx - lookAheadCenter.X;
            double aheadY = cy - lookAheadCenter.Y;
            double aheadZ = cz - lookAheadCenter.Z;
            double lookAheadDistance = Math.Sqrt((aheadX * aheadX) + (aheadY * aheadY) + (aheadZ * aheadZ));

            if (playerDistance > 26.0 && (!isMoving || lookAheadDistance > 34.0))
            {
                expired.Add(pair.Key);
            }
        }

        foreach (long key in expired)
        {
            activeLeafEmitters.Remove(key);
        }
    }

    private bool TryEmitFromNearbyLeafBlock(EntityPos playerPos, Vec3d motion, double horizontalMovement, float windExposure, long nowMs, out int nearCount, out int farCount)
    {
        IBlockAccessor blockAccessor = capi.World.BlockAccessor;
        if (blockAccessor == null)
        {
            nearCount = 0;
            farCount = 0;
            return false;
        }

        bool isMoving = horizontalMovement > 0.02;
        double motionLength = Math.Max(0.0001, Math.Sqrt((motion.X * motion.X) + (motion.Z * motion.Z)));
        GetFacingVector(playerPos, motion, isMoving, motionLength, out double motionNormX, out double motionNormZ);
        float movementFactor = GameMath.Clamp((float)(horizontalMovement / 0.2), 0f, 1f);
        Vec3d lookAheadCenter = new(
            playerPos.X + (motionNormX * (10.0 + (movementFactor * 18.0))),
            playerPos.Y,
            playerPos.Z + (motionNormZ * (10.0 + (movementFactor * 18.0)))
        );
        Vec3d aheadPreloadCenter = new(
            playerPos.X + (motionNormX * AheadPreloadDistance),
            playerPos.Y,
            playerPos.Z + (motionNormZ * AheadPreloadDistance)
        );

        var immediateCandidates = new List<(BlockPos Pos, double Score)>();
        var nearCandidates = new List<(BlockPos Pos, double Score)>();
        var midCandidates = new List<(BlockPos Pos, double Score)>();
        var farCandidates = new List<(BlockPos Pos, double Score)>();
        int baseX = (int)Math.Floor(isMoving ? ((playerPos.X * 0.45) + (lookAheadCenter.X * 0.55)) : playerPos.X);
        int baseY = (int)Math.Floor(playerPos.Y);
        int baseZ = (int)Math.Floor(isMoving ? ((playerPos.Z * 0.45) + (lookAheadCenter.Z * 0.55)) : playerPos.Z);

        for (int dx = -34; dx <= 34; dx++)
        {
            for (int dy = -18; dy <= 18; dy++)
            {
                for (int dz = -34; dz <= 34; dz++)
                {
                    int x = baseX + dx;
                    int y = baseY + dy;
                    int z = baseZ + dz;
                    var pos = new BlockPos(x, y, z);
                    Block block = blockAccessor.GetBlock(pos);
                    if (block == null || block.BlockMaterial != EnumBlockMaterial.Leaves)
                    {
                        continue;
                    }

                    double centerX = x + 0.5;
                    double centerY = y + 0.5;
                    double centerZ = z + 0.5;
                    double relX = centerX - playerPos.X;
                    double relZ = centerZ - playerPos.Z;
                    double horizontalDistance = Math.Sqrt((relX * relX) + (relZ * relZ));
                    if (horizontalDistance < 0.8 || horizontalDistance > FarRingMaxDistance)
                    {
                        continue;
                    }

                    long key = ToKey(x, y, z);
                    long perLeafCooldownMs = horizontalDistance <= ImmediateRingMaxDistance
                        ? 180
                        : horizontalDistance <= NearRingMaxDistance
                            ? 550
                            : horizontalDistance <= MidRingMaxDistance
                                ? 900
                                : 1400;
                    if (activeLeafEmitters.TryGetValue(key, out ActiveLeafEmitterState activeState) && activeState.ExpiresMs > nowMs)
                    {
                        continue;
                    }

                    if (recentLeafTriggers.TryGetValue(key, out long lastTriggerMs) && nowMs - lastTriggerMs < perLeafCooldownMs)
                    {
                        continue;
                    }

                    double aheadRelX = centerX - lookAheadCenter.X;
                    double aheadRelZ = centerZ - lookAheadCenter.Z;
                    double aheadDistance = Math.Sqrt((aheadRelX * aheadRelX) + (aheadRelZ * aheadRelZ));
                    if (isMoving && aheadDistance > 42.0)
                    {
                        continue;
                    }

                    double preloadRelX = centerX - aheadPreloadCenter.X;
                    double preloadRelZ = centerZ - aheadPreloadCenter.Z;
                    double preloadDistance = Math.Sqrt((preloadRelX * preloadRelX) + (preloadRelZ * preloadRelZ));

                    double facingScore = isMoving ? (((relX * motionNormX) + (relZ * motionNormZ) + 1.0) * 0.5) : 0.5;
                    double lookAheadScore = isMoving ? Math.Max(0.0, 1.0 - (aheadDistance / 42.0)) : 0.5;
                    double distanceScore;
                    if (horizontalDistance <= 2.4)
                    {
                        distanceScore = 0.95 - (horizontalDistance * 0.08);
                    }
                    else
                    {
                        distanceScore = 0.9 - ((horizontalDistance - 2.4) / 49.6);
                    }

                    double verticalOffset = centerY - playerPos.Y;
                    double targetVerticalOffset = horizontalDistance <= ImmediateRingMaxDistance ? -0.25 : 0.35;
                    double heightPenalty = Math.Abs(verticalOffset - targetVerticalOffset);
                    double belowBonus = verticalOffset < 0 ? Math.Min(0.08, Math.Abs(verticalOffset) * 0.03) : 0;
                    double aboveBonus = verticalOffset > 0.75 ? Math.Min(0.16, (verticalOffset - 0.75) * 0.035) : 0;
                    double score = (distanceScore * 0.42) + (facingScore * 0.18) + (lookAheadScore * 0.28) + belowBonus + aboveBonus - (heightPenalty * 0.025);
                    if (score <= 0.1)
                    {
                        continue;
                    }

                    if (horizontalDistance <= ImmediateRingMaxDistance)
                    {
                        immediateCandidates.Add((pos, score));
                    }
                    else if (horizontalDistance <= NearRingMaxDistance)
                    {
                        nearCandidates.Add((pos, score));
                    }
                    else if (horizontalDistance <= MidRingMaxDistance)
                    {
                        midCandidates.Add((pos, score));
                    }
                    else if (!isMoving || preloadDistance <= AheadPreloadRadius)
                    {
                        double preloadScore = isMoving ? Math.Max(0.0, 1.0 - (preloadDistance / AheadPreloadRadius)) : 0.5;
                        farCandidates.Add((pos, score + (preloadScore * 0.25)));
                    }
                }
            }
        }

        nearCount = immediateCandidates.Count + nearCandidates.Count;
        farCount = midCandidates.Count + farCandidates.Count;

        if (immediateCandidates.Count == 0 && nearCandidates.Count == 0 && midCandidates.Count == 0 && farCandidates.Count == 0)
        {
            return false;
        }

        float leafFactor = GameMath.Clamp((immediateCandidates.Count * 0.04f) + (nearCandidates.Count * 0.02f) + (midCandidates.Count * 0.01f) + (farCandidates.Count * 0.004f), 0f, 1f);
        int emitted = 0;
        var usedKeys = new HashSet<long>();
        int slotsRemaining = Math.Max(0, MaxActiveLeafEmitters - activeLeafEmitters.Count);
        var activeCounts = GetActiveCountsByRing(nowMs);
        LeafRustleEmitterRing activeCycleRing = GetCycleRing(nowMs);

        int immediateCapacity = Math.Max(0, MaxActiveEmittersPerPool - activeCounts[LeafRustleEmitterRing.Immediate]);
        int immediateTarget = Math.Min(immediateCandidates.Count, Math.Min(Math.Min(slotsRemaining, immediateCapacity), 6));
        emitted += EmitRandomFromPool(immediateCandidates, immediateTarget, playerPos, motionNormX, motionNormZ, nowMs, windExposure, leafFactor, usedKeys, LeafRustleEmitterRing.Immediate, immediate: true);
        slotsRemaining = Math.Max(0, MaxActiveLeafEmitters - (activeLeafEmitters.Count + emitted));

        if (activeCycleRing == LeafRustleEmitterRing.Near && slotsRemaining > 0)
        {
            int nearCapacity = Math.Max(0, MaxActiveEmittersPerPool - activeCounts[LeafRustleEmitterRing.Near]);
            int nearTarget = Math.Min(Math.Min(slotsRemaining, nearCapacity), Math.Clamp(2 + (windExposure >= 0.7f ? 1 : 0), 0, 3));
            emitted += EmitRandomFromPool(nearCandidates, nearTarget, playerPos, motionNormX, motionNormZ, nowMs, windExposure, leafFactor, usedKeys, LeafRustleEmitterRing.Near, immediate: false);
            slotsRemaining = Math.Max(0, MaxActiveLeafEmitters - (activeLeafEmitters.Count + emitted));
        }

        if (activeCycleRing == LeafRustleEmitterRing.Mid && slotsRemaining > 0)
        {
            int midCapacity = Math.Max(0, MaxActiveEmittersPerPool - activeCounts[LeafRustleEmitterRing.Mid]);
            int midTarget = Math.Min(Math.Min(slotsRemaining, midCapacity), Math.Clamp(3 + (windExposure >= 0.85f ? 1 : 0), 0, 4));
            emitted += EmitRandomFromPool(midCandidates, midTarget, playerPos, motionNormX, motionNormZ, nowMs, windExposure, leafFactor, usedKeys, LeafRustleEmitterRing.Mid, immediate: false);
            slotsRemaining = Math.Max(0, MaxActiveLeafEmitters - (activeLeafEmitters.Count + emitted));
        }

        if (activeCycleRing == LeafRustleEmitterRing.Far && slotsRemaining > 0)
        {
            int farCapacity = Math.Max(0, MaxActiveEmittersPerPool - activeCounts[LeafRustleEmitterRing.Far]);
            int farTarget = Math.Min(Math.Min(slotsRemaining, farCapacity), Math.Clamp(3 + (windExposure >= 0.9f ? 2 : 0), 0, 5));
            emitted += EmitRandomFromPool(farCandidates, farTarget, playerPos, motionNormX, motionNormZ, nowMs, windExposure, leafFactor, usedKeys, LeafRustleEmitterRing.Far, immediate: false);
        }

        return emitted > 0;
    }

    private int EmitRandomFromPool(List<(BlockPos Pos, double Score)> sourcePool, int targetCount, EntityPos playerPos, double facingNormX, double facingNormZ, long nowMs, float windExposure, float leafFactor, HashSet<long> usedKeys, LeafRustleEmitterRing ring, bool immediate)
    {
        if (targetCount <= 0 || sourcePool.Count == 0)
        {
            return 0;
        }

        int emitted = 0;
        int attempts = 0;
        int maxAttempts = Math.Max(targetCount * 4, 12);

        while (emitted < targetCount && attempts < maxAttempts)
        {
            attempts++;
            int index = random.Next(sourcePool.Count);
            BlockPos pos = sourcePool[index].Pos;
            if (immediate)
            {
                if (TryEmitSpecific(pos, playerPos, facingNormX, facingNormZ, nowMs, windExposure, leafFactor, usedKeys, ring))
                {
                    emitted++;
                }
            }
            else
            {
                if (TryEmitFromPool(sourcePool, playerPos, facingNormX, facingNormZ, nowMs, windExposure, leafFactor, usedKeys, ring))
                {
                    emitted++;
                }
            }
        }

        return emitted;
    }

    private bool TryEmitSpecific(BlockPos pos, EntityPos playerPos, double facingNormX, double facingNormZ, long nowMs, float windExposure, float leafFactor, HashSet<long> usedKeys, LeafRustleEmitterRing ring)
    {
        long key = ToKey(pos.X, pos.Y, pos.Z);
        if (!usedKeys.Add(key))
        {
            return false;
        }

        double sx = pos.X + 0.5 + ((random.NextDouble() - 0.5) * 0.35);
        double sy = pos.Y + 0.55 + (random.NextDouble() * 0.35);
        double sz = pos.Z + 0.5 + ((random.NextDouble() - 0.5) * 0.35);
        recentLeafTriggers[key] = nowMs;
        activeLeafEmitters[key] = new ActiveLeafEmitterState(nowMs + LeafEmitterLifetimeMs, ring);

        return PlayEmitterAt(sx, sy, sz, playerPos, facingNormX, facingNormZ, windExposure, leafFactor, ring, nowMs);
    }

    private bool TryEmitFromPool(List<(BlockPos Pos, double Score)> sourcePool, EntityPos playerPos, double facingNormX, double facingNormZ, long nowMs, float windExposure, float leafFactor, HashSet<long> usedKeys, LeafRustleEmitterRing ring)
    {
        if (sourcePool.Count == 0)
        {
            return false;
        }

        int poolSize = sourcePool.Count;
        for (int attempt = 0; attempt < Math.Min(poolSize * 2, 32); attempt++)
        {
            var chosen = sourcePool[random.Next(poolSize)];
            long key = ToKey(chosen.Pos.X, chosen.Pos.Y, chosen.Pos.Z);
            if (!usedKeys.Add(key))
            {
                continue;
            }

            recentLeafTriggers[key] = nowMs;
            activeLeafEmitters[key] = new ActiveLeafEmitterState(nowMs + LeafEmitterLifetimeMs, ring);
            double sx = chosen.Pos.X + 0.5 + ((random.NextDouble() - 0.5) * 0.45);
            double sy = chosen.Pos.Y + 0.6 + (random.NextDouble() * 0.5);
            double sz = chosen.Pos.Z + 0.5 + ((random.NextDouble() - 0.5) * 0.45);

            return PlayEmitterAt(sx, sy, sz, playerPos, facingNormX, facingNormZ, windExposure, leafFactor, ring, nowMs);
        }

        return false;
    }

    private bool PlayEmitterAt(double sx, double sy, double sz, EntityPos playerPos, double facingNormX, double facingNormZ, float windExposure, float leafFactor, LeafRustleEmitterRing ring, long nowMs)
    {
        AssetLocation sound = ChooseRustleAlias(windExposure);
        float volume = GameMath.Clamp(0.018f + (windExposure * 0.028f) + (leafFactor * 0.012f), 0.018f, 0.08f);
        float pitch = GameMath.Clamp((float)(0.72 + (random.NextDouble() * 0.56) + ((windExposure - 0.5f) * 0.08f)), 0.68f, 1.32f);

        capi.World.PlaySoundAt(sound, sx, sy, sz, null, EnumSoundType.Ambient, pitch, 220f, volume);
        RegisterDebugEmitter(sx, sy, sz, ring, nowMs, volume);
        return true;
    }

    private void CleanupCooldowns(long nowMs)
    {
        lock (activeEmittersLock)
        {
            activeEmitters.RemoveAll(emitter => emitter.ExpiresMs <= nowMs);
        }

        if (recentLeafTriggers.Count == 0 && activeLeafEmitters.Count == 0)
        {
            return;
        }

        var expired = new List<long>();
        foreach (var pair in recentLeafTriggers)
        {
            if (nowMs - pair.Value > 4000)
            {
                expired.Add(pair.Key);
            }
        }

        foreach (long key in expired)
        {
            recentLeafTriggers.Remove(key);
        }

        expired.Clear();
        foreach (var pair in activeLeafEmitters)
        {
            if (pair.Value.ExpiresMs <= nowMs)
            {
                expired.Add(pair.Key);
            }
        }

        foreach (long key in expired)
        {
            activeLeafEmitters.Remove(key);
        }
    }

    private static long ToKey(int x, int y, int z)
    {
        unchecked
        {
            return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (uint)(z & 0x1FFFFF);
        }
    }

    private static void DecodeKey(long key, out int x, out int y, out int z)
    {
        x = SignExtend21((int)((key >> 42) & 0x1FFFFF));
        y = SignExtend21((int)((key >> 21) & 0x1FFFFF));
        z = SignExtend21((int)(key & 0x1FFFFF));
    }

    private static int SignExtend21(int value)
    {
        return (value & 0x100000) != 0 ? value | unchecked((int)0xFFE00000) : value;
    }

    private static LeafRustleEmitterRing GetCycleRing(long nowMs)
    {
        return ((nowMs / PoolCycleMs) % 3) switch
        {
            0 => LeafRustleEmitterRing.Near,
            1 => LeafRustleEmitterRing.Mid,
            _ => LeafRustleEmitterRing.Far
        };
    }

    private Dictionary<LeafRustleEmitterRing, int> GetActiveCountsByRing(long nowMs)
    {
        var counts = new Dictionary<LeafRustleEmitterRing, int>
        {
            [LeafRustleEmitterRing.Immediate] = 0,
            [LeafRustleEmitterRing.Near] = 0,
            [LeafRustleEmitterRing.Mid] = 0,
            [LeafRustleEmitterRing.Far] = 0
        };

        foreach (var pair in activeLeafEmitters)
        {
            if (pair.Value.ExpiresMs > nowMs)
            {
                counts[pair.Value.Ring]++;
            }
        }

        return counts;
    }

    private static float GetWindExposure()
    {
        float exposure = 1f - GameMath.Clamp(GlobalConstants.CurrentDistanceToRainfallClient / 5f, 0f, 1f);
        return GameMath.Clamp(exposure, 0f, 1f);
    }

    private AssetLocation ChooseRustleAlias(float windExposure)
    {
        float softProbability = GameMath.Clamp(0.75f - (windExposure * 0.5f), 0.25f, 0.75f);
        AssetLocation[] pool = random.NextDouble() < softProbability
            ? SoftRustleAliases
            : BrightRustleAliases;

        return pool[random.Next(pool.Length)];
    }

    private static void GetFacingVector(EntityPos playerPos, Vec3d motion, bool isMoving, double motionLength, out double facingNormX, out double facingNormZ)
    {
        if (isMoving)
        {
            facingNormX = motion.X / motionLength;
            facingNormZ = motion.Z / motionLength;
            return;
        }

        double yaw = playerPos.Yaw;
        facingNormX = -Math.Sin(yaw);
        facingNormZ = Math.Cos(yaw);
        double facingLength = Math.Max(0.0001, Math.Sqrt((facingNormX * facingNormX) + (facingNormZ * facingNormZ)));
        facingNormX /= facingLength;
        facingNormZ /= facingLength;
    }

    private void RegisterDebugEmitter(double x, double y, double z, LeafRustleEmitterRing ring, long nowMs, float volume)
    {
        if (!SurroundSoundLabConfigManager.Current.ShowLeafRustleDebugVisuals)
        {
            return;
        }

        lock (activeEmittersLock)
        {
            activeEmitters.Add(new DebugEmitter
            {
                Position = new Vec3d(x, y, z),
                ExpiresMs = nowMs + LeafEmitterLifetimeMs,
                Ring = ring,
                Volume = volume
            });
        }
    }

    private sealed class DebugEmitter
    {
        public Vec3d Position = new();
        public long ExpiresMs;
        public LeafRustleEmitterRing Ring;
        public float Volume;
    }

    private readonly record struct ActiveLeafEmitterState(long ExpiresMs, LeafRustleEmitterRing Ring);
}

internal enum LeafRustleEmitterRing
{
    Immediate,
    Near,
    Mid,
    Far
}

internal readonly record struct LeafRustleEmitterVisual(Vec3d Position, long ExpiresMs, LeafRustleEmitterRing Ring, float Volume);
