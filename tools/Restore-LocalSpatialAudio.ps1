#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'backup.json') -Raw | ConvertFrom-Json
if (Get-Process Vintagestory -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $manifest.GamePath 'Vintagestory.exe')) {
    throw 'Close Vintage Story before restoring the previous installation.'
}
# Check all payloads before replacing anything. User configuration may legitimately
# change during testing; keep a copy of its latest state before restoring it.
foreach ($file in $manifest.Files) {
    if (Test-Path -LiteralPath $file.Path) {
        if (-not $file.IsConfig -and (Get-FileHash -LiteralPath $file.Path).Hash -ne $file.InstalledHash) {
            throw "File changed since installation; refusing to overwrite: $($file.Path)"
        }
    }
}
foreach ($file in $manifest.Files) {
    if ($file.IsConfig -and (Test-Path -LiteralPath $file.Path)) {
        Copy-Item -LiteralPath $file.Path -Destination (Join-Path $PSScriptRoot ('config-before-restore-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json'))
    }
    if ($file.Backup) {
        Copy-Item -LiteralPath $file.Backup -Destination $file.Path -Force
    } elseif (Test-Path -LiteralPath $file.Path) {
        Remove-Item -LiteralPath $file.Path
    }
}
Write-Host 'Restored the previous Surround Sound mod, OpenAL runtime, and mod settings. Use your usual game shortcut.'
