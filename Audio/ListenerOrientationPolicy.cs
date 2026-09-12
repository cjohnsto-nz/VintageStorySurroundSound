using System;
using System.Numerics;

namespace SurroundSoundLab;

internal static class ListenerOrientationPolicy
{
    internal static (Vector3 Forward, Vector3 Up) Build(bool followPitch, float pitch, float yaw)
    {
        if (!float.IsFinite(yaw)) yaw = 0f;
        if (!float.IsFinite(pitch)) pitch = MathF.PI;
        // Vintage Story's normal upright camera has pitch PI, not zero.
        var horizontal = new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
        if (!followPitch) return (horizontal, Vector3.UnitY);
        var forward = Vector3.Normalize(new Vector3(-MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch), -MathF.Cos(pitch) * MathF.Cos(yaw)));
        // Derive right from yaw so straight-up/down views stay well-defined.
        // OpenAL normalizes the supplied up vector but does not orthogonalize it.
        var right = Vector3.Normalize(Vector3.Cross(horizontal, Vector3.UnitY));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        return (forward, up);
    }
}
