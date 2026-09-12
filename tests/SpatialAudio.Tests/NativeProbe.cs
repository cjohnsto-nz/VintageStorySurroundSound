using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SurroundSoundLab;

internal static class NativeProbe
{
    internal static int Run(string[] args)
    {
        if (args.Length != 4 || args[0] is not ("--render" or "--spatial-probe" or "--auto-start-probe"))
            throw new ArgumentException("--render|--spatial-probe|--auto-start-probe <OpenAL DLL> <output directory> <Front|Above|PitchOff|PitchOn|WeatherStereo|WeatherStereoTilt|WeatherSurround|WeatherSurroundTilt>");
        string directory = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(directory);
        string config = Path.Combine(directory, "alsoft.ini");
        string log = Path.Combine(directory, "openal.log");
        string wave = Path.Combine(directory, "render.wav");
        bool render = args[0] == "--render";
        bool autoStart = args[0] == "--auto-start-probe";
        string backend = render ? $"[wave]\nfile = {wave}\nbformat = false\n" : "[wasapi]\nspatial-api = true\n";
        if (!autoStart)
        {
            File.WriteAllText(config, "[general]\ndrivers = " + (render ? "wave" : "wasapi")
                + "\nchannels = surround714\nfrequency = 48000\nsample-type = float32\nstereo-encoding = basic\n" + backend);
            Environment.SetEnvironmentVariable("ALSOFT_CONF", config);
            Environment.SetEnvironmentVariable("ALSOFT_LOGFILE", log);
            Environment.SetEnvironmentVariable("ALSOFT_LOGLEVEL", "3");
            Environment.SetEnvironmentVariable("ALSOFT_DRIVERS", render ? "wave" : "wasapi");
        }
        else
        {
            foreach (string name in Environment.GetEnvironmentVariables().Keys)
                if (name.StartsWith("ALSOFT_", StringComparison.OrdinalIgnoreCase) || name == "SURROUNDSOUND_SPATIAL_STARTUP")
                    throw new Exception("Auto-start probe requires an environment without audio overrides: " + name);
            if (!SpatialStartupConfiguration.Current.Configured)
                throw new Exception("Game-local startup configuration was not present before this process started.");
        }
        nint library = NativeLibrary.Load(Path.GetFullPath(args[1]));
        NativeLibrary.SetDllImportResolver(typeof(NativeProbe).Assembly, (name, _, _) => name == "OpenAL32" ? library : 0);
        var capturedLog = new StringBuilder();
        LogCallback callback = null;
        SetLogCallback setCallback = null;
        if (autoStart)
        {
            // Register before the first OpenAL initialization. This captures
            // evidence without setting any logging/config environment variable.
            nint setter = alcGetProcAddress(0, "alsoft_set_log_callback");
            if (setter == 0) throw new Exception("Native diagnostic callback unavailable.");
            setCallback = Marshal.GetDelegateForFunctionPointer<SetLogCallback>(setter);
            callback = (_, _, message, length) =>
            {
                try { lock (capturedLog) capturedLog.AppendLine(Marshal.PtrToStringUTF8(message, length)); }
                catch { /* Never throw through native callback code. */ }
            };
            setCallback(callback, 0);
        }
        nint device = 0, context = 0;
        uint source = 0, buffer = 0;
        string version = null;
        string deviceName = null;
        int mode = 0;
        int connected = 1;
        float[] listenerOrientation = null;
        int inputChannels = 0;
        try
        {
            device = alcOpenDevice(null);
            if (device == 0) throw new Exception("Could not open native output; see " + log);
            int[] attributes = OutputContextPolicy.Build(SurroundOutputMode.WindowsSpatialAudio, true, true, false);
            context = alcCreateContext(device, attributes);
            if (context == 0 || !alcMakeContextCurrent(context)) throw new Exception("Could not create native context; see " + log);
            version = Marshal.PtrToStringAnsi(alGetString(0xB002));
            deviceName = Marshal.PtrToStringAnsi(alcGetString(device, 0x1013));
            alcGetIntegerv(device, 0x19AC, 1, out mode);
            if (render)
            {
                bool weather = args[3].StartsWith("Weather", StringComparison.Ordinal);
                bool stereoWeather = args[3].StartsWith("WeatherStereo", StringComparison.Ordinal);
                bool pitchTest = args[3].StartsWith("Pitch", StringComparison.Ordinal);
                Vector3 position = weather ? Vector3.Zero
                    : SpatialTestSignal.WorldPosition(pitchTest ? SpatialTestPosition.Front : Enum.Parse<SpatialTestPosition>(args[3]), Vector3.Zero, -Vector3.UnitZ);
                if (pitchTest || args[3].EndsWith("Tilt", StringComparison.Ordinal))
                {
                    var basis = ListenerOrientationPolicy.Build(args[3] != "PitchOff", MathF.PI * 1.25f, MathF.PI);
                    alListenerfv(0x100F, new[] { basis.Forward.X, basis.Forward.Y, basis.Forward.Z, basis.Up.X, basis.Up.Y, basis.Up.Z });
                    listenerOrientation = new float[6];
                    alGetListenerfv(0x100F, listenerOrientation);
                }
                alGenSources(1, out source);
                alGenBuffers(1, out buffer);
                short[] samples = SpatialTestSignal.BuildSamples();
                if (weather)
                {
                    int channels = stereoWeather ? 2 : 6;
                    short[] bed = new short[samples.Length * channels];
                    for (int frame = 0; frame < samples.Length; frame++)
                        for (int channel = 0; channel < channels; channel++)
                            bed[frame * channels + channel] = (short)(samples[frame] * (channel + 1) / 6);
                    samples = bed;
                }
                alBufferData(buffer, weather ? (stereoWeather ? 0x1103 : 0x120B) : 0x1101, samples, samples.Length * 2, 48000);
                alGetBufferi(buffer, 0x2003, out inputChannels); // AL_CHANNELS
                alSourcei(source, 0x1009, (int)buffer);
                alSourcef(source, 0x1021, 0); // no distance attenuation
                if (weather)
                {
                    alSourcei(source, 0x1214, 0); // speaker bed, no positional spatialization
                    alSourcei(source, 0x1033, 2); // direct channels, remix only unmatched channels
                }
                alSource3f(source, 0x1004, position.X, position.Y, position.Z);
                alSourcePlay(source);
                if (alGetError() != 0) throw new Exception("Native source setup failed.");
                Thread.Sleep(1200);
            }
            else
            {
                Thread.Sleep(5000); // Include several device callback periods; initialization alone can pass before a disconnect.
                alcGetIntegerv(device, 0x313, 1, out connected); // ALC_CONNECTED (ALC_EXT_disconnect)
            }
        }
        finally
        {
            if (source != 0) { alSourceStop(source); alDeleteSources(1, ref source); }
            if (buffer != 0) alDeleteBuffers(1, ref buffer);
            alcMakeContextCurrent(0);
            if (context != 0) alcDestroyContext(context);
            if (device != 0) alcCloseDevice(device);
            setCallback?.Invoke(null, 0);
            GC.KeepAlive(callback);
        }
        if (autoStart) File.WriteAllText(log, capturedLog.ToString());
        string evidence = "";
        if (File.Exists(log))
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            evidence = reader.ReadToEnd();
        }
        bool streamActivated = SpatialNativeLog.ConfirmsHeightStream(evidence);
        object result = render
            ? new { Version = version, OutputMode = mode, Position = args[3], InputChannels = inputChannels, ListenerOrientation = listenerOrientation, Wave = ReadWave(wave), Log = log }
            : new { Version = version, Device = deviceName, OutputMode = mode, Log = log, StreamActivated = streamActivated,
                ConnectedAfterFiveSeconds = connected != 0, NormalLaunchConfiguration = autoStart,
                ConfigPath = autoStart ? SpatialStartupConfiguration.Current.Path : config, ReceiverVerified = false };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(directory, "result.json"), json);
        Console.WriteLine(json);
        return render || (streamActivated && connected != 0) ? 0 : 2;
    }

    private static object ReadWave(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new Exception("Not a RIFF file");
        reader.ReadInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new Exception("Not a WAVE file");
        int channels = 0, bits = 0;
        double[] squares = null;
        long frames = 0;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
            uint length = reader.ReadUInt32();
            long end = reader.BaseStream.Position + length;
            if (chunk == "fmt ")
            {
                reader.ReadUInt16(); channels = reader.ReadUInt16();
                reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            if (chunk == "data")
            {
                if (channels != 12 || bits != 32) throw new Exception($"Expected 12-channel float output, got {channels}/{bits}");
                squares = new double[channels];
                frames = Math.Min(length, reader.BaseStream.Length - reader.BaseStream.Position) / (channels * 4);
                for (long frame = 0; frame < frames; frame++)
                    for (int ch = 0; ch < channels; ch++) { float sample = reader.ReadSingle(); squares[ch] += sample * sample; }
                break;
            }
            reader.BaseStream.Position = end + (length & 1);
        }
        if (squares == null || frames == 0) throw new Exception("No recorded samples");
        double[] rms = squares.Select(x => Math.Sqrt(x / frames)).ToArray();
        return new { Channels = channels, Frames = frames, Rms = rms, HeightEnergy = squares.Skip(8).Sum() / frames,
            BedEnergy = squares.Take(8).Sum() / frames };
    }

    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern nint alcOpenDevice(string name);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern nint alcGetProcAddress(nint device, string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void LogCallback(nint user, byte level, nint message, int length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetLogCallback(LogCallback callback, nint user);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern nint alcCreateContext(nint device, int[] attributes);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool alcMakeContextCurrent(nint context);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alcDestroyContext(nint context);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool alcCloseDevice(nint device);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern nint alGetString(int param);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern nint alcGetString(nint device, int param);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alcGetIntegerv(nint device, int param, int count, out int value);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern int alGetError();
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alListenerfv(int param, float[] values);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alGetListenerfv(int param, [Out] float[] values);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alGenSources(int n, out uint source);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alGenBuffers(int n, out uint buffer);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alBufferData(uint buffer, int format, short[] data, int bytes, int rate);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alGetBufferi(uint buffer, int param, out int value);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alSourcei(uint source, int param, int value);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alSourcef(uint source, int param, float value);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alSource3f(uint source, int param, float x, float y, float z);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alSourcePlay(uint source);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alSourceStop(uint source);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alDeleteSources(int n, ref uint source);
    [DllImport("OpenAL32", CallingConvention = CallingConvention.Cdecl)] private static extern void alDeleteBuffers(int n, ref uint buffer);
}
