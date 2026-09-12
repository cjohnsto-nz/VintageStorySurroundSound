#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AddonPath,
    [string]$GamePath = (Join-Path $env:APPDATA 'Vintagestory'),
    [string]$OriginalDllPath,
    [switch]$ProbeDevice
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $root ('bin\installer-tests\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$game = Join-Path $testRoot 'Game With Spaces'
$data = Join-Path $testRoot 'Custom Data'
$unpacked = Join-Path $testRoot 'Extracted Addon'
New-Item -ItemType Directory -Path (Join-Path $game 'Lib'),(Join-Path $data 'Mods'),(Join-Path $data 'ModConfig'),$unpacked -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path -LiteralPath $AddonPath).Path, $unpacked)
. (Join-Path $unpacked 'SpatialSetup.Common.ps1')
Copy-Item -LiteralPath (Join-Path $GamePath 'Vintagestory.exe') -Destination $game
# A file fixture, not a runnable game. A distinct placeholder is sufficient for
# exact restore testing; pass the original DLL to also exercise real payload bytes.
$native = Join-Path $game 'Lib\OpenAL32.dll'
if ($OriginalDllPath) { Copy-Item -LiteralPath $OriginalDllPath -Destination $native }
else { [IO.File]::WriteAllText($native, 'original runtime fixture') }
$startup = Join-Path $game 'alsoft.ini'
[IO.File]::WriteAllText($startup, "# user's pre-existing options`r`n[general]`r`nperiods = 3`r`n")
$configPath = Join-Path $data 'ModConfig\vintagestorysurroundsound.json'
Write-Json $configPath @{ OutputMode = 7; FollowCameraPitch = $true; UserSentinel = 'keep me' }
$package = Read-Json (Join-Path $unpacked 'package.json')
$oldMod = Join-Path $data 'Mods\custom-old-name.zip'
Copy-Item -LiteralPath (Child-Path $unpacked $package.ModPath) -Destination $oldMod
[IO.File]::WriteAllText((Join-Path $data 'Mods\unrelated.txt'), 'untouched')
$originalHashes = @{}
foreach ($path in @($native, $startup, $configPath, $oldMod)) { $originalHashes[$path] = File-Hash $path }
$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$script:checks = 0
$script:probePending = [bool]$ProbeDevice
function Check($Ok, $Name) {
    if (-not $Ok) { throw "FAIL: $Name" }
    $script:checks++
    Write-Host "PASS: $Name"
}
function Run-Setup($Action = 'Install', [switch]$Fail, [switch]$Preview) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $unpacked 'Setup.ps1'),
        '-GamePath', $game, '-DataPath', $data, '-Action', $Action)
    $checkDevice = $script:probePending -and $Action -eq 'Install' -and -not $Preview -and -not $Fail
    if (-not $checkDevice) { $arguments += '-SkipDeviceCheck' }
    if (-not $Preview) { $arguments += '-Apply' }
    # No developer toolchain in PATH: distribution must use Windows facilities.
    $previousPath = $env:PATH
    try {
        $env:PATH = "$env:WINDIR\System32;$env:WINDIR\System32\WindowsPowerShell\v1.0"
        $ErrorActionPreference = 'Continue'
        $output = & $powershell @arguments 2>&1
        $exitCode = $LASTEXITCODE
    } finally { $env:PATH = $previousPath; $ErrorActionPreference = 'Stop' }
    $output | Out-File -LiteralPath (Join-Path $testRoot ('setup-' + $Action + '-' + [Guid]::NewGuid().ToString('N') + '.log'))
    if ($Fail) { Check ($exitCode -ne 0) "$Action rejected invalid input" }
    elseif ($exitCode -ne 0) { throw ($output -join "`n") }
    if ($checkDevice -and $exitCode -eq 0) { $script:probePending = $false }
}
Run-Setup -Preview
Check (-not (Test-Path -LiteralPath (Join-Path $game 'SurroundSpatialSetup'))) 'Preview does not create installation state'
Run-Setup
$store = Join-Path $game 'SurroundSpatialSetup'
$statePath = Join-Path $store 'installation.json'
$firstState = Read-Json $statePath
Check ($firstState.Installed -and (Read-Json $configPath).OutputMode -eq 8) 'Fresh install selects spatial output'
Check ((Read-Json $configPath).UserSentinel -eq 'keep me' -and (Read-Json $configPath).FollowCameraPitch) 'Install preserves existing user settings'
Check ((File-Hash $native) -eq (File-Hash (Child-Path $unpacked $package.NativePath))) 'Installed native payload hash matches extracted package'
$nativeWriteTime = (Get-Item -LiteralPath $native).LastWriteTimeUtc
$config = Read-Json $configPath
$config.UserSentinel = 'edited after setup'
Write-Json $configPath $config
Run-Setup
$repeatState = Read-Json $statePath
Check (($firstState.Files.Backup -join '|') -eq ($repeatState.Files.Backup -join '|')) 'Repeat setup retains the original restore point'
Check ((Get-Item -LiteralPath $native).LastWriteTimeUtc -eq $nativeWriteTime) 'Repeat setup leaves identical runtime in place'
Check ((Read-Json $configPath).UserSentinel -eq 'edited after setup') 'Repeat setup retains later user configuration edits'
# Simulate a mod-only package update, rehashing the distribution manifest exactly
# as the builder does; the public installer has no testing bypass for verification.
$packagedMod = Child-Path $unpacked $package.ModPath
$zip = [IO.Compression.ZipFile]::Open($packagedMod, 'Update')
try {
    $entry = $zip.CreateEntry('installer-update-test.txt')
    $writer = New-Object IO.StreamWriter($entry.Open())
    try { $writer.Write('mod-only update fixture') } finally { $writer.Dispose() }
} finally { $zip.Dispose() }
($package.Files | Where-Object Path -eq $package.ModPath).Sha256 = File-Hash $packagedMod
Write-Json (Join-Path $unpacked 'package.json') $package
Run-Setup
Check ((File-Hash $oldMod) -eq (File-Hash $packagedMod) -and (Get-Item -LiteralPath $native).LastWriteTimeUtc -eq $nativeWriteTime) 'Mod-only update changes the ZIP without replacing the runtime'
Check (($firstState.Files.Backup -join '|') -eq ((Read-Json $statePath).Files.Backup -join '|')) 'Mod-only update retains pre-install backups'
$duplicate = Join-Path $data 'Mods\duplicate.zip'
Copy-Item -LiteralPath $oldMod -Destination $duplicate
Run-Setup -Fail
Remove-Item -LiteralPath $duplicate
# Externally changed startup config is protected, even during restore.
$startupBytes = [IO.File]::ReadAllBytes($startup)
[IO.File]::AppendAllText($startup, '# external change')
Run-Setup -Action Restore -Fail
Check ((Get-Content -LiteralPath $startup -Raw).Contains('# external change')) 'Restore preserves conflicting external file edits'
[IO.File]::WriteAllBytes($startup, $startupBytes)
$payload = Child-Path $unpacked $package.NativePath
$payloadBytes = [IO.File]::ReadAllBytes($payload)
[IO.File]::AppendAllText($payload, 'damaged')
Run-Setup -Fail
[IO.File]::WriteAllBytes($payload, $payloadBytes)
Check ((File-Hash $native) -eq $package.Runtime.DllSha256) 'Corrupt package rejection leaves installed native file unchanged'

