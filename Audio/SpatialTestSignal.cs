using System;
using System.Numerics;

namespace SurroundSoundLab;

internal enum SpatialTestPosition
{
    Front, Rear, Above, Below, TopFrontLeft, TopFrontRight, TopRearLeft, TopRearRight, VerticalSweep
}

internal static class SpatialTestSignal
{
    internal const int SampleRate = 48000;
    internal const float DurationSeconds = 8;

    // Anchor in world space at the start. Looking around must move the sound
    // relative to the listener, rather than dragging the test with the camera.
    internal static Vector3 WorldPosition(SpatialTestPosition target, Vector3 listener, Vector3 forward, float progress = 0)
    {
        forward.Y = 0;
        forward = forward.LengthSquared() < 0.000001f ? -Vector3.UnitZ : Vector3.Normalize(forward);
        Vector3 right = Vector3.Cross(forward, Vector3.UnitY);
        Vector3 offset = target switch
        {
            SpatialTestPosition.Front => forward * 4,
            SpatialTestPosition.Rear => forward * -4,
            SpatialTestPosition.Above => Vector3.UnitY * 4,
            SpatialTestPosition.Below => Vector3.UnitY * -4,
            SpatialTestPosition.TopFrontLeft => forward * 3 - right * 3 + Vector3.UnitY * 4,
            SpatialTestPosition.TopFrontRight => forward * 3 + right * 3 + Vector3.UnitY * 4,
            SpatialTestPosition.TopRearLeft => forward * -3 - right * 3 + Vector3.UnitY * 4,
            SpatialTestPosition.TopRearRight => forward * -3 + right * 3 + Vector3.UnitY * 4,
            SpatialTestPosition.VerticalSweep => forward * 3 + Vector3.UnitY * (-4 + 8 * Math.Clamp(progress, 0, 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        return listener + offset;
    }

    internal static short[] BuildSamples()
    {
        var samples = new short[(int)(SampleRate * DurationSeconds)];
        var random = new Random(714);
        // Quiet broadband bursts support localization better than a single sine.
        // Smooth edges avoid clicks; one half-second burst per second.
        for (int i = 0; i < samples.Length; i++)
        {
            double phase = (i % SampleRate) / (double)SampleRate;
            double envelope = Math.Clamp(Math.Min(phase / 0.02, (0.5 - phase) / 0.02), 0, 1);
            samples[i] = (short)((random.NextDouble() * 2 - 1) * short.MaxValue * 0.12 * envelope);
        }
        return samples;
    }
}
