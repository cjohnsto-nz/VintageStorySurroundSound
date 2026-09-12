using Vintagestory.API.Client;
using Vintagestory.API.Common;
using HarmonyLib;
using Vintagestory.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;

namespace SurroundSoundLab;

public class SurroundSoundLabModSystem : ModSystem
{
    private const string ConfigLibConfigSavedEvent = "configlib:vintagestorysurroundsound:config-saved";
    private const string ConfigLibConfigReloadEvent = "configlib:config-reload";

    private ICoreAPI api;
    private ICoreClientAPI clientApi;
    private Harmony harmony;
    private ChannelTestService testService;
    private SurroundDebugDialog debugDialog;
    private EntitySoundOcclusionDebugRenderer entitySoundOcclusionDebugRenderer;
    private EntitySoundPosTrackingDebugRenderer entitySoundPosTrackingDebugRenderer;
    private LeafRustleEmitterSystem leafRustleEmitterSystem;
    private LeafRustleDebugRenderer leafRustleDebugRenderer;
    private RainEmitterSystem rainEmitterSystem;
    private RainEmitterDebugRenderer rainEmitterDebugRenderer;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        this.api = api;
        SurroundSoundLabConfigManager.Load(api, Mod.Logger);
        RegisterConfigReloadListeners(api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        clientApi = api;
        harmony = new Harmony("vintagestorysurroundsound.audioopenal");
        harmony.PatchAll();
        CustomSoundRegistry.Register(api, Mod.Logger);
        api.Input.RegisterHotKey("vintagestorysurroundsound.toggledebug", "Surround Sound: Toggle Debug Panel", GlKeys.F9, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("vintagestorysurroundsound.toggledebug", OnToggleDebugPanel);
        ApplyRuntimeConfig();
    }

    private void RegisterConfigReloadListeners(ICoreAPI api)
    {
        api.Event.RegisterEventBusListener(OnConfigLibConfigSaved, filterByEventName: ConfigLibConfigSavedEvent);
        api.Event.RegisterEventBusListener(OnConfigLibConfigReload, filterByEventName: ConfigLibConfigReloadEvent);
    }

    private void OnConfigLibConfigSaved(string eventName, ref EnumHandling handling, IAttribute data)
    {
        if (!IsOwnConfigEvent(data))
        {
            return;
        }

        ReloadAndApplyConfig();
    }

    private void OnConfigLibConfigReload(string eventName, ref EnumHandling handling, IAttribute data)
    {
        if (!IsOwnConfigEvent(data))
        {
            return;
        }

        ReloadAndApplyConfig();
    }

    private bool IsOwnConfigEvent(IAttribute data)
    {
        return (data as ITreeAttribute)?.GetAsString("domain") == Mod.Info.ModID;
    }

    private void ReloadAndApplyConfig()
    {
        if (api == null)
        {
            return;
        }

        SurroundSoundLabConfigManager.Load(api, Mod.Logger);

        if (clientApi != null)
        {
            ApplyRuntimeConfig();
        }
    }

    private void ApplyRuntimeConfig()
    {
        if (clientApi == null)
        {
            return;
        }

        SurroundSoundLabConfig config = SurroundSoundLabConfigManager.Current;

        SoundOcclusion.Initialize(clientApi);

        if (config.EffectiveEnableEntitySoundPosTracking)
        {
            EntitySoundPosTrackingController.Initialize(clientApi);
        }
        else
        {
            EntitySoundPosTrackingController.Dispose();
        }

        if (config.ReplaceVanillaWeatherBeds)
        {
            WeatherBedOverrides.Apply(clientApi, Mod.Logger);
        }
        else
        {
            WeatherBedOverrides.Restore(Mod.Logger);
        }

        RecreateGameAudioContext(clientApi);
        RecreateEmitterSystems(config);
        RecreateDebugTools(config);
    }

    private void RecreateEmitterSystems(SurroundSoundLabConfig config)
    {
        DisposeLeafRustleRuntime();
        DisposeRainRuntime();

        if (config.EffectiveEnableExperimentalLeafRustleEmitters)
        {
            leafRustleEmitterSystem = new LeafRustleEmitterSystem(clientApi);
            leafRustleDebugRenderer = new LeafRustleDebugRenderer(clientApi, leafRustleEmitterSystem);
            clientApi.Event.RegisterRenderer(leafRustleDebugRenderer, EnumRenderStage.Opaque, "vintagestorysurroundsound-leafdebug");
        }

        if (config.EffectiveEnableExperimentalRainEmitters)
        {
            rainEmitterSystem = new RainEmitterSystem(clientApi);
            if (config.EnableDebugTools && config.EffectiveShowRainEmitterDebugVisuals)
            {
                rainEmitterDebugRenderer = new RainEmitterDebugRenderer(clientApi, rainEmitterSystem);
                clientApi.Event.RegisterRenderer(rainEmitterDebugRenderer, EnumRenderStage.Opaque, "vintagestorysurroundsound-raindebug");
            }
        }
    }

    private void RecreateDebugTools(SurroundSoundLabConfig config)
    {
        DisposeDebugUiRuntime();
        DisposeEntityDebugRenderers();

        if (!config.EnableDebugTools)
        {
            return;
        }

        if (!config.LiteMode)
        {
            entitySoundOcclusionDebugRenderer = new EntitySoundOcclusionDebugRenderer(clientApi);
            clientApi.Event.RegisterRenderer(entitySoundOcclusionDebugRenderer, EnumRenderStage.Opaque, "vintagestorysurroundsound-entityocclusiondebug");
            entitySoundPosTrackingDebugRenderer = new EntitySoundPosTrackingDebugRenderer(clientApi);
            clientApi.Event.RegisterRenderer(entitySoundPosTrackingDebugRenderer, EnumRenderStage.Opaque, "vintagestorysurroundsound-entitytrackingdebug");
        }

        testService = new ChannelTestService(clientApi);
        debugDialog = new SurroundDebugDialog(clientApi, testService, leafRustleEmitterSystem, rainEmitterSystem);
        clientApi.Gui.RegisterDialog(debugDialog);
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
        DisposeLeafRustleRuntime();
        DisposeRainRuntime();
        DisposeEntityDebugRenderers();
        DisposeDebugUiRuntime();
        EntitySoundPosTrackingController.Dispose();
        SoundOcclusion.Dispose();
        WeatherBedOverrides.Restore(Mod.Logger);
        harmony?.UnpatchAll(harmony.Id);
        clientApi = null;
        base.Dispose();
    }

    private void DisposeLeafRustleRuntime()
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
        leafRustleEmitterSystem = null;
    }

