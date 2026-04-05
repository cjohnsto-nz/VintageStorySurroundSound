# Surround Sound Lab Plan

## Summary

- OpenAL Soft on this machine exposes multichannel-related extensions and format enums.
- Vintage Story's game-owned context is explicitly created in a stereo output mode.
- The widened `GetSoundFormat()` patch was necessary, but not sufficient.
- Current testing has shown that the patched engine path still only produces audible output on channels 1 and 2.
- The lab-owned context has now proven `5.1` playback on this machine for `FL`, `FR`, `FC`, `SL`, and `SR`.
- Channel 4 has now been confirmed as `LFE`.

The active strategy is now to patch the game-owned context itself, not just the format mapper.
The first implementation of that context patch is now in the mod and awaiting validation.

## Active Implementation Steps

### Phase 1: Keep the lab proof available, but stop relying on it for the main path

- Keep both `Game Context` and `Lab Context` test modes.
- Keep the low-frequency LFE tone for `5.1` channel 4.
- Keep writing every test run and speaker observation to the session JSONL log.
- Treat the lab path as a reference tool, not the primary implementation target, until the context-restoration bug is fully solved.

### Phase 2: Patch the game-owned context

- Patch `AudioOpenAl.initContext(...)` so the mod can request a multichannel output mode.
- Disable HRTF automatically when a surround output mode is requested.
- Query and report the actual output mode the device accepted after context creation.

### Phase 3: Keep the widened format mapping

- Keep the `AudioOpenAl.GetSoundFormat(...)` Harmony patch active for `quad`, `5.1`, `6.1`, and `7.1`.
- Re-test the engine-owned path only after the context-output patch is in place.

### Phase 4: Patch non-mono source behavior

- Patch `LoadedSoundNative.createSoundSource()` so `sample.Channels > 1` can opt into `AL_DIRECT_CHANNELS_SOFT`.
- Preserve current spatial behavior for mono sources.
- Re-test how existing stereo assets behave under multichannel output.

### Phase 5: Audit the live soundscape

- Record every created/started/disposed `LoadedSoundNative` instance.
- Classify sources as mono positional, stereo bed, direct-channel bed, or suspicious mismatch.
- Use the audit data to decide which sound families need follow-up patches versus leaving them to OpenAL spatialization.
- Write an aggregated per-session summary JSON so we can review sessions without parsing raw JSONL.
- Keep non-mono channel-mask controls in the debug panel to isolate front-bed stereo behavior during playtests.

## Tooling

- `F9` opens the debug panel.
- The panel supports both `Game Context` and `Lab Context` test modes.
- Reports remain under `VintagestoryData/Logs/SurroundSoundLab`.
- Structured session logs are written as JSONL in the same folder.
- Reports now explicitly distinguish between "no current context" and "context available".
- Detailed implementation notes now live in `engine-multichannel-investigation.md`.
- Sound-audit strategy now lives in `sound-audit-strategy.md`.
