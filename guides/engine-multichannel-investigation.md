# Vintage Story Multichannel Investigation

## Goal

Work toward a proof of concept where:

- the game-owned OpenAL context can run in a multichannel output mode
- existing stereo game content continues to land on front left and front right
- existing mono positional sounds can benefit from surround panning
- new authored multichannel assets can use `FC`, `LFE`, `SL`, `SR`, and beyond
- future mod patches can layer better channel-aware playback behavior onto the game

## What The Decompiled Game Code Confirms

### 1. The engine explicitly asks OpenAL Soft for stereo output

In `Vintagestory.Client.AudioOpenAl.initContext(...)`:

- when HRTF is disabled, the engine creates the context with:
  - `ALC_HRTF_SOFT = 0`
  - `ALC_OUTPUT_MODE_SOFT = ALC_STEREO_BASIC_SOFT`
- when HRTF is enabled, the engine creates the context with:
  - `ALC_HRTF_SOFT = 1`
  - `ALC_OUTPUT_MODE_SOFT = ALC_STEREO_SOFT`

This is the most important finding in the whole investigation so far.

The engine path stayed stereo after the `GetSoundFormat()` Harmony patch because the game-owned context itself was still being created as stereo.

### 2. The decoder path already preserves multichannel PCM

The decompiled game does not collapse multichannel source data to stereo during decode:

- `AudioOpenAl.LoadWave(...)` reads the WAV channel count directly
- `OggDecoder.OggToWav(...)` preserves `val5.channels`
- `AudioMetaData` stores channel count and raw PCM unchanged

So multichannel content can survive decoding. The engine loses multichannel capability later, not here.

### 3. `GetSoundFormat()` is a hard gate, but only one of them

`AudioOpenAl.GetSoundFormat(...)` currently maps only:

- `1` channel -> mono
- `2` channels -> stereo

Anything above `2` throws.

Our existing Harmony patch removed this gate, but the game-owned path still remained stereo. That proves context mode is the next real bottleneck.

### 4. Runtime source creation is still stereo-first

Every loaded sound goes through `Vintagestory.Client.LoadedSoundNative.createSoundSource()`:

- the engine generates one AL source and one AL buffer
- it uploads the already-decoded PCM with `AL.BufferData(...)`
- it applies position, relative flag, looping, reverb, low-pass, pitch, and gain

The only special handling for non-mono output today is:

- for stereo buffers
- when HRTF is enabled
- set `AL_DIRECT_CHANNELS_SOFT = 2` (`AL_REMIX_UNMATCHED_SOFT`)

There is no equivalent branch for `quad`, `5.1`, `6.1`, or `7.1`.

### 5. The client sound API is designed around positional mono and conventional stereo

The higher-level playback flow in `ClientMain.PlaySoundAtInternal(...)` and `StartPlaying(...)` assumes:

- relative sounds for menu/UI/local playback
- positional sounds for world playback

The engine already warns in developer mode when a stereo sound is loaded as a locational sound, which is a strong hint that:

- mono spatial sounds and
- direct speaker-bed sounds

should be treated as separate playback modes.

## What The Lab Results Mean

### Confirmed by the lab-owned context

The lab-owned context has already proven that this machine can do real `5.1` playback:

- `1` -> `FL`
- `2` -> `FR`
- `3` -> `FC`
- `4` -> `LFE`
- `5` -> `SL`
- `6` -> `SR`

So the runtime and hardware are not the blocker.

### Confirmed by the patched engine path

The current `GetSoundFormat()` patch is not enough.

It allowed the engine path to accept `AL_FORMAT_51CHN16`, but the game-owned path still produced output only on channels `1` and `2`.

That is consistent with the engine still running inside a stereo output context.

## How Multichannel Support Could Be Achieved

## Phase 1: Make the game-owned context multichannel-capable

Patch `AudioOpenAl.initContext(...)` so the mod can choose the requested output mode instead of always requesting stereo.

Recommended mod-controlled modes:

- `Auto`
- `StereoBasic`
- `StereoHrtf`
- `Quad`
- `5.1`
- `6.1`
- `7.1`

### Recommended behavior

- if a surround mode is requested:
  - disable HRTF for that context creation
  - request `ALC_OUTPUT_MODE_SOFT = ALC_5POINT1_SOFT` or similar
