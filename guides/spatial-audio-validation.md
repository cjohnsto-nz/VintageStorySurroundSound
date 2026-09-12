# Spatial audio validation — 2026-09-12

Branch: `codex/spatial-audio`. SDK/game installation: Vintage Story **1.22.7 Stable**, .NET 10, Windows x64.

## Passed

- Release mod build: zero warnings, zero errors.
- 38 managed checks: configuration compatibility, height-preserving context attributes, HRTF behavior, runtime version detection, truthful fallback/activation reporting, context-scoped native log evidence, world-space test positions and bounded test audio.
- Native OpenAL 1.25.2 plus the local ownership patch built with MSVC 19.42 and Windows SDK 10.0.22621. The initial native build emitted one upstream size-conversion warning in `core/voice.cpp`; it did not affect the build result.
- Native front and overhead renders produced twelve-channel float WAV files. Approximately **97.1%** of the overhead source's channel energy reached the four height outputs, versus approximately **28.3%** for the front source. These are renderer measurements, not microphone measurements of the room. The decoder spreads energy across speakers; this is not a claim of isolated-channel output.
- A silent native probe successfully activated and started a **7.1.4 Windows spatial stream** on `AV Receiver (NVIDIA High Definition Audio)`. The activation mask was `0x1ffe`, sample rate 48 kHz, update/buffer sizes 480/960 frames. Those sizes do not measure end-to-end latency.
- Scripted source download/hash verification, patch application and cached native build worked. The sandbox preparation and preflight succeeded.
- The isolated game entered a world and initialized a 7.1.4 spatial stream. Its native log later recorded a callback timeout and `ISpatialAudioObjectRenderStream::Reset failed: 0x88890100`; audio was unavailable during the user's sandbox test. This remains unresolved and is not proof that the sandbox directory caused the failure.
- A subsequent standalone silent probe kept the receiver stream connected for five seconds and queried `ALC_CONNECTED` successfully. The preflight now checks this instead of waiting only 150 ms. This short check does not establish sustained gameplay reliability.
- Installed the full local test into the normal 1.22.7 game, with original mod ZIP, OpenAL DLL and mod settings backed up under `VintagestoryData/SurroundSpatialTest/20260912-185059`. Verified installed hashes, root mod metadata/DLL, forward-slash ZIP entries, and both launcher shortcuts. A five-second probe using the installed DLL also passed; the game was left closed for manual testing.

## Native failure found and corrected

Stock OpenAL Soft 1.25.2 reproduced Windows exception `0xc0000374` during spatial activation. The same crash occurred with upstream `openal-info64.exe`, independently of the managed probe. The 1.24.3 binary also failed. The cause identified in source was a `VT_BLOB` borrowing stack memory that its owning `PROPVARIANT` later frees. The patch allocates and copies with `CoTaskMemAlloc`; the patched build then passed stream activation and startup. See [native patch notes](../native/README.md).

## Evidence locations

- `bin/spatial-tests/patched/Above/result.json` and `render.wav`
- `bin/spatial-tests/patched/Front/result.json` and `render.wav`
- `bin/spatial-tests/patched/Windows/result.json` and `openal.log`
- `bin/spatial-tests/sustained/result.json` and `openal.log` (five-second connected check)
- `bin/openal-build/surround-runtime.json` (source, patch and binary hashes)
- `bin/SpatialSandbox/AudioLogs/` (launcher preflight and game native logs)
- `bin/SpatialSandbox/Data/Logs/client-main.log` (game startup)

## Weather-bed follow-up (superseded elevation experiment)

