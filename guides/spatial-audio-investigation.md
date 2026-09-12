# Spatial audio and Dolby Atmos feasibility

Investigated 2026-09-12 against Surround Sound 1.2.3, repository commit `50fab2c`, the locally installed Vintage Story assemblies, and OpenAL Soft 1.25.2 source. Target: an Atmos receiver or soundbar with height speakers.

Implementation follow-up: `codex/spatial-audio` now contains the prototype. The receiver's Windows spatial stream and native height rendering have been verified. The unmodified upstream DLL crashed during activation; a local ownership fix is required. See [setup](spatial-audio-setup.md), [validation](spatial-audio-validation.md) and [native patch notes](../native/README.md). The investigation below records the findings before implementation.

## Recommendation

Build a Windows-only experimental output path using a newer OpenAL Soft runtime and its existing Windows Spatial Audio backend. Render the game's positional sounds into a 7.1.4 mix, then let Windows supply Atmos output to the HDMI endpoint. This appears feasible without replacing Vintage Story's sound engine. It is source-verified feasibility, not a completed playback test.

Microsoft supports real-time Atmos encoding through its spatial sound platform; the output endpoint must have spatial sound enabled. Dolby also directs Win32 developers to these APIs. This avoids implementing an Atmos encoder in the mod. [Microsoft platform documentation](https://learn.microsoft.com/en-us/windows/win32/coreaudio/spatial-sound), [Dolby Windows implementation](https://professionalsupport.dolby.com/s/article/Windows-Implementation).

“Passthrough” normally means forwarding an already encoded soundtrack. Vintage Story produces a live PCM sound mix, so the useful feature here is real-time Atmos output with height. There is no existing Atmos bitstream in the current playback path to pass through.

## Findings in this installation and repository

- `C:/Users/chris/AppData/Roaming/Vintagestory/Lib/OpenAL32.dll` reports file/product version **1.23.0**, and contains `1.1 ALSOFT 1.23.0`. An existing capability report from 2026-04-05 independently recorded that runtime and 5.1 output. The old report does not establish the currently connected hardware.
- The Windows Spatial Audio option was introduced in **OpenAL Soft 1.24.0**. The release page currently identifies **1.25.2** as the latest stable release; that is the source baseline used for the proposed experiment. Merely changing the mod config on the bundled 1.23.0 is insufficient. [Upstream changelog](https://github.com/kcat/openal-soft/blob/1.25.2/ChangeLog), [1.25.2 release](https://github.com/kcat/openal-soft/releases/tag/1.25.2).
- `Audio/Patches/AudioOpenAlInitContextPatch.cs` already replaces context initialization. `BuildAttributeList()` supports Auto, stereo/HRTF, quad, 5.1, 6.1 and 7.1. There is no height mode.
- Decompiled `AudioOpenAl.UpdateListener()` forwards XYZ position and a forward vector, using world Y as up. Decompiled `LoadedSoundNative` forwards XYZ source positions to OpenAL and retains gain, pitch, looping, low-pass and reverb handling. Height coordinates are already present in the engine path. Camera pitch and near-vertical orientation still need an in-game audit.
- `Audio/EntitySoundPosTracking.cs` updates XYZ positions. Rain and leaf emitters call `PlaySoundAt()` at actual XYZ locations. These mono sounds are good initial height content; new Atmos sound assets are not required for the first experiment.
- `Audio/Patches/AudioOpenAlGetSoundFormatPatch.cs` handles ordinary multichannel PCM through eight channels. This input-buffer limit does not prevent mono sources from being rendered into a larger output layout.
- `Audio/NonMonoChannelMaskController.cs` only chooses stereo upmix targets through eight channels. Its direct-channel setting applies to its upmixed stereo sources, not universally to every multichannel asset. Audit the actual source behavior rather than treating older investigation notes as the current implementation.

## What the existing backend provides

OpenAL Soft 1.25.2 maps its `surround714` output to twelve Windows static audio objects, including top-front-left/right and top-back-left/right. Stream activation leaves dynamic object counts at zero. Thus the game sources are mixed into a height-capable channel bed before Windows receives them; individual entity identities and trajectories are not forwarded as separate dynamic Atmos objects.

If spatial stream initialization fails, the backend tries ordinary WASAPI playback. Audio continuing to play therefore does not prove Atmos succeeded. These findings come directly from `initSpatial()` and `resetProxy()`. [Pinned WASAPI implementation](https://github.com/kcat/openal-soft/blob/1.25.2/alc/backends/wasapi.cpp).

For this target, a correctly rendered 7.1.4 bed is a useful first deliverable. It can contain directional overhead content. Final reproduction on a particular 5.1.2, 5.1.4, 7.1.4 or soundbar arrangement must be checked on that endpoint.

## Smallest useful experiment

Use an isolated game installation with a compatible 64-bit OpenAL Soft 1.25.2 binary. Verify the library actually loaded by the game, rather than assuming that putting a DLL in the mod ZIP changes native resolution.

Supply this process-specific OpenAL configuration before launching the game:

```ini
[general]
drivers = wasapi
channels = surround714
stereo-encoding = basic

[wasapi]
spatial-api = true
```

The upstream sample documents both the output layout and the spatial API option, which it still labels experimental. [Pinned sample configuration](https://github.com/kcat/openal-soft/blob/1.25.2/alsoftrc.sample).

Keep the mod's output mode **Auto** for the first experiment, with OpenAL HRTF disabled. An explicit 5.1/7.1 context request overrides the configured channel layout. The source reads configuration during one-time library initialization, so changing the INI after startup and recreating a context is insufficient. [Context and initialization implementation](https://github.com/kcat/openal-soft/blob/1.25.2/alc/alc.cpp).

Use an `ALSOFT_CONF` environment variable scoped to the launched process or a game-local `alsoft.ini`. OpenAL supports both. Avoid changing the user's global audio configuration. [Configuration loading](https://github.com/kcat/openal-soft/blob/1.25.2/alc/alconfig.cpp).

Select Dolby Atmos for home theater for the relevant Windows HDMI output. Prefer a direct PC-to-receiver/soundbar connection during the first test to reduce variables; qualify any TV relay path separately. Dolby documents the HDMI/Dolby Access setup. [Dolby setup guidance](https://www.dolby.com/en-gb/gaming/).

## Work needed for a distributable feature

| Area | Concrete change |
| --- | --- |
| Native runtime and startup | Pin and verify a compatible runtime; establish native DLL resolution before engine initialization; provide a reversible game-local installation or launcher; include upstream redistribution notices and required source availability. A normal mod ZIP alone is not yet a demonstrated installation route. |
| Configuration and UI | Add an experimental Windows Spatial Audio mode in `Config/SurroundSoundLabConfig.cs` and the ConfigLib JSON. Distinguish backend choice from speaker layout. Surface restart requirements for native/config changes. |
| Context creation | Adapt `AudioOpenAlInitContextPatch` to preserve the configured height layout and disable OpenAL headphone rendering. Check device/context creation errors and offer a working conventional fallback. |
| Diagnostics | Extend `AudioCapabilityReportWriter` and the debug dialog with loaded DLL path/version, requested backend/layout, spatial activation evidence, endpoint identity and fallback state. Report uncertainty honestly. |
| Existing content | Preserve mono positioning and effects. Define explicit behavior for UI/music, stereo upmix and existing surround weather beds. The current upmixer cannot infer a target when output is reported as Auto; do not mistake that for a stereo-only endpoint. |
| Height tests | Extend `ChannelTestService` with mono sources above, in front, behind and below the listener, plus vertical motion and camera rotation. Use a separate Windows static-object test if exact isolation of each height speaker is needed; a panned mono source need not excite only one speaker. |

There is a diagnostic trap: `Device::getOutputMode1()` returns **Any** for 7.1.4 because the existing output-mode query does not represent that layout. The mod consequently displays “Any/Auto”. Adding a guessed 7.1.4 enum value is not a solution. [Output-mode implementation](https://github.com/kcat/openal-soft/blob/1.25.2/alc/device.cpp).

## Validation and acceptance criteria

1. Confirm the loaded native version/path and capture OpenAL initialization logs demonstrating the spatial path and twelve-channel layout. Do not equate a requested configuration with successful activation.
2. Confirm the receiver identifies Atmos input, then separately verify height content with controlled source positions. An Atmos indicator alone does not demonstrate correct overhead placement.
3. Compare sources at ear level and overhead while turning and looking up/down. Test front/rear reversal, listener/source coordinate conventions and near-vertical camera directions.
4. Test rain on an elevated roof, canopy rustles, entities above/below the player, occlusion and reverb. Check UI/music and surround weather routing as well.
5. Test spatial sound disabled, unsupported endpoint, device change, receiver disconnect, world reload and context recreation. Fallback should be audible and explicitly reported.
6. Measure latency, underruns and CPU with dense sound scenes. Qualify the actual receiver/soundbar and any TV relay path, then repeat on each supported game version.

## Larger alternative: dynamic objects

Forwarding each important sound as a dynamic Windows spatial object requires a custom backend or substantial interception of source playback. Work includes PCM feeding, resampling, timing, listener-relative coordinates, sound lifecycle, source prioritization, effects and a fallback channel bed. Microsoft documents a finite, queryable object budget; Atmos home theater currently lists twelve static plus twenty dynamic objects. The engine still owns attenuation, occlusion and reverberation. [Microsoft resource and renderer guidance](https://learn.microsoft.com/en-us/windows/win32/coreaudio/spatial-sound).

This is a separate project and is unnecessary to establish useful height-channel output first. Likewise, the existing stereo HRTF mode is a headphone route; it does not drive an Atmos receiver's ceiling speakers. Simply enabling an AVR upmixer does not preserve the game's original vertical source coordinates.

## Effort estimate and decision

Engineering estimates, assuming working Atmos hardware is available:

- **1–3 developer days:** isolated runtime/config experiment, source-position tests and evidence capture; success is not guaranteed because the backend is experimental.
- **1–2 additional weeks:** experimental distributable feature with startup handling, settings, diagnostics, fallback and regression testing. Native loading or device-specific failures could expand this.
- **Several weeks or more:** a custom dynamic-object renderer with proper lifecycle/effects handling.

Proceed first with the isolated 7.1.4 Spatial Audio experiment. The result should decide whether this becomes a supported experimental mode or needs a custom backend. This investigation changed documentation only; it did not replace the installed DLL, alter Windows audio settings or claim verified Atmos playback.
