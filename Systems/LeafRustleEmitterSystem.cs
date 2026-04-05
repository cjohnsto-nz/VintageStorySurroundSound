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
    private const long DiscoveryRefreshMs = 750;
    private const int PlaybackTickMs = 150;
    private const double ImmediateRingMaxDistance = 2.0;
    private const double NearRingMaxDistance = 5.0;
    private const double MidRingMaxDistance = 20.0;
    private const double FarRingMaxDistance = 52.0;
    private const double AheadPreloadDistance = 25.0;
    private const double AheadPreloadRadius = 16.0;
    private const double MovementRefreshDistance = 2.5;
    private const int DiscoveryHorizontalRadius = 20;
    private const int DiscoveryVerticalRadius = 10;
    private const double MaxScanDistanceSq = FarRingMaxDistance * FarRingMaxDistance;
    private const double MinScanDistanceSq = 0.8 * 0.8;

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

    private readonly List<CandidateLeafBlock> immediateCandidates = new();
    private readonly List<CandidateLeafBlock> nearCandidates = new();
    private readonly List<CandidateLeafBlock> midCandidates = new();
    private readonly List<CandidateLeafBlock> farCandidates = new();

    private long tickListenerId;
    private Vec3d lastPlayerPos = new();
    private bool hasLastPlayerPos;
    private long lastDiscoveryRefreshMs;
    private Vec3d lastDiscoveryCenter = new();
    private bool hasDiscoveryCenter;
    private float cachedWindExposure;
    private float cachedLeafFactor;
    private int cachedNearCount;
    private int cachedFarCount;
    private FacingContext lastFacing;

    public LeafRustleEmitterSystem(ICoreClientAPI capi)
    {
        this.capi = capi;
        tickListenerId = capi.Event.RegisterGameTickListener(OnGameTick, PlaybackTickMs);
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

    public float GetCurrentWindExposure()
    {
        return cachedWindExposure;
    }

    public float GetCurrentSoftSampleProbability()
    {
        return GameMath.Clamp(0.75f - (cachedWindExposure * 0.5f), 0.25f, 0.75f);
    }

    public float GetCurrentBrightSampleProbability()
    {
        return 1f - GetCurrentSoftSampleProbability();
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
            lastPlayerPos.Set(currentPos);
            hasLastPlayerPos = true;
            return;
        }

        double deltaX = currentPos.X - lastPlayerPos.X;
        double deltaY = currentPos.Y - lastPlayerPos.Y;
        double deltaZ = currentPos.Z - lastPlayerPos.Z;
        double horizontalMovement = Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
        lastPlayerPos.Set(currentPos);

        float windExposure = GetWindExposure(currentPos);
        long nowMs = capi.ElapsedMilliseconds;

        if (windExposure < 0.08f)
        {
            ClearCandidateCache();
            CleanupCooldowns(nowMs);
            return;
        }

        FacingContext facing = CreateFacingContext(playerEntity.Pos, deltaX, deltaY, deltaZ, horizontalMovement);
        lastFacing = facing;

        CullOutOfRangeEmitters(playerEntity.Pos, facing, nowMs);
        CleanupCooldowns(nowMs);

        if (ShouldRefreshDiscovery(currentPos, windExposure, nowMs))
        {
            RefreshCandidateCache(playerEntity.Pos, facing, windExposure, nowMs);
        }

        if (activeLeafEmitters.Count >= MaxActiveLeafEmitters)
        {
            return;
        }

        float roomLoss = GetRoomVolumePitchLoss(playerEntity.Pos.AsBlockPos);
        TryEmitFromCache(playerEntity.Pos, facing, windExposure, roomLoss, nowMs);
    }

    private bool ShouldRefreshDiscovery(Vec3d playerPos, float windExposure, long nowMs)
    {
        if (!hasDiscoveryCenter)
        {
            return true;
        }

        if (nowMs - lastDiscoveryRefreshMs >= DiscoveryRefreshMs)
        {
            return true;
        }

        double dx = playerPos.X - lastDiscoveryCenter.X;
        double dy = playerPos.Y - lastDiscoveryCenter.Y;
        double dz = playerPos.Z - lastDiscoveryCenter.Z;
        if ((dx * dx) + (dy * dy) + (dz * dz) >= MovementRefreshDistance * MovementRefreshDistance)
        {
            return true;
        }

        return Math.Abs(windExposure - cachedWindExposure) >= 0.18f;
    }

    private void RefreshCandidateCache(EntityPos playerPos, FacingContext facing, float windExposure, long nowMs)
    {
        immediateCandidates.Clear();
        nearCandidates.Clear();
        midCandidates.Clear();
        farCandidates.Clear();

        IBlockAccessor blockAccessor = capi.World.BlockAccessor;
        if (blockAccessor == null)
        {
            cachedNearCount = 0;
            cachedFarCount = 0;
            cachedLeafFactor = 0f;
            return;
        }

        Vec3d lookAheadCenter = new(
            playerPos.X + (facing.FacingNormX * (10.0 + (facing.MovementFactor * 18.0))),
            playerPos.Y,
            playerPos.Z + (facing.FacingNormZ * (10.0 + (facing.MovementFactor * 18.0)))
        );
        Vec3d aheadPreloadCenter = new(
            playerPos.X + (facing.FacingNormX * AheadPreloadDistance),
            playerPos.Y,
            playerPos.Z + (facing.FacingNormZ * AheadPreloadDistance)
        );

        int baseX = (int)Math.Floor(facing.IsMoving ? ((playerPos.X * 0.45) + (lookAheadCenter.X * 0.55)) : playerPos.X);
        int baseY = (int)Math.Floor(playerPos.Y);
        int baseZ = (int)Math.Floor(facing.IsMoving ? ((playerPos.Z * 0.45) + (lookAheadCenter.Z * 0.55)) : playerPos.Z);

        for (int dx = -DiscoveryHorizontalRadius; dx <= DiscoveryHorizontalRadius; dx++)
        {
            for (int dy = -DiscoveryVerticalRadius; dy <= DiscoveryVerticalRadius; dy++)
            {
                for (int dz = -DiscoveryHorizontalRadius; dz <= DiscoveryHorizontalRadius; dz++)
                {
                    int x = baseX + dx;
                    int y = baseY + dy;
                    int z = baseZ + dz;
                    var pos = new BlockPos(x, y, z);
                    Block block = blockAccessor.GetBlock(pos);
                    if (!IsRustleEligibleBlock(block))
                    {
                        continue;
                    }

                    double centerX = x + 0.5;
                    double centerY = y + 0.5;
                    double centerZ = z + 0.5;
                    double relX = centerX - playerPos.X;
                    double relZ = centerZ - playerPos.Z;
                    double horizontalDistanceSq = (relX * relX) + (relZ * relZ);
                    if (horizontalDistanceSq < MinScanDistanceSq || horizontalDistanceSq > MaxScanDistanceSq)
                    {
                        continue;
                    }

                    long key = ToKey(x, y, z);
                    if (activeLeafEmitters.TryGetValue(key, out ActiveLeafEmitterState activeState) && activeState.ExpiresMs > nowMs)
                    {
                        continue;
                    }

                    long perLeafCooldownMs = GetPerLeafCooldownMs(horizontalDistanceSq);
                    if (recentLeafTriggers.TryGetValue(key, out long lastTriggerMs) && nowMs - lastTriggerMs < perLeafCooldownMs)
                    {
                        continue;
                    }

                    double aheadRelX = centerX - lookAheadCenter.X;
                    double aheadRelZ = centerZ - lookAheadCenter.Z;
                    double aheadDistanceSq = (aheadRelX * aheadRelX) + (aheadRelZ * aheadRelZ);
                    if (facing.IsMoving && aheadDistanceSq > 42.0 * 42.0)
                    {
                        continue;
                    }

                    double preloadRelX = centerX - aheadPreloadCenter.X;
                    double preloadRelZ = centerZ - aheadPreloadCenter.Z;
                    double preloadDistanceSq = (preloadRelX * preloadRelX) + (preloadRelZ * preloadRelZ);

                    double horizontalDistance = Math.Sqrt(horizontalDistanceSq);
                    double facingScore = facing.IsMoving ? (((relX * facing.FacingNormX) + (relZ * facing.FacingNormZ) + 1.0) * 0.5) : 0.5;
                    double lookAheadScore = facing.IsMoving ? Math.Max(0.0, 1.0 - (Math.Sqrt(aheadDistanceSq) / 42.0)) : 0.5;
                    double distanceScore = horizontalDistance <= 2.4
                        ? 0.95 - (horizontalDistance * 0.08)
                        : 0.9 - ((horizontalDistance - 2.4) / 49.6);

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

                    bool isReedLike = IsReedLikeBlock(block);
                    var candidate = new CandidateLeafBlock(pos, score, isReedLike);

                    if (horizontalDistance <= ImmediateRingMaxDistance)
                    {
                        immediateCandidates.Add(candidate);
                    }
                    else if (horizontalDistance <= NearRingMaxDistance)
                    {
                        nearCandidates.Add(candidate);
                    }
                    else if (horizontalDistance <= MidRingMaxDistance)
                    {
                        midCandidates.Add(candidate);
                    }
                    else if (!facing.IsMoving || preloadDistanceSq <= AheadPreloadRadius * AheadPreloadRadius)
                    {
                        double preloadScore = facing.IsMoving ? Math.Max(0.0, 1.0 - (Math.Sqrt(preloadDistanceSq) / AheadPreloadRadius)) : 0.5;
                        farCandidates.Add(candidate with { Score = score + (preloadScore * 0.25) });
                    }
                }
            }
        }

        cachedNearCount = immediateCandidates.Count + nearCandidates.Count;
        cachedFarCount = midCandidates.Count + farCandidates.Count;
        cachedLeafFactor = GameMath.Clamp((immediateCandidates.Count * 0.04f) + (nearCandidates.Count * 0.02f) + (midCandidates.Count * 0.01f) + (farCandidates.Count * 0.004f), 0f, 1f);
        cachedWindExposure = windExposure;
        lastDiscoveryRefreshMs = nowMs;
        lastDiscoveryCenter.Set(playerPos.X, playerPos.Y, playerPos.Z);
        hasDiscoveryCenter = true;
    }

    private bool TryEmitFromCache(EntityPos playerPos, FacingContext facing, float windExposure, float roomLoss, long nowMs)
    {
        if (immediateCandidates.Count == 0 && nearCandidates.Count == 0 && midCandidates.Count == 0 && farCandidates.Count == 0)
        {
            return false;
        }

        int emitted = 0;
        var usedKeys = new HashSet<long>();
        int slotsRemaining = Math.Max(0, MaxActiveLeafEmitters - activeLeafEmitters.Count);
        if (slotsRemaining <= 0)
        {
            return false;
        }

        var activeCounts = GetActiveCountsByRing(nowMs);
        int immediateCapacity = Math.Max(0, MaxActiveEmittersPerPool - activeCounts[LeafRustleEmitterRing.Immediate]);
        int immediateTarget = Math.Min(immediateCandidates.Count, Math.Min(Math.Min(slotsRemaining, immediateCapacity), 6));
        emitted += EmitRandomFromPool(immediateCandidates, immediateTarget, nowMs, windExposure, roomLoss, cachedLeafFactor, usedKeys, LeafRustleEmitterRing.Immediate);
        slotsRemaining = Math.Max(0, MaxActiveLeafEmitters - (activeLeafEmitters.Count + emitted));

        if (slotsRemaining > 0)
        {
            int nearCapacity = Math.Max(0, MaxActiveEmittersPerPool - activeCounts[LeafRustleEmitterRing.Near]);
            int nearTarget = Math.Min(Math.Min(slotsRemaining, nearCapacity), Math.Clamp(1 + (windExposure >= 0.7f ? 1 : 0), 0, 2));
            emitted += EmitRandomFromPool(nearCandidates, nearTarget, nowMs, windExposure, roomLoss, cachedLeafFactor, usedKeys, LeafRustleEmitterRing.Near);
            slotsRemaining = Math.Max(0, MaxActiveLeafEmitters - (activeLeafEmitters.Count + emitted));
        }

        if (slotsRemaining > 0)
        {
            int midCapacity = Math.Max(0, MaxActiveEmittersPerPool - activeCounts[LeafRustleEmitterRing.Mid]);
            int midTarget = Math.Min(Math.Min(slotsRemaining, midCapacity), Math.Clamp(1 + (windExposure >= 0.85f ? 1 : 0), 0, 2));
            emitted += EmitRandomFromPool(midCandidates, midTarget, nowMs, windExposure, roomLoss, cachedLeafFactor, usedKeys, LeafRustleEmitterRing.Mid);
            slotsRemaining = Math.Max(0, MaxActiveLeafEmitters - (activeLeafEmitters.Count + emitted));
        }

        if (slotsRemaining > 0)
        {
            int farCapacity = Math.Max(0, MaxActiveEmittersPerPool - activeCounts[LeafRustleEmitterRing.Far]);
            int farTarget = Math.Min(Math.Min(slotsRemaining, farCapacity), Math.Clamp(1 + (windExposure >= 0.9f ? 1 : 0), 0, 2));
            emitted += EmitRandomFromPool(farCandidates, farTarget, nowMs, windExposure, roomLoss, cachedLeafFactor, usedKeys, LeafRustleEmitterRing.Far);
        }

        return emitted > 0;
    }

    private int EmitRandomFromPool(List<CandidateLeafBlock> sourcePool, int targetCount, long nowMs, float windExposure, float roomLoss, float leafFactor, HashSet<long> usedKeys, LeafRustleEmitterRing ring)
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
            CandidateLeafBlock choice = sourcePool[random.Next(sourcePool.Count)];
            if (TryEmitSpecific(choice, nowMs, windExposure, roomLoss, leafFactor, usedKeys, ring))
            {
                emitted++;
            }
        }

        return emitted;
    }

    private bool TryEmitSpecific(CandidateLeafBlock candidate, long nowMs, float windExposure, float roomLoss, float leafFactor, HashSet<long> usedKeys, LeafRustleEmitterRing ring)
    {
        long key = ToKey(candidate.Pos.X, candidate.Pos.Y, candidate.Pos.Z);
        if (!usedKeys.Add(key))
        {
            return false;
        }

        double sx = candidate.Pos.X + 0.5 + ((random.NextDouble() - 0.5) * 0.4);
        double sy = candidate.Pos.Y + 0.55 + (random.NextDouble() * 0.45);
        double sz = candidate.Pos.Z + 0.5 + ((random.NextDouble() - 0.5) * 0.4);
        recentLeafTriggers[key] = nowMs;
        activeLeafEmitters[key] = new ActiveLeafEmitterState(nowMs + LeafEmitterLifetimeMs, ring);

        return PlayEmitterAt(sx, sy, sz, candidate.IsReedLike, windExposure, roomLoss, leafFactor, ring, nowMs);
    }

    private bool PlayEmitterAt(double sx, double sy, double sz, bool isReedLike, float windExposure, float roomLoss, float leafFactor, LeafRustleEmitterRing ring, long nowMs)
    {
        AssetLocation sound = ChooseRustleAlias(windExposure);
        float volume = GameMath.Clamp(0.018f + (windExposure * 0.028f) + (leafFactor * 0.012f), 0.018f, 0.08f);
        volume *= (1f - roomLoss);
        if (volume <= 0.003f)
        {
            return false;
        }

        float pitch = isReedLike
            ? GameMath.Clamp((float)(0.5 + (random.NextDouble() * 0.34) + ((windExposure - 0.5f) * 0.05f)), 0.45f, 0.9f)
            : GameMath.Clamp((float)(0.72 + (random.NextDouble() * 0.56) + ((windExposure - 0.5f) * 0.08f)), 0.68f, 1.32f);
        pitch = GameMath.Max(0f, pitch - (roomLoss / 4f));

        capi.World.PlaySoundAt(sound, sx, sy, sz, null, EnumSoundType.Ambient, pitch, 150f, volume);
        RegisterDebugEmitter(sx, sy, sz, ring, nowMs, volume);
        return true;
    }

    private void CullOutOfRangeEmitters(EntityPos playerPos, FacingContext facing, long nowMs)
    {
        if (activeLeafEmitters.Count == 0)
        {
            return;
        }

        Vec3d lookAheadCenter = new(
            playerPos.X + (facing.FacingNormX * (12.0 + (facing.MovementFactor * 24.0))),
            playerPos.Y,
            playerPos.Z + (facing.FacingNormZ * (12.0 + (facing.MovementFactor * 24.0)))
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
            double playerDistanceSq = (relX * relX) + (relY * relY) + (relZ * relZ);

            double aheadX = cx - lookAheadCenter.X;
            double aheadY = cy - lookAheadCenter.Y;
            double aheadZ = cz - lookAheadCenter.Z;
            double lookAheadDistanceSq = (aheadX * aheadX) + (aheadY * aheadY) + (aheadZ * aheadZ);

            if (playerDistanceSq > 26.0 * 26.0 && (!facing.IsMoving || lookAheadDistanceSq > 34.0 * 34.0))
            {
                expired.Add(pair.Key);
            }
        }

        foreach (long key in expired)
        {
            activeLeafEmitters.Remove(key);
        }
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

    private void ClearCandidateCache()
    {
        immediateCandidates.Clear();
        nearCandidates.Clear();
        midCandidates.Clear();
        farCandidates.Clear();
        cachedNearCount = 0;
        cachedFarCount = 0;
        cachedLeafFactor = 0f;
        hasDiscoveryCenter = false;
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

    private static long GetPerLeafCooldownMs(double horizontalDistanceSq)
    {
        if (horizontalDistanceSq <= ImmediateRingMaxDistance * ImmediateRingMaxDistance)
        {
            return 180;
        }

        if (horizontalDistanceSq <= NearRingMaxDistance * NearRingMaxDistance)
        {
            return 550;
        }

        if (horizontalDistanceSq <= MidRingMaxDistance * MidRingMaxDistance)
        {
            return 900;
        }

        return 1400;
    }

    private static FacingContext CreateFacingContext(EntityPos playerPos, double deltaX, double deltaY, double deltaZ, double horizontalMovement)
    {
        bool isMoving = horizontalMovement > 0.02;
        double motionLength = Math.Max(0.0001, Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ)));
        GetFacingVector(playerPos, deltaX, deltaZ, isMoving, motionLength, out double facingNormX, out double facingNormZ);
        float movementFactor = GameMath.Clamp((float)(horizontalMovement / 0.2), 0f, 1f);
        return new FacingContext(isMoving, movementFactor, facingNormX, facingNormZ);
    }

    private float GetWindExposure(Vec3d pos)
    {
        Vec3d wind = capi.World?.BlockAccessor?.GetWindSpeedAt(pos);
        if (wind == null)
        {
            return 0f;
        }

        double horizontalWind = Math.Sqrt((wind.X * wind.X) + (wind.Z * wind.Z));
        return GameMath.Clamp((float)(horizontalWind / 2.5), 0f, 1f);
    }

    private float GetRoomVolumePitchLoss(BlockPos pos)
    {
        IBlockAccessor blockAccessor = capi.World?.BlockAccessor;
        if (blockAccessor == null)
        {
            return 0f;
        }

        int distanceToRainFall = blockAccessor.GetDistanceToRainFall(pos, 12, 4);
        return GameMath.Clamp((float)Math.Pow(Math.Max(0f, (distanceToRainFall - 2f) / 10f), 2.0), 0f, 1f);
    }

    private AssetLocation ChooseRustleAlias(float windExposure)
    {
        float softProbability = GameMath.Clamp(0.75f - (windExposure * 0.5f), 0.25f, 0.75f);
        AssetLocation[] pool = random.NextDouble() < softProbability ? SoftRustleAliases : BrightRustleAliases;
        return pool[random.Next(pool.Length)];
    }

    private static bool IsRustleEligibleBlock(Block block)
    {
        if (block == null)
        {
            return false;
        }

        return block.BlockMaterial == EnumBlockMaterial.Leaves || IsReedLikeBlock(block);
    }

    private static bool IsReedLikeBlock(Block block)
    {
        string path = block?.Code?.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.Contains("reed", StringComparison.OrdinalIgnoreCase)
            || path.Contains("rush", StringComparison.OrdinalIgnoreCase)
            || path.Contains("cattail", StringComparison.OrdinalIgnoreCase);
    }

    private static void GetFacingVector(EntityPos playerPos, double deltaX, double deltaZ, bool isMoving, double motionLength, out double facingNormX, out double facingNormZ)
    {
        if (isMoving)
        {
            facingNormX = deltaX / motionLength;
            facingNormZ = deltaZ / motionLength;
            return;
        }

        double yaw = playerPos.Yaw;
        facingNormX = -Math.Sin(yaw);
        facingNormZ = Math.Cos(yaw);
        double facingLength = Math.Max(0.0001, Math.Sqrt((facingNormX * facingNormX) + (facingNormZ * facingNormZ)));
        facingNormX /= facingLength;
        facingNormZ /= facingLength;
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

    private readonly record struct CandidateLeafBlock(BlockPos Pos, double Score, bool IsReedLike);
    private readonly record struct ActiveLeafEmitterState(long ExpiresMs, LeafRustleEmitterRing Ring);
    private readonly record struct FacingContext(bool IsMoving, float MovementFactor, double FacingNormX, double FacingNormZ);

    private sealed class DebugEmitter
    {
        public Vec3d Position = new();
        public long ExpiresMs;
        public LeafRustleEmitterRing Ring;
        public float Volume;
    }
}

internal enum LeafRustleEmitterRing
{
    Immediate,
    Near,
    Mid,
    Far
}

internal readonly record struct LeafRustleEmitterVisual(Vec3d Position, long ExpiresMs, LeafRustleEmitterRing Ring, float Volume);
