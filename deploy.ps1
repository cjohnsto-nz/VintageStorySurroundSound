# Surround Sound Lab deployment script
# Stops the game, builds the mod, packages it into the VS Mods folder, and relaunches the client.

$ErrorActionPreference = 'Stop'

$ProjectName = 'SurroundSoundLab'
$ProjectRoot = $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot 'SurroundSoundLab.csproj'
$ModsDir = 'C:\Users\chris\AppData\Roaming\VintagestoryData\Mods'
$VSProcessName = 'Vintagestory'
$VSExePath = 'C:\Users\chris\AppData\Roaming\Vintagestory\Vintagestory.exe'
$SourceDir = Join-Path $ProjectRoot "bin\Debug\ModPackage\$ProjectName"
$TempDir = Join-Path $env:TEMP 'SurroundSoundLabTempDeploy'

Write-Host 'Checking for running Vintage Story process...' -ForegroundColor Cyan
$vsProcess = Get-Process -Name $VSProcessName -ErrorAction SilentlyContinue
if ($vsProcess) {
    Write-Host 'Vintage Story is running. Stopping process...'
    Stop-Process -Name $VSProcessName -Force
    Start-Sleep -Seconds 2
}

Write-Host 'Removing old build artifacts...' -ForegroundColor Cyan
if (Test-Path (Join-Path $ProjectRoot 'bin')) { Remove-Item -LiteralPath (Join-Path $ProjectRoot 'bin') -Recurse -Force }
if (Test-Path (Join-Path $ProjectRoot 'obj')) { Remove-Item -LiteralPath (Join-Path $ProjectRoot 'obj') -Recurse -Force }

Write-Host 'Cleaning project...' -ForegroundColor Cyan
dotnet clean $ProjectFile
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet clean failed.'
}

Write-Host 'Building SurroundSoundLab...' -ForegroundColor Cyan
dotnet build $ProjectFile
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet build failed.'
}

if (-not (Test-Path $SourceDir)) {
    throw "Packaged mod directory not found at '$SourceDir'."
}

$ModInfoPath = Join-Path $SourceDir 'modinfo.json'
$Version = '0.0.0'
if (Test-Path $ModInfoPath) {
    $modInfo = Get-Content -Raw $ModInfoPath | ConvertFrom-Json
    $Version = $modInfo.version
}

$ZipFileName = "surroundsoundlab_$Version.zip"
$ZipFilePath = Join-Path $ModsDir $ZipFileName

Write-Host "Deploying mod as '$ZipFileName' to '$ModsDir'..." -ForegroundColor Cyan

Get-ChildItem -LiteralPath $ModsDir -Filter 'surroundsoundlab_*.zip' -ErrorAction SilentlyContinue |
    Remove-Item -Force

if (Test-Path $TempDir) {
    Remove-Item -LiteralPath $TempDir -Recurse -Force
}

New-Item -ItemType Directory -Path $TempDir | Out-Null
Copy-Item -Path (Join-Path $SourceDir '*') -Destination $TempDir -Recurse

$ModIconPath = Join-Path $ProjectRoot 'modicon.png'
if (Test-Path $ModIconPath) {
    Copy-Item -LiteralPath $ModIconPath -Destination $TempDir
}

if (Test-Path $ZipFilePath) {
    Remove-Item -LiteralPath $ZipFilePath -Force
}

Write-Host "Creating zip file '$ZipFilePath'..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $TempDir '*') -DestinationPath $ZipFilePath

Write-Host "`nDeployment complete. Launching Vintage Story..." -ForegroundColor Green
Start-Process -FilePath $VSExePath
