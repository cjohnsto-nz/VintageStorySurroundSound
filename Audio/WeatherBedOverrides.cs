using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace SurroundSoundLab;

internal static class WeatherBedOverrides
{
    private static readonly Dictionary<AssetLocation, AudioData> OriginalAudioDataByTarget = new();
    private static bool applied;

    private static readonly (AssetLocation Target, AssetLocation Replacement)[] Replacements =
    {
        (new AssetLocation("game:sounds/weather/tracks/rain-leafless.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/tracks/rain-surround-new.ogg")),
        (new AssetLocation("game:sounds/weather/tracks/rain-leafy.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/tracks/rain-surround-quiet.ogg")),
        (new AssetLocation("game:sounds/weather/wind-leafless.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/wind-surround-leafy2.ogg")),
        (new AssetLocation("game:sounds/weather/wind-leafy.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/wind-surround-leafless2.ogg")),
        (new AssetLocation("game:sounds/weather/lowgrumble.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/rumble-low.ogg")),
        (new AssetLocation("game:sounds/weather/lightning-distant.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/lightning-distant.ogg")),
        (new AssetLocation("game:sounds/weather/tracks/hail.ogg"), new AssetLocation("vintagestorysurroundsound:sounds/weather/hail.wav"))
    };

    public static void Apply(ICoreClientAPI api, ILogger logger)
    {
        if (applied)
        {
            return;
        }

        foreach (var (target, replacement) in Replacements)
        {
            TryRegister(api, logger, target, replacement);
        }

        applied = true;
    }

    public static void Restore(ILogger logger)
    {
        if (!applied)
        {
            return;
        }

        foreach (var (target, _) in Replacements)
        {
            if (OriginalAudioDataByTarget.TryGetValue(target, out AudioData original))
            {
                ScreenManager.soundAudioData[target] = original;
            }
            else
            {
                ScreenManager.soundAudioData.Remove(target);
            }
        }

        applied = false;
        logger.Notification("Restored vanilla weather audio beds.");
    }

    private static void TryRegister(ICoreClientAPI api, ILogger logger, AssetLocation targetLocation, AssetLocation replacementLocation)
    {
        if (!OriginalAudioDataByTarget.ContainsKey(targetLocation))
        {
            if (ScreenManager.soundAudioData.TryGetValue(targetLocation, out AudioData existing))
            {
                OriginalAudioDataByTarget[targetLocation] = existing;
            }
        }

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
