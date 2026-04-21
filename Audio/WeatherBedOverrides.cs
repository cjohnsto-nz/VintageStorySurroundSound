using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client;

namespace SurroundSoundLab;

internal static class WeatherBedOverrides
{
    private static readonly (AssetLocation Target, AssetLocation Replacement)[] Replacements =
    {
        (new AssetLocation("game:sounds/weather/tracks/rain-leafless.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/tracks/rain-surround-new.ogg")),
        (new AssetLocation("game:sounds/weather/tracks/rain-leafy.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/tracks/rain-surround-quiet.ogg")),
        (new AssetLocation("game:sounds/weather/wind-leafless.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/wind-surround-leafy2.ogg")),
        (new AssetLocation("game:sounds/weather/wind-leafy.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/wind-surround-leafless2.ogg")),
        (new AssetLocation("game:sounds/weather/lowgrumble.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/rumble-low.ogg")),
        (new AssetLocation("game:sounds/weather/lightning-distant.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/lightning-distant.ogg")),
        (new AssetLocation("game:sounds/weather/hail.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/hail.wav"))
    };

    public static void Apply(ICoreClientAPI api, ILogger logger)
    {
        foreach (var (target, replacement) in Replacements)
        {
            TryRegister(api, logger, target, replacement);
        }
    }

    private static void TryRegister(ICoreClientAPI api, ILogger logger, AssetLocation targetLocation, AssetLocation replacementLocation)
    {
        IAsset asset = api.Assets.TryGet(replacementLocation);
        if (asset?.Data == null)
        {
            logger.Warning("Could not find surround replacement asset {0} for target {1}.", replacementLocation, targetLocation);
            return;
        }

        ScreenManager.soundAudioData[targetLocation] = ScreenManager.LoadSound(asset);
        logger.Notification("Registered surround weather replacement {0} -> {1}.", targetLocation, replacementLocation);
    }
}
