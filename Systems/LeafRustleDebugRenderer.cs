using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace SurroundSoundLab;

internal sealed class LeafRustleDebugRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly LeafRustleEmitterSystem emitterSystem;

    public double RenderOrder => 0.51;

    public int RenderRange => 999;

    public LeafRustleDebugRenderer(ICoreClientAPI capi, LeafRustleEmitterSystem emitterSystem)
    {
        this.capi = capi;
        this.emitterSystem = emitterSystem;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!SurroundSoundLabConfigManager.Current.ShowLeafRustleDebugVisuals || stage != EnumRenderStage.Opaque)
        {
            return;
        }

        var player = capi.World?.Player?.Entity;
        if (player?.Pos == null)
        {
            return;
        }

        List<LeafRustleEmitterVisual> emitters = emitterSystem.GetActiveEmittersSnapshot(capi.ElapsedMilliseconds);
        var origin = new BlockPos(
            (int)Math.Floor(player.Pos.X),
            (int)Math.Floor(player.Pos.Y),
            (int)Math.Floor(player.Pos.Z),
            player.Pos.Dimension
        );

        if (emitterSystem.TryGetDebugRegions(out LeafRustleDebugRegions regions))
        {
            RenderRegions(origin, regions);
        }

        foreach (LeafRustleEmitterVisual emitter in emitters)
        {
            RenderEmitter(origin, emitter);
        }

    }

    private void RenderEmitter(BlockPos origin, LeafRustleEmitterVisual emitter)
    {
        int color = emitter.Ring switch
        {
            LeafRustleEmitterRing.Immediate => unchecked((int)0xFFFF7A1A),
            LeafRustleEmitterRing.Near => unchecked((int)0xFF4DFF4D),
            LeafRustleEmitterRing.Mid => unchecked((int)0xFFFFFF4D),
            _ => unchecked((int)0xFF4DA6FF)
        };

        float x = (float)(emitter.Position.X - origin.X);
        float y = (float)(emitter.Position.Y - origin.Y);
        float z = (float)(emitter.Position.Z - origin.Z);
        float size = emitter.Ring switch
        {
            LeafRustleEmitterRing.Immediate => 0.35f,
            LeafRustleEmitterRing.Near => 0.45f,
            LeafRustleEmitterRing.Mid => 0.65f,
            _ => 0.9f
        };

        capi.Render.RenderLine(origin, x, y, z, x, y + 1.5f, z, color);
        capi.Render.RenderLine(origin, x - size, y + 0.25f, z, x + size, y + 0.25f, z, color);
        capi.Render.RenderLine(origin, x, y + 0.25f, z - size, x, y + 0.25f, z + size, color);
    }

    private void RenderRegions(BlockPos origin, LeafRustleDebugRegions regions)
    {
        const int discoveryColor = unchecked((int)0x99FFFFFF);
        const int lookAheadColor = unchecked((int)0xAAFF4D4D);
        const int preloadColor = unchecked((int)0xAA4DA6FF);
        const int gazeColor = unchecked((int)0xCC66FFCC);
        const int directionColor = unchecked((int)0xCCFF7A1A);

        double y = regions.PlayerPosition.Y + 0.12;
        RenderSquare(origin, regions.DiscoveryCenter.X, y, regions.DiscoveryCenter.Z, regions.DiscoveryHalfSize, discoveryColor);
        RenderCircle(origin, regions.LookAheadCenter.X, y + 0.06, regions.LookAheadCenter.Z, regions.LookAheadRadius, lookAheadColor);
        RenderCircle(origin, regions.AheadPreloadCenter.X, y + 0.12, regions.AheadPreloadCenter.Z, regions.AheadPreloadRadius, preloadColor);
        RenderCircle(origin, regions.GazePreloadCenter.X, regions.GazePreloadCenter.Y, regions.GazePreloadCenter.Z, regions.GazePreloadRadius, gazeColor);
        RenderWorldLine(origin,
            regions.PlayerPosition.X,
            regions.PlayerPosition.Y + 1.6,
            regions.PlayerPosition.Z,
            regions.GazePreloadCenter.X,
            regions.GazePreloadCenter.Y,
            regions.GazePreloadCenter.Z,
            gazeColor
        );
        RenderCircle(origin,
            regions.PlayerPosition.X + (regions.ViewNormX * 14.0),
            regions.PlayerPosition.Y + 1.6 + (regions.ViewNormY * 14.0),
            regions.PlayerPosition.Z + (regions.ViewNormZ * 14.0),
            regions.GazePreloadCorridorRadius,
            gazeColor
        );

        if (regions.IsMoving)
        {
            RenderWorldLine(origin,
                regions.PlayerPosition.X,
                y + 0.24,
                regions.PlayerPosition.Z,
                regions.PlayerPosition.X + (regions.FacingNormX * 8.0),
                y + 0.24,
                regions.PlayerPosition.Z + (regions.FacingNormZ * 8.0),
                directionColor
            );
        }
    }

    private void RenderSquare(BlockPos origin, double centerX, double y, double centerZ, double halfSize, int color)
    {
        double minX = centerX - halfSize;
        double maxX = centerX + halfSize;
        double minZ = centerZ - halfSize;
        double maxZ = centerZ + halfSize;

        RenderWorldLine(origin, minX, y, minZ, maxX, y, minZ, color);
        RenderWorldLine(origin, maxX, y, minZ, maxX, y, maxZ, color);
        RenderWorldLine(origin, maxX, y, maxZ, minX, y, maxZ, color);
        RenderWorldLine(origin, minX, y, maxZ, minX, y, minZ, color);
    }

    private void RenderCircle(BlockPos origin, double centerX, double y, double centerZ, double radius, int color)
    {
        const int segments = 64;
        double previousX = centerX + radius;
        double previousZ = centerZ;

        for (int i = 1; i <= segments; i++)
        {
            double angle = (Math.PI * 2.0 * i) / segments;
            double x = centerX + (Math.Cos(angle) * radius);
            double z = centerZ + (Math.Sin(angle) * radius);
            RenderWorldLine(origin, previousX, y, previousZ, x, y, z, color);
            previousX = x;
            previousZ = z;
        }
    }

    private void RenderWorldLine(BlockPos origin, double x1, double y1, double z1, double x2, double y2, double z2, int color)
    {
        capi.Render.RenderLine(
            origin,
            (float)(x1 - origin.X),
            (float)(y1 - origin.Y),
            (float)(z1 - origin.Z),
            (float)(x2 - origin.X),
            (float)(y2 - origin.Y),
            (float)(z2 - origin.Z),
            color
        );
    }

    public void Dispose()
    {
    }
}
