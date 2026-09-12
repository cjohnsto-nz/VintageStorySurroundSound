using OpenTK.Audio.OpenAL;
using Vintagestory.Client;

namespace SurroundSoundLab;

internal static class WeatherBedRoutingController
{
    internal static bool IsAmbientBed(LoadedSoundNative sound)
    {
        var parameters = sound?.Params;
        var location = parameters?.Location;
        var position = parameters?.Position;
        return WeatherBedRoutingPolicy.IsAmbientBed(location?.Domain, location?.Path,
            parameters?.RelativePosition ?? false, position?.X ?? 0f, position?.Y ?? 0f, position?.Z ?? 0f);
    }

    internal static void OnSourceCreated(LoadedSoundNative sound)
    {
        if (sound == null || sound.IsDisposed || !IsAmbientBed(sound)) return;
        int channels = LoadedSoundNativeChannelMaskPatch.SampleRef(sound)?.Channels ?? 0;
        int source = LoadedSoundNativeChannelMaskPatch.SourceIdRef(sound);
        if (source == 0 || channels < 2) return;

        // Weather recordings are speaker beds, not sources floating above the
        // camera. Preserve their authored channels and avoid positional remixing.
        if (AL.IsExtensionPresent("AL_SOFT_source_spatialize"))
            AL.Source(source, (ALSourcei)0x1214, 0);
        if (AL.IsExtensionPresent("AL_SOFT_direct_channels"))
            AL.Source(source, (ALSourcei)0x1033,
                AL.IsExtensionPresent("AL_SOFT_direct_channels_remix") ? 2 : 1);
    }
}
