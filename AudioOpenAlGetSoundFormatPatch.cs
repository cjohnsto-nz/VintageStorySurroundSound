using HarmonyLib;
using OpenTK.Audio.OpenAL;
using Vintagestory.Client;

namespace SurroundSoundLab;

[HarmonyPatch(typeof(AudioOpenAl), nameof(AudioOpenAl.GetSoundFormat))]
internal static class AudioOpenAlGetSoundFormatPatch
{
    public static bool Prefix(int channels, int bits, ref ALFormat __result)
    {
        if (!TryResolvePatchedFormat(channels, bits, out var format))
        {
            return true;
        }

        __result = format;
        return false;
    }

    internal static bool TryResolvePatchedFormat(int channels, int bits, out ALFormat format)
    {
        format = default;

        string enumName = (channels, bits) switch
        {
            (4, 8) => "AL_FORMAT_QUAD8",
            (4, 16) => "AL_FORMAT_QUAD16",
            (6, 8) => "AL_FORMAT_51CHN8",
            (6, 16) => "AL_FORMAT_51CHN16",
            (7, 8) => "AL_FORMAT_61CHN8",
            (7, 16) => "AL_FORMAT_61CHN16",
            (8, 8) => "AL_FORMAT_71CHN8",
            (8, 16) => "AL_FORMAT_71CHN16",
            _ => null
        };

        if (enumName == null)
        {
            return false;
        }

        int enumValue;
        try
        {
            enumValue = AL.GetEnumValue(enumName);
        }
        catch
        {
            return false;
        }

        if (enumValue == 0)
        {
            return false;
        }

        format = (ALFormat)enumValue;
        return true;
    }
}
