using System.Numerics;
using SurroundSoundLab;

if (args.Length > 0) return NativeProbe.Run(args);
int count = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    count++;
}
Check((int)SurroundOutputMode.Surround7Point1 == 7 && (int)SurroundOutputMode.WindowsSpatialAudio == 8,
    "Existing saved mode values remain compatible");
foreach (bool allow in new[] { true, false })
foreach (bool force in new[] { true, false })
{
    Check(OutputContextPolicy.Build(SurroundOutputMode.WindowsSpatialAudio, allow, true, force)
        .SequenceEqual(new[] { 0x1992, 0, 0x19AC, 0x19AD, 0 }), "Spatial mode preserves configured height and disables HRTF");
}
Check(OutputContextPolicy.Build(SurroundOutputMode.Surround7Point1, true, true, false)
    .SequenceEqual(new[] { 0x1992, 0, 0x19AC, 0x1506, 0 }), "Explicit 7.1 still selects eight channels");
Check(OutputContextPolicy.Build(SurroundOutputMode.StereoHrtf, true, true, true)
    .SequenceEqual(new[] { 0x1992, 1, 0x19AC, 0x19B2, 0x1007, 48000, 0 }), "Headphone HRTF and 48 kHz remain available");
Check(OutputContextPolicy.Build(SurroundOutputMode.StereoHrtf, false, true, true)
    .SequenceEqual(new[] { 0x1992, 0, 0x19AC, 0x19AD, 0 }), "Disabled HRTF setting is respected");
Check(OutputContextPolicy.Build(SurroundOutputMode.WindowsSpatialAudio, false, false, false).SequenceEqual(new[] { 0 }),
    "Missing output extension uses engine-compatible defaults");
Check(!SpatialAudioPolicy.SupportsSpatialBackend("1.1 ALSOFT 1.23.0"), "Bundled old runtime is reported unsupported");
Check(SpatialAudioPolicy.SupportsSpatialBackend("1.1 ALSOFT 1.24.0"), "First spatial backend release is accepted");
Check(SpatialAudioPolicy.SupportsSpatialBackend("1.1 ALSOFT 1.25.2 (git-test)"), "Version suffixes are accepted");
Check(!SpatialAudioPolicy.SupportsSpatialBackend("1.1 vendor 9.0.0"), "Other OpenAL implementations are not assumed compatible");
Check(!SpatialAudioPolicy.SupportsSpatialBackend(null), "Missing runtime is not reported supported");
string State(bool requested = true, bool startup = true, bool supported = true, bool context = true,
    string mode = "Any/Auto", string error = null) => SpatialAudioPolicy.Evaluate(requested, startup, supported, context, mode, error).State;
Check(State(context: false) == "Unavailable", "No context is unavailable");
Check(State(requested: false) == "NotRequested", "Ordinary playback does not claim spatial mode");
Check(State(supported: false) == "SetupRequired", "Old runtime prompts setup");
Check(State(startup: false) == "RestartRequired", "Changing a setting cannot imply startup configuration was loaded");
Check(State(error: "creation failed") == "Fallback", "Context retry remains visible");
foreach (string mode in new[] { "Stereo", "Stereo Basic", "Stereo HRTF", "5.1", "7.1" })
    Check(State(mode: mode) == "HeightUnavailable", "Horizontal output is not reported as height: " + mode);
Check(State() == "Unverified", "ANY is not proof of Atmos or successful spatial activation");
Check(SpatialAudioPolicy.Evaluate(true, true, true, true, "Any/Auto", null, true).State == "StreamActive",
    "Native activation evidence reports stream status without claiming receiver format");
string positiveLog = "Spatial audio stream activated: static mask 0x1ffe\nPost-start: 7.1.4 Surround";
Check(SpatialNativeLog.ConfirmsHeightStream(positiveLog), "Native evidence requires twelve static channels and successful start");
Check(!SpatialNativeLog.ConfirmsHeightStream("Post-start: 7.1.4 Surround"), "A 12-channel wave or PCM backend is not a spatial stream");
Check(!SpatialNativeLog.ConfirmsHeightStream(positiveLog + "\nFailed to start spatial audio stream"), "Start failure defeats an earlier activation message");
string testLog = Path.GetTempFileName();
try
{
    File.WriteAllText(testLog, positiveLog);
    long oldLength = new FileInfo(testLog).Length;
    File.AppendAllText(testLog, "\nPost-start: Stereo");
    Check(!SpatialNativeLog.ReadInitialization(testLog, oldLength), "Prior context's success cannot validate a new fallback context");
}
finally { File.Delete(testLog); }
var origin = new Vector3(100, 40, 200);
var forward = -Vector3.UnitZ;
Check(SpatialTestSignal.WorldPosition(SpatialTestPosition.Above, origin, forward) == origin + Vector3.UnitY * 4,
    "Above is world Y, independent of absolute location");
Check(SpatialTestSignal.WorldPosition(SpatialTestPosition.TopFrontLeft, origin, forward) == origin + new Vector3(-3, 4, -3),
    "Top-front-left orientation matches OpenAL forward -Z");
Check(SpatialTestSignal.WorldPosition(SpatialTestPosition.TopRearRight, origin, Vector3.UnitX) == origin + new Vector3(-3, 4, 3),
    "Turning 90 degrees rotates horizontal test anchors correctly");
Check(SpatialTestSignal.WorldPosition(SpatialTestPosition.Above, origin, Vector3.UnitY) == origin + Vector3.UnitY * 4,
    "Vertical camera direction cannot produce NaN coordinates");
