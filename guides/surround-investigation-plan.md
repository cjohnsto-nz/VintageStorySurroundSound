# Surround Sound Plan

## Summary

- OpenAL Soft on this machine exposes multichannel-related extensions and format enums.
- Vintage Story's game-owned context can now be recreated into a multichannel mode and is working in real `5.1` on this machine.
- Stereo output enablement is no longer the main problem.
- The next major phase is cataloging and then selectively worldizing stereo effects that currently play as listener-relative stereo beds.

## Active Implementation Steps

### Phase 1: Keep surround output stable and package-friendly

- Keep both `Game Context` and `Lab Context` test modes.
- Keep the low-frequency LFE tone for `5.1` channel 4.
- Keep diagnostics opt-in and user-facing logging quiet by default.
- Keep user-selectable output modes and stereo upmix working.

### Phase 2: Build a stereo-effect catalog

- Use sound-audit summaries to identify every observed stereo asset.
- Trace each stereo asset back to the decompiled game logic or asset JSON that drives it.
- Record trigger conditions, volume behavior, range, loop/dispose behavior, and sound category.
- Split assets into buckets:
  - UI / non-world
  - player-state / headspace
  - weather beds
  - status effects
  - held / vehicle loops

### Phase 3: Define target behavior per stereo asset family

- Do not convert everything to mono world emitters.
- For each family, decide whether the target should be:
  - listener-relative
  - a deliberate multichannel bed
  - mono in-world emitters
- Use `stereo-effect-catalog.md` as the decision log.

### Phase 4: Prototype worldized weather first

- Start with weather beds, especially leafy vs leafless wind/rain.
- Reuse existing engine signals where possible:
  - weather intensity
  - weather event type
  - nearby leaf density
  - wind-affectedness
- Favor chance-based mono emitters over one-loop-per-block designs.
- Use the new `weather-audio-plan.md` as the target architecture for rain first and then hail, wind, and foliage.
- Treat rain as a hybrid system:
  - far-field surround bed
  - canopy / overhead layer
  - pooled mono detail emitters
  - shelter-aware filtering and blending

### Phase 5: Implement one family at a time

- Make one replacement family at a time.
- After each family:
  - deploy
  - playtest
  - audit
  - compare behavior against the previous bed

### Phase 6: Keep tooling useful for the grind

- Keep session JSONL logging opt-in.
- Keep the aggregated sound-audit summary report.
- Keep the channel-mask controls for isolating non-mono beds.
- Keep adding catalog-friendly fields to the audit when needed.

## Tooling

- `F9` opens the debug panel.
- The panel supports both `Game Context` and `Lab Context` test modes.
- Reports remain under `VintagestoryData/Logs/VintageStorySurroundSound`.
- Structured session logs are written as JSONL in the same folder.
- Detailed implementation notes live in `engine-multichannel-investigation.md`.
- Sound-audit strategy now lives in `sound-audit-strategy.md`.
- Stereo conversion inventory now lives in `stereo-effect-catalog.md`.
- Weather replacement architecture now lives in `weather-audio-plan.md`.
- Candidate library research now lives in `weather-library-research.md`.
