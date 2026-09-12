# Experimental spatial audio setup

Maintainers: see the [release plan and acceptance gates](spatial-audio-release-plan.md). A prebuilt development add-on can now be generated; it has not been published or qualified as a stable release.

This feature branch adds a 7.1.4 Windows Spatial Audio output path for an Atmos receiver or soundbar with height speakers. It preserves positional mono sounds and existing OpenAL effects. It sends a mixed channel bed, not one dynamic Atmos object per game sound.

## Prebuilt add-on candidate

Maintainers can run `tools/Build-SpatialRelease.ps1` to create a normal mod ZIP, Windows x64 add-on ZIP, matching modified OpenAL source ZIP and checksums under a new `bin/releases` directory. Building requires the developer tools below; using the extracted add-on requires only Windows PowerShell 5.1, which ships with Windows. The combined candidate uses mod version 2.0.0.

Extract the entire add-on and run **Install.cmd** with the game closed. Choose the game/data directories, review the paths and confirm. Setup verifies the packaged files and game version, checks the default spatial endpoint, installs the matching mod and native runtime, and configures normal launch. The game-local `SurroundSpatialSetup` directory retains the original restore point through repeat setup and mod-only updates. Keep that directory and the extracted add-on. **Restore.cmd** restores the original files; **Diagnostic.cmd** and **Conventional.cmd** provide optional comparison/logging. Full instructions are in the add-on's `README.txt`.

An existing developer-prototype installation must first use its original `Restore-LocalSpatialAudio.ps1`. Public setup refuses to treat an already-installed identical spatial DLL as an original backup. Your existing prototype remains usable while testing the new installer separately.

Installer regression checks: `tools/Test-SpatialInstaller.ps1 -AddonPath <add-on-zip>` under Windows PowerShell 5.1. Add `-ProbeDevice` for the hardware preflight. The file-only checks do not launch the game or alter the normal installation.

## Start the isolated game

In PowerShell 7, from the repository:

```powershell
.\tools\Start-SpatialAudio.ps1
```

The first run requires .NET 10 SDK, CMake, Git, Visual Studio 2022 C++ Build Tools and a Windows SDK. It builds the mod and patched OpenAL runtime. Later runs reuse the native build after checking its hash.

The launcher creates `bin/SpatialSandbox`, copies game binaries and built-in mods, shares the installed read-only game assets via a junction, and creates a separate `Data` folder for settings, mods and worlds. Your normal game and data folders are not modified. Use `-GamePath 'C:\path\to\Vintagestory'` to select a different compatible game installation, and use a separate `-Destination` for each game version.

Use `-PrepareOnly` to build and prepare without starting the game. Use `-Conventional` to start the sandbox with ordinary automatic speaker output for comparison. Close the sandbox game before rerunning the launcher. Do not run the copied EXE directly: the launcher supplies the process-specific configuration and logging before audio initialization.

Before starting, select **Dolby Atmos for home theater** on the intended Windows HDMI output, and make that endpoint the Windows default playback device. The launcher uses a silent standalone preflight to verify that a twelve-channel Windows spatial stream can start. A crash, timeout, fallback or missing activation evidence prevents launching the game; its log is retained under `bin/SpatialSandbox/AudioLogs`. This preflight does not change Windows audio settings and does not prove which format the receiver is decoding.

The isolated data folder starts fresh; sign in if the game requests it and create a disposable test world. The launcher enables the mod's debug tools and selects `WindowsSpatialAudio` (saved enum value 8). Use the same default output device in the game's audio settings. If you select a different in-game device, inspect a fresh in-game report for that endpoint rather than relying on the launcher's default-device preflight.

## Install into the normal game for local testing

This requires more than a normal mod ZIP: the installer replaces `Lib/OpenAL32.dll` in the selected game with the patched runtime. Close that game first, then run:

```powershell
.\tools\Install-LocalSpatialAudio.ps1
```

It builds and installs the mod, preserves the other mod settings while selecting spatial output and enabling debug tools, and backs up the previous mod ZIP, native DLL and mod configuration under `VintagestoryData/SurroundSpatialTest/<timestamp>`. Other mods and worlds are retained. The ZIP reads its version from current mod metadata.

**Use your ordinary Vintage Story shortcut or executable.** Setup installs `alsoft.ini` beside the game executable; OpenAL automatically reads it before initializing audio, regardless of the working directory. No machine-wide environment variables or special launch arguments are required. The mod recognizes the game-local configuration and retains the selected spatial mode.

For an existing local test installation, enable this without rebuilding/replacing the runtime:

```powershell
.\tools\Enable-StandardSpatialLaunch.ps1 -InstallationPath '<VintagestoryData>/SurroundSpatialTest/<timestamp>'
```

Close the game first. This adds the config to the existing restore manifest, preserving any original `alsoft.ini` and retaining the original mod/runtime backups. Repeating this setup does not replace the original config backup. The mod update on this branch is needed to recognize startup without the legacy launcher marker.

The desktop **Vintage Story - Spatial Audio Test** shortcut remains optional for diagnostics: it supplies a timestamped native log. The **Vintage Story - Standard Audio** shortcut selects conventional output for comparison, using the same patched runtime. Normal launches lack that native trace, so the spatial panel can show **Unverified** even when playback works: startup configuration was detected, but native activation and receiver format have not been verified for that context. Reports identify the configuration source/path. Explicit `ALSOFT_CONF` overrides take precedence, and settings edited after the process started require a full restart.

