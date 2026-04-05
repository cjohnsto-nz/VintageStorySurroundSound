using System;
using System.Collections.Generic;
using System.Threading;
using OpenTK.Audio.OpenAL;
using Vintagestory.API.Client;
using Vintagestory.Client;

namespace SurroundSoundLab;

internal sealed class ChannelTestService : IDisposable
{
    private const int DirectChannelsSoft = 0x1033;
    private static readonly TimeSpan LabOperationCooldown = TimeSpan.FromMilliseconds(350);

    private readonly ICoreClientAPI capi;
    private readonly List<PendingGameContextSound> pendingGameContextSounds = new();
    private readonly object syncRoot = new();
    private readonly object labOperationSyncRoot = new();
    private readonly long cleanupListenerId;

    private bool labOperationInFlight;
    private DateTime lastLabOperationFinishedUtc = DateTime.MinValue;

    public AudioTestResult LastResult { get; private set; }
    public LabContextProbeResult LastLabProbe { get; private set; }

    public event Action<AudioTestResult> TestCompleted;
    public event Action<LabContextProbeResult> LabProbeCompleted;

    public ChannelTestService(ICoreClientAPI capi)
    {
        this.capi = capi;
        cleanupListenerId = capi.Event.RegisterGameTickListener(OnGameTick, 50);
    }

    public bool TryPlaySingleChannelTone(AudioTestContextType contextType, string formatKey, int activeChannelIndex, out string message)
    {
        return contextType switch
        {
            AudioTestContextType.GameContext => TryPlayUsingGameContext(formatKey, activeChannelIndex, out message),
            AudioTestContextType.LabContext => QueueLabContextTest(formatKey, activeChannelIndex, out message),
            AudioTestContextType.PatchedEnginePath => TryPlayUsingPatchedEnginePath(formatKey, activeChannelIndex, out message),
            _ => ThrowUnknownContext(out message)
        };
    }

