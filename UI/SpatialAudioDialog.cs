using System;
using Vintagestory.API.Client;

namespace SurroundSoundLab;

internal sealed class SpatialAudioDialog : GuiDialog
{
    private readonly ChannelTestService service;
    private GuiElementDynamicText status;
    private GuiElementDynamicText feedback;
    public override string ToggleKeyCombinationCode => null;

    public SpatialAudioDialog(ICoreClientAPI capi, ChannelTestService service) : base(capi)
    {
        this.service = service;
    }

    public override void OnGuiOpened()
    {
        ClearComposers();
        var background = ElementStdBounds.DialogBackground().WithFixedPadding(20);
        var composer = capi.Gui.CreateCompo("vintagestorysurroundsound.spatial", ElementStdBounds.AutosizedMainDialog)
            .AddShadedDialogBG(background)
            .AddDialogTitleBar("Spatial Audio (experimental)", () => TryClose())
            .BeginChildElements(background)
            .AddDynamicText(BuildStatus(), CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 0, 660, 195), "status")
            .AddStaticText("Quiet, 8-second mono tests anchored in the world. Look around while listening.\nThese test directions, not isolated speaker channels. Above does not prove Atmos.",
                CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 205, 660, 60));
        var positions = Enum.GetValues<SpatialTestPosition>();
        for (int i = 0; i < positions.Length; i++)
        {
            SpatialTestPosition target = positions[i];
            composer.AddSmallButton(Label(target), () => Play(target), ElementBounds.Fixed((i % 3) * 220, 275 + (i / 3) * 42, 210, 32));
        }
        composer.AddSmallButton("Stop test", () => { service.StopSpatialTests(); return true; }, ElementBounds.Fixed(0, 407, 210, 32))
            .AddSmallButton("Refresh status", () => { status.SetNewText(BuildStatus()); return true; }, ElementBounds.Fixed(220, 407, 210, 32))
            .AddSmallButton("Write report", WriteReport, ElementBounds.Fixed(440, 407, 210, 32));
        string[] observations = { "Overhead", "EarLevel", "Below", "WrongDirection", "NoOutput" };
        for (int i = 0; i < observations.Length; i++)
        {
            string observation = observations[i];
            composer.AddSmallButton("Heard: " + observation, () => Observe(observation),
                ElementBounds.Fixed((i % 3) * 220, 455 + (i / 3) * 42, 210, 32));
        }
        SingleComposer = composer.AddDynamicText("", CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 545, 660, 65), "feedback")
            .EndChildElements().Compose();
        status = composer.GetDynamicText("status");
        feedback = composer.GetDynamicText("feedback");
        base.OnGuiOpened();
    }

    private string BuildStatus()
    {
        var report = AudioCapabilityReportWriter.CaptureReport();
        var spatial = report.SpatialAudio;
        return $"OpenAL: {report.OpenAlVersion ?? "unavailable"}\n"
            + $"Output query: {report.ActualOutputMode} (Any/Auto can include height)\n"
            + $"Spatial status: {spatial.State}\n{spatial.Detail}\n"
            + $"Follow camera pitch: {(report.FollowCameraPitch ? "On" : "Off")}. Listener orientation is in Write report.\n"
            + "For Atmos speakers: select Dolby Atmos for home theater in Windows.\n"
            + "The receiver's input format and audible height must be checked separately.";
    }

    private bool Play(SpatialTestPosition target)
    {
        service.TryPlaySpatialTest(target, out string message);
        feedback.SetNewText(message);
        return true;
    }

    private bool WriteReport()
    {
        string path = AudioCapabilityReportWriter.WriteReport(capi.Logger);
        feedback.SetNewText("Report written:\n" + path);
        return true;
    }

    private bool Observe(string observation)
    {
        string testId = service.LastResult?.SpatialTest != null ? service.LastResult.TestId : null;
        service.RecordSpeakerObservation(testId, observation, out string message);
        feedback.SetNewText(message);
        return true;
    }

    private static string Label(SpatialTestPosition target) => target switch
    {
        SpatialTestPosition.TopFrontLeft => "Top front left",
        SpatialTestPosition.TopFrontRight => "Top front right",
        SpatialTestPosition.TopRearLeft => "Top rear left",
        SpatialTestPosition.TopRearRight => "Top rear right",
        SpatialTestPosition.VerticalSweep => "Below to above",
        _ => target.ToString()
    };

    public override void OnGuiClosed()
    {
        service.StopSpatialTests();
        base.OnGuiClosed();
    }
}
