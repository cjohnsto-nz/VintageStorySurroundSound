# Surround Sound 2.0.0-dev.1 prerelease

This prerelease adds optional Windows Spatial Audio with 7.1.4 height output and
fixes the Bell alarm cutting out when entity sound occlusion is enabled ([#5](https://github.com/cjohnsto-nz/VintageStorySurroundSound/issues/5)).
It targets Vintage Story 1.22.7. Spatial audio is experimental; remaining testing
and known limitations are listed below.

## Audio changes

- Experimental Windows spatial output for a compatible receiver or soundbar.
  Positional sounds can reach height speakers through Windows' spatial renderer.
  This supplies a mixed channel bed, not encoded Atmos passthrough or individual
  dynamic Atmos objects for every game sound.
- Optional **Follow camera pitch** setting, enabled by default. Looking up/down
  changes the direction of positional audio relative to your view. Existing
  explicit off settings are preserved.
- Weather beds keep their original stereo/surround speaker channels. They bypass
  stereo expansion and do not rotate into height speakers with camera pitch.
  Ground rain and other positioned emitters retain positional playback.
- Bell alarms retain their fade-in and looping volume under occlusion. Attenuation
  is applied once to native gain, preserving vanilla volume changes, fades and
  category sliders; repeated static-sound updates no longer compound attenuation.
- Spatial diagnostics report setup/fallback state and listener orientation. Source
  tests cover above/below, front/rear, elevated corners and a vertical sweep.

## Installation

Ordinary surround uses the normal mod ZIP. Height output additionally requires the
optional **Windows x64 spatial add-on**, which includes a patched OpenAL runtime.
Extract that add-on and run `Install.cmd` with the game closed. Review the selected
game/data folders and confirm; setup installs the matching mod, checks the default
spatial endpoint and preserves original files. Launch the game normally afterward.

The add-on runs with Windows PowerShell 5.1 and needs no developer tools. Keep its
extracted folder and the game's `SurroundSpatialSetup` directory for diagnostics
and restoration. `Restore.cmd` restores the original DLL, mod and configuration.
Game updates require a separately qualified package; setup does not automatically
repair native files after an update.

Developer-prototype installations must use their original restore script before
adopting public setup. Setup refuses to record an already-installed identical
spatial DLL as the original backup.

## Qualification still open

The combined candidate needs final gameplay listening, including the corrected
weather and Bell alarm, a sustained session, device-loss checks and installation
on a separate machine without developer tools. An earlier prototype recorded a
spatial stream timeout/reset failure. Receiver input format must be checked on
the receiver itself; successful stream activation does not verify that display.
