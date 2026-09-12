using System;
using HarmonyLib;
using OpenTK.Audio.OpenAL;
using OpenTK.Mathematics;
using Vintagestory.API.Common;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace SurroundSoundLab;

[HarmonyPatch(typeof(AudioOpenAl), "initContext")]
internal static class AudioOpenAlInitContextPatch
{
    private const int AlcHrtfSoft = 0x1992;
    private const int AlcOutputModeSoft = 0x19AC;
    private const int AlcStereoBasicSoft = 0x19AE;

    private static readonly AccessTools.FieldRef<AudioOpenAl, ALContext> ContextRef =
        AccessTools.FieldRefAccess<AudioOpenAl, ALContext>("Context");

    private static readonly AccessTools.FieldRef<AudioOpenAl, ALDevice> DeviceRef =
        AccessTools.FieldRefAccess<AudioOpenAl, ALDevice>("Device");

    internal static string LastRequestedOutputMode { get; private set; } = "Stereo (engine default)";
    internal static string LastActualOutputMode { get; private set; } = "Unknown";
    internal static string LastInitializationFailure { get; private set; }
    internal static bool SpatialBedRequested { get; private set; }
    internal static int ContextGeneration { get; private set; }
    internal static bool HeightStreamStartedAtInitialization { get; private set; }

    public static bool Prefix(AudioOpenAl __instance, ILogger logger)
    {
        LastInitializationFailure = null;
        LastActualOutputMode = "Unavailable";
        SpatialBedRequested = false;
        ContextGeneration++;
        HeightStreamStartedAtInitialization = false;
        string nativeLogPath = Environment.GetEnvironmentVariable("ALSOFT_LOGFILE");
        long nativeLogStart = SpatialNativeLog.Length(nativeLogPath);
        try
        {
            if (DeviceRef(__instance) != ALDevice.Null)
            {
                ALC.MakeContextCurrent(ALContext.Null);
                ALC.DestroyContext(ContextRef(__instance));
                ALC.CloseDevice(DeviceRef(__instance));
                DeviceRef(__instance) = ALDevice.Null;
                ContextRef(__instance) = ALContext.Null;
            }

            string desiredDevice = ClientSettings.AudioDevice;
            if (!ALC.GetString((AlcGetStringList)4115).Contains(desiredDevice))
            {
                desiredDevice = null;
                ClientSettings.AudioDevice = null;
            }

            ALDevice device = ALC.OpenDevice(desiredDevice);
            if (device == ALDevice.Null && desiredDevice != null)
                device = ALC.OpenDevice(null);
            DeviceRef(__instance) = device;
            if (device == ALDevice.Null)
                throw new InvalidOperationException("OpenAL could not open an output device.");

            bool allowHrtfSetting = ClientSettings.AllowSettingHRTFAudio;
            bool outputModeExtension = device != ALDevice.Null && ALC.IsExtensionPresent(device, "ALC_SOFT_output_mode");
            SurroundOutputMode requestedMode = SurroundSoundLabConfigManager.Current.OutputMode;
            bool useHrtf = OutputContextPolicy.UseHrtf(requestedMode, allowHrtfSetting);
            AudioOpenAl.UseHrtf = useHrtf;

            int[] attributes = OutputContextPolicy.Build(requestedMode, allowHrtfSetting, outputModeExtension, ClientSettings.Force48kHzHRTFAudio);
            LastRequestedOutputMode = DescribeRequestedMode(requestedMode, outputModeExtension, useHrtf);

            ALContext context = ALC.CreateContext(device, attributes);
            if (context == ALContext.Null)
            {
                string error = ALC.GetError(device).ToString();
                LastInitializationFailure = $"Requested audio context failed ({error}); retrying stereo output.";
                logger.Warning("[Surround Sound] " + LastInitializationFailure);
                context = ALC.CreateContext(device, outputModeExtension
                    ? new[] { AlcHrtfSoft, 0, AlcOutputModeSoft, AlcStereoBasicSoft, 0 }
                    : new[] { 0 });
                AudioOpenAl.UseHrtf = false;
            }
            ContextRef(__instance) = context;
            if (context == ALContext.Null || !ALC.MakeContextCurrent(context))
                throw new InvalidOperationException("OpenAL could not make an audio context current.");
            AudioOpenAl.CheckALError(logger, "Start");
            AL.Listener((ALListener3f)4102, 0f, 0f, 0f);
            AL.Listener(ALListenerf.Gain, Math.Clamp(ClientSettings.MasterSoundLevel / 100f, 0f, 1f));

            ALContextAttributes contextAttributes = ALC.GetContextAttributes(device);
            LastActualOutputMode = AudioOutputModeHelper.ReadCurrentOutputMode(device);
            HeightStreamStartedAtInitialization = SpatialNativeLog.ReadInitialization(nativeLogPath, nativeLogStart);
            if (requestedMode == SurroundOutputMode.WindowsSpatialAudio)
            {
                SpatialAudioStatus status = SpatialAudioStatus.Capture(AL.Get(ALGetString.Version), true, LastActualOutputMode);
                SpatialBedRequested = (status.State is "Unverified" or "StreamActive") && LastActualOutputMode == "Any/Auto"
                    && status.StartupConfigured && status.RuntimeSupported;
                logger.Notification("[Surround Sound] Spatial audio: {0}. {1}", status.State, status.Detail);
            }
            logger.Notification(
                "OpenAL Initialized. Available Mono/Stereo Sources: {0}/{1}",
                contextAttributes.MonoSources,
                contextAttributes.StereoSources
            );

            AudioOpenAl.HasEffectsExtension = ALC.EFX.IsExtensionPresent(device);
            if (!AudioOpenAl.HasEffectsExtension)
            {
                logger.Notification("OpenAL Effects Extension not found. Disabling extra sound effects now.");
            }
        }
        catch (Exception e)
        {
            LastInitializationFailure = e.Message;
            AudioOpenAl.UseHrtf = false;
            if (ContextRef(__instance) != ALContext.Null)
            {
                ALC.MakeContextCurrent(ALContext.Null);
                ALC.DestroyContext(ContextRef(__instance));
                ContextRef(__instance) = ALContext.Null;
            }
            if (DeviceRef(__instance) != ALDevice.Null)
            {
                ALC.CloseDevice(DeviceRef(__instance));
                DeviceRef(__instance) = ALDevice.Null;
            }
            logger.Error("Failed creating audio context");
            logger.Error(e);
        }

        return false;
    }

