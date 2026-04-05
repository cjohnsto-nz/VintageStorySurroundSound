# Surround Sound Checklist

This mod exists to explore whether Vintage Story can be extended toward surround output without guessing.

## Rules

- Make one change at a time.
- Prefer capability probes and file-based reports before playback changes.
- Keep the first steps read-only or observational where possible.

## Current Findings

- Vintage Story audio initialization lives in `Vintagestory.Client.AudioOpenAl`.
- Sound buffer/source creation lives in `Vintagestory.Client.LoadedSoundNative`.
- The current decompiled `GetSoundFormat()` implementation supports only 1-channel and 2-channel audio until patched.
- The decode path preserves multichannel PCM; it does not collapse OGG or WAV content to stereo.
- The game is aware of OpenAL Soft concepts like HRTF and `AL_DIRECT_CHANNELS_SOFT`.
- The game-owned OpenAL context now works in real multichannel on this machine.
- `vintagestorysurroundsound.json` now supports user-selectable output modes with `Auto` as the default.
- Sound audit events now record `LoadedSoundNative` source creation, playback start, and disposal into the session JSONL.
- The mod can now write an aggregated per-session sound-audit summary JSON grouped by sound asset.
- The `F9` panel now supports non-mono speaker masking for `FL`, `FR`, `FC`, `LFE`, `SL`, and `SR`.
- Stereo weather, UI, underwater, and temporal-stability effects are now the main follow-up area.
- `SystemPlayerSounds` owns the current fly-wind and underwater loops.
- `SystemClientTickingBlocks` plus `SystemPlayerSounds` already provide a block-driven ambient model we can reuse for worldized emitters.
- `SystemClientTickingBlocks` also computes `GlobalConstants.CurrentNearbyRelLeavesCountClient`, which is a likely future hook for leafy weather worldization.
- The first catalog now lives in `stereo-effect-catalog.md`.

## Checklist

- [x] Create a separate experimental mod scaffold.
- [x] Add a first client-side capability probe that writes a report to disk.
- [x] Capture a report from the current machine and inspect available OpenAL extensions/features.
- [x] Determine whether the shipped OpenAL binding exposes multichannel AL formats directly.
- [x] Add a first in-game debug panel for capability summary and channel tests.
- [x] Add structured per-session JSONL logging for probe runs and channel tests.
- [x] Add speaker-observation capture for each channel test.
- [x] Add an isolated lab-owned OpenAL context test path.
- [x] Write the current surround investigation plan to file.
- [x] Write engine notes for the current stereo bottlenecks.
- [x] Distinguish "no current OpenAL context" in reports instead of treating it like missing capability.
- [x] Add lab-context single-flight / cooldown protection to avoid overlapping temporary context tests.
- [x] Use an LFE-friendly low-frequency tone for channel 4 in `5.1` tests.
- [x] Determine whether decoded game assets can preserve more than 2 channels.
- [x] Decide that the next real experiment is a multi-channel buffer upload/playback test in a lab-owned context.
- [x] Run the lab-owned context tests and capture results in the session log.
- [x] Determine whether the lab-owned context can produce audible output beyond channels 1 and 2.
- [x] Confirm channel 4 as `LFE` using the dedicated low-frequency tone.
- [x] If the lab-owned context succeeds, draft the first minimal Harmony patch plan for `GetSoundFormat()`.
- [x] Test the widened `GetSoundFormat()` patch against the game-owned context.
- [x] Determine whether `GetSoundFormat()` alone is sufficient for multichannel engine playback.
- [x] Investigate whether the game-owned context itself is being created in stereo mode.
- [x] Patch `AudioOpenAl.initContext(...)` so the game-owned context can request a multichannel output mode.
- [x] Force the game-owned audio context to recreate after the patch installs.
- [x] Add user-configurable output mode settings with `Auto` as the default.
- [x] Add a first systematic sound-audit pipeline for live sound instances.
- [x] Write the sound-audit strategy to file.
- [x] Add an aggregated sound-audit summary report grouped by sound asset.
- [x] Add debug-panel controls for masking individual speakers on non-mono buffers.
- [x] Query and record the actual output mode the game-owned context accepted.
- [x] Re-test existing stereo content under a multichannel game-owned context.
- [x] Write a first-pass stereo effect catalog from audit data and decompiled logic.
- [x] Extend the audit summary to include playback parameter rollups for cataloging work.
- [x] Regenerate the audit summary with the newer volume/pitch/range rollups.
- [ ] Find the exact weather audio driver for the listener-relative weather beds.
- [ ] Find the exact temporal-stability sound driver.
- [ ] Build a per-asset decision matrix:
  - keep listener-relative
  - keep as multichannel bed
  - convert to mono in-world emitters
- [x] Write a reusable weather-audio architecture that can start with rain and extend to hail, wind, and foliage.
- [x] Research candidate sound libraries that fit the target runtime formats and licensing constraints.
- [ ] Decide whether rain prototyping will use:
  - licensed surround beds
  - self-rendered beds
  - or temporary placeholder assets
- [ ] Prototype the first worldized weather family.
- [ ] Validate the weather prototype with deploy + playtest before moving to the next family.

## Current Session Output

Reports are written under `VintagestoryData/Logs/VintageStorySurroundSound`.
The debug panel is available in-game on `F9`.
Structured session logs are written in the same folder as `session-*.jsonl`.
