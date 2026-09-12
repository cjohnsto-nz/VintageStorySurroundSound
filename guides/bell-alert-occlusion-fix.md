# Bell alarm occlusion correction (issue #5)

Vanilla 1.22.7 `EntityBell.OnReceivedServerPacket` handles packet 1025 by
creating a reusable looping entity sound at volume zero, starting it, then
fading it in over 0.25 seconds. Packet 1026, death and despawn fade it out.

The mod previously captured `Params.Volume` during `Start()` and retained it
as a fixed entity base volume. For the Bell this was zero. A subsequent
occlusion refresh (500 ms by default) reapplied zero volume, silencing the
still-playing loop after vanilla's fade-in. Static sound refreshes also used
the already-attenuated `Params.Volume`, allowing attenuation to compound.

The correction stores only an occlusion multiplier per sound. It applies that
multiplier to vanilla's category gain at native source creation and both
`SetVolume` overloads. Occlusion refresh calls parameterless `SetVolume()`;
it does not modify vanilla's volume or fade state. Entity tracking no longer
captures a base volume. Ordinary untracked sounds use a multiplier of one.

## Verification

```powershell
dotnet run --project tests/Occlusion.Tests -c Release -- 'C:\path\to\Vintagestory'
```

The integration harness uses the installed game's actual `LoadedSoundNative`
class, Harmony patching and native OpenAL gain/state queries. It uses a silent
null backend and an isolated settings directory under its build output; it
does not launch the game, use the receiver or modify user settings.

All 16 checks passed against Vintage Story 1.22.7: zero-volume startup, actual
vanilla fade-in/out, repeated refreshes, playback beyond the buffer duration,
obstruction removal, volume changes, category slider changes, alarm reuse,
native source recreation, unaffected untracked audio and clearing occlusion.
The Release mod build passed with zero warnings/errors.

The harness follows the Bell's playback lifecycle but does not run its AI or
network packets. In-game confirmation with a Bell is still needed. No game
installation, issue closure or publication was performed by this fix.
