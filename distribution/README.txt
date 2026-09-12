Surround Sound - experimental Windows spatial add-on

Requires Windows x64, the Vintage Story version listed in package.json, and a
Windows spatial audio endpoint. For an Atmos receiver/soundbar, select that HDMI
output as the Windows default and enable Dolby Atmos for home theater before setup.
This is a mixed 7.1.4 channel bed sent through Windows, not encoded-file passthrough
or a separate dynamic Atmos object for every game sound.

INSTALL
Extract the entire add-on ZIP to a folder you can keep. Close the game. Double-click
Install.cmd, select your game and data folders, review the files shown and type YES.
Windows PowerShell 5.1 is included with Windows; no developer tools are needed.
Setup installs the matching mod ZIP too. Keep only one Surround Sound mod in Mods.
The silent device check takes five seconds. It checks stream activation, not the
format displayed by your receiver. Inspect that display separately.

Launch the game using your ordinary shortcut. With a custom data directory, keep
--dataPath "your data folder" in that shortcut. Pitch tilt is enabled by default and can be switched
off. Existing explicit settings are preserved. Stereo/surround weather beds keep their authored horizontal speaker channels.
The add-on does not require an everyday special launcher.

Protected game directories may require running Install.cmd as administrator.
Setup does not elevate itself or terminate a running game. It does not change
Windows audio settings or machine-wide environment variables.

UPDATE AND RESTORE
Rerun setup from a newer matching package to update. An identical native DLL is
left in place; the original backup is retained through repeat and mod-only updates.
Keep the game's SurroundSpatialSetup folder: it contains original files and the
restore record. Keep this extracted add-on folder for its restore/diagnostic tools.
Close the game and double-click Restore.cmd to restore original DLL, mod and
configuration. The latest mod settings are also saved under operations before
restore. Unrelated mods and worlds are untouched. Restore does not need working
audio hardware or the original package's native payload.

If setup is interrupted, rerun it with the same directories to recover before
continuing, or run the recovery command below. If a tracked DLL, ZIP or OpenAL
configuration was changed externally, setup explains the conflict and preserves it.
Do not delete the backup folder to bypass a conflict.

Users of the developer spatial prototype must run its original restore script
before adopting this public installer. Setup refuses to label an already-patched
DLL as the original. Game updates require a separately qualified package; this
installer does not repair or replace native files automatically after updates.

OPTIONAL DIAGNOSTICS
Diagnostic.cmd starts the installed game with native logging to
SurroundSpatialSetup/AudioLogs. Conventional.cmd starts ordinary audio using the
patched DLL and saves the mod's OutputMode as Auto. To re-enable spatial output,
use Diagnostic.cmd once or select WindowsSpatialAudio in the mod and restart.
Full restoration is a separate operation (Restore.cmd).

ADVANCED COMMANDS (Windows PowerShell, from this extracted folder)
Preview: .\Setup.ps1 -GamePath 'C:\Games\Vintage Story' -DataPath 'D:\VS Data'
Install: add -Apply to the preview command.
Restore: .\Setup.ps1 -Action Restore -GamePath '...' -DataPath '...' -Apply
Recover: .\Setup.ps1 -Action Recover -GamePath '...' -DataPath '...' -Apply
Offline file setup/testing: explicitly add -SkipDeviceCheck. This does not verify
hardware support and is not a workaround for an unsupported audio endpoint.

KNOWN LIMITS
The earlier prototype recorded a spatial callback timeout/reset failure. Sustained
gameplay and device-loss handling remain under qualification. If audio stops,
restart the game; retain logs when reporting it. Directional source tests spread
energy between speakers and are not isolated height-channel tests.

SOURCE AND NOTICES
See NOTICE.txt and notices/ for native component notices. The matching source ZIP
listed in package.json contains the full modified OpenAL source and build recipe.
Release checksums detect damaged files; they are not a publisher signature.
