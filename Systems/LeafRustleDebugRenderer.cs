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
        if (emitters.Count == 0)
        {
            return;
        }

        var origin = new BlockPos(
            (int)Math.Floor(player.Pos.X),
            (int)Math.Floor(player.Pos.Y),
            (int)Math.Floor(player.Pos.Z),
            player.Pos.Dimension
        );

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

    public void Dispose()
    {
    }
}
