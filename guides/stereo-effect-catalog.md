# Stereo Effect Catalog

## Purpose

This document is the working inventory for stereo sounds that need review now that surround output is functioning.

The goal is not to replace everything with mono world emitters blindly. We need to separate:

- UI or headspace sounds that should probably remain non-world.
- Stereo beds that should become deliberate multichannel beds.
- Stereo effects that should become mono in-world emitters.

## Sources Used

- `VintagestoryData/Logs/SurroundSoundLab/sound-audit-summary-20260405-022151.json`
- `VintagestoryData/Logs/SurroundSoundLab/session-20260405-021840.jsonl`
- Decompiled client code under `_decomp_vs_lib`
- Survival asset JSON under `assets/survival`

## Core Engine Behavior

### Listener-relative fallback

`ClientMain.PlaySoundAt(...)` treats `(0, 0, 0)` as listener-relative. That means any system calling `PlaySoundAt(sound, 0, 0, 0, ...)` creates a headspace/non-world sound by default.

Relevant file:

- `Vintagestory.Client.NoObf.ClientMain`

### Final OpenAL source setup

`LoadedSoundNative.createSoundSource()` applies:

- gain = `soundParams.Volume * GlobalVolume`
- loop = `soundParams.ShouldLoop`
- position = `soundParams.Position` when present
- relative mode = `soundParams.RelativePosition`
- attenuation based on `Range` and `ReferenceDistance`

Global category volume comes from:

- `MusicLevel` for music
- `WeatherSoundLevel` for `EnumSoundType.Weather`
- `AmbientSoundLevel` for ambient
- `EntitySoundLevel` for entity
- `SoundLevel` for everything else

Relevant file:

- `Vintagestory.Client.LoadedSoundNative`

### Existing scalable world-ambient pattern

The game already has a good pattern for block-driven ambient sound:

- `SystemClientTickingBlocks` scans nearby blocks
- it groups `block.Sounds.Ambient` by asset and section
- `SystemPlayerSounds` loads one looping sound per merged ambient cluster
- `AmbientSound.updatePosition()` moves each sound to the nearest point on its merged bounding boxes
- `AmbientSound.AdjustedVolume` scales with nearby block count and block-provided strength

This is the most promising model for future "leaf rustle from actual leaves" work.

Relevant files:

- `Vintagestory.Client.NoObf.SystemClientTickingBlocks`
- `Vintagestory.Client.NoObf.SystemPlayerSounds`
- `Vintagestory.Client.NoObf.AmbientSound`

## Catalog

## 1. UI Button Sounds

### Assets

- `sounds/menubutton.ogg`
- `sounds/menubutton_down.ogg`

### Current behavior

- `2ch`
- `RelativePosition = true`
- `ShouldLoop = false`
- `DisposeOnFinish = true`
- `Range = 32`
- `ReferenceDistance = 3`

### Trigger

GUI code calls `GuiAPI.PlaySound(...)`, which resolves to `ClientMain.PlaySound(...)` and then `PlaySoundAt(..., 0, 0, 0, ...)`, making the sound listener-relative.

`ScreenManager.LoadSoundsInitial()` also preloads `sounds/menubutton*` very early.

Relevant files:

- `Vintagestory.Client.NoObf.GuiAPI`
- `Vintagestory.Client.NoObf.GuiElementModCell`
- `Vintagestory.Client.ScreenManager`
- `Vintagestory.Client.NoObf.ClientMain`

### Recommendation

Do not convert these to world emitters.

They belong in the "non-world/UI" bucket and should be handled separately from surround worldization.

## 2. Player-State / Headspace Loops

### Asset

- `sounds/environment/wind.ogg`

### Current behavior

- `2ch`
- loaded once as a loop
- listener-relative
- non-disposing
- sound category = normal `Sound`

### Trigger

`SystemPlayerSounds` starts it when fall speed exceeds the threshold:

- `abs(FallSpeed) - 0.05 > 0.2`

Volume behavior:

- target = `min(1, abs(flySpeed))`
- current volume eases toward target over time

Relevant file:

- `Vintagestory.Client.NoObf.SystemPlayerSounds`

### Recommendation

Treat as player-state audio, not a world emitter. It may deserve a better multichannel bed later, but it should not be prioritized with leaf/rain worldization.

### Asset

- `sounds/environment/underwater.ogg`

### Current behavior

- `2ch`
- loaded once as a loop
- listener-relative
- non-disposing
- sound category = normal `Sound`

### Trigger

`SystemPlayerSounds.OnSwimDepthChange(...)`

- starts when `EyesInWaterDepth != 0`
- stops when eyes leave water
- volume = `min(0.1, EyesInWaterDepth / 2)`

Relevant file:

- `Vintagestory.Client.NoObf.SystemPlayerSounds`

### Recommendation

Treat as a headspace/occlusion effect, not a world emitter.

## 3. Weather Stereo Beds

### Observed assets