Check(SpatialTestSignal.WorldPosition(SpatialTestPosition.VerticalSweep, origin, forward, 0).Y == 36
    && SpatialTestSignal.WorldPosition(SpatialTestPosition.VerticalSweep, origin, forward, 1).Y == 44,
    "Sweep moves continuously from below to above");
var samples = SpatialTestSignal.BuildSamples();
Check(samples.Length == 384000 && samples.Max(x => Math.Abs((int)x)) <= 3933,
    "Test signal has bounded level and eight-second duration");
Check(samples[0] == 0 && samples.Skip(24000).Take(24000).All(x => x == 0), "Burst edges and silent half-second are preserved");
Check(samples.Take(24000).Any(x => x != 0), "Test signal is audible rather than all silence");
Check(WeatherBedRoutingPolicy.IsAmbientBed("game", "sounds/weather/tracks/rain-leafless.ogg", true, 0, 0, 0)
    && WeatherBedRoutingPolicy.IsAmbientBed("game", "sounds/weather/wind-leafy.ogg", false, 0, 0, 0),
    "Relative and unpositioned rain/wind recordings use speaker-bed routing");
Check(!WeatherBedRoutingPolicy.IsAmbientBed("game", "sounds/weather/lightning-distant.ogg", false, 10, 5, 20),
    "Explicit world-positioned use of a bed asset keeps positional routing");
foreach (string path in new[] { "sounds/weather/rain-mono-1.ogg", "sounds/weather/rain-mono-4.ogg",
    "sounds/weather/lightning-near.ogg", "sounds/weather/lightning-verynear.ogg", "sounds/weather/hail.ogg", "sounds/foliage/leaves-mono-1.ogg" })
    Check(!WeatherBedRoutingPolicy.IsBed("game", path)
        && !WeatherBedRoutingPolicy.IsBed("vintagestorysurroundsound", path),
        "Bed routing excludes emitters and unrelated effects: " + path);
Check(!WeatherBedRoutingPolicy.IsBed("anothermod", "sounds/weather/wind-leafy.ogg"),
    "Bed routing does not capture other mods' identically named assets");
foreach (string path in new[] { "sounds/weather/tracks/hail.ogg", "sounds/weather/tracks/verylowtremble.ogg",
    "sounds/weather/tracks/lowtremble.ogg", "sounds/weather/lowgrumble.ogg", "sounds/weather/lightning-distant.ogg" })
    Check(WeatherBedRoutingPolicy.IsBed("game", path), "Weather bed retains authored channels: " + path);
foreach (string path in new[] { "sounds/weather/hail.wav", "sounds/weather/rumble-low.ogg", "sounds/weather/lightning-distant.ogg",
    "sounds/weather/tracks/rain-surround-canopy.ogg", "sounds/weather/tracks/rain-surround-loud.ogg" })
    Check(WeatherBedRoutingPolicy.IsBed("vintagestorysurroundsound", path), "Replacement bed retains authored channels: " + path);
var levelBasis = ListenerOrientationPolicy.Build(true, MathF.PI, MathF.PI);
var downBasis = ListenerOrientationPolicy.Build(true, MathF.PI * 1.25f, MathF.PI);
var disabledBasis = ListenerOrientationPolicy.Build(false, MathF.PI * 1.25f, MathF.PI);
Check(Vector3.Distance(levelBasis.Forward, -Vector3.UnitZ) < 0.0001f && Vector3.Distance(levelBasis.Up, Vector3.UnitY) < 0.0001f,
    "Vintage Story pitch PI retains an upright listener");
Check(Vector3.Dot(-Vector3.UnitZ, downBasis.Up) > 0.7f, "Looking down raises a fixed source in front relative to the listener");
Check(Vector3.Dot(Vector3.UnitY, downBasis.Forward) < -0.7f, "Looking down places a fixed overhead source behind the listener");
Check(Vector3.Distance(disabledBasis.Forward, -Vector3.UnitZ) < 0.0001f && disabledBasis.Up == Vector3.UnitY,
    "Disabled pitch restores yaw-only orientation");
foreach (float pitch in new[] { MathF.PI / 2, MathF.PI * 0.75f, MathF.PI, MathF.PI * 1.25f, MathF.PI * 1.5f })
{
    var basis = ListenerOrientationPolicy.Build(true, pitch, 0.7f);
    Check(MathF.Abs(basis.Forward.Length() - 1f) < 0.0001f && MathF.Abs(basis.Up.Length() - 1f) < 0.0001f
        && MathF.Abs(Vector3.Dot(basis.Forward, basis.Up)) < 0.0001f,
        "Listener basis remains orthonormal, including vertical view: " + pitch);
}
string localSpatialIni = "[general]\ndrivers = wasapi\nchannels = surround714\n[wasapi]\nspatial-api = true\n";
Check(SpatialStartupConfiguration.HasSpatialSettings(localSpatialIni), "Normal-launch configuration enables spatial startup without a launcher marker");
Check(!SpatialStartupConfiguration.HasSpatialSettings(localSpatialIni + "[wasapi]\nspatial-api = false\n"),
    "A later conventional override defeats earlier spatial settings");
Check(!SpatialStartupConfiguration.HasSpatialSettings(localSpatialIni.Replace("surround714", "surround71")), "Horizontal configuration is not a height request");
Check(!SpatialStartupConfiguration.HasSpatialSettings(localSpatialIni.Replace("drivers = wasapi", "drivers = wave")), "Wave renderer configuration is not a Windows spatial request");
Check(!SpatialStartupConfiguration.HasSpatialSettings(null), "Missing startup configuration is not treated as configured");
Console.WriteLine($"{count} checks passed.");
return 0;
