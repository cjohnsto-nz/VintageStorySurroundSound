using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SurroundSoundLab;

internal sealed class RainEmitterSystem : IDisposable
{
    private const int PlaybackTickMs = 250;
    private const long EmitterLifetimeMs = 2400;
    private const long CandidateRefreshMs = 1000;
    private const long SlotSpawnIntervalMs = 1800;
    private const double CandidateMoveRefreshDistance = 2.0;
    private const int HorizontalRadius = 18;
    private const int VerticalRadius = 6;
    private const float PlaybackRange = 70f;
    private const float Volume = 0.32f;
    private const int SlotsPerQuadrant = 4;
    private const double MinEmitterDistanceSq = 7.5 * 7.5;
    private const double MaxActiveDistanceSq = 26.0 * 26.0;
    private const int MaxSpawnsPerTick = 2;

    private static readonly AssetLocation[] RainAliases =
    {
        CustomSoundRegistry.RainOneAlias,
        CustomSoundRegistry.RainTwoAlias,
        CustomSoundRegistry.RainThreeAlias,
        CustomSoundRegistry.RainFourAlias
    };

    private readonly ICoreClientAPI capi;
    private readonly Random random = new();
    private readonly Dictionary<RainEmitterQuadrant, List<RainCandidate>> candidatesByQuadrant = new()
    {
        [RainEmitterQuadrant.FrontLeft] = new List<RainCandidate>(),
        [RainEmitterQuadrant.FrontRight] = new List<RainCandidate>(),
        [RainEmitterQuadrant.BackLeft] = new List<RainCandidate>(),
        [RainEmitterQuadrant.BackRight] = new List<RainCandidate>()
    };
    private readonly Dictionary<int, ActiveRainEmitterState> activeEmitters = new();
    private readonly Dictionary<int, long> slotNextSpawnMs = new();
    private readonly List<RainEmitterVisual> activeVisuals = new();
    private readonly object visualsLock = new();

    private long tickListenerId;
    private long lastCandidateRefreshMs;
    private Vec3d lastRefreshPos = new();
    private bool hasLastRefreshPos;

