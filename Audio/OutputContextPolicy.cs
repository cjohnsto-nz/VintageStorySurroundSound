namespace SurroundSoundLab;

internal static class OutputContextPolicy
{
    internal static bool UseHrtf(SurroundOutputMode mode, bool allowHrtf) => allowHrtf && mode == SurroundOutputMode.StereoHrtf;

    internal static int[] Build(SurroundOutputMode mode, bool allowHrtf, bool outputModeExtension, bool force48kHz)
    {
        const int hrtf = 0x1992, output = 0x19AC, frequency = 0x1007;
        bool useHrtf = UseHrtf(mode, allowHrtf);
        if (!allowHrtf && mode == SurroundOutputMode.StereoHrtf) mode = SurroundOutputMode.Auto;
        if (!outputModeExtension)
        {
            if (!allowHrtf) return new[] { 0 };
            if (!useHrtf) return new[] { hrtf, 0, 0 };
            return force48kHz ? new[] { hrtf, 1, frequency, 48000, 0 } : new[] { hrtf, 1, 0 };
        }
        int layout = mode switch
        {
            SurroundOutputMode.StereoBasic => 0x19AE,
            SurroundOutputMode.Stereo => 0x1501,
            SurroundOutputMode.StereoHrtf => 0x19B2,
            SurroundOutputMode.Quad => 0x1503,
            SurroundOutputMode.Surround5Point1 => 0x1504,
            SurroundOutputMode.Surround6Point1 => 0x1505,
            SurroundOutputMode.Surround7Point1 => 0x1506,
            // 7.1.4 is configured before startup. ANY preserves that layout;
            // there is no public 7.1.4 enum in ALC_SOFT_output_mode.
            _ => 0x19AD
        };
        return useHrtf && force48kHz
            ? new[] { hrtf, 1, output, layout, frequency, 48000, 0 }
            : new[] { hrtf, useHrtf ? 1 : 0, output, layout, 0 };
    }
}
