using Vintagestory.API.Client;
using Vintagestory.API.Common;
using HarmonyLib;
using Vintagestory.Client;

namespace SurroundSoundLab;

public class SurroundSoundLabModSystem : ModSystem
{
    private Harmony harmony;
    private ChannelTestService testService;
    private SurroundDebugDialog debugDialog;

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        harmony = new Harmony("surroundsoundlab.audioopenal");
        harmony.PatchAll();
        RecreateGameAudioContext(api);
        SurroundSessionLogWriter.InitializeSession();
        testService = new ChannelTestService(api);
        debugDialog = new SurroundDebugDialog(api, testService);
        api.Gui.RegisterDialog(debugDialog);
        api.Input.RegisterHotKey("surroundsoundlab.toggledebug", "Surround Sound Lab: Toggle Debug Panel", GlKeys.F9, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("surroundsoundlab.toggledebug", OnToggleDebugPanel);

        try
        {
            var report = AudioCapabilityReportWriter.CaptureReport();
            SurroundSessionLogWriter.AppendProbeReport(report, "startup");
            string filePath = AudioCapabilityReportWriter.WriteReport(api.Logger);
            api.Logger.Notification("[SurroundSoundLab] Audio capability report written to " + filePath);
            api.Logger.Notification("[SurroundSoundLab] Press F9 for the surround debug panel.");
            api.Logger.Notification("[SurroundSoundLab] Session log: " + SurroundSessionLogWriter.SessionFilePath);
            api.Logger.Notification("[SurroundSoundLab] AudioOpenAl.initContext patch active (requesting multichannel output).");
            api.Logger.Notification("[SurroundSoundLab] AudioOpenAl.GetSoundFormat patch active.");
        }
        catch (System.Exception ex)
        {
            api.Logger.Error("[SurroundSoundLab] Failed to write audio capability report: " + ex);
        }
    }

    private static void RecreateGameAudioContext(ICoreClientAPI api)
    {
        try
        {
            if (ScreenManager.Platform == null)
            {
                return;
            }

            string currentDevice = ScreenManager.Platform.CurrentAudioDevice;
            ScreenManager.Platform.CurrentAudioDevice = currentDevice;
            api.Logger.Notification("[SurroundSoundLab] Recreated game audio context after patch install.");
        }
        catch (System.Exception ex)
        {
            api.Logger.Warning("[SurroundSoundLab] Could not recreate game audio context after patch install: " + ex.Message);
        }
    }

    private bool OnToggleDebugPanel(KeyCombination keyCombination)
    {
        debugDialog?.Toggle();
        return true;
    }

    public override void Dispose()
    {
        testService?.Dispose();
        harmony?.UnpatchAll(harmony.Id);
        base.Dispose();
    }
}
