#requires -Version 7.0
# Stock 1.25.2 crashes during spatial activation on the tested receiver.
# Build the pinned source with the ownership fix instead of installing that DLL.
[CmdletBinding()]
param()
& (Join-Path $PSScriptRoot 'Build-SpatialRuntime.ps1')
