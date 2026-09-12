#requires -Version 7.0
# Add startup configuration to an existing local test installation. The normal
# game executable and shortcut then work without process environment overrides.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$InstallationPath)
$ErrorActionPreference = 'Stop'
$InstallationPath = (Resolve-Path -LiteralPath $InstallationPath).Path
$settings = Get-Content -LiteralPath (Join-Path $InstallationPath 'installation.json') -Raw | ConvertFrom-Json
$manifestPath = Join-Path $InstallationPath 'backup.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
$gamePath = (Resolve-Path -LiteralPath $settings.GamePath).Path
if ($manifest.GamePath -ne $gamePath) { throw 'Installation and backup manifest identify different games.' }
if (Get-Process Vintagestory -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $gamePath 'Vintagestory.exe')) {
    throw 'Close Vintage Story before enabling normal-launch spatial audio.'
}
$audioPath = Join-Path $gamePath 'alsoft.ini'
$template = (Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\native\spatial-auto-start.ini') -Raw).Trim()
$hadFile = Test-Path -LiteralPath $audioPath
$previous = if ($hadFile) { [IO.File]::ReadAllBytes($audioPath) } else { $null }
$existing = if ($hadFile) { Get-Content -LiteralPath $audioPath -Raw } else { '' }
$record = @($manifest.Files | Where-Object { $_.Path -eq $audioPath })
if ($record.Count -gt 1) { throw 'Duplicate audio configuration entries in backup manifest.' }
if ($record.Count -eq 1 -and $hadFile -and (Get-FileHash -LiteralPath $audioPath).Hash -ne $record[0].InstalledHash) {
    throw 'Game-local audio configuration changed since setup. Preserve your edits before rerunning setup.'
}
$manifestBefore = [IO.File]::ReadAllBytes($manifestPath)
if ($record.Count -eq 0) {
    $backup = $null
    if ($hadFile) {
        $backup = Join-Path $InstallationPath 'original-alsoft.ini'
        if (Test-Path -LiteralPath $backup) { throw 'Untracked alsoft.ini backup already exists; refusing to overwrite it.' }
        Copy-Item -LiteralPath $audioPath -Destination $backup
    }
    $record = @(@{ Path = $audioPath; Backup = $backup; IsConfig = $false; InstalledHash = '' })
    $manifest.Files += $record[0]
}
try {
    # Append overrides once, preserving pre-existing unrelated options and the
    # original file's restore point. Subsequent setup must not back up a patch.
    if (-not $existing.TrimEnd().EndsWith($template, [StringComparison]::Ordinal)) {
        $prefix = if ($existing.Trim().Length) { $existing.TrimEnd() + "`r`n`r`n" } else { '' }
        [IO.File]::WriteAllText($audioPath, $prefix + $template + "`r`n", [Text.UTF8Encoding]::new($false))
    }
    $record[0].InstalledHash = (Get-FileHash -LiteralPath $audioPath).Hash
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
} catch {
    if ($hadFile) { [IO.File]::WriteAllBytes($audioPath, $previous) }
    elseif (Test-Path -LiteralPath $audioPath) { Remove-Item -LiteralPath $audioPath }
    [IO.File]::WriteAllBytes($manifestPath, $manifestBefore)
    throw
}
Write-Host "Spatial startup configured: $audioPath"
Write-Host 'Use your ordinary Vintage Story shortcut or executable. The diagnostic shortcut is optional.'
Write-Host "Original restore point retained: $InstallationPath"
