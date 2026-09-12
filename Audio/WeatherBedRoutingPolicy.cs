namespace SurroundSoundLab;

internal static class WeatherBedRoutingPolicy
{
    internal static bool IsBed(string domain, string path) => domain switch
    {
        "game" => path is "sounds/weather/tracks/rain-leafless.ogg" or "sounds/weather/tracks/rain-leafy.ogg"
            or "sounds/weather/wind-leafless.ogg" or "sounds/weather/wind-leafy.ogg"
            or "sounds/weather/tracks/hail.ogg" or "sounds/weather/tracks/lowtremble.ogg"
            or "sounds/weather/tracks/verylowtremble.ogg" or "sounds/weather/lowgrumble.ogg"
            or "sounds/weather/lightning-distant.ogg",
        "vintagestorysurroundsound" => path is "sounds/weather/tracks/rain-surround-new.ogg"
            or "sounds/weather/tracks/rain-surround-quiet.ogg" or "sounds/weather/tracks/rain-surround-loud.ogg"
            or "sounds/weather/tracks/rain-surround-canopy.ogg" or "sounds/weather/rumble-low.ogg"
            or "sounds/weather/lightning-distant.ogg" or "sounds/weather/hail.wav"
            or "sounds/weather/wind-surround-leafy2.ogg" or "sounds/weather/wind-surround-leafless2.ogg",
        _ => false
    };

    internal static bool IsAmbientBed(string domain, string path, bool relativePosition, float x, float y, float z) =>
        IsBed(domain, path) && (relativePosition || (x == 0f && y == 0f && z == 0f));
}