To restore the original mod, runtime and settings, close the game and run `pwsh -File '<backup folder>/Restore-LocalSpatialAudio.ps1'`. Restoration saves the latest mod configuration before reverting it and refuses to overwrite a tracked payload changed after installation. It also restores the original game-local OpenAL config, or removes the one setup created. Use the ordinary game shortcut after restoration. This is an experimental local setup, not a self-contained mod release.

The sandbox experienced a stream disconnect after successful initialization. A later standalone five-second receiver probe stayed connected; local gameplay and audible height output still need confirmation.

## Listening checks

Open **F9 → Spatial Audio** in the world. Tests emit quiet, broadband mono bursts for eight seconds, anchored to the starting world position. Starting another spatial test or closing the spatial panel stops the current test.

- Compare Front, Rear, Above and Below while looking around.
- Try the four elevated corner positions and the Below to above sweep.
- Record what you heard with the observation buttons. These observations are linked to the test in the session log.
- Check the receiver's input-format display separately. An Atmos indicator alone does not confirm correct height direction; a source test is not an isolated speaker-channel test.
- Test roof rain, canopy foliage, entities above/below the player, occlusion and reverberation. Compare music/UI and weather beds as well.

7.1.4 has no speakers below the listener. The Below test checks negative-elevation routing; it does not promise a dedicated floor channel.

The stereo upmix stays in the horizontal 7.1 bed when the spatial layout is requested; mono world sources provide actual height information. It does not copy music into ceiling channels.

Rain, wind, hail, storm tremble/rumble and distant-thunder ambient beds retain their original stereo or surround channels. They bypass stereo upmix and use direct horizontal speaker routing, so looking up/down does not rotate them into height speakers. The replacement rain/wind recordings already contain six channels; stereo tremble beds remain stereo. Unmatched channels can be remixed for smaller output layouts. Mono beds keep the game's original behavior. Rain impact emitters, foliage emitters, nearby lightning effects and explicit world-positioned uses of these assets retain positional routing. Changes take effect when sounds are recreated, normally on world load. The hail replacement targets the actual `sounds/weather/tracks/hail.ogg` asset.

Vintage Story 1.22.7 passes only the horizontal view vector to its audio listener (`SystemSoundEngine.OnRenderFrame` explicitly supplies zero for Y). **Output → Follow camera pitch** (`FollowCameraPitch`, default true) optionally applies the full player-view pitch after that update, with a perpendicular listener-up vector. Enabled: looking down moves a fixed front source upward relative to the listener, and a fixed overhead source rearward. Disabled: the original yaw-only listener remains in control. This follows the player-view direction used by the game's sound engine; it does not implement camera roll or a detached third-person/free-camera listener. It works with ordinary OpenAL output as well, though audible elevation depends on the renderer and speakers/headphones.

Save through ConfigLib to apply the setting; the existing config reload rebuilds the audio context. New or missing pitch settings default to on; an explicit saved false value remains off. F9 → Spatial Audio shows its state, and Write report captures `FollowCameraPitch` plus the six current OpenAL `ListenerOrientation` values (forward XYZ, up XYZ) for runtime verification.

The status panel distinguishes missing setup, restart required, horizontal fallback, unverified configuration, and a spatial stream started at initialization. `Any/Auto` in the OpenAL output query can include 7.1.4. `StreamActive` is based on the patched native log for the current game-context initialization; it does not certify receiver format or continued device availability after a disconnection. Write report includes loaded DLL path/version, process config/log paths and the spatial status.

The existing F9 summary panel also exposes a Spatial Audio button. Full paths and extended diagnostics are written to reports rather than squeezed into the spatial panel.

## Automated verification

```powershell
.\tools\Test-SpatialAudio.ps1
.\tools\Test-SpatialAudio.ps1 -ProbeDevice
.\tools\Test-StandardSpatialLaunch.ps1
```

The first command checks context attributes, fallback reporting, source coordinates and signal bounds, then renders front/overhead sources through the actual patched native library to twelve-channel WAV files. It asserts strong overhead-channel localization, the positional pitch toggle, and original-channel weather routing with zero height output and unchanged channel balance when tilted. The second additionally attempts a silent spatial stream on the default Windows endpoint.

Results and native logs are under `bin/spatial-tests/patched`. Hardware tests remain necessary for receiver Atmos indication, perceived direction, latency, dropout handling and actual gameplay.

`Test-StandardSpatialLaunch.ps1` launches a separate probe executable with the config beside it, a different working directory, and no OpenAL environment overrides. It verifies config discovery, native spatial activation and connection after five seconds using a native log callback registered before initialization. It does not launch the game or alter Windows audio settings; evidence is under `bin/spatial-tests/normal-launch-*`.

## Why a patched native library is required

The game's bundled OpenAL Soft 1.23.0 predates the Windows spatial backend. During implementation, the upstream 1.25.2 spatial path also reproduced a native ownership bug on this machine. The feature therefore builds a pinned 1.25.2 source revision with a small allocation/cleanup correction and an activation diagnostic. See [native/README.md](../native/README.md) for the root cause, source patch and rebuild details.

Installing only the mod DLL/ZIP is insufficient for this prototype. The installed runtime plus game-local configuration support normal game launch; the experimental launcher remains useful for native diagnostics. Development archives are generated locally; no mod release, native binary publication or upstream bug submission is included.
