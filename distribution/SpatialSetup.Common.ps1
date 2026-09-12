#requires -Version 5.1
Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Read-Json($Path) { Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
function Write-Json($Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 30), (New-Object Text.UTF8Encoding($false)))
}
function File-Hash($Path) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
    return $null
}
function Child-Path($Root, $Relative) {
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $path = [IO.Path]::GetFullPath((Join-Path $base $Relative))
    if (-not $path.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes its directory: $Relative" }
    # Do not follow junctions/symlinks when replacing files or reading backups.
    $item = $path
    while ($item.Length -ge $base.TrimEnd('\').Length) {
        if (Test-Path -LiteralPath $item) {
            if ((Get-Item -LiteralPath $item -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Choose a directory without junctions or symlinks: $item"
            }
        }
        $item = [IO.Path]::GetDirectoryName($item)
        if (-not $item) { break }
    }
    return $path
}
function Assert-Closed($GamePath) {
    $exe = Join-Path $GamePath 'Vintagestory.exe'
    foreach ($process in @(Get-Process Vintagestory -ErrorAction SilentlyContinue)) {
        if (-not $process.Path -or $process.Path -eq $exe) { throw 'Close the selected Vintage Story game before setup, restore or switching audio modes.' }
    }
}
function Get-ModInfo($Path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry('modinfo.json')
        if ($entry) {
            $reader = New-Object IO.StreamReader($entry.Open())
            try { return ($reader.ReadToEnd() | ConvertFrom-Json) } finally { $reader.Dispose() }
        }
    } finally { $archive.Dispose() }
}
function Find-Mod($DataPath) {
    $found = @()
    $mods = Child-Path $DataPath 'Mods'
    if (Test-Path -LiteralPath $mods) {
        foreach ($entry in Get-ChildItem -LiteralPath $mods) {
            if ($entry.PSIsContainer) {
                $info = Join-Path $entry.FullName 'modinfo.json'
                if ((Test-Path -LiteralPath $info) -and (Read-Json $info).modid -eq 'vintagestorysurroundsound') {
                    throw "Unpacked Surround Sound mod found: $($entry.FullName). Move it out of Mods before setup."
                }
            } elseif ($entry.Extension -eq '.zip') {
                $info = Get-ModInfo $entry.FullName
                if ($info -and $info.modid -eq 'vintagestorysurroundsound') { $found += $entry.Name }
            }
        }
    }
    if ($found.Count -gt 1) { throw 'Multiple Surround Sound mod ZIPs found. Keep one before setup.' }
    if ($found.Count) { return $found[0] }
}
function Record-Path($Record, $GamePath, $DataPath) {
    switch ($Record.Kind) {
        'Native' { return (Child-Path $GamePath 'Lib\OpenAL32.dll') }
        'Startup' { return (Child-Path $GamePath 'alsoft.ini') }
        'Config' { return (Child-Path $DataPath 'ModConfig\vintagestorysurroundsound.json') }
        'Mod' {
            if ($Record.Name -ne [IO.Path]::GetFileName($Record.Name) -or -not $Record.Name.EndsWith('.zip')) { throw 'Invalid tracked mod filename.' }
            return (Child-Path $DataPath ('Mods\' + $Record.Name))
        }
        default { throw 'Unknown installation record.' }
    }
}
function Assert-State($State, $GamePath, $DataPath) {
    if ($State.Schema -ne 1 -or $State.GamePath -ne $GamePath -or $State.DataPath -ne $DataPath) { throw 'Restore record belongs to a different game/data directory or setup version.' }
    $kinds = @($State.Files | ForEach-Object { $_.Kind } | Sort-Object)
    if (($kinds -join ',') -ne 'Config,Mod,Native,Startup') { throw 'Incomplete or duplicate installation records.' }
    foreach ($record in $State.Files) { Record-Path $record $GamePath $DataPath | Out-Null }
}
function Replace-File($Source, $Target, $ExpectedHash) {
    $temporary = $Target + '.surround-' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        Copy-Item -LiteralPath $Source -Destination $temporary
        if ((File-Hash $temporary) -ne $ExpectedHash) { throw 'Copy verification failed.' }
        # Windows PowerShell binds $null to an empty string for string parameters.
        # File.Replace requires an actual null when no second backup is requested.
        for ($attempt = 0; ; $attempt++) {
            try {
                if (Test-Path -LiteralPath $Target) { [IO.File]::Replace($temporary, $Target, [NullString]::Value) }
                else { [IO.File]::Move($temporary, $Target) }
                break
            } catch {
                # Newly copied ZIPs/DLLs can be held briefly by Windows scanners.
                if ($attempt -ge 5 -or -not (Test-Path -LiteralPath $temporary)) { throw "Could not replace ${Target}: $($_.Exception.Message)" }
                Start-Sleep -Milliseconds 200
            }
        }
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}
function Invoke-DeviceCheck($PackageRoot, $DllPath, $OutputPath) {
    $powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $PackageRoot 'Probe-Spatial.ps1') + '" -DllPath "' + $DllPath + '" -OutputPath "' + $OutputPath + '"'
    $process = Start-Process -FilePath $powershell -ArgumentList $arguments -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $OutputPath 'probe-output.txt') -RedirectStandardError (Join-Path $OutputPath 'probe-error.txt')
    try {
        # Windows PowerShell's Start-Process can otherwise lose ExitCode after
        # WaitForExit releases its handle, even when the child succeeded.
        $processHandle = $process.Handle
        if (-not $process.WaitForExit(20000)) { $process.Kill(); $process.WaitForExit(); throw "Spatial check timed out. Logs: $OutputPath" }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "Spatial device check failed (exit $($process.ExitCode)). Select the receiver and Dolby Atmos for home theater in Windows, then retry. Logs: $OutputPath" }
        Get-Content -LiteralPath (Join-Path $OutputPath 'probe-output.txt') | Write-Host
    } finally { $process.Dispose() }
}
function Recover-Transaction($Store, $AllowedPaths) {
    $journalPath = Child-Path $Store 'pending.json'
    if (-not (Test-Path -LiteralPath $journalPath)) { return }
    $journal = Read-Json $journalPath
    # Validate every target and snapshot before changing anything.
    foreach ($entry in $journal.Entries) {
        if ($entry.Path -notin $AllowedPaths) { throw 'Pending transaction has an unexpected target. Keep this directory for manual recovery.' }
        if ($entry.Before) {
            $backup = Child-Path $Store $entry.Before
            if ((File-Hash $backup) -ne $entry.BeforeHash) { throw "Transaction backup damaged: $backup" }
        }
        $current = File-Hash $entry.Path
        if ($current -ne $entry.BeforeHash -and $current -ne $entry.AfterHash) {
            throw "File changed during interrupted setup: $($entry.Path). Preserve the change before recovery."
        }
    }
    foreach ($entry in @($journal.Entries)[($journal.Entries.Count - 1)..0]) {
        if ((File-Hash $entry.Path) -eq $entry.BeforeHash) { continue }
        if ($entry.Before) { Replace-File (Child-Path $Store $entry.Before) $entry.Path $entry.BeforeHash }
        elseif (Test-Path -LiteralPath $entry.Path) { Remove-Item -LiteralPath $entry.Path -Force }
    }
    Remove-Item -LiteralPath $journalPath
    Write-Host 'Recovered the previous incomplete operation.'
}
function Invoke-Transaction($Store, $Changes, $AllowedPaths) {
    $journalPath = Child-Path $Store 'pending.json'
    if (Test-Path -LiteralPath $journalPath) { throw 'Recover the pending operation first.' }
    $transaction = 'transactions\' + [Guid]::NewGuid().ToString('N')
    New-Item -ItemType Directory -Path (Child-Path $Store $transaction) -Force | Out-Null
    $entries = @()
    foreach ($change in $Changes) {
        if ($change.Path -notin $AllowedPaths) { throw 'Unexpected transaction target.' }
        $beforeHash = File-Hash $change.Path
        $afterHash = if ($change.Source) { File-Hash $change.Source } else { $null }
        if ($change.Source -and -not $afterHash) { throw 'Missing installation payload.' }
        if ($beforeHash -eq $afterHash) { continue }
        $before = $null
        if ($beforeHash) {
            $before = $transaction + '\' + $entries.Count + '.bak'
            Copy-Item -LiteralPath $change.Path -Destination (Child-Path $Store $before)
        }
        $entries += [pscustomobject]@{ Path = $change.Path; Before = $before; BeforeHash = $beforeHash; AfterHash = $afterHash; Source = $change.Source }
    }
    if (-not $entries.Count) { return }
    $preparedJournal = Child-Path $Store ($transaction + '\journal.json')
    Write-Json $preparedJournal @{ Entries = $entries }
    [IO.File]::Move($preparedJournal, $journalPath)
    try {
        foreach ($entry in $entries) {
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($entry.Path)) -Force | Out-Null
            if ($entry.Source) {
                # Write to a sibling, verify, then replace atomically. An interrupted
                # copy must never leave a partially overwritten game DLL.
                Replace-File $entry.Source $entry.Path $entry.AfterHash
            } elseif (Test-Path -LiteralPath $entry.Path) { Remove-Item -LiteralPath $entry.Path -Force }
            if ((File-Hash $entry.Path) -ne $entry.AfterHash) { throw 'Installed file verification failed.' }
        }
        Remove-Item -LiteralPath $journalPath
    } catch {
        $failure = $_
        Recover-Transaction $Store $AllowedPaths
        throw $failure
    }
}