    private static string DescribeRequestedMode(SurroundOutputMode requestedMode, bool outputModeExtension, bool useHrtf)
    {
        if (requestedMode == SurroundOutputMode.WindowsSpatialAudio)
            return "Windows Spatial Audio / 7.1.4 (experimental)";
        if (!outputModeExtension)
        {
            return useHrtf ? "Stereo HRTF (fallback)" : "Stereo Basic (fallback)";
        }

        return requestedMode switch
        {
            SurroundOutputMode.Auto => "Auto",
            SurroundOutputMode.StereoBasic => "Stereo Basic",
            SurroundOutputMode.Stereo => "Stereo",
            SurroundOutputMode.StereoHrtf => "Stereo HRTF",
            SurroundOutputMode.Quad => "Quad",
            SurroundOutputMode.Surround5Point1 => "5.1",
            SurroundOutputMode.Surround6Point1 => "6.1",
            SurroundOutputMode.Surround7Point1 => "7.1",
            _ => requestedMode.ToString()
        };
    }
}

internal static class AudioOutputModeHelper
{
    internal const int AlcOutputModeSoft = 0x19AC;
    internal const int AlcAnySoft = 0x19AD;
    internal const int AlcMonoSoft = 0x1500;
    internal const int AlcStereoSoft = 0x1501;
    internal const int AlcStereoBasicSoft = 0x19AE;
    internal const int AlcStereoUhjSoft = 0x19AF;
    internal const int AlcStereoHrtfSoft = 0x19B2;
    internal const int AlcQuadSoft = 0x1503;
    internal const int Alc5Point1Soft = 0x1504;
    internal const int Alc6Point1Soft = 0x1505;
    internal const int Alc7Point1Soft = 0x1506;

    internal static string ReadCurrentOutputMode(ALDevice device)
    {
        try
        {
            if (device == ALDevice.Null || !ALC.IsExtensionPresent(device, "ALC_SOFT_output_mode"))
            {
                return "Unavailable";
            }

            int rawMode = ALC.GetInteger(device, (AlcGetInteger)AlcOutputModeSoft);
            return Describe(rawMode);
        }
        catch
        {
            return "Unknown";
        }
    }

    internal static string Describe(int rawMode)
    {
        return rawMode switch
        {
            AlcAnySoft => "Any/Auto",
            AlcMonoSoft => "Mono",
            AlcStereoSoft => "Stereo",
            AlcStereoBasicSoft => "Stereo Basic",
            AlcStereoUhjSoft => "Stereo UHJ",
            AlcStereoHrtfSoft => "Stereo HRTF",
            AlcQuadSoft => "Quad",
            Alc5Point1Soft => "5.1",
            Alc6Point1Soft => "6.1",
            Alc7Point1Soft => "7.1",
            0 => "Unavailable",
            _ => $"0x{rawMode:X}"
        };
    }
}
