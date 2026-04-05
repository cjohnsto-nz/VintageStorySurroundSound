# Weather Library Research

## Goal

Find source libraries that fit the runtime shape we actually need for Vintage Story weather replacement work.

For this mod, the most useful source material is:

- discrete `5.0` or `5.1` surround beds
- mono spot elements for impacts, rustles, drips, and branches
- optionally ambisonic masters that can be rendered down to discrete surround

## Practical Format Fit

### Best fit for runtime

- `5.0` or `5.1` WAV beds
- mono one-shots and mono loops

### Good fit for authoring, less direct for runtime

- AmbiX / ambisonic recordings that can be rendered offline to `5.0` or `5.1`
- multitrack authoring collections that can be rendered to runtime-safe stems

## Strong Candidates

## 1. BOOM Library - Thunder & Rain

Why it stands out:

- explicit surround edition
- rain, thunder, and storm recordings in one package
- designed as a professional nature ambience library

Published details:

- 66 files
- 24-bit / 48 kHz WAV
- surround edition includes `5.0` plus stereo
- includes 35 rain intensities, 22 isolated thunder recordings, and 9 storm recordings

Best use here:

- far-field rain bed
- storm mass
- thunder layer

Source:

- https://www.boomlibrary.com/sound-effects/thunder-rain/

## 2. Mindful Audio - Wild Rain

Why it stands out:

- rain on vegetation and forest floor
- explicit surround files
- very relevant to leafy outdoor environments

Published details:

- 69 stereo and 69 surround WAV files
- 24-bit / 96 kHz
- surround delivered as `5.0`
- described as rainforest rain on vegetation and forest floor, from sparse to heavy rain

Best use here:

- leafy rain bed reference
- canopy rain tone
- forest-floor rain bed

Source:

- https://mindful-audio.com/wild-rain

## 3. Sonik Sound Library - Spatial Rain & Thunders

Why it stands out:

- ships with AmbiX originals plus rendered surround
- directly useful if we want both authoring flexibility and runtime-ready beds

Published details:

- 60 files
- 24-bit / 48 kHz
- formats listed as stereo, surround, and ambisonic
- library description says AmbiX recordings can be decoded to mono, stereo, binaural, quad, `5.0`, `5.1`, `7.1`, and more
- includes supplied `5.0` and stereo versions

Best use here:

- prototype beds
- offline render workflow experiments
- thunder and hail environment layers

Source:

- https://sonniss.com/sound-effects/spatial-rain-thunders/

## 4. Audiokinetic Strata - Ambience Detailed Rain

Why it stands out:

- strongest implementation reference
- not just files, but a fully layered rain design system

Published details:

- ambisonic rain collection guide exposes distinct layers for:
  - base texture
  - reverb texture
  - drop texture
  - intensity and wetness control
- subprojects are designed for multichannel output routing

Best use here:

- implementation reference
- authoring inspiration
- possibly source material if licensing and integration model are acceptable

Important caution:

Strata licensing is more restrictive than a typical simple royalty-free pack. The license allows rendered sounds to be synchronized into user-developed products, but it also explicitly restricts redistribution of licensed sounds as standalone files or in a way that invites extraction. For a mod that ships loose or easily extractable assets, this needs very careful review before using Strata-origin material directly.

Sources:

- https://www.audiokinetic.com/en/public-library/library_1.0.0_336_strata/?id=track_layers_specific_to_the_ambience_detailed_rain_collection&source=StrataLibrary
- https://www.audiokinetic.com/download/documents/License_Agreements/Audiokinetic_Strata_Click_Through_License_Agreement_2022-09-23.pdf

## 5. BOOM Library - Winds of Nature

Why it stands out:

- direct fit for the next phase after rain
- explicit surround wind and foliage movement

Published details:

- 130 sounds in stereo edition, 260 files in surround edition
- 24-bit / 48 kHz WAV
- surround version includes binaural stereo plus `5.0`
- specifically mentions rustling leaves, pine hiss, and natural winds

Best use here:

- wind bed replacement
- foliage movement reference
- future leaf/canopy work

Source:

- https://www.boomlibrary.com/sound-effects/winds-of-nature/

## 6. Dramatic Cat - Rustling Winds

Why it stands out:

- explicit foliage and vegetation motion library
- includes surround files

Published details:

- 53 files
- 24-bit / 96 kHz
- 43 stereo files and 10 surround files
- surround delivered as `5.0`
- includes bamboo, forest canopies, shrubs, grass, reeds, and tree movement

Best use here:

- leaf and foliage replacement work
- canopy sweeteners
- wind-driven vegetation detail

Source:

- https://sonniss.com/sound-effects/rustling-winds/

## 7. BOOM Library - RAIN plug-in

Why it stands out:

- not a library, but a procedural authoring option
- useful if we decide to render custom rain beds instead of licensing fixed loops

Published details:

- marketed as a dynamic rain generator
- intended to generate rain scenes ranging from city downpours to wetland trickles

Best use here:

- authoring and rendering custom beds
- not a direct runtime dependency

Source:

- https://www.boomlibrary.com/sound-effects/the-complete-boom-tools/

## Recommendation By Use Case

### Best immediate rain bed candidates

- BOOM Thunder & Rain
- Mindful Audio Wild Rain
- Sonik Spatial Rain & Thunders

### Best reference for how to structure the system

- Audiokinetic Strata Detailed Rain

### Best follow-up libraries for wind / leaf work

- BOOM Winds of Nature
- Rustling Winds

## Gaps Still Not Solved By A Single Library

I did not find one clearly ideal off-the-shelf pack that gives all of the following in a single game-ready bundle:

- discrete surround rain beds
- mono surface-specific rain impacts
- leafy canopy variants
- roof / shelter patterns
- wind-driven foliage sweeteners

That means the most realistic path is probably hybrid:

- license or create surround beds
- author or source mono spot details separately
- build the runtime logic ourselves

## Licensing Risk Notes

I am not giving legal advice, but there is one practical rule we should assume:

- if a license forbids redistributing source files or standalone sounds, we should not ship those files in a way that players can trivially extract them from the mod package

This matters especially for:

- Strata collections
- any library that only permits synchronized use in a finished product

For the safest shipping story, we should prefer libraries whose terms clearly support game distribution, and we should assume we may need to render, edit, or derive final assets rather than shipping purchased source libraries verbatim.

## Suggested Acquisition Strategy

1. Use Strata rain documentation as the structural design reference.
2. Choose one surround rain bed library for fast prototyping.
3. Choose one wind / foliage library for the next phase.
4. Build our own mono spot set for impacts and leaf detail if licensing or library shape makes direct reuse awkward.
