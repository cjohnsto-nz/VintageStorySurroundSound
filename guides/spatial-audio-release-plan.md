# Spatial audio release plan

Status: implementation and prebuilt development packaging are available. The Windows PowerShell 5.1 installer has file-based install/restore and failure-recovery tests; final candidate review, clean-machine installation and hardware qualification remain open. This document is the release checklist for the spatial-audio PR. Checking in the implementation does not publish the add-on.

## Release scope and decisions

- Keep one Surround Sound mod. Ordinary surround and the optional pitch setting remain available through the normal mod installation. Windows height output additionally requires the spatial runtime and startup configuration.
- Publish the spatial runtime as an optional, prebuilt Windows x64 add-on. Users must not need Git, .NET SDK, CMake, Visual Studio, PowerShell 7 or a source checkout to install it.
- Treat Vintage Story updates as compatibility boundaries. Qualify named game versions for each release. **Automatic repair, automatic DLL replacement after game updates, and compatibility across untested game versions are out of scope.** A changed or unsupported installation gets a clear explanation and manual setup instructions.
- Initially qualify Windows x64 and Vintage Story 1.22.7, the installed version used for development. The 2.0.0 mod now declares that minimum game version, and the add-on accepts exactly 1.22.7. Expand declared support only after testing other versions. No 1.21, Linux or macOS spatial support is planned for this release.
- Describe the feature as experimental Windows Spatial Audio with 7.1.4 height output. The implementation supplies a static channel bed to Windows; it is not encoded-file passthrough, a Dolby encoder bundled with the mod, or one dynamic Atmos object per game source. Verify Atmos on the receiver separately.
- Keep `OutputMode=Auto` as the normal output default. From 2.0.0-dev.1, `FollowCameraPitch=true` is the default for new/missing settings; an explicit saved false remains respected. The spatial installer explicitly selects spatial output.
- The maintainer selected **2.0.0** for the combined spatial/installer and Bell-occlusion update. Mod metadata and assembly versions are updated together. The maintainer has authorized **2.0.0-dev.1** as a GitHub prerelease for external testing. The package builder supports exact prerelease names from a clean commit; stable promotion still requires the remaining review and hardware checks.
- Version the native dependency separately: initially OpenAL Soft `1.25.2+surround-spatial1`, with source, patch, build recipe and resulting binary hashes. Mod-only changes should not require reinstalling an unchanged native runtime.

## What is already implemented

- [x] Height-preserving OpenAL context selection, runtime/setup diagnostics and conventional output modes.
- [x] Native spatial-activation ownership fix and pinned source build.
- [x] World-positioned spatial test panel and opt-in pitch tilt with an orthonormal listener basis.
- [x] Rain, wind, hail, storm tremble/rumble and distant-thunder beds preserve authored speaker channels, bypass stereo upmix and remain independent of camera pitch; ground emitters retain world positioning.
- [x] Local setup, dedicated launcher, backup and restore scripts used successfully on the maintainer's normal installation.
- [x] 71 managed checks, native 12-channel rendering tests, short receiver activation/connection probes and local installation verification.

See [validation record](spatial-audio-validation.md) for evidence and limits. User-reported initial playback success is not a substitute for the remaining hardware tests. The weather routing correction still needs listening confirmation after the elevated prototype produced audible phasing.

## Stage 1 — prepare a reviewable candidate

Owner: maintainer / implementation PR.

- [ ] Review the feature together with this plan. The branch starts at `50fab2c` on `feature/sound-pos-tracking`; four existing commits after `origin/main` provide the 1.2.3 config, assets and weather-override baseline. The PR against `main` includes that dependency explicitly.
- [ ] Resolve review findings without mixing in unrelated projects or runtime binaries.
- [ ] Confirm the first candidate's exact mod version and game compatibility list. Update `modinfo.json`, any meaningful assembly version, filenames and release notes consistently. Read versions from metadata in packaging scripts rather than hard-coding 1.2.3.
- [ ] Preserve enum values and old configurations. Verify missing pitch configuration resolves to true and explicit false remains respected.
- [ ] Record the candidate commit and the exact game SDK, architecture and runtime revision used to build it.

Exit: a fixed candidate commit, reviewed feature scope and exact version metadata. The initial PR stays draft while public setup and hardware acceptance remain incomplete.

## Stage 2 — build user-facing artifacts

Owner: packaging implementation.

| Artifact | Required contents | Exclusions |
| --- | --- | --- |
| Normal mod ZIP | Root `modinfo.json`, mod DLL, assets and ConfigLib settings | Game assemblies, native build outputs, logs, credentials and developer tools |
| Windows spatial add-on ZIP | Prebuilt x64 OpenAL DLL, setup entry point, launcher, ordinary-audio option, restore command, user guide and runtime manifest | Game executable, game assets, personal settings and worlds |
| Corresponding native source ZIP | Exact modified source, dated modification notices, patch, complete build recipe and applicable license/notices | Local paths and private build data |
| Checksums / release manifest | SHA-256 of each artifact; candidate commit, game compatibility, mod version and runtime identity | Machine-specific absolute paths |

