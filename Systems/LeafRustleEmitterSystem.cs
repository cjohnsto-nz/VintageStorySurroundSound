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
    private const double NearRingMaxDistance = 4.5;
    private const double MidRingMaxDistance = 14.0;
    private const double FarRingMaxDistance = 28.0;

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
    private long nextAllowedEmitMs;
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
        if (nowMs < nextAllowedEmitMs)
        {
            CleanupCooldowns(nowMs);
            return;
        }

        Vec3d motion = new(deltaX, deltaY, deltaZ);
        if (TryEmitFromNearbyLeafBlock(playerEntity.Pos, motion, horizontalMovement, windExposure, nowMs, out int nearCount, out int farCount))
        {
            float leafDensityFactor = GameMath.Clamp((nearCount * 0.06f) + (farCount * 0.015f), 0f, 1f);
            float movementFactor = GameMath.Clamp((float)(horizontalMovement / 0.18), 0f, 1f);
            bool enteringLeafZone = !hadLeafPresenceLastTick && (nearCount > 0 || farCount > 0);
            long cooldownMs = (long)(1200 - (windExposure * 780) - (leafDensityFactor * 420) - (movementFactor * 520));
            if (enteringLeafZone)
            {
                cooldownMs = Math.Min(cooldownMs, 90);
            }

            nextAllowedEmitMs = nowMs + Math.Clamp(cooldownMs, 80, 1800);
            hadLeafPresenceLastTick = true;
        }
        else
        {
            hadLeafPresenceLastTick = nearCount > 0 || farCount > 0;
        }

        CleanupCooldowns(nowMs);
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
        double motionNormX = isMoving ? (motion.X / motionLength) : 0.0;
        double motionNormZ = isMoving ? (motion.Z / motionLength) : 0.0;

        var nearCandidates = new List<(BlockPos Pos, double Score)>();
        var midCandidates = new List<(BlockPos Pos, double Score)>();
        var farCandidates = new List<(BlockPos Pos, double Score)>();
        int baseX = (int)Math.Floor(playerPos.X);
        int baseY = (int)Math.Floor(playerPos.Y);
        int baseZ = (int)Math.Floor(playerPos.Z);

        for (int dx = -20; dx <= 20; dx++)
        {
            for (int dy = -2; dy <= 3; dy++)
            {
                for (int dz = -20; dz <= 20; dz++)
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

                    long key = ToKey(x, y, z);
                    if (recentLeafTriggers.TryGetValue(key, out long lastTriggerMs) && nowMs - lastTriggerMs < 1800)
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

                    double facingScore = isMoving ? (((relX * motionNormX) + (relZ * motionNormZ) + 1.0) * 0.5) : 0.5;
                    double distanceScore;
                    if (horizontalDistance <= 2.4)
                    {
                        distanceScore = 0.95 - (horizontalDistance * 0.08);
                    }
                    else
                    {
                        distanceScore = 0.9 - ((horizontalDistance - 2.4) / 17.6);
                    }

                    double heightPenalty = Math.Abs(centerY - (playerPos.Y + 0.8));
                    double score = (facingScore * 0.25) + (distanceScore * 0.75) - (heightPenalty * 0.08);
                    if (score <= 0.1)
                    {
                        continue;
                    }

                    if (horizontalDistance <= NearRingMaxDistance)
                    {
                        nearCandidates.Add((pos, score));
                    }
                    else if (horizontalDistance <= MidRingMaxDistance)
                    {
                        midCandidates.Add((pos, score));
                    }
                    else
                    {
                        farCandidates.Add((pos, score));
                    }
                }
            }
        }

        nearCount = nearCandidates.Count;
        farCount = midCandidates.Count + farCandidates.Count;

        if (nearCount == 0 && midCandidates.Count == 0 && farCandidates.Count == 0)
        {
            return false;
        }

        nearCandidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        midCandidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        farCandidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        float leafFactor = GameMath.Clamp((nearCount * 0.035f) + (midCandidates.Count * 0.012f) + (farCandidates.Count * 0.005f), 0f, 1f);
        float movementFactorBurst = GameMath.Clamp((float)(horizontalMovement / 0.2), 0f, 1f);

        int burstCount = 2;
        burstCount += windExposure >= 0.2f ? 1 : 0;
        burstCount += windExposure >= 0.4f ? 1 : 0;
        burstCount += windExposure >= 0.6f ? 2 : 0;
        burstCount += windExposure >= 0.8f ? 2 : 0;
        burstCount += movementFactorBurst >= 0.25f ? 1 : 0;
        burstCount += movementFactorBurst >= 0.55f ? 2 : 0;
        burstCount += leafFactor >= 0.3f ? 1 : 0;
        burstCount += leafFactor >= 0.6f ? 2 : 0;
        burstCount = Math.Clamp(burstCount, 2, 13);

        int guaranteedNear = nearCandidates.Count > 0
            ? Math.Min(5, 1 + (movementFactorBurst >= 0.2f ? 1 : 0) + (movementFactorBurst >= 0.55f ? 1 : 0) + (windExposure >= 0.65f ? 1 : 0) + (windExposure >= 0.85f ? 1 : 0))
            : 0;
        int guaranteedMid = midCandidates.Count > 0
            ? Math.Min(5, 1 + (windExposure >= 0.45f ? 1 : 0) + (windExposure >= 0.75f ? 1 : 0) + (leafFactor >= 0.45f ? 1 : 0) + (movementFactorBurst >= 0.45f ? 1 : 0))
            : 0;

        int emitted = 0;
        var usedKeys = new HashSet<long>();

        for (int i = 0; i < burstCount; i++)
        {
            List<(BlockPos Pos, double Score)> primaryPool;
            int primaryPoolSize;

            if (i < guaranteedNear)
            {
                primaryPool = nearCandidates;
                primaryPoolSize = 8;
            }
            else if (i < guaranteedNear + guaranteedMid)
            {
                primaryPool = midCandidates;
                primaryPoolSize = 10;
            }
            else
            {
                double roll = random.NextDouble();
                if (nearCandidates.Count > 0 && roll < (0.25 + (movementFactorBurst * 0.25)))
                {
                    primaryPool = nearCandidates;
                    primaryPoolSize = 8;
                }
                else if (midCandidates.Count > 0 && roll < 0.8)
                {
                    primaryPool = midCandidates;
                    primaryPoolSize = 10;
                }
                else
                {
                    primaryPool = farCandidates.Count > 0 ? farCandidates : (midCandidates.Count > 0 ? midCandidates : nearCandidates);
                    primaryPoolSize = primaryPool == farCandidates ? 12 : (primaryPool == midCandidates ? 10 : 8);
                }
            }

            if (TryEmitFromPool(primaryPool, primaryPoolSize, nowMs, windExposure, leafFactor, usedKeys))
            {
                emitted++;
                continue;
            }

            if (TryEmitFromPool(midCandidates, 10, nowMs, windExposure, leafFactor, usedKeys))
            {
                emitted++;
            }
            else if (TryEmitFromPool(farCandidates, 12, nowMs, windExposure, leafFactor, usedKeys))
            {
                emitted++;
            }
            else if (TryEmitFromPool(nearCandidates, 8, nowMs, windExposure, leafFactor, usedKeys))
            {
                emitted++;
            }
        }

        return emitted > 0;
    }

    private bool TryEmitFromPool(List<(BlockPos Pos, double Score)> sourcePool, int maxPoolSize, long nowMs, float windExposure, float leafFactor, HashSet<long> usedKeys)
    {
        if (sourcePool.Count == 0)
        {
            return false;
        }

        int poolSize = Math.Min(maxPoolSize, sourcePool.Count);
        for (int attempt = 0; attempt < poolSize * 2; attempt++)
        {
            var chosen = sourcePool[random.Next(poolSize)];
            long key = ToKey(chosen.Pos.X, chosen.Pos.Y, chosen.Pos.Z);
            if (!usedKeys.Add(key))
            {
                continue;
            }

            recentLeafTriggers[key] = nowMs;

            AssetLocation sound = ChooseRustleAlias(windExposure);
            double sx = chosen.Pos.X + 0.5 + ((random.NextDouble() - 0.5) * 0.45);
            double sy = chosen.Pos.Y + 0.6 + (random.NextDouble() * 0.5);
            double sz = chosen.Pos.Z + 0.5 + ((random.NextDouble() - 0.5) * 0.45);
            float volume = GameMath.Clamp(0.005f + (windExposure * 0.018f) + (leafFactor * 0.007f), 0.005f, 0.036f);
            float pitch = GameMath.Clamp((float)(0.86 + (random.NextDouble() * 0.32) + ((windExposure - 0.5f) * 0.05f)), 0.8f, 1.2f);

            capi.World.PlaySoundAt(sound, sx, sy, sz, null, EnumSoundType.Ambient, pitch, 110f, volume);
            return true;
        }

        return false;
    }

    private void CleanupCooldowns(long nowMs)
    {
        if (recentLeafTriggers.Count == 0)
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
    }

    private static long ToKey(int x, int y, int z)
    {
        unchecked
        {
            return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (uint)(z & 0x1FFFFF);
        }
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
}
