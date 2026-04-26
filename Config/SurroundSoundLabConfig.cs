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
    public bool EnableDebugTools { get; set; } = false;
    public SurroundOutputMode OutputMode { get; set; } = SurroundOutputMode.Auto;
    public float ListenerBackwardOffset { get; set; } = 0.5f;
    public bool UpmixStereoToSurround { get; set; } = true;
    public float StereoUpmixGainDb { get; set; } = -6f;
    public bool ReplaceVanillaWeatherBeds { get; set; } = true;
    public float SoundRangeMultiplier { get; set; } = 3f;
    public bool EnableExperimentalLeafRustleEmitters { get; set; } = true;
    public float LeafRustleVolumeMultiplier { get; set; } = 1.75f;
    public float LeafRustlePitchVariationMultiplier { get; set; } = 1.5f;
    public bool EnableEntitySoundPosTracking { get; set; } = true;
    public bool EnableEntitySoundPosTrackingInference { get; set; } = true;
    public bool EnableEntitySoundBlockOcclusion { get; set; } = true;
    public int EntitySoundBlockOcclusionMaxBlocks { get; set; } = 8;
    public float EntitySoundBlockOcclusionVolumePerBlock { get; set; } = 0.78f;
    public float EntitySoundBlockOcclusionLowPassPerBlock { get; set; } = 0.8f;
    public float EntitySoundBlockOcclusionMinVolumeFactor { get; set; } = 0.08f;
    public float EntitySoundBlockOcclusionMinLowPass { get; set; } = 0.04f;
    public bool ShowEntitySoundOcclusionDebugRays { get; set; } = false;
    public bool EnableEntitySoundDoppler { get; set; } = false;
    public float EntitySoundDopplerStrength { get; set; } = 1f;
    public float EntitySoundDopplerSpeedOfSound { get; set; } = 343f;
    public float EntitySoundDopplerMinPitchFactor { get; set; } = 0.5f;
    public float EntitySoundDopplerMaxPitchFactor { get; set; } = 2.0f;
    public float EntitySoundDopplerVelocitySmoothingSeconds { get; set; } = 0.35f;
    public float EntitySoundDopplerPitchSmoothingSeconds { get; set; } = 0.18f;
    public float EntitySoundDopplerDeadZoneBlocksPerSecond { get; set; } = 0.08f;
    public int EntitySoundPosTrackingUpdateMs { get; set; } = 16;
    public int MaxTrackedEntitySounds { get; set; } = 128;
    public float EntitySoundPosTrackingInferenceMaxDistance { get; set; } = 1.25f;
    public bool FreezeOneShotEntitySoundsOnDespawn { get; set; } = true;
    public bool StopLoopingEntitySoundsOnDespawn { get; set; } = true;
    public bool EnableExperimentalRainEmitters { get; set; } = true;
    public bool ShowLeafRustleDebugVisuals { get; set; } = false;
    public bool ShowRainEmitterDebugVisuals { get; set; } = false;
    public bool EnableSoundAudit { get; set; } = false;
}

internal static class SurroundSoundLabConfigManager
{
    internal const string ConfigFileName = "vintagestorysurroundsound.json";
    internal const string LegacyConfigFileName = "surroundsoundlab.json";

    public static SurroundSoundLabConfig Current { get; private set; } = new();

    public static void Load(ICoreAPI api, ILogger logger)
    {
        try
        {
            Current = api.LoadModConfig<SurroundSoundLabConfig>(ConfigFileName)
                ?? api.LoadModConfig<SurroundSoundLabConfig>(LegacyConfigFileName)
                ?? new SurroundSoundLabConfig();
            api.StoreModConfig(Current, ConfigFileName);
        }
        catch (System.Exception ex)
        {
            logger.Warning("[VintageStorySurroundSound] Failed to load config, using defaults: " + ex.Message);
            Current = new SurroundSoundLabConfig();
        }
    }
}
