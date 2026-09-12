#requires -Version 7.0
[CmdletBinding()]
param([switch]$ProbeDevice, [string]$RuntimePath)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'tests\SpatialAudio.Tests\SpatialAudio.Tests.csproj'
dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Managed spatial tests failed.' }
$runtime = if ($RuntimePath) { [pscustomobject]@{ DllPath = (Resolve-Path -LiteralPath $RuntimePath).Path } }
    else { & (Join-Path $PSScriptRoot 'Get-SpatialRuntime.ps1') }
$results = Join-Path $root 'bin\spatial-tests\patched'
foreach ($direction in @('Above', 'Front')) {
    dotnet run --project $project -c Release --no-build -- --render $runtime.DllPath (Join-Path $results $direction) $direction
    if ($LASTEXITCODE -ne 0) { throw "Native $direction render failed." }
}
$above = Get-Content -Raw -LiteralPath (Join-Path $results 'Above\result.json') | ConvertFrom-Json
$front = Get-Content -Raw -LiteralPath (Join-Path $results 'Front\result.json') | ConvertFrom-Json
$heightFraction = $above.Wave.HeightEnergy / ($above.Wave.HeightEnergy + $above.Wave.BedEnergy)
if ($heightFraction -lt 0.75 -or $above.Wave.HeightEnergy -le $front.Wave.HeightEnergy * 2) {
    throw 'Overhead source did not produce the expected height-channel localization.'
}
Write-Host ('PASS: Native twelve-channel output; {0:P1} of overhead source energy in height channels.' -f $heightFraction)
$weatherResults = @{}
foreach ($weather in @('WeatherStereo', 'WeatherStereoTilt', 'WeatherSurround', 'WeatherSurroundTilt')) {
    dotnet run --project $project -c Release --no-build -- --render $runtime.DllPath (Join-Path $results $weather) $weather
    if ($LASTEXITCODE -ne 0) { throw "Native $weather render failed." }
    $result = Get-Content -Raw -LiteralPath (Join-Path $results "$weather\result.json") | ConvertFrom-Json
    $weatherResults[$weather] = $result
    $stereo = $weather.StartsWith('WeatherStereo')
    $expectedChannels = if ($stereo) { 2 } else { 6 }
    $activeChannels = if ($stereo) { @(0, 1) } else { @(0, 1, 2, 3, 6, 7) }
    if ($result.InputChannels -ne $expectedChannels -or $result.Wave.HeightEnergy -gt 1e-12) {
        throw "$weather changed the original channel count or sent audio to height channels."
    }
    foreach ($channel in 0..11) {
        if ($channel -in $activeChannels) {
            if ($result.Wave.Rms[$channel] -le 0.0001) { throw "$weather lost authored channel $channel." }
        } elseif ($result.Wave.Rms[$channel] -gt 1e-6) { throw "$weather leaked into channel $channel." }
    }
}
foreach ($weather in @('WeatherStereo', 'WeatherSurround')) {
    $level = $weatherResults[$weather].Wave.Rms
    $tilted = $weatherResults["${weather}Tilt"].Wave.Rms
    if ($weatherResults["${weather}Tilt"].ListenerOrientation[1] -gt -0.7) { throw 'Weather pitch comparison did not tilt the listener.' }
    foreach ($channel in 0..11) {
        if ([Math]::Abs($level[$channel] / $level[0] - $tilted[$channel] / $tilted[0]) -gt 0.01) {
            throw "$weather channel balance changed with camera pitch."
        }
    }
}
Write-Host 'PASS: Stereo and 5.1 weather beds retain their original speaker channels, with zero height energy and pitch-invariant balance.'
$pitchResults = @{}
foreach ($scenario in @('PitchOff', 'PitchOn')) {
    dotnet run --project $project -c Release --no-build -- --render $runtime.DllPath (Join-Path $results $scenario) $scenario
    if ($LASTEXITCODE -ne 0) { throw "Native $scenario render failed." }
    $pitchResults[$scenario] = Get-Content -Raw -LiteralPath (Join-Path $results "$scenario\result.json") | ConvertFrom-Json
}
$offFraction = $pitchResults.PitchOff.Wave.HeightEnergy / ($pitchResults.PitchOff.Wave.HeightEnergy + $pitchResults.PitchOff.Wave.BedEnergy)
$onFraction = $pitchResults.PitchOn.Wave.HeightEnergy / ($pitchResults.PitchOn.Wave.HeightEnergy + $pitchResults.PitchOn.Wave.BedEnergy)
if ($onFraction -lt $offFraction + 0.2 -or [Math]::Abs($pitchResults.PitchOff.ListenerOrientation[1]) -gt 0.001 -or
    $pitchResults.PitchOn.ListenerOrientation[1] -gt -0.7) {
    throw 'Pitch toggle did not reach the expected native output.'
}
Write-Host ('PASS: Front-source height energy: pitch off {0:P1}, looking down with pitch on {1:P1}.' -f $offFraction, $onFraction)
if ($ProbeDevice) {
    dotnet run --project $project -c Release --no-build -- --spatial-probe $runtime.DllPath (Join-Path $results 'Windows') Front
    if ($LASTEXITCODE -ne 0) { throw 'Windows spatial stream activation failed; see the native log.' }
}
