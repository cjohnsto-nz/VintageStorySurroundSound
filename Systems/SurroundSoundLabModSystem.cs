using Vintagestory.API.Client;
using Vintagestory.API.Common;
using HarmonyLib;
using Vintagestory.Client;

namespace SurroundSoundLab;

public class SurroundSoundLabModSystem : ModSystem
{
    private ICoreClientAPI clientApi;
    private Harmony harmony;
    private ChannelTestService testService;
    private SurroundDebugDialog debugDialog;
    private LeafRustleEmitterSystem leafRustleEmitterSystem;
    private LeafRustleDebugRenderer leafRustleDebugRenderer;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        SurroundSoundLabConfigManager.Load(api, Mod.Logger);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        clientApi = api;
        harmony = new Harmony("vintagestorysurroundsound.audioopenal");
        harmony.PatchAll();
        CustomSoundRegistry.Register(api, Mod.Logger);
        if (SurroundSoundLabConfigManager.Current.ReplaceVanillaWeatherBeds)
        {
            WeatherBedOverrides.Apply(api, Mod.Logger);
        }
        RecreateGameAudioContext(api);
        if (SurroundSoundLabConfigManager.Current.EnableExperimentalLeafRustleEmitters)
        {
            leafRustleEmitterSystem = new LeafRustleEmitterSystem(api);
            if (SurroundSoundLabConfigManager.Current.EnableDebugTools && SurroundSoundLabConfigManager.Current.ShowLeafRustleDebugVisuals)
            {
                leafRustleDebugRenderer = new LeafRustleDebugRenderer(api, leafRustleEmitterSystem);
                api.Event.RegisterRenderer(leafRustleDebugRenderer, EnumRenderStage.Opaque, "vintagestorysurroundsound-leafdebug");
            }
        }
        if (SurroundSoundLabConfigManager.Current.EnableDebugTools)
        {
            testService = new ChannelTestService(api);
            debugDialog = new SurroundDebugDialog(api, testService, leafRustleEmitterSystem);
            api.Gui.RegisterDialog(debugDialog);
            api.Input.RegisterHotKey("vintagestorysurroundsound.toggledebug", "Surround Sound: Toggle Debug Panel", GlKeys.F9, HotkeyType.GUIOrOtherControls);
            api.Input.SetHotKeyHandler("vintagestorysurroundsound.toggledebug", OnToggleDebugPanel);
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
        if (leafRustleDebugRenderer != null)
        {
            if (clientApi != null)
            {
                clientApi.Event.UnregisterRenderer(leafRustleDebugRenderer, EnumRenderStage.Opaque);
            }

            leafRustleDebugRenderer.Dispose();
            leafRustleDebugRenderer = null;
        }

        leafRustleEmitterSystem?.Dispose();
        testService?.Dispose();
        harmony?.UnpatchAll(harmony.Id);
        clientApi = null;
        base.Dispose();
    }
}
