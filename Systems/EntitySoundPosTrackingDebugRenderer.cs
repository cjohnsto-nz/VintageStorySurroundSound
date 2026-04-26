using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace SurroundSoundLab;

internal sealed class EntitySoundPosTrackingDebugRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;

    public double RenderOrder => 0.512;

    public int RenderRange => 999;

    public EntitySoundPosTrackingDebugRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!SurroundSoundLabConfigManager.Current.ShowEntitySoundPosTrackingDebugVisuals || stage != EnumRenderStage.Opaque)
        {
            return;
        }

        var player = capi.World?.Player?.Entity;
        if (player?.Pos == null)
        {
            return;
        }

        List<EntitySoundPosTrackingDebugVisual> trackedSounds = EntitySoundPosTrackingController.GetDebugSnapshot();
        if (trackedSounds.Count == 0)
        {
            return;
        }

        var origin = new BlockPos(
            (int)Math.Floor(player.Pos.X),
            (int)Math.Floor(player.Pos.Y),
            (int)Math.Floor(player.Pos.Z),
            player.Pos.Dimension
        );

        for (int i = 0; i < trackedSounds.Count; i++)
        {
            RenderTrackedSound(origin, trackedSounds[i]);
        }
    }

    private void RenderTrackedSound(BlockPos origin, EntitySoundPosTrackingDebugVisual tracked)
    {
        int color = tracked.Inferred ? unchecked((int)0xFF4DA6FF) : unchecked((int)0xFFFFCC33);

        RenderWorldLine(
            origin,
            tracked.EntityPosition.X,
            tracked.EntityPosition.Y,
            tracked.EntityPosition.Z,
            tracked.SoundPosition.X,
            tracked.SoundPosition.Y,
            tracked.SoundPosition.Z,
            color
        );

        RenderCross(origin, tracked.EntityPosition, 0.22, color);
        RenderCross(origin, tracked.SoundPosition, 0.35, color);
        RenderWorldLine(
            origin,
            tracked.EntityPosition.X,
            tracked.EntityPosition.Y,
            tracked.EntityPosition.Z,
            tracked.EntityPosition.X,
            tracked.EntityPosition.Y + 1.2,
            tracked.EntityPosition.Z,
            color
        );
    }

    private void RenderCross(BlockPos origin, Vec3d position, double size, int color)
    {
        RenderWorldLine(origin, position.X - size, position.Y, position.Z, position.X + size, position.Y, position.Z, color);
        RenderWorldLine(origin, position.X, position.Y - size, position.Z, position.X, position.Y + size, position.Z, color);
        RenderWorldLine(origin, position.X, position.Y, position.Z - size, position.X, position.Y, position.Z + size, color);
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