#requires -Version 7.0
[CmdletBinding()]
param([switch]$Conventional)
$ErrorActionPreference = 'Stop'
try {
    $settings = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installation.json') -Raw | ConvertFrom-Json
    $gameExe = Join-Path $settings.GamePath 'Vintagestory.exe'
    if (Get-Process Vintagestory -ErrorAction SilentlyContinue | Where-Object Path -eq $gameExe) {
        throw 'Close Vintage Story before switching audio modes.'
    }
    $config = Get-Content -LiteralPath $settings.ModConfig -Raw | ConvertFrom-Json -AsHashtable
    $config.OutputMode = if ($Conventional) { 0 } else { 8 }
    $config.EnableDebugTools = $true
    $config | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $settings.ModConfig -Encoding utf8
    $logs = Join-Path $PSScriptRoot 'AudioLogs'
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    $start = [Diagnostics.ProcessStartInfo]::new($gameExe)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $settings.GamePath
    $start.ArgumentList.Add('--dataPath')
    $start.ArgumentList.Add($settings.DataPath)
    $start.Environment['ALSOFT_CONF'] = Join-Path $PSScriptRoot $(if ($Conventional) { 'conventional.ini' } else { 'spatial.ini' })
    $start.Environment['ALSOFT_LOGLEVEL'] = '3'
    $start.Environment['ALSOFT_LOGFILE'] = Join-Path $logs ('openal-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.log')
    $start.Environment['ALSOFT_DRIVERS'] = 'wasapi'
    $start.Environment['SURROUNDSOUND_SPATIAL_STARTUP'] = if ($Conventional) { '0' } else { '1' }
    [Diagnostics.Process]::Start($start) | Out-Null
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'launcher-error.txt')
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Surround Sound launcher') | Out-Null
    throw
}