    public RainEmitterSystem(ICoreClientAPI capi)
    {
        this.capi = capi;
        long nowMs = capi.ElapsedMilliseconds;
        int totalSlots = SlotsPerQuadrant * 4;
        for (int slotIndex = 0; slotIndex < totalSlots; slotIndex++)
        {
            // Seed each slot into its own phase so startup ramps in instead of spawning all at once.
            slotNextSpawnMs[slotIndex] = nowMs + (slotIndex * 220L) + random.Next(0, 180);
        }

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

    public List<RainEmitterVisual> GetActiveEmittersSnapshot(long nowMs)
    {
        lock (visualsLock)
        {
            activeVisuals.RemoveAll(v => v.ExpiresMs <= nowMs);
            return new List<RainEmitterVisual>(activeVisuals);
        }
    }

    public int GetActiveRainEmitterCount(long nowMs)
    {
        Cleanup(nowMs);
        return activeEmitters.Count;
    }

    public (int FrontLeft, int FrontRight, int BackLeft, int BackRight) GetActiveRainEmitterQuadrantCounts(long nowMs)
    {
        Cleanup(nowMs);
        int frontLeft = 0;
        int frontRight = 0;
        int backLeft = 0;
        int backRight = 0;

        foreach (var pair in activeEmitters)
        {
            if (pair.Value.ExpiresMs <= nowMs)
            {
                continue;
            }

            switch (pair.Value.Quadrant)
            {
                case RainEmitterQuadrant.FrontLeft:
                    frontLeft++;
                    break;
                case RainEmitterQuadrant.FrontRight:
                    frontRight++;
                    break;
                case RainEmitterQuadrant.BackLeft:
                    backLeft++;
                    break;
                case RainEmitterQuadrant.BackRight:
                    backRight++;
                    break;
            }
        }

        return (frontLeft, frontRight, backLeft, backRight);
    }

    private void OnGameTick(float deltaTime)
    {
        if (!SurroundSoundLabConfigManager.Current.EnableExperimentalRainEmitters)
        {
            return;
        }

        Entity playerEntity = capi.World?.Player?.Entity;
        if (playerEntity?.Pos == null)
        {
            return;
        }

        long nowMs = capi.ElapsedMilliseconds;
        Cleanup(nowMs);
        CullOutOfRangeEmitters(playerEntity.Pos, nowMs);

        Vec3d playerPos = playerEntity.Pos.XYZ;
        float roomLoss = GetRoomVolumePitchLoss(playerEntity.Pos.AsBlockPos);
        if (ShouldRefreshCandidates(playerPos, nowMs))
        {
            RefreshCandidates(playerEntity.Pos, nowMs);
        }

        int spawnedThisTick = 0;
        for (int slotIndex = 0; slotIndex < SlotsPerQuadrant * 4; slotIndex++)
        {
            if (spawnedThisTick >= MaxSpawnsPerTick)
            {
                break;
            }

            if (slotNextSpawnMs.TryGetValue(slotIndex, out long nextSpawnMs) && nowMs < nextSpawnMs)
            {
                continue;
            }

            if (activeEmitters.TryGetValue(slotIndex, out ActiveRainEmitterState active) && active.ExpiresMs > nowMs)
            {
                continue;
            }

            RainEmitterQuadrant quadrant = GetQuadrantForSlot(slotIndex);
            List<RainCandidate> candidates = candidatesByQuadrant[quadrant];
            if (candidates.Count == 0)
            {
                continue;
            }

            RainCandidate chosen = PickCandidateForSlot(candidates, quadrant, slotIndex);
            PlayEmitter(chosen, quadrant, slotIndex, nowMs, roomLoss);
            slotNextSpawnMs[slotIndex] = nowMs + SlotSpawnIntervalMs + random.Next(200, 900);
            spawnedThisTick++;
        }
    }

    private bool ShouldRefreshCandidates(Vec3d playerPos, long nowMs)
    {
        if (!hasLastRefreshPos)
        {
            return true;
        }

        if (nowMs - lastCandidateRefreshMs >= CandidateRefreshMs)
        {
            return true;
        }

        double dx = playerPos.X - lastRefreshPos.X;
        double dy = playerPos.Y - lastRefreshPos.Y;
        double dz = playerPos.Z - lastRefreshPos.Z;
        return (dx * dx) + (dy * dy) + (dz * dz) >= CandidateMoveRefreshDistance * CandidateMoveRefreshDistance;
    }

    private void RefreshCandidates(EntityPos playerPos, long nowMs)
    {
        foreach (List<RainCandidate> list in candidatesByQuadrant.Values)
        {
            list.Clear();
        }

        IBlockAccessor blockAccessor = capi.World.BlockAccessor;
        if (blockAccessor == null)
        {
            return;
        }

        GetFacingAxes(playerPos, out double forwardX, out double forwardZ, out double rightX, out double rightZ);

        int baseX = (int)Math.Floor(playerPos.X);
        int baseY = (int)Math.Floor(playerPos.Y);
        int baseZ = (int)Math.Floor(playerPos.Z);

        for (int dx = -HorizontalRadius; dx <= HorizontalRadius; dx++)
        {
            for (int dy = -VerticalRadius; dy <= VerticalRadius; dy++)
            {
                for (int dz = -HorizontalRadius; dz <= HorizontalRadius; dz++)
                {
                    int x = baseX + dx;
                    int y = baseY + dy;
                    int z = baseZ + dz;
                    BlockPos pos = new(x, y, z);
                    Block block = blockAccessor.GetBlock(pos);
                    if (block == null || block.Id == 0)
                    {
                        continue;
                    }

                    if (blockAccessor.GetDistanceToRainFall(pos, 2, 2) > 0)
                    {
                        continue;
                    }

                    double centerX = x + 0.5;
                    double centerY = y + 1.0;
                    double centerZ = z + 0.5;
                    double relX = centerX - playerPos.X;
                    double relZ = centerZ - playerPos.Z;
                    double distSq = (relX * relX) + (relZ * relZ);
                    if (distSq < MinEmitterDistanceSq || distSq > HorizontalRadius * HorizontalRadius)
                    {
                        continue;
                    }

                    double forwardDot = (relX * forwardX) + (relZ * forwardZ);
                    double rightDot = (relX * rightX) + (relZ * rightZ);
                    RainEmitterQuadrant quadrant = GetQuadrant(forwardDot, rightDot);
                    double distance = Math.Sqrt(distSq);
                    double score = 0.55 + (distance / HorizontalRadius);
                    candidatesByQuadrant[quadrant].Add(new RainCandidate(centerX, centerY, centerZ, score));
                }
            }
        }

        foreach (var pair in candidatesByQuadrant)
        {
            pair.Value.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (pair.Value.Count > 32)
            {
                pair.Value.RemoveRange(32, pair.Value.Count - 32);
            }
        }

        lastCandidateRefreshMs = nowMs;
        lastRefreshPos.Set(playerPos.X, playerPos.Y, playerPos.Z);
        hasLastRefreshPos = true;
    }

    private RainCandidate PickCandidateForSlot(List<RainCandidate> candidates, RainEmitterQuadrant quadrant, int slotIndex)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        int localSlot = slotIndex % SlotsPerQuadrant;
        int bandSize = Math.Max(1, candidates.Count / SlotsPerQuadrant);
        int start = Math.Min(candidates.Count - 1, localSlot * bandSize);
        int end = Math.Min(candidates.Count, start + bandSize);
        int index = random.Next(start, end);
        return candidates[index];
    }

