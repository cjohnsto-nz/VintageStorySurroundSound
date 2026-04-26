using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SurroundSoundLab;

internal sealed class EntitySoundOcclusionDebugRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;

    public double RenderOrder => 0.515;
    public int RenderRange => 999;

    public EntitySoundOcclusionDebugRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!SurroundSoundLabConfigManager.Current.ShowEntitySoundOcclusionDebugRays || stage != EnumRenderStage.Opaque)
        {
            return;
        }

        var player = capi.World?.Player?.Entity;
        if (player?.Pos == null)
        {
            return;
        }

        List<EntitySoundOcclusionDebugRay> rays = SoundOcclusion.GetDebugRaySnapshot(capi.ElapsedMilliseconds);
        if (rays.Count == 0)
        {
            return;
        }

        var origin = new BlockPos(
            (int)Math.Floor(player.Pos.X),
            (int)Math.Floor(player.Pos.Y),
            (int)Math.Floor(player.Pos.Z),
            player.Pos.Dimension
        );

        foreach (EntitySoundOcclusionDebugRay ray in rays)
        {
            RenderRay(origin, ray);
        }
    }

    private void RenderRay(BlockPos origin, EntitySoundOcclusionDebugRay ray)
    {
        int color = GetRayColor(ray);
        RenderWorldLine(origin, ray.From.X, ray.From.Y, ray.From.Z, ray.To.X, ray.To.Y, ray.To.Z, color);

        const double markerSize = 0.25;
        RenderWorldLine(origin, ray.To.X - markerSize, ray.To.Y, ray.To.Z, ray.To.X + markerSize, ray.To.Y, ray.To.Z, color);
        RenderWorldLine(origin, ray.To.X, ray.To.Y - markerSize, ray.To.Z, ray.To.X, ray.To.Y + markerSize, ray.To.Z, color);
        RenderWorldLine(origin, ray.To.X, ray.To.Y, ray.To.Z - markerSize, ray.To.X, ray.To.Y, ray.To.Z + markerSize, color);
    }

    private static int GetRayColor(EntitySoundOcclusionDebugRay ray)
    {
        int maxBlocks = Math.Max(1, ray.MaxBlocks);
        float t = Math.Clamp(ray.OccludingBlocks / (float)maxBlocks, 0f, 1f);
        int red = (int)Math.Round(255 * t);
        int green = (int)Math.Round(255 * (1f - t));
        return ColorUtil.ToRgba(204, red, green, 0);
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
