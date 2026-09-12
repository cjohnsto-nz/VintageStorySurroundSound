#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$OpenAlPath = (Join-Path $env:APPDATA 'Vintagestory\Lib\OpenAL32.dll'),
    [string]$ConfigurationPath = (Join-Path $PSScriptRoot '..\native\spatial-auto-start.ini')
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'tests\SpatialAudio.Tests\SpatialAudio.Tests.csproj'
dotnet build $project -c Release | Out-Host
if ($LASTEXITCODE) { throw 'Probe build failed.' }
$hostPath = Join-Path $root ('bin\spatial-tests\normal-launch-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $hostPath | Out-Null
$probeBuild = Join-Path $root 'tests\SpatialAudio.Tests\bin\Release\net10.0-windows'
foreach ($name in @('SpatialAudio.Tests.exe', 'SpatialAudio.Tests.dll', 'SpatialAudio.Tests.deps.json', 'SpatialAudio.Tests.runtimeconfig.json')) {
    Copy-Item -LiteralPath (Join-Path $probeBuild $name) -Destination $hostPath
}
Copy-Item -LiteralPath $ConfigurationPath -Destination (Join-Path $hostPath 'alsoft.ini')
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $hostPath 'SpatialAudio.Tests.exe'))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
# A different working directory proves configuration discovery uses the EXE's
# folder, not the shell's current directory or the launcher's environment.
$start.WorkingDirectory = $root
foreach ($name in @($start.Environment.Keys)) {
    if ($name.StartsWith('ALSOFT_', [StringComparison]::OrdinalIgnoreCase) -or $name -eq 'SURROUNDSOUND_SPATIAL_STARTUP') {
        $start.Environment.Remove($name) | Out-Null
    }
}
foreach ($argument in @('--auto-start-probe', (Resolve-Path -LiteralPath $OpenAlPath).Path, (Join-Path $hostPath 'results'), 'Front')) {
    $start.ArgumentList.Add($argument)
}
$probe = [Diagnostics.Process]::Start($start)
if (-not $probe.WaitForExit(15000)) { $probe.Kill($true); throw "Normal-launch probe timed out: $hostPath" }
if ($probe.ExitCode) { throw "Normal-launch probe failed ($($probe.ExitCode)): $hostPath" }
$result = Get-Content -LiteralPath (Join-Path $hostPath 'results\result.json') -Raw | ConvertFrom-Json
if (-not ($result.StreamActivated -and $result.ConnectedAfterFiveSeconds -and $result.NormalLaunchConfiguration)) {
    throw "Normal-launch stream verification failed: $hostPath"
}
Write-Host "PASS: EXE-local configuration started 7.1.4 without audio environment overrides, using $($result.Device)."
Write-Host "Evidence: $hostPath"
