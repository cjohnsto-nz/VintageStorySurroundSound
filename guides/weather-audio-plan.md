# Weather Audio Plan

## Purpose

This document defines the target architecture for replacing listener-relative weather beds with a more modern surround approach.

The goal is not to make every weather sound a point source. The goal is to combine:

- a stable far-field surround bed
- localized mono emitters for nearby detail
- state-driven filtering and blending for shelter, foliage, and exposure

This matches the common AAA ambience pattern of blending 2D beds with 3D spots rather than choosing only one or the other.

## Design Principles

- Keep the far-field weather readable and continuous.
- Make nearby detail feel grounded in the world.
- Avoid per-block emitters.
- Use a small, budgeted emitter pool around the listener.
- Reuse existing game signals whenever possible.
- Design one weather framework that can be extended to rain, hail, snow, wind, and foliage.

## Industry Pattern

The most useful reference pattern is the ambience workflow described by Audiokinetic:

- use 2D beds to provide a stable ambient layer
- use 3D spots and one-shots to provide local detail
- drive transitions with gameplay state, shelter, and exposure

Sources:

- https://www.audiokinetic.com/en/blog/designing-ambience-systems-in-wwise/
- https://www.audiokinetic.com/en/public-library/library_1.0.0_336_strata/?id=track_layers_specific_to_the_ambience_detailed_rain_collection&source=StrataLibrary

For rain specifically, Strata's Detailed Rain collection is a strong reference because it separates:

- base texture
- reverb texture
- drop texture
- intensity and wetness control

That maps cleanly to the hybrid system we want in Vintage Story.

## Target Runtime Model

### Shared weather layers

Every weather type should be expressible through the same layer model:

1. `FarFieldBed`
   A listener-relative surround bed that communicates the broad weather state.

2. `NearDetailEmitters`
   A pooled set of mono world emitters placed around the player to add local detail.

3. `OverheadLayer`
   A special layer for roof, canopy, cave-mouth, or shelter interactions.

4. `SurfaceLayer`
   Surface-specific variants such as leaves, dirt, stone, wood, metal, or water.

5. `LowFrequencyLayer`
   Optional low-energy component for thunder, distant storm mass, or heavy gust pressure.

6. `Sweeteners`
   Short one-shots for gusts, branch snaps, roof rattles, drips, runoff, and similar events.

### Shared state inputs

Every weather family should be driven by the same state bundle:

- global weather intensity
- weather type
- wind speed
- wind direction if available
- listener shelter factor
- listener sky exposure
- nearby leaf density
- nearby surface composition
- player indoor / outdoor status
- listener height and biome context if useful

## Runtime Systems

### 1. `WeatherAcousticState`

Central derived state updated at a low fixed rate such as `4-10 Hz`.

Responsibilities:

- read weather event type and intensity
- compute shelter and exposure
- compute nearby leaf density
- compute nearby surface mix
- expose normalized values for downstream layers

### 2. `WeatherBedController`

Owns the far-field surround beds.

Responsibilities:

- start and stop the correct bed family
- blend intensity variations
- adjust volume, EQ, and width based on shelter and exposure
- avoid abrupt restarts when weather changes

### 3. `WeatherEmitterField`

Owns a fixed-size pool of mono emitters near the listener.

Responsibilities:

- spawn from a budget, not per block
- choose candidate positions around the player
- score candidates by surface and exposure
- schedule randomized one-shots or short loops
- maintain voice limits and cooldowns

### 4. `WeatherSurfaceResolver`

Maps sampled positions to surface classes.

Initial surface classes:

- leaves
- dirt
- stone
- wood
- metal
- water
- roof / interior overhead

### 5. `WeatherOcclusionModel`

Controls sheltered and indoor behavior.

Responsibilities:

- reduce far rain bed when under solid cover
- boost roof patter or drip detail when appropriate
- low-pass or darken exterior layers when indoors

## Rain First

Rain is the first target because:

