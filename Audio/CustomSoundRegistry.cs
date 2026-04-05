using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client;

namespace SurroundSoundLab;

internal static class CustomSoundRegistry
{
    public static readonly AssetLocation LeafRustleOneAlias = new("vintagestorysurroundsound:sounds/foliage/leaves-mono-1.ogg");
    public static readonly AssetLocation LeafRustleTwoAlias = new("vintagestorysurroundsound:sounds/foliage/leaves-mono-2.ogg");
    public static readonly AssetLocation LeafRustleThreeAlias = new("vintagestorysurroundsound:sounds/foliage/leaves-mono-3.ogg");
    public static readonly AssetLocation LeafRustleFourAlias = new("vintagestorysurroundsound:sounds/foliage/leaves-mono-4.ogg");
    public static readonly AssetLocation RainOneAlias = new("vintagestorysurroundsound:sounds/weather/rain-mono-1.ogg");
    public static readonly AssetLocation RainTwoAlias = new("vintagestorysurroundsound:sounds/weather/rain-mono-2.ogg");
    public static readonly AssetLocation RainThreeAlias = new("vintagestorysurroundsound:sounds/weather/rain-mono-3.ogg");
    public static readonly AssetLocation RainFourAlias = new("vintagestorysurroundsound:sounds/weather/rain-mono-4.ogg");

    private static readonly (AssetLocation Target, AssetLocation Source)[] Aliases =
    {
        (LeafRustleOneAlias, new AssetLocation("vintagestorysurroundsound:sounds/foliage/leaves-mono-1.wav")),
        (LeafRustleTwoAlias, new AssetLocation("vintagestorysurroundsound:sounds/foliage/leaves-mono-2.wav")),
        (LeafRustleThreeAlias, new AssetLocation("vintagestorysurroundsound:sounds/foliage/leaves-mono-3.wav")),
        (LeafRustleFourAlias, new AssetLocation("vintagestorysurroundsound:sounds/foliage/leaves-mono-4.wav")),
        (RainOneAlias, new AssetLocation("vintagestorysurroundsound:sounds/weather/rain-mono-1.wav")),
        (RainTwoAlias, new AssetLocation("vintagestorysurroundsound:sounds/weather/rain-mono-2.wav")),
        (RainThreeAlias, new AssetLocation("vintagestorysurroundsound:sounds/weather/rain-mono-3.wav")),
        (RainFourAlias, new AssetLocation("vintagestorysurroundsound:sounds/weather/rain-mono-4.wav"))
    };

    public static void Register(ICoreClientAPI api, ILogger logger)
    {
        foreach (var (target, source) in Aliases)
        {
            IAsset asset = api.Assets.TryGet(source);
            if (asset?.Data == null)
            {
                logger.Warning("Could not find custom sound asset {0} for alias {1}.", source, target);
                continue;
            }

            ScreenManager.soundAudioData[target] = ScreenManager.LoadSound(asset);
        }
    }
}
