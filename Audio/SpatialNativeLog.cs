using System;
using System.IO;

namespace SurroundSoundLab;

internal static class SpatialNativeLog
{
    internal static long Length(string path)
    {
        try { return string.IsNullOrEmpty(path) ? 0 : new FileInfo(path).Length; }
        catch (Exception) { return 0; }
    }

    internal static bool ConfirmsHeightStream(string text) => text != null
        && text.Contains("Spatial audio stream activated: static mask 0x1ffe")
        && text.Contains("Post-start: 7.1.4 Surround")
        && !text.Contains("Failed to start spatial audio stream");

    internal static bool ReadInitialization(string path, long startOffset)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (startOffset > stream.Length) return false;
            stream.Position = Math.Max(startOffset, stream.Length - 65536);
            using var reader = new StreamReader(stream);
            return ConfirmsHeightStream(reader.ReadToEnd());
        }
        catch (Exception) { return false; }
    }
}