- it already exists as a small number of stereo weather beds
- it has clear leafy and leafless variants
- it benefits strongly from a hybrid bed + spot approach
- it provides a reusable template for hail and snow

### Rain layer breakdown

1. `RainFarField`
   Exterior surround rain wash.

2. `RainCanopy`
   Foliage-heavy layer for nearby leaves, branches, and drip texture.

3. `RainImpacts`
   Mono world emitters for nearby impact surfaces.

4. `RainRoof`
   Shelter-specific roof and overhead patter.

5. `RainLow`
   Low storm mass or distant thunder support, not constant LFE-heavy rain.

### Rain emitter strategy

Do not emit from every wet block.

Use a pool such as:

- `0-2` roof / overhead emitters
- `4-8` near detail emitters
- `0-2` water or runoff emitters

Candidate placement rules:

- pick positions in a ring around the player
- bias toward visible and exposed surfaces
- bias toward leaves when nearby leaf density is high
- avoid clustering several emitters into the same tiny area
- resample slowly enough to feel stable

### Rain state parameters

Recommended normalized controls:

- `RainIntensity`
- `CanopyFactor`
- `ShelterFactor`
- `WetSurfaceFactor`
- `WindFactor`
- `StormMass`

These should drive:

- bed volume
- emitter spawn rate
- emitter pitch spread
- surface choice
- filtering
- sweetener frequency

## Extending to Other Weather Types

### Hail

Reuse the rain framework with:

- harder transient beds
- more metal / roof / stone emphasis
- lower emitter density but sharper impacts
- stronger roof and window sweeteners

### Snow

Use:

- softer far-field air/noise bed
- sparse mono impacts
- more wind and cloth detail than impact density
- strong shelter contrast

### Wind

Use:

- surround bed for air mass
- mono emitters for object interactions
- foliage, branch, reed, and structure surface types
- windbreak / shelter filtering near buildings and terrain

### Leaves / Foliage

Treat this as a specialized wind-driven surface family rather than a standalone weather type.

Use:

- nearby leaf density as a density scalar
- sampled foliage clusters as candidate emitter positions
- mono rustles, creaks, twig ticks, and drip one-shots

## Asset Format Recommendations

For the current mod/runtime, the most practical ingest formats are:

- discrete surround beds as `5.0` or `5.1` WAV
- mono one-shots and short loops for world emitters
- optional stereo alternates for fallback

Recommended runtime targets:

- beds: `24-bit / 48 kHz` WAV, discrete `5.0` or `5.1`
- spot emitters: `24-bit / 48 kHz` mono WAV

Ambisonics can still be useful during authoring, but our current runtime path is better suited to pre-rendered discrete surround beds plus mono spots.

## Why Not Purely Worldized Rain

A pure emitter-only system is not the usual AAA approach because:

- it is expensive
- it creates voice-management problems
- it tends to sound sparse unless heavily layered
- it loses the stable sense of "the whole world is raining"

The bed is not a compromise. It is part of the intended design.

## Recommended Implementation Order

1. Keep current rain beds as the baseline surround layer.
2. Identify the exact game logic that drives rainy weather loops.
3. Introduce a separate rain controller without deleting the current beds yet.
4. Add a small mono emitter pool for rain impacts.
5. Add canopy-specific emitters using nearby leaf density.
6. Add shelter / roof treatment.
7. Compare against the original beds and then decide what legacy stereo content to retire.

## Vintage Story-Specific Hooks To Reuse

- existing weather intensity and event type
- existing weather category gain controls
- `SystemClientTickingBlocks`
- `GlobalConstants.CurrentNearbyRelLeavesCountClient`
- ambient block clustering patterns already used by `AmbientSound`

## Open Questions

- Where exactly are the current rain and wind beds started and updated in the client code?
- Which signals already exist for shelter and sky exposure?
- Can we reuse existing ambient cluster logic for foliage-driven emitters, or is a lighter custom sampler better?
- How much emitter budget is safe before we lose the simplicity we just gained by enabling surround cleanly?