- if `Auto` is requested:
  - prefer `ALC_ANY_SOFT`
  - let OpenAL Soft choose the best device mode
- after context creation:
  - query and record the actual mode the device accepted

### Why this is the first patch

Without a multichannel game-owned context, the rest of the engine will keep behaving like a stereo client no matter what format enums we unlock.

## Phase 2: Keep the `GetSoundFormat()` widening patch

The current Harmony patch on `AudioOpenAl.GetSoundFormat(...)` is still necessary.

The engine must be able to return:

- `AL_FORMAT_QUAD16`
- `AL_FORMAT_51CHN16`
- `AL_FORMAT_61CHN16`
- `AL_FORMAT_71CHN16`

and the matching `8-bit` forms where supported.

This patch should stay, but it now becomes the second step, not the first.

## Phase 3: Patch source creation for non-mono buffers

Patch `LoadedSoundNative.createSoundSource()` so non-mono sources are treated deliberately.

### Proposed rule

- `sample.Channels == 1`
  - keep current behavior
  - let OpenAL spatialize the mono source across the speaker layout
- `sample.Channels > 1`
  - set `AL_DIRECT_CHANNELS_SOFT`
  - do not rely on positional virtualization for the buffer

### Why this helps

This gives us the model we want:

- existing mono world sounds still spatialize naturally in surround
- existing stereo game content stays on front left and front right
- future authored multichannel sounds can reach the extra channels directly

### Suggested default

Use `AL_REMIX_UNMATCHED_SOFT` for safety.

That way:

- on a matching output mode, channels route directly
- on a smaller output mode, content remains audible instead of vanishing

For exact test content, a stricter `drop unmatched` option could still be useful later.

## Phase 4: Add an explicit multichannel playback model for mods

The base game API does not currently distinguish:

- positional spatial sound
- direct stereo
- direct multichannel bed

That means long-term surround support will be cleaner if the mod introduces its own playback semantics.

### Suggested mod-side model

Add a custom params object or metadata layer with fields like:

- `PlaybackMode`
  - `SpatialMono`
  - `DirectStereo`
  - `DirectMultichannel`
- `Layout`
  - `Stereo`
  - `Quad`
  - `5.1`
  - `6.1`
  - `7.1`
- `ApplyReverb`
- `ApplyLowPass`
- `AllowRemixFallback`

This can initially live entirely in the mod, even if it is backed by Harmony patches into the game-owned source creation path.

## Recommended Proof-Of-Concept Patch Order

1. Patch `AudioOpenAl.initContext(...)` to request `5.1` or `Auto` instead of stereo.
2. Query and report the actual accepted output mode after context creation.
3. Keep the widened `GetSoundFormat()` patch in place.
4. Patch `LoadedSoundNative.createSoundSource()` so all `sample.Channels > 1` sources set `AL_DIRECT_CHANNELS_SOFT`.
5. Re-test:
   - existing stereo content
   - mono positional world sounds
   - custom `5.1` assets
6. Only after that:
   - add better API semantics for authored multichannel playback

## Expected Outcome Of That Patch Order

If those patches work as intended:

- mono world sounds should spatialize into the multichannel layout
- stereo content should continue to land on `FL` and `FR`
- authored `5.1` assets should be able to use `FC`, `LFE`, `SL`, and `SR`

That is the cleanest path to "existing stereo still works, extra channels become usable."

## Risks And Compatibility Notes

### HRTF

HRTF is fundamentally a stereo rendering path in this engine setup.

If multichannel output is requested, HRTF should be treated as incompatible and disabled for that context.

### Reverb and low-pass filters

`LoadedSoundNative.createSoundSource()` applies reverb and low-pass behavior to every source today.

For direct multichannel bed playback, those effects may not make sense and may need to be disabled or made configurable.

### Context recreation

The game already recreates the context when the audio device changes or HRTF settings change.

Any new multichannel-mode state must survive that recreation path.

### Temporary lab contexts

The current lab-owned context path has already destabilized the game audio context during repeated testing.

That means future proof-of-concept work should prefer patching the game-owned context over continuing to stack temporary contexts on top of it during normal play.

## Most Likely Next Real Implementation Task

Patch `AudioOpenAl.initContext(...)` so the game-owned context can request `5.1` output, then re-run the existing `PatchedEnginePath` speaker tests with the widened `GetSoundFormat()` patch still active.
