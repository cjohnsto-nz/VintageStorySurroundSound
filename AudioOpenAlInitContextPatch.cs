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
    private const int HrtfEnabled = 0x1501;
    private const int HrtfDisabled = 0x19AE;
    private const int AlcFrequency = 0x1007;
    private const int Alc5Point1Soft = 0x1504;
    private const string PreferredOutputModeName = "5.1";

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
            bool surroundRequested = outputModeExtension;
            bool useHrtf = allowHrtfSetting && ClientSettings.UseHRTFAudio && !surroundRequested;
            AudioOpenAl.UseHrtf = useHrtf;

            int[] attributes = BuildAttributeList(allowHrtfSetting, useHrtf, outputModeExtension, surroundRequested);
            LastRequestedOutputMode = surroundRequested ? PreferredOutputModeName : (useHrtf ? "Stereo (HRTF)" : "Stereo (Basic)");

            ALContext context = ALC.CreateContext(device, attributes);
            ContextRef(__instance) = context;
            ALC.MakeContextCurrent(context);
            AudioOpenAl.CheckALError(logger, "Start");
            AL.Listener((ALListener3f)4102, 0f, 0f, 0f);

            ALContextAttributes contextAttributes = ALC.GetContextAttributes(device);
            LastActualOutputMode = AudioOutputModeHelper.ReadCurrentOutputMode(device);
            logger.Notification(
                "OpenAL Initialized. Available Mono/Stereo Sources: {0}/{1}",
                contextAttributes.MonoSources,
                contextAttributes.StereoSources
            );
            logger.Notification(
                "OpenAL Output Mode Requested/Actual: {0}/{1}",
                LastRequestedOutputMode,
                LastActualOutputMode
            );
            if (surroundRequested && ClientSettings.UseHRTFAudio)
            {
                logger.Notification("OpenAL HRTF disabled for surround output request.");
            }

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

    private static int[] BuildAttributeList(bool allowHrtfSetting, bool useHrtf, bool outputModeExtension, bool surroundRequested)
    {
        if (surroundRequested)
        {
            return new[]
            {
                AlcHrtfSoft, 0,
                AlcOutputModeSoft, Alc5Point1Soft,
                0
            };
        }

        if (!allowHrtfSetting)
        {
            return new[] { 0 };
        }

        if (!useHrtf)
        {
            return outputModeExtension
                ? new[] { AlcHrtfSoft, 0, AlcOutputModeSoft, HrtfDisabled, 0 }
                : new[] { AlcHrtfSoft, 0, 0 };
        }

        return ClientSettings.Force48kHzHRTFAudio
            ? (outputModeExtension
                ? new[] { AlcHrtfSoft, 1, AlcOutputModeSoft, HrtfEnabled, AlcFrequency, 48000, 0 }
                : new[] { AlcHrtfSoft, 1, AlcFrequency, 48000, 0 })
            : (outputModeExtension
                ? new[] { AlcHrtfSoft, 1, AlcOutputModeSoft, HrtfEnabled, 0 }
                : new[] { AlcHrtfSoft, 1, 0 });
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