- The user reported that the installed spatial test works. Receiver input-format indication was not separately recorded.
- Added rain-bed elevation of approximately 45 degrees and wind-bed elevation of approximately 30 degrees, restricted to the named background loops in spatial mode. Ground rain emitters and other effects are excluded.
- 47 managed checks pass, including asset selection and emitter exclusions. Native six-channel bed renders into 7.1.4 show height-channel energy fractions of 27.9% for the baseline, 56.6% for wind and 71.9% for rain, with signal in all four height outputs. These are software measurements with synthetic independent channel signals; listening with the weather assets remains necessary.
- Inspected the installed 1.22.7 engine: `SystemSoundEngine.OnRenderFrame` passes `viewVector.X, 0f, viewVector.Z` to the audio listener, so camera pitch is intentionally absent from current listener orientation. This change preserves that behavior.

## Pitch and remaining weather beds follow-up (weather elevation superseded)

- Added opt-in `FollowCameraPitch`, with an orthonormal listener forward/up basis applied after the engine's flattened listener update. Default remains false; enabled explicitly in the local test configuration. Reports now include the setting and actual OpenAL orientation.
- Included hail, storm tremble/rumble, distant-thunder and additional replacement rain beds. Mono ambient beds also receive broad overhead rendering. Explicit world-positioned effects and ground rain emitters remain excluded. Fixed the hail override target to the engine's actual `sounds/weather/tracks/hail.ogg` path.
- Release build: zero warnings/errors. 66 managed checks passed, including setting-off behavior, the game's pitch convention, front/overhead direction transformations and vertical-view orthogonality.
- Native renders verified front-source height energy changing from 28.3% with pitch off to 72.0% while looking down 45 degrees with pitch on; native orientation readback matched each setting. The mono weather bed produced 96.4% height-channel energy. Existing multichannel weather comparisons still passed. These remain renderer measurements, not room measurements.
- Installed the updated ZIP and enabled pitch in normal game data. Prior prototype/settings were backed up under `SurroundSpatialTest/20260912-185059/updates/20260912-192216`; the original pre-spatial restore backup remains intact. Native runtime unchanged. In-game listening and patch activation remain to be checked by the user.

## Normal-launch follow-up

- Added game-local `alsoft.ini` startup and mod detection using the process executable's directory. Configuration is snapshotted, must predate the process, and respects an explicit `ALSOFT_CONF` override. A legacy launcher marker is no longer required.
- Release build passed with zero warnings/errors; 71 managed checks passed. A standalone native probe with no OpenAL environment overrides and a different working directory discovered its EXE-local config, activated 7.1.4 on the NVIDIA HDMI receiver and remained connected after five seconds. Evidence: `bin/spatial-tests/normal-launch-20260912-201452-854`.
- Setup round-trip verification preserved a pre-existing config byte-for-byte, retained one original backup through repeat setup, and restored the original hash. PowerShell syntax and diff checks passed.
- Installed the updated mod and `Vintagestory/alsoft.ini` into the normal installation. All four tracked file hashes matched. The existing pre-spatial restore point now also removes the added INI (or would restore an original file if one existed); the native DLL was unchanged. Prior mod/settings saved under `SurroundSpatialTest/20260912-185059/updates/20260912-201550`.
- The ordinary game shortcut is ready for manual testing. No full-game launch has yet been verified for this revision. Normal launches do not have the optional launcher's native trace, so detected configuration may be reported as Unverified rather than asserting stream activation or receiver format.

## Weather speaker routing correction

