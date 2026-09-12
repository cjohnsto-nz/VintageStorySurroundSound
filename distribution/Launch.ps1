#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$GamePath = (Join-Path $env:APPDATA 'Vintagestory'),
    [ValidateSet('Diagnostic', 'Conventional')][string]$Mode = 'Diagnostic',
    [switch]$Interactive
)
. (Join-Path $PSScriptRoot 'SpatialSetup.Common.ps1')
if ($Interactive) {
    $choice = Read-Host "Game directory [Enter for $GamePath]"
    if ($choice) { $GamePath = $choice.Trim('"') }
}
$GamePath = [IO.Path]::GetFullPath($GamePath).TrimEnd('\')
$store = Child-Path $GamePath 'SurroundSpatialSetup'
$state = Read-Json (Child-Path $store 'installation.json')
Assert-State $state $GamePath $state.DataPath
if (-not $state.Installed) { throw 'Spatial setup has been restored. Use your normal game shortcut.' }
if (Test-Path -LiteralPath (Child-Path $store 'pending.json')) { throw 'An operation was interrupted. Run Setup.ps1 -Action Recover -Apply first.' }
Assert-Closed $GamePath
$setupLock = [IO.File]::Open((Child-Path $store 'setup.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
$configPath = Record-Path ($state.Files | Where-Object Kind -eq 'Config') $GamePath $state.DataPath
$config = Read-Json $configPath
$config | Add-Member -NotePropertyName OutputMode -NotePropertyValue $(if ($Mode -eq 'Conventional') { 0 } else { 8 }) -Force
Write-Json $configPath $config
$logs = Child-Path $store 'AudioLogs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = Child-Path $GamePath 'Vintagestory.exe'
$start.WorkingDirectory = $GamePath
$start.UseShellExecute = $false
$start.Arguments = '--dataPath "' + $state.DataPath + '"'
$start.EnvironmentVariables['ALSOFT_CONF'] = Join-Path $PSScriptRoot $(if ($Mode -eq 'Conventional') { 'conventional.ini' } else { 'spatial.ini' })
$start.EnvironmentVariables['ALSOFT_DRIVERS'] = 'wasapi'
$start.EnvironmentVariables['ALSOFT_LOGLEVEL'] = '3'
$start.EnvironmentVariables['ALSOFT_LOGFILE'] = Join-Path $logs ('openal-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.log')
$start.EnvironmentVariables['SURROUNDSOUND_SPATIAL_STARTUP'] = if ($Mode -eq 'Conventional') { '0' } else { '1' }
[Diagnostics.Process]::Start($start) | Out-Null
Write-Host 'Game started. Conventional mode saves OutputMode=Auto; use Diagnostic launch or select WindowsSpatialAudio in the mod to enable spatial output again.'
} finally { $setupLock.Dispose() }
