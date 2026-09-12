#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Install', 'Restore', 'Recover')][string]$Action = 'Install',
    [string]$GamePath = (Join-Path $env:APPDATA 'Vintagestory'),
    [string]$DataPath = (Join-Path $env:APPDATA 'VintagestoryData'),
    [switch]$Apply,
    [switch]$Interactive,
    [switch]$SkipDeviceCheck
)
. (Join-Path $PSScriptRoot 'SpatialSetup.Common.ps1')
if (-not [Environment]::Is64BitProcess -or [Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'Use 64-bit Windows PowerShell on Windows x64.' }
if ($Interactive) {
    $choice = Read-Host "Game directory [Enter for $GamePath]"
    if ($choice) { $GamePath = $choice.Trim('"') }
    $choice = Read-Host "Data directory [Enter for $DataPath]"
    if ($choice) { $DataPath = $choice.Trim('"') }
}
$GamePath = [IO.Path]::GetFullPath($GamePath).TrimEnd('\')
$DataPath = [IO.Path]::GetFullPath($DataPath).TrimEnd('\')
$exe = Child-Path $GamePath 'Vintagestory.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Selected folder does not contain Vintagestory.exe.' }
$reader = New-Object IO.BinaryReader([IO.File]::OpenRead($exe))
try {
    $reader.BaseStream.Position = 0x3c
    $reader.BaseStream.Position = $reader.ReadInt32()
    if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'Select the Windows x64 Vintage Story executable.' }
} finally { $reader.Dispose() }
Assert-Closed $GamePath
$store = Child-Path $GamePath 'SurroundSpatialSetup'
$statePath = Child-Path $store 'installation.json'
$package = Read-Json (Join-Path $PSScriptRoot 'package.json')
if ($package.Schema -ne 1) { throw 'Unsupported package format.' }
if ($Action -eq 'Install') {
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
    if ($version -notin $package.GameVersions) { throw "This package supports game $($package.GameVersions -join ', '); selected game is $version." }
    foreach ($file in $package.Files) {
        $payload = Child-Path $PSScriptRoot $file.Path
        if ((File-Hash $payload) -ne $file.Sha256) { throw "Package is incomplete or changed: $($file.Path). Extract a fresh download." }
    }
    if ((Get-ModInfo (Child-Path $PSScriptRoot $package.ModPath)).version -ne $package.ModVersion) { throw 'Packaged mod version mismatch.' }
}
$state = if (Test-Path -LiteralPath $statePath) { Read-Json $statePath } else { $null }
if ($state) { Assert-State $state $GamePath $DataPath }
$pending = Test-Path -LiteralPath (Child-Path $store 'pending.json')
$modName = Find-Mod $DataPath
if ($state -and ($state.Installed -or $pending)) {
    $trackedMod = @($state.Files | Where-Object Kind -eq 'Mod')[0].Name
    if ($modName -and $modName -ne $trackedMod) { throw "Installed mod moved to $modName. Restore or resolve the tracked archive $trackedMod first." }
    $modName = $trackedMod
}
if (-not $modName) { $modName = 'vintagestorysurroundsound_' + $package.ModVersion + '.zip' }
$records = @(
    [pscustomobject]@{ Kind = 'Native'; Name = ''; Backup = $null; OriginalHash = $null; InstalledHash = $null },
    [pscustomobject]@{ Kind = 'Mod'; Name = $modName; Backup = $null; OriginalHash = $null; InstalledHash = $null },
    [pscustomobject]@{ Kind = 'Config'; Name = ''; Backup = $null; OriginalHash = $null; InstalledHash = $null },
    [pscustomobject]@{ Kind = 'Startup'; Name = ''; Backup = $null; OriginalHash = $null; InstalledHash = $null }
)
if ($state -and ($state.Installed -or $pending)) { $records = $state.Files }
$allowed = @($records | ForEach-Object { Record-Path $_ $GamePath $DataPath }) + @($statePath)
Write-Host "$Action for $GamePath (data: $DataPath)"
foreach ($path in $allowed) { Write-Host "  $path" }
if (-not $Apply) { Write-Host 'Preview only. Use -Apply to perform this operation.'; return }
if ($Interactive -and (Read-Host 'Type YES to continue') -cne 'YES') { Write-Host 'Cancelled.'; return }
New-Item -ItemType Directory -Path $store -Force | Out-Null
$lock = $null
try {
    try { $lock = [IO.File]::Open((Child-Path $store 'setup.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw "Setup is already running or this directory is not writable: $store. For a protected installation, run setup as administrator." }
    Recover-Transaction $store $allowed
    if ($Action -eq 'Recover') { return }
    # Recovery may have reverted installation.json. Reload before planning writes.
    $state = if (Test-Path -LiteralPath $statePath) { Read-Json $statePath } else { $null }
    if ($state) { Assert-State $state $GamePath $DataPath }
    if ($state -and $state.Installed) { $records = $state.Files }
    if ($Action -eq 'Restore' -and (-not $state -or -not $state.Installed)) { Write-Host 'No active spatial installation to restore.'; return }
    if ($state -and $state.Installed) {
        foreach ($record in $records) {
            $path = Record-Path $record $GamePath $DataPath
            if ($record.Kind -ne 'Config' -and (File-Hash $path) -ne $record.InstalledHash) {
                throw "File changed since setup: $path. Preserve or resolve your change before proceeding."
            }
            if ($record.Backup -and (File-Hash (Child-Path $store $record.Backup)) -ne $record.OriginalHash) { throw 'Original backup is missing or damaged.' }
        }
    }
    if ($Action -eq 'Install' -and (-not $state -or -not $state.Installed)) {
        $nativeHash = File-Hash (Record-Path ($records | Where-Object Kind -eq 'Native') $GamePath $DataPath)
        $startupFile = Record-Path ($records | Where-Object Kind -eq 'Startup') $GamePath $DataPath
        $spatialConfigured = (Test-Path -LiteralPath $startupFile) -and
            ((Get-Content -LiteralPath $startupFile -Raw) -match '(?im)^\s*spatial-api\s*=\s*true\s*$')
        $developerActive = $false
        $developerRoot = Child-Path $DataPath 'SurroundSpatialTest'
        if (Test-Path -LiteralPath $developerRoot) {
            foreach ($directory in Get-ChildItem -LiteralPath $developerRoot -Directory) {
                $backupPath = Child-Path $developerRoot ($directory.Name + '\backup.json')
                if (-not (Test-Path -LiteralPath $backupPath)) { continue }
                $developer = Read-Json $backupPath
                if ($developer.GamePath -eq $GamePath -and @($developer.Files | Where-Object {
                    $_.Path -eq (Join-Path $GamePath 'Lib\OpenAL32.dll') -and $_.InstalledHash -eq $nativeHash
                }).Count) { $developerActive = $true }
            }
        }
        if ($developerActive -or $spatialConfigured) {
            throw 'An existing spatial setup has no public restore record. Use its original restore procedure before adopting this installer; existing files were not changed.'
        }
    }
    $work = Child-Path $store ('operations\' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $changes = @()
    if ($Action -eq 'Install') {
        if (-not $SkipDeviceCheck) {
            Invoke-DeviceCheck $PSScriptRoot (Child-Path $PSScriptRoot $package.NativePath) $work
        } else { Write-Host 'Device check skipped explicitly; hardware support has not been verified.' }
        $configPath = Record-Path ($records | Where-Object Kind -eq 'Config') $GamePath $DataPath
        $config = if (Test-Path -LiteralPath $configPath) { Read-Json $configPath } else { [pscustomobject]@{} }
        $config | Add-Member -NotePropertyName OutputMode -NotePropertyValue 8 -Force
        Write-Json (Join-Path $work 'config.json') $config
        $startupPath = Record-Path ($records | Where-Object Kind -eq 'Startup') $GamePath $DataPath
        $template = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'spatial.ini') -Raw).Trim()
        $existing = if (Test-Path -LiteralPath $startupPath) { Get-Content -LiteralPath $startupPath -Raw } else { '' }
        if (-not $existing.TrimEnd().EndsWith($template, [StringComparison]::Ordinal)) { $existing = $existing.TrimEnd() + "`r`n`r`n" + $template + "`r`n" }
        [IO.File]::WriteAllText((Join-Path $work 'alsoft.ini'), $existing, (New-Object Text.UTF8Encoding($false)))
        $sources = @{
            Native = (Child-Path $PSScriptRoot $package.NativePath)
            Mod = (Child-Path $PSScriptRoot $package.ModPath)
            Config = (Join-Path $work 'config.json')
            Startup = (Join-Path $work 'alsoft.ini')
        }
        if (-not $state -or -not $state.Installed) {
            # Never silently adopt the developer prototype or another native install:
            # an identical DLL without our record is not an original restore point.
            if ((File-Hash (Record-Path $records[0] $GamePath $DataPath)) -eq (File-Hash $sources.Native)) {
                throw 'This runtime is already installed without a public setup record. Restore the developer test installation first, then run this setup.'
            }
            $originals = 'originals\' + [Guid]::NewGuid().ToString('N')
            New-Item -ItemType Directory -Path (Child-Path $store $originals) -Force | Out-Null
            foreach ($record in $records) {
                $path = Record-Path $record $GamePath $DataPath
                $record.Backup = $null
                $record.OriginalHash = File-Hash $path
                if ($record.OriginalHash) {
                    $record.Backup = $originals + '\' + $record.Kind + '.bak'
                    Copy-Item -LiteralPath $path -Destination (Child-Path $store $record.Backup)
                    if ((File-Hash (Child-Path $store $record.Backup)) -ne $record.OriginalHash) { throw 'Original backup verification failed.' }
                }
            }
        }
        foreach ($record in $records) {
            $record.InstalledHash = File-Hash $sources[$record.Kind]
            $changes += @{ Path = (Record-Path $record $GamePath $DataPath); Source = $sources[$record.Kind] }
        }
        $state = [pscustomobject]@{ Schema = 1; Installed = $true; GamePath = $GamePath; DataPath = $DataPath; ModVersion = $package.ModVersion; RuntimeVersion = $package.RuntimeVersion; Files = $records }
    } else {
        $configPath = Record-Path ($records | Where-Object Kind -eq 'Config') $GamePath $DataPath
        if (Test-Path -LiteralPath $configPath) { Copy-Item -LiteralPath $configPath -Destination (Join-Path $work 'latest-user-config.json') }
        foreach ($record in $records) {
            $source = if ($record.Backup) { Child-Path $store $record.Backup } else { $null }
            $changes += @{ Path = (Record-Path $record $GamePath $DataPath); Source = $source }
        }
        $state.Installed = $false
    }
    $preparedState = Join-Path $work 'installation.json'
    Write-Json $preparedState $state
    $changes += @{ Path = $statePath; Source = $preparedState }
    Assert-Closed $GamePath
    Invoke-Transaction $store $changes $allowed
    Write-Host "$Action completed and file hashes verified. Restore records: $store"
    if ($Action -eq 'Install') {
        Write-Host 'Launch Vintage Story normally. For a custom data folder, retain --dataPath in your normal shortcut.'
        Write-Host 'Stream activation does not verify the receiver input format. Check its display and listen to the spatial tests.'
    }
} finally { if ($lock) { $lock.Dispose() } }
