using HarmonyLib;
using OpenTK.Audio.OpenAL;
using OpenTK.Mathematics;
using Vintagestory.Client.NoObf;

namespace SurroundSoundLab;

// The engine flattens the view vector inside this renderer. Apply the full
// orthonormal listener basis after its update; disabling this setting leaves
// the original per-frame yaw-only update in control.
[HarmonyPatch(typeof(SystemSoundEngine), nameof(SystemSoundEngine.OnRenderFrame))]
internal static class ListenerPitchPatch
{
    public static void Postfix(ClientMain ___game)
    {
        if (!SurroundSoundLabConfigManager.Current.FollowCameraPitch || ___game?.EntityPlayer == null
            || ALC.GetCurrentContext() == ALContext.Null) return;
        var position = ___game.EntityPlayer.Pos;
        var basis = ListenerOrientationPolicy.Build(true, position.Pitch, position.Yaw);
        var forward = new Vector3(basis.Forward.X, basis.Forward.Y, basis.Forward.Z);
        var up = new Vector3(basis.Up.X, basis.Up.Y, basis.Up.Z);
        AL.Listener(ALListenerfv.Orientation, ref forward, ref up);
    }
}