- `sounds/weather/tracks/rain-leafless.ogg`
- `sounds/weather/tracks/rain-leafy.ogg`
- `sounds/weather/tracks/verylowtremble.ogg`
- `sounds/weather/tracks/hail.ogg`
- `sounds/weather/wind-leafless.ogg`
- `sounds/weather/wind-leafy.ogg`

### Current observed behavior

All of these were observed as:

- `2ch`
- `SoundType = Weather`
- `RelativePosition = true`
- `ShouldLoop = true`
- `DisposeOnFinish = false`
- `Range = 16`
- `ReferenceDistance = 3`
- `Position = (0, 0, 0)` when present

### Known logic

We have not yet found the exact hardcoded call site in the decompiled C# output.

What we do know:

- they are loaded as weather-category sounds
- they are listener-relative loops
- hail is definitely part of the weather event system, because the game ships `smallhail.json` and `largehail.json` weather event configs with `precType: "hail"`
- the client computes nearby leaf density in `SystemClientTickingBlocks` and stores it in `GlobalConstants.CurrentNearbyRelLeavesCountClient`

That leaf-density signal is a strong candidate for how the engine chooses leafy vs leafless weather beds.

Relevant files / assets:

- `Vintagestory.Client.NoObf.SystemClientTickingBlocks`
- `assets/game/config/weatherevents/smallhail.json`
- `assets/game/config/weatherevents/largehail.json`

### Recommendation

This is the highest-priority bucket for true worldization.

Target direction:

- replace listener-relative leafy/leafless beds with mono emitter fields
- drive emitter density/intensity from existing weather intensity + nearby leaf density + wind exposure
- keep the existing "leafy vs leafless" intensity model, but move it into world space

### Leaf rustle concept

For leaves specifically, a promising future design is:

- create mono leaf-rustle variants for each current intensity band
- sample only a subset of nearby eligible leaf blocks, not every visible leaf
- bias selection toward wind-affected leaves
- use chance-based emission rather than permanent looping sources per block
- cap concurrent emitters aggressively

This should scale much better than trying to attach one looping source to thousands of leaves.

## 4. Temporal Stability / Status Loops

### Observed assets

- `sounds/effect/tempstab-drain.ogg`
- `sounds/effect/tempstab-low.ogg`
- `sounds/effect/tempstab-verylow.ogg`

### Current observed behavior

- `2ch`
- listener-relative
- looping
- non-disposing
- `Range = 32`
- `ReferenceDistance = 3`
- `SoundType = SoundGlitchunaffected`

### Known logic

We have confirmed the HUD reads temporal stability state from:

- `temporalStability`
- `tempStabChangeVelocity`

in `HudHotbar`, but the exact sound-loading call site for these three assets has not yet been found in the decompiled C# output.

Relevant file:

- `Vintagestory.Client.NoObf.HudHotbar`

### Recommendation

Do not assume these should become world emitters.

These are probably player-status/headspace effects. They belong in a separate design track from weather rustle and environmental beds.

## 5. Held / Vehicle Stereo Beds

### Observed assets

- `sounds/raft-idle.ogg`
- `sounds/raft-moving.ogg`

### Current observed behavior

- `2ch`
- non-positional bed in the audited session

### Known logic

The exact raft call site has not yet been traced, but the game does have generic support for held-item idle loops:

- `HudHotbar.updateHotbarSounds(...)` loads idle sounds as listener-relative loops
- pitch and volume are pulled from item `HeldSounds`

Relevant file:

- `Vintagestory.Client.NoObf.HudHotbar`

### Recommendation

Treat vehicles and held-idle loops as a separate bucket after weather.

Some of these may want mono world emitters.
Some may want deliberate stereo or multichannel beds.

## 6. Existing Worldized Weather Example

### Asset

- `environment/rainwindow`

### Why it matters

The game already ships a block-driven weather ambient pattern through `BlockRainAmbient`.

Examples:

- `assets/survival/blocktypes/glass/full-plain.json`
- `assets/survival/blocktypes/glass/pane.json`

Those blocks declare:

- `ambient = "environment/rainwindow"`
- `ambientSoundType = "Weather"`
- `ambientBlockCount = 4`
- `ambientMaxDistanceMerge = 1`

This is the cleanest existing proof that some weather sounds already work as world-space block ambient rather than global listener-relative beds.

### Recommendation

Use this as the reference implementation style for future in-world weather replacements.

## Working Priorities

### Priority 1

- Weather beds:
  - `wind-leafy`
  - `wind-leafless`
  - `rain-leafy`
  - `rain-leafless`
  - `hail`
  - `verylowtremble`

### Priority 2

- Vehicle / raft loops
- any held-item idle loops that remain stereo and feel screen-space

### Priority 3

- UI sounds
- underwater
- temporal stability

These likely need design decisions, not automatic worldization.

## Immediate Next Cataloging Tasks

- Regenerate the sound-audit summary with the newer volume/pitch/range rollups.
- Find the exact weather audio driver that creates the listener-relative weather loops.
- Find the exact temporal-stability sound driver.
- Build a "target behavior" matrix for each stereo asset:
  - keep listener-relative
  - keep as multichannel bed
  - convert to mono in-world emitters
