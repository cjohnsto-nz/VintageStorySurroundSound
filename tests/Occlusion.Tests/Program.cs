using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using HarmonyLib;
using OpenTK.Audio.OpenAL;
using SurroundSoundLab;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

string game = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vintagestory"));
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (string folder in new[] { game, Path.Combine(game, "Lib"), Path.Combine(game, "Mods") })
    {
        string file = Path.Combine(folder, name.Name + ".dll");
        if (File.Exists(file)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
    }
    return null;
};
return Regression.Run(game);

internal static class Regression
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Run(string game)
    {
        // No receiver or user settings: use OpenAL's silent null backend and
        // an isolated game settings directory for this process only.
        string data = Path.Combine(AppContext.BaseDirectory, "runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        GamePaths.DataPath = data;
        string ini = Path.Combine(data, "alsoft.ini");
        File.WriteAllText(ini, "[general]\ndrivers = null\nchannels = stereo\n");
        Environment.SetEnvironmentVariable("ALSOFT_CONF", ini);
        Environment.SetEnvironmentVariable("ALSOFT_DRIVERS", "null");
        Environment.SetEnvironmentVariable("ALSOFT_LOGFILE", Path.Combine(data, "openal.log"));
        // OpenTK registers its own resolver. Preload the selected DLL by its
        // absolute path so that resolver finds this module by name.
        NativeLibrary.Load(Path.Combine(game, "Lib", "OpenAL32.dll"));
        ClientSettings.EntitySoundLevel = 80;
        var harmony = new Harmony("surroundsound.tests.bell-occlusion");
        harmony.CreateClassProcessor(typeof(LoadedSoundNativeOcclusionGainPatch)).Patch();
        harmony.Patch(AccessTools.Method(typeof(LoadedSoundNative), nameof(LoadedSoundNative.SetVolume), new[] { typeof(float) }),
            postfix: new HarmonyMethod(typeof(Regression), nameof(CaptureRequestedVolume)));
        var device = ALC.OpenDevice(null);
        if (device == ALDevice.Null) throw new Exception("Silent OpenAL backend unavailable.");
        var context = ALC.CreateContext(device, new int[] { 0 });
        if (context == ALContext.Null || !ALC.MakeContextCurrent(context)) throw new Exception("Silent context unavailable.");
        LoadedSoundNative sound = null;
        int checks = 0;
        void Check(bool value, string description)
        {
            if (!value) throw new Exception("FAIL: " + description);
            Console.WriteLine("PASS: " + description);
            checks++;
        }
        void Near(float actual, float expected, string description) => Check(Math.Abs(actual - expected) < 0.015f, description + $" ({actual:F4})");
        float Gain()
        {
            int id = (int)AccessTools.Field(typeof(LoadedSoundNative), "sourceId").GetValue(sound);
            AL.GetSource(id, ALSourcef.Gain, out float gain);
            return gain;
        }
        void AwaitFade()
        {
            if (!SpinWait.SpinUntil(() => !sound.IsFadingIn && !sound.IsFadingOut, 5000)) throw new Exception("Vanilla fade did not complete.");
        }
        try
        {
            var sample = (AudioMetaData)RuntimeHelpers.GetUninitializedObject(typeof(AudioMetaData));
            sample.Pcm = new byte[48000 * 2]; // Silent mono buffer; playback state still advances.
            sample.Channels = 1;
            sample.BitsPerSample = 16;
            sample.Rate = 48000;
            sample.Loaded = 3;
            sound = new LoadedSoundNative(new SoundParams
            {
                Location = new AssetLocation("game", "sounds/creature/bell/alarm.ogg"),
                ShouldLoop = true, DisposeOnFinish = false, Volume = 0, SoundType = EnumSoundType.Entity
            }, sample);
            sound.Start();
            OcclusionGainController.SetFactor(sound, 0.5f);
            Near(sound.Params.Volume, 0, "Bell starts silently without changing its authored volume");
            sound.FadeIn(0.25f, null);
            AwaitFade();
            // Vanilla's exponential fade can overshoot its target within a
            // batch. Verify its actual last request, not an idealized envelope.
            Near(Gain(), lastRequestedVolume * 0.4f, "Actual vanilla fade-in retains category volume and occlusion");
            for (int i = 0; i < 10; i++) OcclusionGainController.SetFactor(sound, 0.5f);
            Near(sound.Params.Volume, 1, "Repeated occlusion refreshes do not reset or compound volume");
            Near(Gain(), 0.4f, "Bell remains audible after repeated refreshes");
            Thread.Sleep(1100);
            Check(sound.IsPlaying && sound.Params.ShouldLoop, "Bell remains looping beyond the source buffer duration");
            OcclusionGainController.SetFactor(sound, 1f);
            Near(Gain(), 0.8f, "Removing obstruction restores full authored gain");
            sound.SetVolume(0.6f);
            OcclusionGainController.SetFactor(sound, 0.5f);
            Near(sound.Params.Volume, 0.6f, "Explicit vanilla volume changes remain unattenuated in SoundParams");
            Near(Gain(), 0.24f, "New vanilla volume and occlusion combine once");
            ClientSettings.EntitySoundLevel = 40;
            sound.SetVolume();
            Near(Gain(), 0.12f, "Category slider refresh preserves occlusion");
            sound.FadeOut(0.25f, null);
            AwaitFade();
            Check(Gain() < 0.01f, "Vanilla fade-out reaches silence while occluded");
            sound.Stop();
            Check(sound.HasStopped && !sound.IsDisposed, "Reusable alarm can stop without disposal");
            sound.Start();
            sound.FadeIn(0.25f, null);
            AwaitFade();
            OcclusionGainController.SetFactor(sound, 0.5f);
            Near(Gain(), 0.2f, "Second alarm start fades back in without stale base volume");
            AccessTools.Method(typeof(LoadedSoundNative), "disposeSoundSource").Invoke(sound, null);
            AccessTools.Method(typeof(LoadedSoundNative), "createSoundSource").Invoke(sound, null);
            Near(Gain(), sound.Params.Volume * 0.2f, "Recreated native source retains its occlusion factor");
            using (var untracked = new LoadedSoundNative(new SoundParams { Volume = 0.4f, SoundType = EnumSoundType.Entity }, sample))
            {
                int id = (int)AccessTools.Field(typeof(LoadedSoundNative), "sourceId").GetValue(untracked);
                AL.GetSource(id, ALSourcef.Gain, out float gain);
                Near(gain, 0.16f, "Untracked sounds retain ordinary category gain");
            }
            OcclusionGainController.Clear();
            sound.SetVolume();
            Near(Gain(), 0.4f, "Clearing occlusion restores conventional gain");
            Check(AL.GetError() == ALError.NoError, "Native calls completed without OpenAL errors");
            Console.WriteLine($"{checks} Bell/occlusion integration checks passed against {FileVersionInfo(game)}.");
            return 0;
        }
        finally
        {
            sound?.Dispose();
            harmony.UnpatchAll(harmony.Id);
            ALC.MakeContextCurrent(ALContext.Null);
            ALC.DestroyContext(context);
            ALC.CloseDevice(device);
        }
    }

    private static string FileVersionInfo(string game) => System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(game, "Vintagestory.exe")).FileVersion;
    private static volatile float lastRequestedVolume;
    private static void CaptureRequestedVolume(float __0) => lastRequestedVolume = __0;
}
