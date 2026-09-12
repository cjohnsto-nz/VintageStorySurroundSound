#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$GamePath = (Join-Path $env:APPDATA 'Vintagestory'),
    [string]$Destination,
    [string]$NativeBuildRoot,
    [switch]$Prerelease
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
$gameVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $GamePath 'Vintagestory.exe')).FileVersion
if ($gameVersion -ne '1.22.7') { throw 'This spatial candidate is currently qualified for building against Vintage Story 1.22.7 only.' }
$metadata = Get-Content -LiteralPath (Join-Path $root 'modinfo.json') -Raw | ConvertFrom-Json
$candidate = if ($Prerelease) { $metadata.version } else { $metadata.version + '-dev-' + (Get-Date -Format 'yyyyMMdd-HHmmss') }
if ($Prerelease -and $metadata.version -notmatch '^\d+\.\d+\.\d+-[0-9A-Za-z.-]+$') { throw 'A prerelease build requires a prerelease version in modinfo.json.' }
if ($Prerelease -and (git -C $root status --porcelain)) { throw 'Commit changes before building a publishable prerelease.' }
if (-not $Destination) { $Destination = Join-Path $root ('bin\releases\' + $candidate) }
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw 'Choose a new output directory; existing artifacts will not be overwritten.' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
if (-not $NativeBuildRoot) { $NativeBuildRoot = Join-Path $Destination 'build' }
dotnet build (Join-Path $root 'VintageStorySurroundSound.csproj') -c Release "-p:GamePath=$GamePath" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
$runtime = & (Join-Path $PSScriptRoot 'Build-SpatialRuntime.ps1') -BuildRoot $NativeBuildRoot
$stage = Join-Path $Destination 'staging'
$addon = Join-Path $stage 'addon'
$sourceBundle = Join-Path $stage 'source'
New-Item -ItemType Directory -Path $addon,$sourceBundle -Force | Out-Null
$modName = $metadata.modid + '_' + $metadata.version + '.zip'
$modZip = Join-Path $Destination $modName
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $root 'bin\Release\ModPackage\VintageStorySurroundSound'), $modZip)
Copy-Item -LiteralPath (Join-Path $root 'distribution') -Destination (Join-Path $stage 'distribution') -Recurse
Get-ChildItem -LiteralPath (Join-Path $stage 'distribution') -File | Copy-Item -Destination $addon
Copy-Item -LiteralPath (Join-Path $root 'native\spatial-auto-start.ini') -Destination (Join-Path $addon 'spatial.ini')
New-Item -ItemType Directory -Path (Join-Path $addon 'payload') -Force | Out-Null
Copy-Item -LiteralPath $modZip -Destination (Join-Path $addon ('payload\' + $modName))
Copy-Item -LiteralPath $runtime.DllPath -Destination (Join-Path $addon 'payload\OpenAL32.dll')
Copy-Item -LiteralPath $runtime.SourcePath -Destination (Join-Path $sourceBundle 'openal-soft-1.25.2') -Recurse
Copy-Item -LiteralPath (Join-Path $root 'native\openal-soft-spatial-ownership.patch') -Destination $sourceBundle
$modifications = @'
Surround Sound modifications, 2026-09-12
Based on OpenAL Soft 1.25.2 (https://github.com/kcat/openal-soft/tree/1.25.2).
alc/backends/wasapi.cpp: allocate/copy owned VT_BLOB memory with CoTaskMemAlloc,
throw on allocation failure, and trace successful spatial stream activation.
The corresponding patch is included; it is already applied to this source tree.
The modified library remains dynamically replaceable as Lib/OpenAL32.dll.
'@
[IO.File]::WriteAllText((Join-Path $sourceBundle 'openal-soft-1.25.2\MODIFICATIONS.txt'), $modifications)
[IO.File]::WriteAllText((Join-Path $sourceBundle 'MODIFICATIONS.txt'), $modifications)
$recipe = @'
#requires -Version 7.0
$ErrorActionPreference = 'Stop'
cmake -S (Join-Path $PSScriptRoot 'openal-soft-1.25.2') -B (Join-Path $PSScriptRoot 'build') -G 'Visual Studio 17 2022' -A x64 -DALSOFT_EXAMPLES=OFF -DALSOFT_UTILS=OFF -DALSOFT_TESTS=OFF -DALSOFT_BACKEND_WASAPI=ON -DALSOFT_BACKEND_WAVE=ON -DALSOFT_UPDATE_BUILD_VERSION=OFF -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded
if ($LASTEXITCODE -ne 0) { throw 'Configure failed. Install VS 2022 C++ Build Tools, Windows SDK and CMake.' }
cmake --build (Join-Path $PSScriptRoot 'build') --config Release --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
'@
[IO.File]::WriteAllText((Join-Path $sourceBundle 'Build.ps1'), $recipe)
$notices = Join-Path $addon 'notices'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
# Retain all standalone notices, plus complete files carrying embedded notices
# for bundled components. The full source archive also retains every header.
$noticeFiles = @(Get-ChildItem -LiteralPath $runtime.SourcePath -Recurse -File | Where-Object { $_.Name -match '(?i)copying|licen[sc]e|notice' })
$embedded = @('common/ghc_filesystem.h', 'common/filesystem.h', 'common/filesystem.cpp', 'common/dlopennote.h',
    'common/pffft.h', 'common/pffft.cpp', 'core/bs2b.h', 'core/bs2b.cpp', 'core/mixer/mixer_sse2.cpp', 'core/mixer/mixer_sse41.cpp')
$noticeFiles += @($embedded | ForEach-Object { Get-Item -LiteralPath (Join-Path $runtime.SourcePath $_) })
foreach ($file in $noticeFiles | Sort-Object FullName -Unique) {
    $relative = [IO.Path]::GetRelativePath($runtime.SourcePath, $file.FullName)
    $target = Join-Path $notices $relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
[IO.File]::WriteAllText((Join-Path $addon 'NOTICE.txt'), $modifications + "`nSee notices/ for OpenAL, PFFFT, fmt, GSL, filesystem and other embedded notices.`nCorresponding modified source is supplied alongside this add-on.`n")
$sourceName = 'surround-spatial-source-' + $candidate + '.zip'
$addonName = 'surround-spatial-windows-x64-' + $candidate + '.zip'
$commit = (git -C $root rev-parse HEAD).Trim()
$dirty = [bool](git -C $root status --porcelain)
$runtimeManifest = [ordered]@{ Version = $runtime.Version; DllSha256 = $runtime.DllSha256; ArchiveSha256 = $runtime.ArchiveSha256; PatchSha256 = $runtime.PatchSha256; SourceUrl = $runtime.SourceUrl; PatchVerified = $runtime.PatchVerified; SourceTreeSha256 = $runtime.SourceTreeSha256; Architecture = 'x64'; Generator = 'Visual Studio 17 2022'; RuntimeLibrary = 'MultiThreaded' }
$compilerFile = Get-ChildItem -LiteralPath (Join-Path $NativeBuildRoot 'openal-build\CMakeFiles') -Recurse -Filter CMakeCXXCompiler.cmake | Select-Object -First 1
if ($compilerFile) {
    $compiler = Get-Content -LiteralPath $compilerFile.FullName -Raw
    if ($compiler -match 'set\(CMAKE_CXX_COMPILER_VERSION "([^"]+)"\)') { $runtimeManifest.CompilerVersion = $Matches[1] }
}
$nativeProject = Get-Content -LiteralPath (Join-Path $NativeBuildRoot 'openal-build\OpenAL.vcxproj') -Raw
if ($nativeProject -match '<WindowsTargetPlatformVersion>([^<]+)</WindowsTargetPlatformVersion>') { $runtimeManifest.WindowsSdkVersion = $Matches[1] }
$runtimeManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sourceBundle 'build-manifest.json') -Encoding utf8NoBOM
[IO.Compression.ZipFile]::CreateFromDirectory($sourceBundle, (Join-Path $Destination $sourceName))
$files = @(Get-ChildItem -LiteralPath $addon -Recurse -File | ForEach-Object {
    [ordered]@{ Path = [IO.Path]::GetRelativePath($addon, $_.FullName).Replace('\','/'); Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
$manifest = [ordered]@{ Schema = 1; Candidate = $candidate; DevelopmentBuild = (-not $Prerelease); Prerelease = [bool]$Prerelease; Commit = $commit; DirtyWorktree = $dirty;
    ModVersion = $metadata.version; RuntimeVersion = $runtime.Version; GameVersions = @($gameVersion);
    ModPath = ('payload/' + $modName); NativePath = 'payload/OpenAL32.dll'; SourceArchive = $sourceName;
    SourceSha256 = (Get-FileHash -LiteralPath (Join-Path $Destination $sourceName)).Hash; Runtime = $runtimeManifest; Files = $files }
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $addon 'package.json') -Encoding utf8NoBOM
[IO.Compression.ZipFile]::CreateFromDirectory($addon, (Join-Path $Destination $addonName))
$artifacts = @($modName, $addonName, $sourceName)
foreach ($name in $artifacts) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $Destination $name))
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.Contains('\') -or $entry.FullName.StartsWith('/') -or $entry.FullName.Split('/') -contains '..') { throw "Nonportable archive entry: $($entry.FullName)" }
            if ($entry.FullName -match '(?i)(^|/)(Vintagestory(API|Lib)?\.(exe|dll)|.*\.pdb)$') { throw 'Proprietary game or debug file in archive.' }
        }
    } finally { $archive.Dispose() }
}
$hashes = @($artifacts | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $Destination $_)).Hash.ToLowerInvariant() + '  ' + $_ })
[IO.File]::WriteAllLines((Join-Path $Destination 'SHA256SUMS.txt'), $hashes)
Write-Host "Candidate packaged: $Destination"
[pscustomobject]@{ Directory = $Destination; Addon = (Join-Path $Destination $addonName); Source = (Join-Path $Destination $sourceName); Mod = $modZip; StagedAddon = $addon }
