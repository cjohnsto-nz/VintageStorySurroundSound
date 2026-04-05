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
    private LeafRustleEmitterSystem leafRustleEmitterSystem;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        SurroundSoundLabConfigManager.Load(api, Mod.Logger);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        harmony = new Harmony("vintagestorysurroundsound.audioopenal");
        harmony.PatchAll();
        CustomSoundRegistry.Register(api, Mod.Logger);
        if (SurroundSoundLabConfigManager.Current.ReplaceVanillaWeatherBeds)
        {
            WeatherBedOverrides.Apply(api, Mod.Logger);
        }
        RecreateGameAudioContext(api);
        testService = new ChannelTestService(api);
        if (SurroundSoundLabConfigManager.Current.EnableExperimentalLeafRustleEmitters)
        {
            leafRustleEmitterSystem = new LeafRustleEmitterSystem(api);
        }
        debugDialog = new SurroundDebugDialog(api, testService);
        api.Gui.RegisterDialog(debugDialog);
        api.Input.RegisterHotKey("vintagestorysurroundsound.toggledebug", "Surround Sound: Toggle Debug Panel", GlKeys.F9, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("vintagestorysurroundsound.toggledebug", OnToggleDebugPanel);
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
        }
        catch (System.Exception ex)
        {
            api.Logger.Warning("[VintageStorySurroundSound] Could not recreate game audio context after patch install: " + ex.Message);
        }
    }

    private bool OnToggleDebugPanel(KeyCombination keyCombination)
    {
        debugDialog?.Toggle();
        return true;
    }

    public override void Dispose()
    {
        leafRustleEmitterSystem?.Dispose();
        testService?.Dispose();
        harmony?.UnpatchAll(harmony.Id);
        base.Dispose();
    }
}