    private void DisposeRainRuntime()
    {
        if (rainEmitterDebugRenderer != null)
        {
            if (clientApi != null)
            {
                clientApi.Event.UnregisterRenderer(rainEmitterDebugRenderer, EnumRenderStage.Opaque);
            }

            rainEmitterDebugRenderer.Dispose();
            rainEmitterDebugRenderer = null;
        }

        rainEmitterSystem?.Dispose();
        rainEmitterSystem = null;
    }

    private void DisposeEntityDebugRenderers()
    {
        if (entitySoundOcclusionDebugRenderer != null)
        {
            if (clientApi != null)
            {
                clientApi.Event.UnregisterRenderer(entitySoundOcclusionDebugRenderer, EnumRenderStage.Opaque);
            }

            entitySoundOcclusionDebugRenderer.Dispose();
            entitySoundOcclusionDebugRenderer = null;
        }

        if (entitySoundPosTrackingDebugRenderer != null)
        {
            if (clientApi != null)
            {
                clientApi.Event.UnregisterRenderer(entitySoundPosTrackingDebugRenderer, EnumRenderStage.Opaque);
            }

            entitySoundPosTrackingDebugRenderer.Dispose();
            entitySoundPosTrackingDebugRenderer = null;
        }
    }

    private void DisposeDebugUiRuntime()
    {
        if (debugDialog != null)
        {
            debugDialog.TryClose();
            if (clientApi?.World is ClientMain clientMain)
            {
                clientMain.UnregisterDialog(debugDialog);
            }

            debugDialog.Dispose();
            debugDialog = null;
        }

        testService?.Dispose();
        testService = null;
    }
}
