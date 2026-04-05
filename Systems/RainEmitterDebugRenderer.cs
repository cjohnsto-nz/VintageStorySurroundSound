using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace SurroundSoundLab;

internal sealed class RainEmitterDebugRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly RainEmitterSystem emitterSystem;

    public double RenderOrder => 0.52;
    public int RenderRange => 999;

    public RainEmitterDebugRenderer(ICoreClientAPI capi, RainEmitterSystem emitterSystem)
    {
        this.capi = capi;
        this.emitterSystem = emitterSystem;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!SurroundSoundLabConfigManager.Current.EnableDebugTools || !SurroundSoundLabConfigManager.Current.ShowRainEmitterDebugVisuals || stage != EnumRenderStage.Opaque)
        {
            return;
        }

        var player = capi.World?.Player?.Entity;
        if (player?.Pos == null)
        {
            return;
        }

        List<RainEmitterVisual> emitters = emitterSystem.GetActiveEmittersSnapshot(capi.ElapsedMilliseconds);
        if (emitters.Count == 0)
        {
            return;
        }

        var origin = new BlockPos((int)Math.Floor(player.Pos.X), (int)Math.Floor(player.Pos.Y), (int)Math.Floor(player.Pos.Z), player.Pos.Dimension);
        foreach (RainEmitterVisual emitter in emitters)
        {
            RenderEmitter(origin, emitter);
        }
    }

    private void RenderEmitter(BlockPos origin, RainEmitterVisual emitter)
    {
        int color = emitter.Quadrant switch
        {
            RainEmitterQuadrant.FrontLeft => unchecked((int)0xFF4DFF4D),
            RainEmitterQuadrant.FrontRight => unchecked((int)0xFF4DA6FF),
            RainEmitterQuadrant.BackLeft => unchecked((int)0xFFFFFF4D),
            _ => unchecked((int)0xFFFF7A1A)
        };

        float x = (float)(emitter.Position.X - origin.X);
        float y = (float)(emitter.Position.Y - origin.Y);
        float z = (float)(emitter.Position.Z - origin.Z);
        capi.Render.RenderLine(origin, x, y, z, x, y + 1.2f, z, color);
        capi.Render.RenderLine(origin, x - 0.35f, y + 0.2f, z, x + 0.35f, y + 0.2f, z, color);
        capi.Render.RenderLine(origin, x, y + 0.2f, z - 0.35f, x, y + 0.2f, z + 0.35f, color);
    }

    public void Dispose()
    {
    }
}