- The maintainer reported audible phasing with the elevated, pitch-sensitive weather beds. Earlier height-energy measurements verified routing but did not establish acceptable sound quality.
- Inspected the actual Ogg identification headers: replacement rain/wind/rumble/distant-thunder recordings have six channels, while vanilla low/very-low tremble recordings have two. Removed forced elevation and listener-following source positioning. Ambient weather beds now bypass stereo expansion, and multichannel/stereo beds use nonspatial direct-channel output with unmatched-channel remix fallback. Positional emitters and the pitch setting remain available.
- Release build against 1.22.7 passed with zero warnings/errors; 71 managed checks passed. Native stereo and 5.1 renders retained their two/six input channels, produced zero height energy, and preserved channel balance with the listener tilted down 45 degrees. Stereo reached only FL/FR; 5.1 reached FL/FR/FC/LFE/SL/SR. Positional tests still measured 97.1% overhead energy and a front-source change from 28.3% to 72.0% with pitch enabled.
- Installed the corrected 20-entry mod ZIP into the normal game after confirming it was closed. Archive layout and installed SHA-256 verified; original restore backups retained, previous prototype saved under `SurroundSpatialTest/20260912-185059/updates/20260912-203217`. Runtime, startup INI and user configuration were unchanged. The game is ready for manual listening through the normal shortcut.
- Evidence: `bin/spatial-tests/patched/WeatherStereo`, `WeatherStereoTilt`, `WeatherSurround`, `WeatherSurroundTilt`. These synthetic renders verify routing; actual weather listening remains necessary to assess the reported phasing.

## Remaining hardware/game checks

Receiver Atmos format indication and perceived overhead direction, in-world debug panel layout and playback, ordinary gameplay effects, world reload, device disconnect/reconnect, latency and sustained-load dropouts still require validation. No other Vintage Story version has been qualified by this run. The mod's existing version metadata is retained because this is feature-branch work, not a published release.

## 2.0.0 packaging and combined occlusion candidate

- The maintainer selected mod version 2.0.0 and requested inclusion of the concurrent Bell/occlusion correction. Assembly/file versions are 2.0.0.0; minimum game dependency is 1.22.7. The Bell harness passed all 16 integration checks against the actual 1.22.7 sound implementation and a silent OpenAL backend. See [Bell investigation](bell-alert-occlusion-fix.md).
- The prebuilt add-on includes Windows PowerShell 5.1 setup, preview/confirmation, diagnostic/conventional launch, restoration, packaged mod/native payloads, source/notice linkage and checksums. Setup journals file replacements, retains one original restore point and verifies payload/backup hashes. File-fixture tests passed for repeat/mod-only updates, custom paths, absent originals, duplicate/corrupt packages, conflicts, a sharing violation after an earlier write, interrupted-operation recovery and exact restore hashes. The packaged preflight successfully started 7.1.4 through Windows PowerShell 5.1 with no developer tools in PATH.
- Fresh native packaging initially reproduced the ownership crash because `git apply` silently skipped hunks under the repository's ignored build directory. Fixed source-root discovery and patch line endings, added reverse-application verification and source-tree hashes, then rebuilt and verified a working native payload. The existing locally installed working DLL was not overwritten during this investigation.
- The corrected native candidate passed all 71 managed spatial checks and native twelve-channel, weather routing and pitch comparisons. Native renderer measurements remain approximately 97.1% overhead energy, zero direct weather height energy and 28.3% versus 72.0% front-source energy for pitch off/on.
- Installer tests use the real game executable for version/architecture checks and original runtime bytes for restore checks, but only copy files into separate fixture directories. They do not constitute gameplay or a separate machine without developer tools. Final 2.0.0 listening, clean-machine setup, receiver format, sustained play and device-loss qualification remain open.

## 2.0.0-dev.1 prerelease preparation

- The maintainer authorized a GitHub prerelease with the mod and installer and requested camera pitch on by default. Mod metadata and assembly informational version are `2.0.0-dev.1`; numeric assembly/file versions remain `2.0.0.0`.
- Verified the installed game's actual `ProperVersion.SemVer` parser accepts `2.0.0-dev.1` and orders it below stable `2.0.0`. Verified new/missing pitch settings resolve to true and explicit false survives deserialization with the game's Newtonsoft.Json library. ConfigLib's default is updated to match.
- The package builder's `-Prerelease` option requires a clean committed tree and prerelease metadata, names artifacts with that exact version, and marks the package as a prerelease rather than a timestamped development snapshot. The prerelease remains limited to Vintage Story 1.22.7 / Windows x64 spatial setup; the existing hardware and clean-machine qualification limits are disclosed in its notes.
