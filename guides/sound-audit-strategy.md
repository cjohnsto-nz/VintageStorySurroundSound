# Sound Audit Strategy

## Goal

Systematically identify:

- every sound source the game creates
- every sound source that actually starts playback
- whether the source is mono, stereo, or multichannel
- whether it is configured as relative, positional, or direct-channel
- which sounds are likely safe spatial sounds versus likely stereo beds or content mismatches

## Current Instrumentation

`SurroundSoundLab` now patches `LoadedSoundNative` at three points:

- `createSoundSource()`
- `Start()`
- `Dispose()`

When sound audit is enabled in `surroundsoundlab.json`, each event is written to the session JSONL log as a `sound-audit` event.

The mod can also write an aggregated summary JSON for the current session from the `F9` debug panel.

## Config

`surroundsoundlab.json`

- `OutputMode`
  - `Auto` by default
  - also supports `StereoBasic`, `Stereo`, `StereoHrtf`, `Quad`, `Surround5Point1`, `Surround6Point1`, `Surround7Point1`
- `EnableSoundAudit`
  - `true` by default for the lab mod

## Captured Data Per Sound Instance

Each audit event records:

- instance id
- event type
  - `SourceCreated`
  - `PlaybackStarted`
  - `Disposed`
- asset location
- `SoundType`
- channel count
- bit depth
- sample rate
- source id
- buffer id
- relative-position flag
- whether a position exists
- whether the position is non-zero
- loop/dispose flags
- range and reference distance
- requested output mode
- actual output mode
- `AL_DIRECT_CHANNELS_SOFT` value when readable

## Routing Classification Heuristics

The audit currently classifies sounds into categories such as:

- `PositionalMono`
- `MonoOriginOrListenerSpace`
- `StereoBedOrEngineManagedStereo`
- `StereoWithPositionalFlags`
- `DirectStereoBed`
- `Direct6chBed`
- `MultichannelWithoutDirectChannels`
- `MultichannelWithPositionalFlags`

These are intentionally routing-focused, not content-focused.

They answer:

- is this source likely spatialized by OpenAL?
- is this source likely a stereo bed?
- is this source explicitly using direct-channel playback?
- is this source in an ambiguous/problematic configuration?

## Recommended Analysis Workflow

1. Start a fresh play session.
2. Reproduce a scenario:
   - menu
   - world join
   - ambient exploration
   - combat
   - UI/inventory
3. Close the session or stop the test.
4. Inspect the session JSONL for `sound-audit` events.
5. Group by `Location`.
6. Compare:
   - `Channels`
   - `RoutingClassification`
   - `RelativePosition`
   - `HasNonZeroPosition`
   - `DirectChannelsValue`

## What We Expect To Learn

This should let us answer:

- which sounds are mono and benefit naturally from surround spatialization
- which sounds are plain stereo content
- which sounds are suspicious stereo-or-multichannel sources being used positionally
- whether the engine is already setting direct-channel behavior anywhere useful
- which sound families should be targeted first for better surround semantics

## Next Useful Enhancements

- Add counters for:
  - first seen
  - starts
  - disposes
  - channel layout
- Flag repeated mismatches automatically, for example:
  - stereo sources with positional flags
  - multichannel sources without direct-channel routing
- Add richer categorization for UI, weather, ambience, and music families.
- Add explicit source-volume and listener-distance snapshots for suspicious sounds.

## Channel Mute Debugging

The `F9` panel now also exposes speaker-mute toggles for `FL`, `FR`, `FC`, `LFE`, `SL`, and `SR`.

Important limitation:

- These mutes currently apply only to non-mono buffers during source creation.
- Mono positional sounds are not globally muted at the final mixer level.

This is still useful for identifying front-bed stereo content, because ordinary stereo assets are uploaded as 2-channel buffers and can be masked per channel before OpenAL sees them.