- [x] Build the mod against an explicitly selected installed SDK. Keep proprietary game dependencies on the maintainer's build machine or an appropriately provisioned private runner; do not put them in published artifacts.
- [x] Build native code from the pinned archive and checked-in patch in a clean build directory. Record compiler, Windows SDK, architecture and build options. Hash the actual deliverable; do not promise identical DLL hashes across different compiler environments.
- [x] Separate build-time scripts from distribution-time setup. `Build-SpatialRelease.ps1` produces a prebuilt add-on containing `distribution/Setup.ps1` and `Install.cmd`. The older `Install-LocalSpatialAudio.ps1` remains a developer-only utility.
- [ ] Final clean-machine check: the selected implementation is `Install.cmd` plus Windows PowerShell 5.1 setup. Automated tests run under 5.1 with developer tools removed from PATH, but a separate machine without those tools still needs testing. Do not require elevation for a writable per-user game install; request it only if the selected protected game directory requires it.
- [x] Generate ZIPs with `/` entry separators and inspect their root layout. Verify every artifact against its release manifest.
- [ ] Retain OpenAL and embedded-component notices and provide corresponding modified source alongside the binary. Audit the complete assembled source/artifact, not only the top-level COPYING file. References: [OpenAL license](https://github.com/kcat/openal-soft/blob/1.25.2/COPYING), [native build notes](../native/README.md).

Exit: downloadable candidate artifacts that install without a source tree or build toolchain.

## Stage 3 — implement and verify setup / restore

Owner: packaging implementation and installer tester.

The intended user flow is: install the normal mod, run spatial setup once, select the HDMI receiver and Dolby Atmos for home theater in Windows, then launch the game normally. A game-local `alsoft.ini` supplies startup settings; a special shortcut is only needed for optional native diagnostic logging. Setup must also support a user who has not installed the mod yet, using the matching packaged candidate ZIP.

- [ ] Locate or allow selection of the game and data directories. Verify Windows x64, the declared game version, writable destination and a closed game. Do not terminate the user's game.
- [ ] Show which game DLL, mod and configuration will change before applying setup. Preserve other mods, saves and settings.
- [x] Capture the original DLL, original matching mod archive and relevant configuration once, before replacement. Keep that original restore point through repeat setup and mod-only updates. The public installer uses one active restore record, retaining original backups through repeat/mod-only updates. It refuses to adopt an identical untracked runtime; developer-prototype users must restore their original installation first.
- [x] Install only verified payloads. Track completed writes and roll back this installation attempt if copying or configuration fails. Test interrupted/partial setup as well as success.
- [x] Implement normal launch through EXE-local `alsoft.ini`, recognize it in the mod, preserve its original restore point, and probe the startup mechanism without audio environment overrides. Included in the prebuilt development add-on.
- [x] Package an optional diagnostic launcher that configures only its game process, including the chosen data path and native logging. Do not alter machine-wide OpenAL settings or require this launcher for everyday use.
- [x] Provide a conventional-audio launch option and a clearly separate full restore option. Conventional launch continues using the patched DLL; full restore returns the original DLL and settings.
- [x] Recognize an already installed identical runtime and leave it in place during a mod-only update. Reject conflicting duplicate mod installations with an actionable explanation.
- [x] Restore the exact selected installation without deleting unrelated files. Retain the latest user config before reverting it. If a file changed since setup, explain the conflict rather than overwriting it blindly.
- [x] Perform a silent backend check and surface unsupported hardware, missing spatial configuration or stream failure clearly. Never equate successful stream activation with confirmed Dolby receiver format.
- [ ] Test ordinary paths, paths containing spaces, a custom data directory, repeat setup, mod-only update, full restore and failure rollback. Future game-version migration or automatic repair is not required.

Exit: install/restore round trip verified against original hashes on a clean test installation, with the game still usable in conventional mode.

## Stage 4 — qualify audio and hardware

Owner: maintainer plus external beta testers with their own receivers/soundbars.

Automated candidate checks:

```powershell
dotnet build .\VintageStorySurroundSound.csproj -c Release -p:GamePath=<selected-game-directory>
.\tools\Test-SpatialAudio.ps1
# Only on a configured spatial endpoint, with the game closed:
.\tools\Test-SpatialAudio.ps1 -ProbeDevice
```

- [ ] Run these checks on the fixed candidate, retain summaries and hashes, and inspect the final ZIP separately from the build output.
- [ ] Add a per-height-channel routing diagnostic distinct from world-positioned sound tests. Existing directional tests intentionally distribute energy across speakers and cannot prove channel isolation.
- [ ] Verify ordinary stereo, 5.1/7.1 and headphone HRTF behavior, plus unsupported-runtime behavior, with the new features disabled.
- [ ] Listen to front/rear/above/corner/sweep sources while stationary and while moving. Verify pitch on/off, including near-vertical views, and capture actual listener orientation from reports.
- [ ] Test all weather beds for stable horizontal routing without phasing while looking up/down, including volume transitions and sheltered/interior conditions. Verify positional rain/foliage emitters, nearby lightning, music, UI, occlusion and reverberation remain appropriate. Bass/LFE is not an isolated directional height test.
- [ ] Record Windows endpoint, spatial provider, HDMI route, receiver/soundbar model, physical speaker layout, reported input format and listening result. Start with the maintainer's NVIDIA HDMI receiver; obtain at least one independent compatible system before calling the add-on broadly usable.
- [ ] Complete at least a one-hour gameplay session, several world unload/reload cycles, pause/resume and device disconnect/reconnect. Document behavior for device loss even if recovery requires restarting the game.
- [ ] Investigate the recorded spatial-stream timeout/reset failure. Stable release requires either a fix or a reproduced, understood limitation with a reliable documented recovery. A five-second probe alone does not close this issue.
- [ ] Recheck the final installed pitch/weather build, not only earlier prototypes. Publish the actual test matrix and keep unsupported configurations out of compatibility claims.

Exit: recorded pass/fail results for the declared target. An experimental beta may carry explicit known limitations; stable promotion requires the blocking runtime and installation issues to be closed.

## Stage 5 — publish the beta, then promote

Owner: maintainer. Publication and merging require the corresponding explicit instruction; raising the implementation PR does not authorize either.

- [ ] Finalize release notes around user-visible behavior, required spatial setup, supported game versions, limitations and removal instructions.
- [ ] Tag the approved candidate and create a GitHub prerelease containing the normal mod, Windows add-on, matching native source and checksums. Use the same candidate commit for all artifacts.
- [ ] Publish the mod ZIP on VS Mod DB when its metadata/version format is confirmed. Link clearly to the optional add-on and setup guide. Do not present the add-on as another ZIP to drop into Mods.
- [ ] Verify the live downloads, hashes, compatibility selection and source links. Reinstall from the downloaded artifacts rather than the build folder.
- [ ] Collect beta feedback with a short report template: game/mod/runtime versions, output device, receiver input format, logs and reproduction steps. Keep passwords, account tokens and world saves out of routine reports.
- [ ] Promote a fixed, qualified candidate to stable. If code changes during beta, rebuild, retest the affected behavior and publish new hashes; do not silently replace an existing version's files.

## Native maintenance

Propose submitting the ownership fix and minimal reproduction upstream as a separate action. Upstream acceptance would allow replacing the local patch with a qualified upstream release later; it is not a prerequisite for an accurately labeled experimental beta. Never substitute a newer native library without rebuilding and validating that exact dependency.

The next release steps are **candidate review, a clean-machine install check and the hardware acceptance record**. The packaged development candidate and automated installer matrix are available through `Build-SpatialRelease.ps1` and `Test-SpatialInstaller.ps1`. Automatic game-update repair is not part of either task.

## Implemented packaging and installer verification

`tools/Build-SpatialRelease.ps1` builds the mod, builds or verifies a pinned native source tree, and produces the normal mod ZIP, Windows add-on ZIP, corresponding modified-source ZIP and SHA-256 list. Artifact manifests record the commit, dirty-tree status, SDK/game version, compiler version and native source/patch/DLL hashes. Candidates remain explicitly marked development builds with mod version 2.0.0; building them does not publish a release.

`distribution/Setup.ps1` defaults to a read-only preview; `Install.cmd` displays the selected paths and requests confirmation before applying setup. It verifies packaged hashes and the declared game version, preserves unrelated configuration fields, retains original backups, serializes setup with a file lock, and journals replacements. An interrupted operation is recovered before a subsequent write. Atomic sibling-file replacement avoids partially overwritten game DLLs; rollback and restore validate their saved copies. Device preflight runs in a separate Windows PowerShell process with a 20-second timeout.

`tools/Test-SpatialInstaller.ps1 -AddonPath <zip>` extracts the actual add-on and exercises Windows PowerShell 5.1 with paths containing spaces, custom data, an existing mod with a custom filename, absent original files, repeat setup, mod-only update, duplicate/corrupt input rejection, conflicting edits, a real mid-operation sharing violation, interrupted-operation recovery and exact original hashes after restore. `-ProbeDevice` additionally exercises the installer's packaged preflight. These are file fixtures using the real game executable for version identification, not automated gameplay tests.
