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
    private const int AlcFrequency = 0x1007;
    private const int AlcStereoBasicSoft = 0x19AE;
    private const int AlcStereoSoft = 0x1501;
    private const int AlcStereoHrtfSoft = 0x19B2;
    private const int AlcAnySoft = 0x19AD;
    private const int AlcQuadSoft = 0x1503;
    private const int Alc5Point1Soft = 0x1504;
    private const int Alc6Point1Soft = 0x1505;
    private const int Alc7Point1Soft = 0x1506;

    private static readonly AccessTools.FieldRef<AudioOpenAl, ALContext> ContextRef =
        AccessTools.FieldRefAccess<AudioOpenAl, ALContext>("Context");

    private static readonly AccessTools.FieldRef<AudioOpenAl, ALDevice> DeviceRef =
        AccessTools.FieldRefAccess<AudioOpenAl, ALDevice>("Device");

    internal static string LastRequestedOutputMode { get; private set; } = "Stereo (engine default)";
    internal static string LastActualOutputMode { get; private set; } = "Unknown";

    public static bool Prefix(AudioOpenAl __instance, ILogger logger)
    {
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
            DeviceRef(__instance) = device;

            bool allowHrtfSetting = ClientSettings.AllowSettingHRTFAudio;
            bool outputModeExtension = device != ALDevice.Null && ALC.IsExtensionPresent(device, "ALC_SOFT_output_mode");
            SurroundOutputMode requestedMode = SurroundSoundLabConfigManager.Current.OutputMode;
            bool useHrtf = ShouldUseHrtf(allowHrtfSetting, requestedMode);
            AudioOpenAl.UseHrtf = useHrtf;

            int[] attributes = BuildAttributeList(allowHrtfSetting, useHrtf, outputModeExtension, requestedMode);
            LastRequestedOutputMode = DescribeRequestedMode(requestedMode, outputModeExtension, useHrtf);

            ALContext context = ALC.CreateContext(device, attributes);
            ContextRef(__instance) = context;
            ALC.MakeContextCurrent(context);
            AudioOpenAl.CheckALError(logger, "Start");
            AL.Listener((ALListener3f)4102, 0f, 0f, 0f);
            AL.Listener(ALListenerf.Gain, Math.Clamp(ClientSettings.MasterSoundLevel / 100f, 0f, 1f));

            ALContextAttributes contextAttributes = ALC.GetContextAttributes(device);
            LastActualOutputMode = AudioOutputModeHelper.ReadCurrentOutputMode(device);
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
            logger.Error("Failed creating audio context");
            logger.Error(e);
        }

        return false;
    }

    private static int[] BuildAttributeList(bool allowHrtfSetting, bool useHrtf, bool outputModeExtension, SurroundOutputMode requestedMode)
    {
        if (!allowHrtfSetting && requestedMode == SurroundOutputMode.StereoHrtf)
        {
            requestedMode = SurroundOutputMode.Auto;
        }

        if (!outputModeExtension)
        {
            if (!allowHrtfSetting)
            {
                return new[] { 0 };
            }

            if (!useHrtf)
            {
                return new[] { AlcHrtfSoft, 0, 0 };
            }

            return ClientSettings.Force48kHzHRTFAudio
                ? new[] { AlcHrtfSoft, 1, AlcFrequency, 48000, 0 }
                : new[] { AlcHrtfSoft, 1, 0 };
        }

        int? outputModeValue = requestedMode switch
        {
            SurroundOutputMode.Auto => AlcAnySoft,
            SurroundOutputMode.StereoBasic => AlcStereoBasicSoft,
            SurroundOutputMode.Stereo => AlcStereoSoft,
            SurroundOutputMode.StereoHrtf => AlcStereoHrtfSoft,
            SurroundOutputMode.Quad => AlcQuadSoft,
            SurroundOutputMode.Surround5Point1 => Alc5Point1Soft,
            SurroundOutputMode.Surround6Point1 => Alc6Point1Soft,
            SurroundOutputMode.Surround7Point1 => Alc7Point1Soft,
            _ => AlcAnySoft
        };

        if (!useHrtf)
        {
            return new[] { AlcHrtfSoft, 0, AlcOutputModeSoft, outputModeValue.Value, 0 };
        }

        return ClientSettings.Force48kHzHRTFAudio
            ? new[] { AlcHrtfSoft, 1, AlcOutputModeSoft, outputModeValue.Value, AlcFrequency, 48000, 0 }
            : new[] { AlcHrtfSoft, 1, AlcOutputModeSoft, outputModeValue.Value, 0 };
    }

    private static bool ShouldUseHrtf(bool allowHrtfSetting, SurroundOutputMode requestedMode)
    {
        if (!allowHrtfSetting)
        {
            return false;
        }

        return requestedMode == SurroundOutputMode.StereoHrtf;
    }

    private static string DescribeRequestedMode(SurroundOutputMode requestedMode, bool outputModeExtension, bool useHrtf)
    {
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
