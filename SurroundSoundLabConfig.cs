using Vintagestory.API.Common;

namespace SurroundSoundLab;

public enum SurroundOutputMode
{
    Auto,
    StereoBasic,
    Stereo,
    StereoHrtf,
    Quad,
    Surround5Point1,
    Surround6Point1,
    Surround7Point1
}

public sealed class SurroundSoundLabConfig
{
    public SurroundOutputMode OutputMode { get; set; } = SurroundOutputMode.Auto;
    public bool UpmixStereoToSurround { get; set; } = true;
    public bool EnableSoundAudit { get; set; } = false;
}

internal static class SurroundSoundLabConfigManager
{
    internal const string ConfigFileName = "surroundsoundlab.json";

    public static SurroundSoundLabConfig Current { get; private set; } = new();

    public static void Load(ICoreAPI api, ILogger logger)
    {
        try
        {
            Current = api.LoadModConfig<SurroundSoundLabConfig>(ConfigFileName) ?? new SurroundSoundLabConfig();
            api.StoreModConfig(Current, ConfigFileName);
        }
        catch (System.Exception ex)
        {
            logger.Warning("[SurroundSoundLab] Failed to load config, using defaults: " + ex.Message);
            Current = new SurroundSoundLabConfig();
        }
    }
}
