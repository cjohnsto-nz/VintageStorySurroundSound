using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SurroundSoundLab;

internal sealed record SpatialStartupConfiguration(bool Configured, string Path, string Source)
{
    // Snapshot once. Editing a config after OpenAL has initialized must not
    // make the mod report that the new settings were loaded by this process.
    private static readonly Lazy<SpatialStartupConfiguration> Startup = new(Capture);
    internal static SpatialStartupConfiguration Current => Startup.Value;

    private static SpatialStartupConfiguration Capture()
    {
        string explicitPath = Environment.GetEnvironmentVariable("ALSOFT_CONF");
        string path = explicitPath;
        string source = string.IsNullOrWhiteSpace(explicitPath) ? "Game-local alsoft.ini" : "Process ALSOFT_CONF";
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "alsoft.ini");
            path = System.IO.Path.GetFullPath(path);
            using var process = Process.GetCurrentProcess();
            bool presentAtStartup = File.Exists(path) && File.GetLastWriteTimeUtc(path) <= process.StartTime.ToUniversalTime();
            string driver = Environment.GetEnvironmentVariable("ALSOFT_DRIVERS");
            bool driverCompatible = string.IsNullOrWhiteSpace(driver) || driver.Equals("wasapi", StringComparison.OrdinalIgnoreCase);
            bool configured = presentAtStartup && driverCompatible && HasSpatialSettings(File.ReadAllText(path));
            return new(configured, path, source);
        }
        catch { return new(false, path, source); }
    }

    internal static bool HasSpatialSettings(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string section = "general";
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
            int equals = line.IndexOf('=');
            if (equals < 1) continue;
            values[section + "/" + line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }
        bool Is(string key, string expected) => values.TryGetValue(key, out string value)
            && value.Equals(expected, StringComparison.OrdinalIgnoreCase);
        return Is("general/drivers", "wasapi") && Is("general/channels", "surround714") && Is("wasapi/spatial-api", "true");
    }
}
