using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace SurroundSoundLab;

internal sealed class SurroundDebugDialog : GuiDialog
{
    private readonly ChannelTestService testService;
    private GuiElementDynamicText statusText;
    private GuiElementDynamicText summaryText;
    private AudioCapabilityReport latestReport;
    private AudioTestContextType selectedContext = AudioTestContextType.GameContext;
    private string pendingObservationTestId;

    public override string ToggleKeyCombinationCode => null;
    public override double DrawOrder => 0.2;

    public SurroundDebugDialog(ICoreClientAPI capi, ChannelTestService testService) : base(capi)
    {
        this.testService = testService;
        latestReport = AudioCapabilityReportWriter.CaptureReport();
        testService.TestCompleted += OnTestCompleted;
        testService.LabProbeCompleted += OnLabProbeCompleted;
        Compose();
    }

    public override void OnGuiOpened()
    {
        latestReport = AudioCapabilityReportWriter.CaptureReport();
        Compose();
        base.OnGuiOpened();
    }

    private void Compose()
    {
        ClearComposers();

        ElementBounds bgBounds = ElementStdBounds.DialogBackground().WithFixedPadding(GuiStyle.ElementToDialogPadding, GuiStyle.ElementToDialogPadding);
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog;
        ElementBounds textBounds = ElementBounds.Fixed(0, 0, 760, 360);

        var composer = capi.Gui.CreateCompo("surroundsoundlab.debug", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar("Surround Sound Debug", () => TryClose())
            .BeginChildElements(bgBounds)
            .AddDynamicText(BuildSummaryText(), CairoFont.WhiteSmallText(), textBounds, "summary")
            .AddSmallButton("Refresh Report", () => OnRefreshReport(), ElementStdBounds.MenuButton(5.2f, EnumDialogArea.LeftFixed).WithFixedWidth(180))
            .AddSmallButton("Write Report", () => OnWriteReport(), ElementStdBounds.MenuButton(5.2f).WithFixedWidth(180))
            .AddSmallButton("Probe Lab Context", () => OnProbeLabContext(), ElementStdBounds.MenuButton(5.2f, EnumDialogArea.RightFixed).WithFixedWidth(180))
            .AddSmallButton("Write Audit Summary", () => OnWriteAuditSummary(), ElementStdBounds.MenuButton(5.7f, EnumDialogArea.LeftFixed).WithFixedWidth(180))
            .AddSmallButton("Use Game Context", () => SetContext(AudioTestContextType.GameContext), ElementStdBounds.MenuButton(6.2f, EnumDialogArea.LeftFixed).WithFixedWidth(180))
            .AddSmallButton("Use Lab Context", () => SetContext(AudioTestContextType.LabContext), ElementStdBounds.MenuButton(6.2f).WithFixedWidth(180))
            .AddSmallButton("Use Engine Patch", () => SetContext(AudioTestContextType.PatchedEnginePath), ElementStdBounds.MenuButton(6.2f, EnumDialogArea.RightFixed).WithFixedWidth(180))
            .AddSmallButton("Stereo Left", () => PlayTest("stereo16", 0), ElementStdBounds.MenuButton(7.2f, EnumDialogArea.LeftFixed).WithFixedWidth(160))
            .AddSmallButton("Stereo Right", () => PlayTest("stereo16", 1), ElementStdBounds.MenuButton(7.2f, EnumDialogArea.RightFixed).WithFixedWidth(160))
            .AddSmallButton("5.1 Ch 1", () => PlayTest("5.1-16", 0), ElementStdBounds.MenuButton(8.2f, EnumDialogArea.LeftFixed).WithFixedWidth(140))
            .AddSmallButton("5.1 Ch 2", () => PlayTest("5.1-16", 1), ElementStdBounds.MenuButton(8.2f).WithFixedWidth(140))
            .AddSmallButton("5.1 Ch 3", () => PlayTest("5.1-16", 2), ElementStdBounds.MenuButton(8.2f, EnumDialogArea.RightFixed).WithFixedWidth(140))
            .AddSmallButton("5.1 Ch 4", () => PlayTest("5.1-16", 3), ElementStdBounds.MenuButton(9.2f, EnumDialogArea.LeftFixed).WithFixedWidth(140))
            .AddSmallButton("5.1 Ch 5", () => PlayTest("5.1-16", 4), ElementStdBounds.MenuButton(9.2f).WithFixedWidth(140))
            .AddSmallButton("5.1 Ch 6", () => PlayTest("5.1-16", 5), ElementStdBounds.MenuButton(9.2f, EnumDialogArea.RightFixed).WithFixedWidth(140))
            .AddSmallButton("Mark FL", () => MarkSpeaker("FL"), ElementStdBounds.MenuButton(10.2f, EnumDialogArea.LeftFixed).WithFixedWidth(110))
            .AddSmallButton("Mark FR", () => MarkSpeaker("FR"), ElementStdBounds.MenuButton(10.2f).WithFixedWidth(110))
            .AddSmallButton("Mark FC", () => MarkSpeaker("FC"), ElementStdBounds.MenuButton(10.2f, EnumDialogArea.RightFixed).WithFixedWidth(110))
            .AddSmallButton("Mark LFE", () => MarkSpeaker("LFE"), ElementStdBounds.MenuButton(11.2f, EnumDialogArea.LeftFixed).WithFixedWidth(110))
            .AddSmallButton("Mark SL", () => MarkSpeaker("SL"), ElementStdBounds.MenuButton(11.2f).WithFixedWidth(110))
            .AddSmallButton("Mark SR", () => MarkSpeaker("SR"), ElementStdBounds.MenuButton(11.2f, EnumDialogArea.RightFixed).WithFixedWidth(110))
            .AddSmallButton("No Output", () => MarkSpeaker("NoOutput"), ElementStdBounds.MenuButton(12.2f).WithFixedWidth(140))
            .AddSmallButton("Toggle Mute FL", () => ToggleMutedSpeaker(SurroundSpeaker.FL), ElementStdBounds.MenuButton(13.2f, EnumDialogArea.LeftFixed).WithFixedWidth(150))
            .AddSmallButton("Toggle Mute FR", () => ToggleMutedSpeaker(SurroundSpeaker.FR), ElementStdBounds.MenuButton(13.2f).WithFixedWidth(150))
            .AddSmallButton("Toggle Mute FC", () => ToggleMutedSpeaker(SurroundSpeaker.FC), ElementStdBounds.MenuButton(13.2f, EnumDialogArea.RightFixed).WithFixedWidth(150))
            .AddSmallButton("Toggle Mute LFE", () => ToggleMutedSpeaker(SurroundSpeaker.LFE), ElementStdBounds.MenuButton(14.2f, EnumDialogArea.LeftFixed).WithFixedWidth(150))
            .AddSmallButton("Toggle Mute SL", () => ToggleMutedSpeaker(SurroundSpeaker.SL), ElementStdBounds.MenuButton(14.2f).WithFixedWidth(150))
            .AddSmallButton("Toggle Mute SR", () => ToggleMutedSpeaker(SurroundSpeaker.SR), ElementStdBounds.MenuButton(14.2f, EnumDialogArea.RightFixed).WithFixedWidth(150))
            .AddSmallButton("Clear Mutes", () => ClearMutedSpeakers(), ElementStdBounds.MenuButton(15.2f).WithFixedWidth(150))
            .AddDynamicText("5.1 expected order: FL, FR, FC, LFE, SL, SR", CairoFont.WhiteDetailText(), ElementBounds.Fixed(0, 1160, 760, 30), "status")
            .EndChildElements()
            .Compose();

        SingleComposer = composer;
        summaryText = composer.GetDynamicText("summary");
        statusText = composer.GetDynamicText("status");
    }

    private bool OnRefreshReport()
    {
        latestReport = AudioCapabilityReportWriter.CaptureReport();
        RefreshSummaryText();
        statusText?.SetNewText($"Capability probe refreshed. Session log: {SurroundSessionLogWriter.SessionFilePath}");
        return true;
    }

    private bool OnWriteReport()
    {
        string path = AudioCapabilityReportWriter.WriteReport(capi.Logger);
        latestReport = AudioCapabilityReportWriter.CaptureReport();
        SurroundSessionLogWriter.AppendProbeReport(latestReport, "game-context");
        RefreshSummaryText();
        statusText?.SetNewText($"Wrote report: {path}");
        return true;
    }

    private bool OnProbeLabContext()
    {
        testService.QueueLabContextProbe(out var message);
        statusText?.SetNewText(message);
        return true;
    }

    private bool OnWriteAuditSummary()
    {
        string path = testService.WriteSoundAuditSummary();
        RefreshSummaryText();
        statusText?.SetNewText($"Wrote audit summary: {path}");
        return true;
    }

    private bool SetContext(AudioTestContextType contextType)
    {
        selectedContext = contextType;
        RefreshSummaryText();
        statusText?.SetNewText($"Selected {selectedContext} for playback tests.");
        return true;
    }

    private bool PlayTest(string formatKey, int channelIndex)
    {
        if (testService.TryPlaySingleChannelTone(selectedContext, formatKey, channelIndex, out var message))
        {
            statusText?.SetNewText(message);
        }
        else
        {
            statusText?.SetNewText(message);
        }

        return true;
    }

    private bool MarkSpeaker(string speakerCode)
    {
        if (testService.RecordSpeakerObservation(pendingObservationTestId, speakerCode, out var message))
        {
            pendingObservationTestId = null;
            RefreshSummaryText();
        }

        statusText?.SetNewText(message);
        return true;
    }

    private bool ToggleMutedSpeaker(SurroundSpeaker speaker)
    {
        testService.ToggleMutedSpeaker(speaker, out var message);
        RefreshSummaryText();
        statusText?.SetNewText(message);
        return true;
    }

    private bool ClearMutedSpeakers()
    {
        testService.ClearMutedSpeakers(out var message);
        RefreshSummaryText();
        statusText?.SetNewText(message);
        return true;
    }

    private void OnTestCompleted(AudioTestResult result)
    {
        pendingObservationTestId = result.TestId;
        latestReport = AudioCapabilityReportWriter.CaptureReport();
        RefreshSummaryText();
        statusText?.SetNewText($"{BuildResultLine(result)} Mark the observed speaker if you heard it.");
    }

    private void OnLabProbeCompleted(LabContextProbeResult result)
    {
        RefreshSummaryText();
        statusText?.SetNewText(result.Success
            ? "Lab context probe completed. See summary and session log."
            : $"Lab context probe failed: {result.ErrorMessage}");
    }

    private void RefreshSummaryText()
    {
        summaryText?.SetNewText(BuildSummaryText());
    }

    private string BuildSummaryText()
    {
        var report = latestReport ?? AudioCapabilityReportWriter.CaptureReport();
        var sb = new StringBuilder();

        sb.AppendLine("Capability Probe");
        sb.AppendLine($"Selected context: {selectedContext}");
        sb.AppendLine($"Context status: {report.ContextStatus}");
        sb.AppendLine($"Renderer: {report.OpenAlRenderer ?? "unknown"}");
        sb.AppendLine($"Version: {report.OpenAlVersion ?? "unknown"}");
        sb.AppendLine($"Playback device: {report.PlaybackDevice ?? "unknown"}");
        sb.AppendLine($"Default device: {report.DefaultPlaybackDevice ?? "unknown"}");
        sb.AppendLine($"Config: mode {SurroundSoundLabConfigManager.Current.OutputMode}, sound audit {(SurroundSoundLabConfigManager.Current.EnableSoundAudit ? "enabled" : "disabled")}");
        sb.AppendLine($"Stereo upmix: {(SurroundSoundLabConfigManager.Current.UpmixStereoToSurround ? $"enabled ({SurroundSoundLabConfigManager.Current.StereoUpmixGainDb:0.##} dB)" : "disabled")}");
        sb.AppendLine($"Output mode: requested {report.RequestedOutputMode ?? "unknown"}, actual {report.ActualOutputMode ?? "unknown"}");
        sb.AppendLine($"Session log: {SurroundSessionLogWriter.SessionFilePath ?? "not initialized"}");
        sb.AppendLine($"Last audit summary: {SoundAuditSummaryCollector.LastSummaryFilePath ?? "not written yet"}");
        sb.AppendLine($"Muted non-mono speakers: {testService.GetMutedSpeakerSummary()}");

        if (report.ContextAttributes != null)
        {
            sb.AppendLine($"Context: {report.ContextAttributes.Frequency} Hz, mono {report.ContextAttributes.MonoSources}, stereo {report.ContextAttributes.StereoSources}");
        }

        sb.AppendLine();
        sb.AppendLine("Extension Checks");
        foreach (var kvp in report.KnownExtensionChecks)
        {
            sb.AppendLine($"{kvp.Key}: {kvp.Value}");
        }

        sb.AppendLine();
        sb.AppendLine("Format Probe");
        if (report.FormatSupport != null)
        {
            foreach (var kvp in report.FormatSupport)
            {
                sb.AppendLine($"{kvp.Key}: present={kvp.Value.Present}, enum={kvp.Value.EnumValue}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Last Test");
        sb.AppendLine(testService.LastResult != null ? BuildResultLine(testService.LastResult) : "No test run yet.");

        sb.AppendLine();
        sb.AppendLine("Last Lab Probe");
        sb.AppendLine(BuildLabProbeSummary(testService.LastLabProbe));

        sb.AppendLine();
        sb.AppendLine("Channel 4 uses a low-frequency LFE test tone.");
        sb.AppendLine("Mute buttons only affect newly uploaded non-mono buffers; mono positional sounds are unaffected.");
        sb.AppendLine("Use F9 to reopen this panel.");
        return sb.ToString();
    }

    private static string BuildResultLine(AudioTestResult result)
    {
        string status = result.Success ? "success" : "failed";
        string error = string.IsNullOrWhiteSpace(result.ErrorMessage) ? result.AlError ?? result.AlcError ?? "none" : result.ErrorMessage;
        string observed = result.SpeakerObserved ?? "not recorded";
        return $"{result.ContextType} {result.FormatKey} ch {result.ChannelIndex + 1}: {status}, observed={observed}, error={error}";
    }

    private static string BuildLabProbeSummary(LabContextProbeResult probe)
    {
        if (probe == null) return "No lab probe run yet.";
        if (!probe.Success) return $"Failed: {probe.ErrorMessage}";

        var sb = new StringBuilder();
        sb.Append($"Device {probe.DeviceName ?? "unknown"}");
        foreach (var kvp in probe.Formats)
        {
            var item = kvp.Value;
            sb.Append($" | {kvp.Key}: {(item.UploadSucceeded && string.IsNullOrEmpty(item.AlError) ? "ok" : "fail")}");
        }

        return sb.ToString();
    }
}
