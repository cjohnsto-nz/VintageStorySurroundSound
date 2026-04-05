using HarmonyLib;
using OpenTK.Mathematics;
using Vintagestory.Client;

namespace SurroundSoundLab;

[HarmonyPatch(typeof(AudioOpenAl), "UpdateListener")]
internal static class AudioOpenAlUpdateListenerPatch
{
    public static void Prefix(ref Vector3 position, Vector3 orientation)
    {
        float backwardOffset = SurroundSoundLabConfigManager.Current.ListenerBackwardOffset;
        if (backwardOffset == 0f)
        {
            return;
        }

        Vector2 forward = new(orientation.X, orientation.Z);
        if (forward.LengthSquared < 1e-6f)
        {
            return;
        }

        forward.Normalize();
        position.X -= forward.X * backwardOffset;
        position.Z -= forward.Y * backwardOffset;
    }
}