    public bool QueueLabContextProbe(out string message)
    {
        if (!TryBeginLabOperation(out message))
        {
            return false;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var probe = RunLabContextProbe();
                LastLabProbe = probe;
                SurroundSessionLogWriter.AppendLabProbe(probe);
                capi.Event.EnqueueMainThreadTask(() => LabProbeCompleted?.Invoke(probe), "vintagestorysurroundsound-labprobe");
            }
            finally
            {
                EndLabOperation();
            }
        });

        message = "Lab context probe queued. Wait for it to finish before starting another lab operation.";
        return true;
    }

    public bool RecordSpeakerObservation(string testId, string speakerObserved, out string message)
    {
        if (string.IsNullOrWhiteSpace(testId))
        {
            message = "No recent test is awaiting speaker confirmation.";
            return false;
        }

        if (LastResult != null && LastResult.TestId == testId)
        {
            LastResult.SpeakerObserved = speakerObserved;
        }

        SurroundSessionLogWriter.AppendSpeakerObservation(testId, speakerObserved);
        message = $"Recorded speaker observation: {speakerObserved}.";
        return true;
    }

    public bool ToggleMutedSpeaker(SurroundSpeaker speaker, out string message)
    {
        bool nowMuted = NonMonoChannelMaskController.ToggleSpeaker(speaker);
        TryRefreshLoadedSounds();
        message = nowMuted
            ? $"Muted {speaker} for non-mono buffers. Existing loaded sounds were refreshed."
            : $"Unmuted {speaker} for non-mono buffers. Existing loaded sounds were refreshed.";
        return true;
    }

    public bool ClearMutedSpeakers(out string message)
    {
        NonMonoChannelMaskController.Clear();
        TryRefreshLoadedSounds();
        message = "Cleared non-mono channel mutes and refreshed loaded sounds.";
        return true;
    }

    public string GetMutedSpeakerSummary()
    {
        return NonMonoChannelMaskController.DescribeMutedSpeakers();
    }

    public string WriteSoundAuditSummary()
    {
        return SoundAuditSummaryCollector.WriteCurrentSummary();
    }

    private bool TryPlayUsingGameContext(string formatKey, int activeChannelIndex, out string message)
    {
        if (!TryResolveFormat(formatKey, out var formatInfo, out message))
        {
            return false;
        }

        if (activeChannelIndex < 0 || activeChannelIndex >= formatInfo.Channels)
        {
            message = $"Invalid channel index {activeChannelIndex + 1} for {formatKey}.";
            return false;
        }

        int source = 0;
        int buffer = 0;
        var result = CreateResult(AudioTestContextType.GameContext, formatKey, formatInfo, activeChannelIndex);

        try
        {
            short[] samples = BuildToneBuffer(formatInfo.Channels, activeChannelIndex, formatKey, 48000);
            source = AL.GenSource();
            buffer = AL.GenBuffer();

            AL.BufferData(buffer, (ALFormat)formatInfo.EnumValue, samples, 48000);
            AL.Source(source, ALSourcei.Buffer, buffer);
            AL.Source(source, ALSourceb.Looping, false);

            if (AL.IsExtensionPresent("AL_SOFT_direct_channels"))
            {
                AL.Source(source, (ALSourcei)DirectChannelsSoft, 1);
            }

            AL.SourcePlay(source);
            result.StartedPlayback = true;
            result.AlError = ReadAlError();
            result.Success = string.IsNullOrEmpty(result.AlError);
            result.ErrorMessage = result.Success ? null : $"OpenAL error: {result.AlError}";

            if (result.Success)
            {
                lock (syncRoot)
                {
                    pendingGameContextSounds.Add(new PendingGameContextSound
                    {
                        Buffer = buffer,
                        Source = source,
                        CleanupAfterUtc = DateTime.UtcNow.AddSeconds(1.8)
                    });
                }

                source = 0;
                buffer = 0;
            }
            else
            {
                CleanupSource(source, buffer);
            }
        }
        catch (Exception ex)
        {
            CleanupSource(source, buffer);
            result.ErrorMessage = ex.Message;
            result.AlError = ReadAlError();
            result.Success = false;
        }

        PublishResult(result);
        message = BuildUserMessage(result);
        return result.Success;
    }

    private bool TryPlayUsingPatchedEnginePath(string formatKey, int activeChannelIndex, out string message)
    {
        if (!TryResolveFormat(formatKey, out var formatInfo, out message))
        {
            return false;
        }

        if (activeChannelIndex < 0 || activeChannelIndex >= formatInfo.Channels)
        {
            message = $"Invalid channel index {activeChannelIndex + 1} for {formatKey}.";
            return false;
        }

        int source = 0;
        int buffer = 0;
        var result = CreateResult(AudioTestContextType.PatchedEnginePath, formatKey, formatInfo, activeChannelIndex);

        try
        {
            short[] samples = BuildToneBuffer(formatInfo.Channels, activeChannelIndex, formatKey, 48000);
            source = AL.GenSource();
            buffer = AL.GenBuffer();

            ALFormat engineFormat = AudioOpenAl.GetSoundFormat(formatInfo.Channels, 16);
            result.FormatEnumValue = (int)engineFormat;
            AL.BufferData(buffer, engineFormat, samples, 48000);
            AL.Source(source, ALSourcei.Buffer, buffer);
            AL.Source(source, ALSourceb.Looping, false);

            if (AL.IsExtensionPresent("AL_SOFT_direct_channels"))
            {
                AL.Source(source, (ALSourcei)DirectChannelsSoft, 1);
            }

            AL.SourcePlay(source);
            result.StartedPlayback = true;
            result.AlError = ReadAlError();
            result.Success = string.IsNullOrEmpty(result.AlError);
            result.ErrorMessage = result.Success ? null : $"OpenAL error: {result.AlError}";

            if (result.Success)
            {
                lock (syncRoot)
                {
                    pendingGameContextSounds.Add(new PendingGameContextSound
                    {
                        Buffer = buffer,
                        Source = source,
                        CleanupAfterUtc = DateTime.UtcNow.AddSeconds(1.8)
                    });
                }

                source = 0;
                buffer = 0;
            }
            else
            {
                CleanupSource(source, buffer);
            }
        }
        catch (Exception ex)
        {
            CleanupSource(source, buffer);
            result.ErrorMessage = ex.Message;
            result.AlError = ReadAlError();
            result.Success = false;
        }

        PublishResult(result);
        message = BuildUserMessage(result);
        return result.Success;
    }

    private bool QueueLabContextTest(string formatKey, int activeChannelIndex, out string message)
    {
        if (!TryResolveFormat(formatKey, out var formatInfo, out message))
        {
            return false;
        }

        if (activeChannelIndex < 0 || activeChannelIndex >= formatInfo.Channels)
        {
            message = $"Invalid channel index {activeChannelIndex + 1} for {formatKey}.";
            return false;
        }

        if (!TryBeginLabOperation(out message))
        {
            return false;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var result = RunLabContextTest(formatKey, formatInfo, activeChannelIndex);
                capi.Event.EnqueueMainThreadTask(() => PublishResult(result), "vintagestorysurroundsound-labtest");
            }
            finally
            {
                EndLabOperation();
            }
        });

        message = $"Queued lab-context test for {formatInfo.EnumName} channel {activeChannelIndex + 1}.";
        return true;
    }

    private AudioTestResult RunLabContextTest(string formatKey, (int EnumValue, int Channels, string EnumName, string DeviceName) formatInfo, int activeChannelIndex)
    {
        var result = CreateResult(AudioTestContextType.LabContext, formatKey, formatInfo, activeChannelIndex);
        ALContext previousContext = ALC.GetCurrentContext();
        ALDevice device = ALDevice.Null;
        ALContext context = ALContext.Null;

        try
        {
            device = ALC.OpenDevice(formatInfo.DeviceName);
            result.DeviceName = SafeDeviceName(device, formatInfo.DeviceName);
            context = ALC.CreateContext(device, Array.Empty<int>());
            ALC.MakeContextCurrent(context);

            int source = 0;
            int buffer = 0;
            try
            {
                short[] samples = BuildToneBuffer(formatInfo.Channels, activeChannelIndex, formatKey, 48000);
                source = AL.GenSource();
                buffer = AL.GenBuffer();
                AL.BufferData(buffer, (ALFormat)formatInfo.EnumValue, samples, 48000);
                AL.Source(source, ALSourcei.Buffer, buffer);
                AL.Source(source, ALSourceb.Looping, false);

                if (AL.IsExtensionPresent("AL_SOFT_direct_channels"))
                {
                    AL.Source(source, (ALSourcei)DirectChannelsSoft, 1);
                }

                AL.SourcePlay(source);
                result.StartedPlayback = true;
                result.AlError = ReadAlError();
                result.AlcError = ReadAlcError(device);
                result.Success = string.IsNullOrEmpty(result.AlError) && string.IsNullOrEmpty(result.AlcError);
                if (!result.Success)
                {
                    result.ErrorMessage = $"AL={result.AlError ?? "ok"}, ALC={result.AlcError ?? "ok"}";
                }

                Thread.Sleep(1400);
                CleanupSource(source, buffer);
            }
            finally
            {
                RestorePreviousContext(previousContext);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.AlError = ReadAlError();
            if (device != ALDevice.Null)
            {
                result.AlcError = ReadAlcError(device);
            }
        }
        finally
        {
            if (context != ALContext.Null)
            {
                ALC.DestroyContext(context);
            }

            if (device != ALDevice.Null)
            {
                ALC.CloseDevice(device);
            }

            RestorePreviousContext(previousContext);
        }

        return result;
    }

    private LabContextProbeResult RunLabContextProbe()
    {
        var probe = new LabContextProbeResult
        {
            TimestampUtc = DateTime.UtcNow
        };

        ALContext previousContext = ALC.GetCurrentContext();
        ALDevice device = ALDevice.Null;
        ALContext context = ALContext.Null;

        try
        {
            var baseReport = AudioCapabilityReportWriter.CaptureReport();
            string preferredDevice = baseReport.PlaybackDevice ?? baseReport.DefaultPlaybackDevice;
            device = ALC.OpenDevice(preferredDevice);
            probe.DeviceName = SafeDeviceName(device, preferredDevice);
            context = ALC.CreateContext(device, Array.Empty<int>());
            ALC.MakeContextCurrent(context);

            var report = AudioCapabilityReportWriter.CaptureReport();
            SurroundSessionLogWriter.AppendProbeReport(report, "lab-context");

            foreach (var kvp in report.FormatSupport)
            {
                var format = kvp.Value;
                var formatProbe = new LabFormatProbeResult
                {
                    FormatKey = kvp.Key,
                    FormatEnumName = format.EnumName,
                    FormatEnumValue = format.EnumValue,
                    Channels = format.Channels,
                    Present = format.Present
                };

                if (!format.Present)
                {
                    formatProbe.ErrorMessage = "Format enum not present.";
                    probe.Formats[kvp.Key] = formatProbe;
                    continue;
                }

                int source = 0;
                int buffer = 0;
                try
                {
                    short[] samples = BuildToneBuffer(format.Channels, 0, kvp.Key, 48000);
                    source = AL.GenSource();
                    buffer = AL.GenBuffer();
                    AL.BufferData(buffer, (ALFormat)format.EnumValue, samples, 48000);
                    formatProbe.UploadSucceeded = string.IsNullOrEmpty(ReadAlError());
                    AL.Source(source, ALSourcei.Buffer, buffer);
                    if (AL.IsExtensionPresent("AL_SOFT_direct_channels"))
                    {
                        AL.Source(source, (ALSourcei)DirectChannelsSoft, 1);
                    }
                    AL.SourcePlay(source);
                    formatProbe.PlaybackStarted = true;
                    formatProbe.AlError = ReadAlError();
                    formatProbe.AlcError = ReadAlcError(device);
                }
                catch (Exception ex)
                {
                    formatProbe.ErrorMessage = ex.Message;
                    formatProbe.AlError = ReadAlError();
                    formatProbe.AlcError = ReadAlcError(device);
                }
                finally
                {
                    CleanupSource(source, buffer);
                }

                probe.Formats[kvp.Key] = formatProbe;
            }

            probe.Success = true;
        }
        catch (Exception ex)
        {
            probe.Success = false;
            probe.ErrorMessage = ex.Message;
        }
        finally
        {
            if (context != ALContext.Null)
            {
                ALC.DestroyContext(context);
            }

            if (device != ALDevice.Null)
            {
                ALC.CloseDevice(device);
            }

            RestorePreviousContext(previousContext);
        }

        return probe;
    }

    private void PublishResult(AudioTestResult result)
    {
        LastResult = result;
        SurroundSessionLogWriter.AppendTestRun(result);
        TestCompleted?.Invoke(result);
    }

    private static void CleanupSource(int source, int buffer)
    {
        try
        {
            if (source != 0)
            {
                AL.SourceStop(source);
                AL.DeleteSource(source);
            }

            if (buffer != 0)
            {
                AL.DeleteBuffer(buffer);
            }
        }
        catch
        {
        }
    }

    private void OnGameTick(float dt)
    {
        List<PendingGameContextSound> expired = null;
        lock (syncRoot)
        {
            for (int i = pendingGameContextSounds.Count - 1; i >= 0; i--)
            {
                if (pendingGameContextSounds[i].CleanupAfterUtc > DateTime.UtcNow) continue;
                expired ??= new List<PendingGameContextSound>();
                expired.Add(pendingGameContextSounds[i]);
                pendingGameContextSounds.RemoveAt(i);
            }
        }

        if (expired == null) return;
        foreach (var sound in expired)
        {
            CleanupSource(sound.Source, sound.Buffer);
        }
    }

    private static bool ThrowUnknownContext(out string message)
    {
        message = "Unknown audio test context.";
        return false;
    }

    private static AudioTestResult CreateResult(AudioTestContextType contextType, string formatKey, (int EnumValue, int Channels, string EnumName, string DeviceName) formatInfo, int activeChannelIndex)
    {
        return new AudioTestResult
        {
            TestId = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow,
            ContextType = contextType,
            DeviceName = formatInfo.DeviceName,
            FormatKey = formatKey,
            FormatEnumName = formatInfo.EnumName,
            FormatEnumValue = formatInfo.EnumValue,
            ChannelIndex = activeChannelIndex,
            Channels = formatInfo.Channels
        };
    }

    private static bool TryResolveFormat(string formatKey, out (int EnumValue, int Channels, string EnumName, string DeviceName) formatInfo, out string message)
    {
        formatInfo = default;
        message = null;

        var report = AudioCapabilityReportWriter.CaptureReport();
        if (report.FormatSupport == null || !report.FormatSupport.TryGetValue(formatKey, out var support))
        {
            message = $"Unknown format '{formatKey}'.";
            return false;
        }

        if (!support.Present)
        {
            message = $"{support.EnumName} is not available on this device.";
            return false;
        }

        formatInfo = (support.EnumValue, support.Channels, support.EnumName, report.PlaybackDevice ?? report.DefaultPlaybackDevice);
        return true;
    }

    private static short[] BuildToneBuffer(int channels, int activeChannelIndex, string formatKey, int sampleRate)
    {
        var tone = GetToneSpec(formatKey, activeChannelIndex);
        int frameCount = (int)(sampleRate * tone.DurationSeconds);
        var data = new short[frameCount * channels];
        double twoPi = Math.PI * 2.0;

        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            double t = frameIndex / (double)sampleRate;
            short sample = (short)(Math.Sin(twoPi * tone.FrequencyHz * t) * short.MaxValue * tone.Amplitude);
            data[(frameIndex * channels) + activeChannelIndex] = sample;
        }

        return data;
    }

    private static (double FrequencyHz, float DurationSeconds, double Amplitude) GetToneSpec(string formatKey, int activeChannelIndex)
    {
        if (formatKey == "5.1-16" && activeChannelIndex == 3)
        {
            return (45.0, 1.6f, 0.6);
        }

        return (523.25, 0.85f, 0.28);
    }

    private static string BuildUserMessage(AudioTestResult result)
    {
        if (!result.Success)
        {
            return $"[{result.ContextType}] Test failed: {result.ErrorMessage ?? result.AlError ?? "unknown error"}";
        }

        return $"[{result.ContextType}] Played {result.FormatEnumName} channel {result.ChannelIndex + 1}.";
    }

    private static string ReadAlError()
    {
        var error = AL.GetError();
        return error == ALError.NoError ? null : AL.GetErrorString(error);
    }

    private static string ReadAlcError(ALDevice device)
    {
        var error = ALC.GetError(device);
        return error == AlcError.NoError ? null : error.ToString();
    }

    private static string SafeDeviceName(ALDevice device, string fallback)
    {
        try
        {
            return device != ALDevice.Null ? ALC.GetString(device, AlcGetString.DeviceSpecifier) : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static void RestorePreviousContext(ALContext previousContext)
    {
        try
        {
            ALC.MakeContextCurrent(previousContext);
        }
        catch
        {
        }
    }

    private static void TryRefreshLoadedSounds()
    {
        try
        {
            LoadedSoundNative.ChangeOutputDevice(() => { });
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        capi.Event.UnregisterGameTickListener(cleanupListenerId);
        lock (syncRoot)
        {
            foreach (var sound in pendingGameContextSounds)
            {
                CleanupSource(sound.Source, sound.Buffer);
            }

            pendingGameContextSounds.Clear();
        }
    }

    private bool TryBeginLabOperation(out string message)
    {
        lock (labOperationSyncRoot)
        {
            if (labOperationInFlight)
            {
                message = "A lab-context operation is already running. Wait for it to finish.";
                return false;
            }

            var remainingCooldown = LabOperationCooldown - (DateTime.UtcNow - lastLabOperationFinishedUtc);
            if (remainingCooldown > TimeSpan.Zero)
            {
                message = $"Lab context is cooling down. Try again in {Math.Ceiling(remainingCooldown.TotalMilliseconds)} ms.";
                return false;
            }

            labOperationInFlight = true;
            message = null;
            return true;
        }
    }

    private void EndLabOperation()
    {
        lock (labOperationSyncRoot)
        {
            labOperationInFlight = false;
            lastLabOperationFinishedUtc = DateTime.UtcNow;
        }
    }

    private sealed class PendingGameContextSound
    {
        public int Source { get; set; }
        public int Buffer { get; set; }
        public DateTime CleanupAfterUtc { get; set; }
    }
}