# Exercise real filesystem failures after an earlier write has succeeded.
$allowed = @($repeatState.Files | ForEach-Object { Record-Path $_ $game $data }) + @($statePath)
$replacement = Join-Path $testRoot 'replacement.txt'
[IO.File]::WriteAllText($replacement, 'transaction test replacement')
$nativeBefore = File-Hash $native
$lock = [IO.File]::Open($startup, 'Open', 'Read', 'Read')
$failed = $false
try {
    try { Invoke-Transaction $store @(@{ Path = $native; Source = $replacement }, @{ Path = $startup; Source = $replacement }) $allowed }
    catch { $failed = $true }
} finally { $lock.Dispose() }
Check ($failed -and (File-Hash $native) -eq $nativeBefore -and -not (Test-Path -LiteralPath (Join-Path $store 'pending.json'))) 'Mid-install sharing violation rolls back completed writes'

# Model termination after an atomic replacement but before journal completion.
$interruptedBackup = 'transactions\interrupted-before.bak'
Copy-Item -LiteralPath $native -Destination (Child-Path $store $interruptedBackup)
Write-Json (Join-Path $store 'pending.json') @{ Entries = @([pscustomobject]@{ Path = $native; Before = $interruptedBackup; BeforeHash = $nativeBefore; AfterHash = (File-Hash $replacement); Source = $replacement }) }
Copy-Item -LiteralPath $replacement -Destination $native -Force
Run-Setup -Action Recover
Check ((File-Hash $native) -eq $nativeBefore -and -not (Test-Path -LiteralPath (Join-Path $store 'pending.json'))) 'Next invocation recovers interrupted setup from durable journal'
Run-Setup -Action Restore
foreach ($path in $originalHashes.Keys) { Check ((File-Hash $path) -eq $originalHashes[$path]) ('Restore reproduces original bytes: ' + [IO.Path]::GetFileName($path)) }
Check ((Get-Content -LiteralPath (Join-Path $data 'Mods\unrelated.txt') -Raw) -eq 'untouched') 'Unrelated mod-directory files are preserved'
$savedConfigs = @(Get-ChildItem -LiteralPath (Join-Path $store 'operations') -Recurse -Filter latest-user-config.json)
Check (@($savedConfigs | Where-Object { (Read-Json $_.FullName).UserSentinel -eq 'edited after setup' }).Count -gt 0) 'Restore saves the latest user configuration separately'
Run-Setup -Action Restore
Check (-not (Read-Json $statePath).Installed) 'Repeated restore is harmless'

# A user without a prior mod/config/OpenAL INI gets added files removed on restore.
Remove-Item -LiteralPath $oldMod,$configPath,$startup
Run-Setup
$addedMod = Join-Path $data ('Mods\vintagestorysurroundsound_' + $package.ModVersion + '.zip')
Check (Test-Path -LiteralPath $addedMod) 'Fresh setup also installs the matching mod when absent'
Run-Setup -Action Restore
Check (-not (Test-Path -LiteralPath $addedMod) -and -not (Test-Path -LiteralPath $configPath) -and -not (Test-Path -LiteralPath $startup)) 'Restore removes only files introduced by setup'

# A different prototype DLL hash must not become a new "original" merely
# because the distribution was rebuilt with another compiler/path.
$legacy = Join-Path $data 'SurroundSpatialTest\legacy'
New-Item -ItemType Directory -Path $legacy -Force | Out-Null
Write-Json (Join-Path $legacy 'backup.json') @{ GamePath = $game; Files = @(@{ Path = $native; InstalledHash = (File-Hash $native) }) }
Run-Setup -Fail
Check ((File-Hash $native) -eq $originalHashes[$native]) 'Existing developer setup is rejected even when its runtime hash differs from the package'
Remove-Item -LiteralPath (Join-Path $legacy 'backup.json')

if ($ProbeDevice) {
    Check (-not $script:probePending) 'Windows PowerShell 5.1 installer preflight activates the packaged native runtime'
}
Write-Host "$script:checks installer checks passed under PowerShell $($PSVersionTable.PSVersion). Evidence: $testRoot"