    private void PlayEmitter(RainCandidate candidate, RainEmitterQuadrant quadrant, int slotIndex, long nowMs, float roomLoss)
    {
        AssetLocation alias = RainAliases[random.Next(RainAliases.Length)];
        float adjustedVolume = GameMath.Clamp(Volume * (1f - roomLoss), 0f, Volume);
        if (adjustedVolume <= 0.01f)
        {
            activeEmitters.Remove(slotIndex);
            return;
        }

        float pitch = GameMath.Clamp((float)(0.9 + (random.NextDouble() * 0.16)), 0.88f, 1.08f);
        pitch = GameMath.Max(0f, pitch - (roomLoss / 4f));
        capi.World.PlaySoundAt(alias, candidate.X, candidate.Y, candidate.Z, null, EnumSoundType.Ambient, pitch, PlaybackRange, adjustedVolume);
        activeEmitters[slotIndex] = new ActiveRainEmitterState(nowMs + EmitterLifetimeMs, candidate.X, candidate.Y, candidate.Z, quadrant);

        if (SurroundSoundLabConfigManager.Current.EnableDebugTools && SurroundSoundLabConfigManager.Current.ShowRainEmitterDebugVisuals)
        {
            lock (visualsLock)
            {
                activeVisuals.Add(new RainEmitterVisual(new Vec3d(candidate.X, candidate.Y, candidate.Z), nowMs + EmitterLifetimeMs, quadrant));
            }
        }
    }

    private void Cleanup(long nowMs)
    {
        List<int> expired = new();
        foreach (var pair in activeEmitters)
        {
            if (pair.Value.ExpiresMs <= nowMs)
            {
                expired.Add(pair.Key);
            }
        }

        foreach (int slot in expired)
        {
            activeEmitters.Remove(slot);
        }

        lock (visualsLock)
        {
            activeVisuals.RemoveAll(v => v.ExpiresMs <= nowMs);
        }
    }

    private void CullOutOfRangeEmitters(EntityPos playerPos, long nowMs)
    {
        if (activeEmitters.Count == 0)
        {
            return;
        }

        List<int> expired = new();
        foreach (var pair in activeEmitters)
        {
            if (pair.Value.ExpiresMs <= nowMs)
            {
                expired.Add(pair.Key);
                continue;
            }

            double dx = pair.Value.X - playerPos.X;
            double dy = pair.Value.Y - playerPos.Y;
            double dz = pair.Value.Z - playerPos.Z;
            double distSq = (dx * dx) + (dy * dy) + (dz * dz);
            if (distSq > MaxActiveDistanceSq)
            {
                expired.Add(pair.Key);
            }
        }

        foreach (int slotIndex in expired)
        {
            activeEmitters.Remove(slotIndex);
            slotNextSpawnMs[slotIndex] = nowMs + random.Next(120, 420);
        }
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

    private static RainEmitterQuadrant GetQuadrantForSlot(int slotIndex)
    {
        return (slotIndex / SlotsPerQuadrant) switch
        {
            0 => RainEmitterQuadrant.FrontLeft,
            1 => RainEmitterQuadrant.FrontRight,
            2 => RainEmitterQuadrant.BackLeft,
            _ => RainEmitterQuadrant.BackRight
        };
    }

    private static void GetFacingAxes(EntityPos playerPos, out double forwardX, out double forwardZ, out double rightX, out double rightZ)
    {
        double yaw = playerPos.Yaw;
        forwardX = -Math.Sin(yaw);
        forwardZ = Math.Cos(yaw);
        rightX = forwardZ;
        rightZ = -forwardX;
    }

    private static RainEmitterQuadrant GetQuadrant(double forwardDot, double rightDot)
    {
        if (forwardDot >= 0)
        {
            return rightDot >= 0 ? RainEmitterQuadrant.FrontRight : RainEmitterQuadrant.FrontLeft;
        }

        return rightDot >= 0 ? RainEmitterQuadrant.BackRight : RainEmitterQuadrant.BackLeft;
    }

    private readonly record struct RainCandidate(double X, double Y, double Z, double Score);
    private readonly record struct ActiveRainEmitterState(long ExpiresMs, double X, double Y, double Z, RainEmitterQuadrant Quadrant);
}

internal enum RainEmitterQuadrant
{
    FrontLeft,
    FrontRight,
    BackLeft,
    BackRight
}

internal readonly record struct RainEmitterVisual(Vec3d Position, long ExpiresMs, RainEmitterQuadrant Quadrant);
