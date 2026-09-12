#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$GamePath = (Join-Path $env:APPDATA 'Vintagestory'),
    [string]$DataPath = (Join-Path $env:APPDATA 'VintagestoryData')
)
$ErrorActionPreference = 'Stop'
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
$DataPath = (Resolve-Path -LiteralPath $DataPath).Path
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameExe = Join-Path $GamePath 'Vintagestory.exe'
if (Get-Process Vintagestory -ErrorAction SilentlyContinue | Where-Object Path -eq $gameExe) { throw 'Close the installed game first.' }
dotnet build (Join-Path $root 'VintageStorySurroundSound.csproj') -c Release "-p:GamePath=$GamePath" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
$runtime = & (Join-Path $PSScriptRoot 'Get-SpatialRuntime.ps1')
$package = Join-Path $root 'bin\Release\ModPackage\VintageStorySurroundSound'
$version = (Get-Content -LiteralPath (Join-Path $root 'modinfo.json') -Raw | ConvertFrom-Json).version
$zipPath = Join-Path $root ('bin\Release\vintagestorysurroundsound_' + $version + '-spatial-test.zip')
# ZipFile uses portable forward-slash entry paths.
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
[IO.Compression.ZipFile]::CreateFromDirectory($package, $zipPath)
$modDirectory = Join-Path $DataPath 'Mods'
$matching = @()
foreach ($entry in Get-ChildItem -LiteralPath $modDirectory) {
    if ($entry.PSIsContainer) {
        $info = Join-Path $entry.FullName 'modinfo.json'
        if ((Test-Path -LiteralPath $info) -and (Get-Content -LiteralPath $info -Raw | ConvertFrom-Json).modid -eq 'vintagestorysurroundsound') {
            throw "Existing unpacked mod found at $($entry.FullName); package it before using this ZIP installer."
        }
    } elseif ($entry.Extension -eq '.zip') {
        $archive = [IO.Compression.ZipFile]::OpenRead($entry.FullName)
        try {
            $info = $archive.GetEntry('modinfo.json')
            if ($info) {
                $reader = [IO.StreamReader]::new($info.Open())
                try { $metadata = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                if ($metadata.modid -eq 'vintagestorysurroundsound') { $matching += $entry.FullName }
            }
        } finally { $archive.Dispose() }
    }
}
if ($matching.Count -gt 1) { throw 'Multiple installed Surround Sound archives found; resolve duplicates first.' }
$modTarget = if ($matching.Count) { $matching[0] } else { Join-Path $modDirectory ('vintagestorysurroundsound_' + $version + '.zip') }
$session = Join-Path $DataPath ('SurroundSpatialTest\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $session -Force | Out-Null
$modConfig = Join-Path $DataPath 'ModConfig\vintagestorysurroundsound.json'
$config = if (Test-Path -LiteralPath $modConfig) { Get-Content -LiteralPath $modConfig -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
$config.OutputMode = 8
$config.EnableDebugTools = $true
$preparedConfig = Join-Path $session 'mod-config.json'
$config | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $preparedConfig -Encoding utf8
$changes = @(
    @{ Path = (Join-Path $GamePath 'Lib\OpenAL32.dll'); Source = $runtime.DllPath; IsConfig = $false },
    @{ Path = $modTarget; Source = $zipPath; IsConfig = $false },
    @{ Path = $modConfig; Source = $preparedConfig; IsConfig = $true }
)
$files = @()
foreach ($change in $changes) {
    $backup = $null
    if (Test-Path -LiteralPath $change.Path) {
        $backup = Join-Path $session ('original-' + [IO.Path]::GetFileName($change.Path))
        Copy-Item -LiteralPath $change.Path -Destination $backup
    }
    $files += @{ Path = $change.Path; Backup = $backup; InstalledHash = (Get-FileHash -LiteralPath $change.Source).Hash; IsConfig = $change.IsConfig }
}
@{ GamePath = $GamePath; Files = $files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $session 'backup.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Restore-LocalSpatialAudio.ps1') -Destination $session
foreach ($change in $changes) { Copy-Item -LiteralPath $change.Source -Destination $change.Path -Force }
foreach ($file in $files) {
    if ((Get-FileHash -LiteralPath $file.Path).Hash -ne $file.InstalledHash) { throw "Installed file verification failed: $($file.Path)" }
}
@{ GamePath = $GamePath; DataPath = $DataPath; ModConfig = $modConfig; Runtime = $runtime } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $session 'installation.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Start-InstalledSpatialAudio.ps1') -Destination $session
foreach ($mode in @('spatial', 'conventional')) {
    $channels = if ($mode -eq 'spatial') { 'surround714' } else { '' }
    $enabled = if ($mode -eq 'spatial') { 'true' } else { 'false' }
    "[general]`ndrivers = wasapi`nchannels = $channels`nstereo-encoding = basic`n`n[wasapi]`nspatial-api = $enabled`n" |
        Set-Content -LiteralPath (Join-Path $session "$mode.ini") -Encoding ascii
}
Get-ChildItem -LiteralPath $runtime.SourcePath -File | Where-Object Name -Match '^(COPYING|LICENSE)' | Copy-Item -Destination $session
$desktop = [Environment]::GetFolderPath('Desktop')
$shell = New-Object -ComObject WScript.Shell
foreach ($mode in @('Spatial Audio Test', 'Standard Audio')) {
    $shortcut = $shell.CreateShortcut((Join-Path $desktop "Vintage Story - $mode.lnk"))
    $shortcut.TargetPath = (Get-Command pwsh).Source
    $shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $session 'Start-InstalledSpatialAudio.ps1') + '"' + $(if ($mode -eq 'Standard Audio') { ' -Conventional' } else { '' })
    $shortcut.WorkingDirectory = $GamePath
    $shortcut.IconLocation = "$gameExe,0"
    $shortcut.Description = "Normal installed Vintage Story with $mode settings."
    $shortcut.Save()
}
Write-Host "Installed and verified: $modTarget"
& (Join-Path $PSScriptRoot 'Enable-StandardSpatialLaunch.ps1') -InstallationPath $session
Write-Host "Backups and launcher: $session"
Write-Host 'Normal game launch supports spatial audio. Optional diagnostic shortcuts: Vintage Story - Spatial Audio Test / Vintage Story - Standard Audio'
