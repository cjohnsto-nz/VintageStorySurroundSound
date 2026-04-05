# Vintage Story Stereo Notes

## Confirmed Stereo-Enforcing Points

### `AudioOpenAl.initContext(...)`

- The engine creates one main OpenAL playback context for the whole client.
- The context attribute list is hard-coded around stereo output:
  - when HRTF is disabled it requests `ALC_OUTPUT_MODE_SOFT = ALC_STEREO_BASIC_SOFT`
  - when HRTF is enabled it requests `ALC_OUTPUT_MODE_SOFT = ALC_STEREO_SOFT`
- This means the game is not simply "defaulting" to stereo. It is explicitly asking OpenAL Soft for stereo output.
- This is the main reason the engine-owned path remained stereo even after widening `GetSoundFormat()`.

### `AudioOpenAl.GetSoundFormat(...)`

- This method only maps:
  - `1` channel to mono formats
  - `2` channels to stereo formats
- Any channel count greater than `2` throws `NotSupportedException`.
- This is a hard format gate, but it is not the only stereo bottleneck.

### `LoadedSoundNative.createSoundSource()`

- The engine asks `AudioOpenAl.GetSoundFormat(sample.Channels, sample.BitsPerSample)` for every loaded sound.
- The decoder output is uploaded unchanged through `AL.BufferData(...)`.
- The engine only sets `AL_DIRECT_CHANNELS_SOFT` for stereo buffers and only when HRTF is enabled.
- There is no branch that deliberately treats `4/6/7/8` channel buffers as direct speaker feeds.

### `ClientMain.PlaySoundAtInternal(...)` / `StartPlaying(...)`

- The higher-level client sound API is built around:
  - relative sounds for UI/local playback
  - positional sounds for world playback
- The engine already knows stereo positional sounds are a content mismatch and warns in developer mode that they "will not attenuate correctly."
- This is a clue that mono positional sounds and non-mono direct-channel sounds should be treated as two different playback models.

## Decoder And Buffer Findings

- `AudioOpenAl.LoadWave(...)` preserves the channel count from the WAV header.
- `OggDecoder.OggToWav(...)` preserves `val5.channels` from the Vorbis stream and writes interleaved PCM for all decoded channels.
- `AudioMetaData` stores `Channels`, `Rate`, `BitsPerSample`, and raw PCM without collapsing multichannel data to stereo.
- So the decode path is not the limiter. The limiter is context mode plus runtime format/source handling.

## Current Conclusion

- The runtime and machine are capable of surround playback.
- The lab-owned context has already proven `5.1` speaker routing on this machine, including `LFE`.
- The current Vintage Story playback path is still stereo-first because:
  - the main context is explicitly created in a stereo output mode
  - the built-in format mapper rejects channels above `2`
  - the source setup logic does not treat multichannel buffers as first-class direct-channel playback

## Likely Patch Targets

- First mandatory target:
  - `AudioOpenAl.initContext(...)`
- Second target:
  - `AudioOpenAl.GetSoundFormat(...)`
- Third target:
  - `LoadedSoundNative.createSoundSource()`

## Higher-Risk Areas

- Replacing the entire `AudioOpenAl.initContext(...)` method without carefully mirroring the game's device and error handling
- Running temporary lab contexts on top of the live game context during normal play
- Applying reverb or low-pass behavior unchanged to future direct multichannel bed playback
