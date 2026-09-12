#requires -Version 7.0
# Creates an isolated game + data directory, then optionally launches it.
# The installed game, installed mods and existing worlds are never overwritten.
[CmdletBinding()]
param(
    [string]$GamePath = (Join-Path $env:APPDATA 'Vintagestory'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\bin\SpatialSandbox'),
    [switch]$PrepareOnly,
    [switch]$Conventional
)
$ErrorActionPreference = 'Stop'
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
$Destination = [IO.Path]::GetFullPath($Destination)
if ($Destination.TrimEnd('\') -eq $GamePath.TrimEnd('\') -or
    $Destination.StartsWith($GamePath.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $GamePath.StartsWith($Destination.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The sandbox must be separate from the installed game.'
}
$markerPath = Join-Path $Destination 'surround-spatial-sandbox.json'
if (Test-Path -LiteralPath $Destination) {
    if (-not (Test-Path -LiteralPath $markerPath)) { throw 'Destination exists without a sandbox marker. Choose an empty destination.' }
    $existing = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
    if ($existing.Kind -ne 'SurroundSoundSpatialSandbox' -or $existing.SourceGame -ne $GamePath) {
        throw 'Sandbox marker does not match the selected game installation.'
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $GamePath 'Vintagestory.exe'))) { throw 'Vintage Story executable is missing.' }
$gameExe = Join-Path $Destination 'Vintagestory.exe'
$running = Get-Process -Name Vintagestory -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $gameExe }
if ($running) { throw 'Close the spatial sandbox game before updating or restarting it.' }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectFile = Join-Path $projectRoot 'VintageStorySurroundSound.csproj'
dotnet build $projectFile -c Release "-p:GamePath=$GamePath" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
$runtime = & (Join-Path $PSScriptRoot 'Get-SpatialRuntime.ps1')
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
@{ Kind = 'SurroundSoundSpatialSandbox'; SourceGame = $GamePath; Runtime = $runtime } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $markerPath -Encoding utf8
# Copy only game binaries and built-in mods; assets are shared via a junction.
Get-ChildItem -LiteralPath $GamePath -File | Where-Object { $_.Extension -in '.dll','.exe','.json','.pdb','.txt','.config' } |
    Where-Object { $_.Name -notlike 'unins*' } | Copy-Item -Destination $Destination -Force
foreach ($folder in @('Lib', 'Mods')) {
    $targetFolder = Join-Path $Destination $folder
    New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $GamePath $folder) | Copy-Item -Destination $targetFolder -Recurse -Force
}
$assetLink = Join-Path $Destination 'assets'
$sourceAssets = Join-Path $GamePath 'assets'
if (-not (Test-Path -LiteralPath $assetLink)) {
    New-Item -ItemType Junction -Path $assetLink -Target $sourceAssets | Out-Null
} elseif ((Get-Item -LiteralPath $assetLink).Target -ne $sourceAssets) {
    throw 'Sandbox assets link does not match the selected game.'
}
Copy-Item -LiteralPath $runtime.DllPath -Destination (Join-Path $Destination 'Lib\OpenAL32.dll') -Force
$notices = Join-Path $Destination 'OpenAL-notices'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
Get-ChildItem -LiteralPath $runtime.SourcePath -File | Where-Object { $_.Name -match '^(COPYING|LICENSE)' } |
    Copy-Item -Destination $notices -Force
$runtime | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $notices 'runtime.json') -Encoding utf8
$dataPath = Join-Path $Destination 'Data'
$configDir = Join-Path $dataPath 'ModConfig'
$modDir = Join-Path $dataPath 'Mods\VintageStorySurroundSound'
New-Item -ItemType Directory -Path $configDir,$modDir -Force | Out-Null
$package = Join-Path $projectRoot 'bin\Release\ModPackage\VintageStorySurroundSound'
Get-ChildItem -LiteralPath $package | Copy-Item -Destination $modDir -Recurse -Force
$configFile = Join-Path $configDir 'vintagestorysurroundsound.json'
$config = if (Test-Path -LiteralPath $configFile) { Get-Content -Raw -LiteralPath $configFile | ConvertFrom-Json -AsHashtable } else { @{} }
$config.OutputMode = if ($Conventional) { 0 } else { 8 }
$config.EnableDebugTools = $true
$config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $configFile -Encoding utf8
$audioConfig = Join-Path $Destination 'alsoft.ini'
$channels = if ($Conventional) { '' } else { 'surround714' }
$spatial = if ($Conventional) { 'false' } else { 'true' }
@"
[general]
drivers = wasapi
channels = $channels
stereo-encoding = basic

[wasapi]
spatial-api = $spatial
"@ | Set-Content -LiteralPath $audioConfig -Encoding ascii
Write-Host "Prepared spatial sandbox: $Destination"
Write-Host "OpenAL $($runtime.Version); original game and data untouched."
if ($PrepareOnly) { return }
$logDir = Join-Path $Destination 'AudioLogs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
if (-not $Conventional) {
    # An isolated probe catches native crashes before the game or world is open.
    $testProject = Join-Path $projectRoot 'tests\SpatialAudio.Tests\SpatialAudio.Tests.csproj'
    dotnet build $testProject -c Release | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the spatial preflight.' }
    $probeExe = Join-Path $projectRoot 'tests\SpatialAudio.Tests\bin\Release\net10.0-windows\SpatialAudio.Tests.exe'
    $probeDir = Join-Path $logDir ('preflight-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $probeInfo = [Diagnostics.ProcessStartInfo]::new($probeExe)
    $probeInfo.UseShellExecute = $false
    $probeInfo.CreateNoWindow = $true
    foreach ($argument in @('--spatial-probe', (Join-Path $Destination 'Lib\OpenAL32.dll'), $probeDir, 'Front')) {
        $probeInfo.ArgumentList.Add($argument)
    }
    $probe = [Diagnostics.Process]::Start($probeInfo)
    if (-not $probe.WaitForExit(15000)) {
        $probe.Kill($true)
        throw "Spatial preflight timed out. See $probeDir. Use -Conventional for ordinary output."
    }
    if ($probe.ExitCode -ne 0) { throw "Spatial preflight failed ($($probe.ExitCode)). See $probeDir. Use -Conventional for ordinary output." }
}
# ProcessStartInfo scopes all configuration to the child. Do not mutate the
# user's environment or the running PowerShell session's OpenAL settings.
$startInfo = [Diagnostics.ProcessStartInfo]::new($gameExe)
$startInfo.WorkingDirectory = $Destination
$startInfo.UseShellExecute = $false
$startInfo.ArgumentList.Add('--dataPath')
$startInfo.ArgumentList.Add($dataPath)
$startInfo.Environment['ALSOFT_CONF'] = $audioConfig
$startInfo.Environment['ALSOFT_LOGLEVEL'] = '3'
$startInfo.Environment['ALSOFT_LOGFILE'] = Join-Path $logDir ('openal-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
$startInfo.Environment['SURROUNDSOUND_SPATIAL_STARTUP'] = if ($Conventional) { '0' } else { '1' }
# Explicit per-process overrides beat any global user OpenAL configuration.
$startInfo.Environment['ALSOFT_DRIVERS'] = 'wasapi'
[Diagnostics.Process]::Start($startInfo) | Out-Null
