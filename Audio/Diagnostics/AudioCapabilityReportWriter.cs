using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTK.Audio.OpenAL;
using Vintagestory.API.Common;

namespace SurroundSoundLab;

internal static class AudioCapabilityReportWriter
{
    private const int AlcAllDevicesSpecifier = 0x1013;
    private const int AlcDefaultAllDevicesSpecifier = 0x1012;

    public static AudioCapabilityReport CaptureReport()
    {
        ALContext currentContext = ALC.GetCurrentContext();
        ALDevice currentDevice = currentContext != ALContext.Null ? ALC.GetContextsDevice(currentContext) : ALDevice.Null;
        bool hasCurrentContext = currentContext != ALContext.Null && currentDevice != ALDevice.Null;

        return new AudioCapabilityReport
        {
            CreatedAtUtc = DateTime.UtcNow,
            HasCurrentContext = hasCurrentContext,
            ContextStatus = hasCurrentContext ? "Current OpenAL context available." : "No current OpenAL context is active on this thread.",
            OpenAlVersion = hasCurrentContext ? SafeGet(() => AL.Get(ALGetString.Version)) : null,
            OpenAlVendor = hasCurrentContext ? SafeGet(() => AL.Get(ALGetString.Vendor)) : null,
            OpenAlRenderer = hasCurrentContext ? SafeGet(() => AL.Get(ALGetString.Renderer)) : null,
            PlaybackDevice = hasCurrentContext ? SafeGet(() => ALC.GetString(currentDevice, AlcGetString.DeviceSpecifier)) : null,
            DefaultPlaybackDevice = SafeGet(() => ALC.GetString(ALDevice.Null, (AlcGetString)AlcDefaultAllDevicesSpecifier)),
            RequestedOutputMode = AudioOpenAlInitContextPatch.LastRequestedOutputMode,
            ActualOutputMode = hasCurrentContext ? AudioOutputModeHelper.ReadCurrentOutputMode(currentDevice) : "Unavailable",
            PlaybackDevices = ReadPlaybackDevices(),
            AlExtensions = hasCurrentContext ? ReadAlExtensions() : Array.Empty<string>(),
            AlcExtensions = hasCurrentContext ? ReadAlcExtensions(currentDevice) : Array.Empty<string>(),
            KnownExtensionChecks = new Dictionary<string, bool>
            {
                ["AL_EXT_MCFORMATS"] = hasCurrentContext && AL.IsExtensionPresent("AL_EXT_MCFORMATS"),
                ["ALC_SOFT_HRTF"] = hasCurrentContext && ALC.IsExtensionPresent(currentDevice, "ALC_SOFT_HRTF"),
                ["ALC_ENUMERATE_ALL_EXT"] = ALC.IsExtensionPresent(ALDevice.Null, "ALC_ENUMERATE_ALL_EXT"),
                ["AL_SOFT_direct_channels"] = hasCurrentContext && AL.IsExtensionPresent("AL_SOFT_direct_channels"),
                ["AL_SOFT_direct_channels_remix"] = hasCurrentContext && AL.IsExtensionPresent("AL_SOFT_direct_channels_remix"),
                ["AL_SOFT_source_resampler"] = hasCurrentContext && AL.IsExtensionPresent("AL_SOFT_source_resampler"),
                ["AL_SOFT_source_spatialize"] = hasCurrentContext && AL.IsExtensionPresent("AL_SOFT_source_spatialize")
            },
            ContextAttributes = hasCurrentContext ? ReadContextAttributes(currentDevice) : null,
            FormatSupport = ProbeFormats()
        };
    }

    public static string WriteReport(ILogger logger)
    {
        string logDir = GetLogDir();
        Directory.CreateDirectory(logDir);

        var report = CaptureReport();
        string filePath = Path.Combine(logDir, $"audio-capabilities-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(filePath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return filePath;
    }

    public static string GetLogDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VintagestoryData",
            "Logs",
            "VintageStorySurroundSound"
        );
    }

    private static string SafeGet(Func<string> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }

    private static string[] ReadAlExtensions()
    {
        try
        {
            var extensions = AL.Get(ALGetString.Extensions);
            if (string.IsNullOrWhiteSpace(extensions)) return Array.Empty<string>();
            return extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string[] ReadAlcExtensions(ALDevice device)
    {
        try
        {
            var extensions = ALC.GetString(device, AlcGetString.Extensions);
            if (string.IsNullOrWhiteSpace(extensions)) return Array.Empty<string>();
            return extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string[] ReadPlaybackDevices()
    {
        try
        {
            return ALC.GetString((AlcGetStringList)AlcAllDevicesSpecifier)
                .Where(device => !string.IsNullOrWhiteSpace(device))
                .OrderBy(device => device)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static ContextAttributesReport ReadContextAttributes(ALDevice device)
    {
        try
        {
            var attributes = ALC.GetContextAttributes(device);
            return new ContextAttributesReport
            {
                Frequency = attributes.Frequency ?? 0,
                MonoSources = attributes.MonoSources ?? 0,
                StereoSources = attributes.StereoSources ?? 0,
                Refresh = attributes.Refresh ?? 0,
                Sync = attributes.Sync ?? false
            };
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, FormatSupportReport> ProbeFormats()
    {
        var probeTargets = new Dictionary<string, (string EnumName, int Channels)>
        {
            ["mono16"] = ("AL_FORMAT_MONO16", 1),
            ["stereo16"] = ("AL_FORMAT_STEREO16", 2),
            ["quad16"] = ("AL_FORMAT_QUAD16", 4),
            ["5.1-16"] = ("AL_FORMAT_51CHN16", 6),
            ["6.1-16"] = ("AL_FORMAT_61CHN16", 7),
            ["7.1-16"] = ("AL_FORMAT_71CHN16", 8)
        };

        var result = new Dictionary<string, FormatSupportReport>();
        foreach (var entry in probeTargets)
        {
            int enumValue = 0;
            bool present = false;
            try
            {
                enumValue = AL.GetEnumValue(entry.Value.EnumName);
                present = enumValue != 0;
            }
            catch
            {
            }

            result[entry.Key] = new FormatSupportReport
            {
                EnumName = entry.Value.EnumName,
                EnumValue = enumValue,
                Channels = entry.Value.Channels,
                Present = present
            };
        }

        return result;
    }
}

internal sealed class AudioCapabilityReport
{
    public DateTime CreatedAtUtc { get; set; }
    public bool HasCurrentContext { get; set; }
    public string ContextStatus { get; set; }
    public string OpenAlVersion { get; set; }
    public string OpenAlVendor { get; set; }
    public string OpenAlRenderer { get; set; }
    public string PlaybackDevice { get; set; }
    public string DefaultPlaybackDevice { get; set; }
    public string RequestedOutputMode { get; set; }
    public string ActualOutputMode { get; set; }
    public string[] PlaybackDevices { get; set; }
    public string[] AlExtensions { get; set; }
    public string[] AlcExtensions { get; set; }
    public Dictionary<string, bool> KnownExtensionChecks { get; set; }
    public ContextAttributesReport ContextAttributes { get; set; }
    public Dictionary<string, FormatSupportReport> FormatSupport { get; set; }
}

internal sealed class ContextAttributesReport
{
    public int Frequency { get; set; }
    public int Refresh { get; set; }
    public int MonoSources { get; set; }
    public int StereoSources { get; set; }
    public bool Sync { get; set; }
}

internal sealed class FormatSupportReport
{
    public string EnumName { get; set; }
    public int EnumValue { get; set; }
    public int Channels { get; set; }
    public bool Present { get; set; }
}
