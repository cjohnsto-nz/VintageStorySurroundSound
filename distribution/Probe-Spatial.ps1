#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$DllPath, [Parameter(Mandatory = $true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
try {
    $DllPath = (Resolve-Path -LiteralPath $DllPath).Path
    $OutputPath = (Resolve-Path -LiteralPath $OutputPath).Path
    $config = Join-Path $OutputPath 'probe.ini'
    $log = Join-Path $OutputPath 'openal.log'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'spatial.ini') -Destination $config
    $env:ALSOFT_CONF = $config
    $env:ALSOFT_DRIVERS = 'wasapi'
    $env:ALSOFT_LOGFILE = $log
    $env:ALSOFT_LOGLEVEL = '3'
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class SpatialDeviceProbe {
    [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("OpenAL32", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr alcOpenDevice(IntPtr name);
    [DllImport("OpenAL32", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr alcCreateContext(IntPtr device, int[] attributes);
    [DllImport("OpenAL32", CallingConvention=CallingConvention.Cdecl)] static extern void alcGetIntegerv(IntPtr device, int param, int count, out int value);
    [DllImport("OpenAL32", CallingConvention=CallingConvention.Cdecl)] static extern void alcDestroyContext(IntPtr context);
    [DllImport("OpenAL32", CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] static extern bool alcCloseDevice(IntPtr device);
    public static bool Run(string path) {
        if (LoadLibraryEx(path, IntPtr.Zero, 8) == IntPtr.Zero) throw new Exception("Cannot load x64 OpenAL runtime: " + Marshal.GetLastWin32Error());
        IntPtr device = IntPtr.Zero, context = IntPtr.Zero;
        try {
            device = alcOpenDevice(IntPtr.Zero);
            if (device == IntPtr.Zero) throw new Exception("Cannot open default Windows audio output.");
            context = alcCreateContext(device, new int[] { 0x1992, 0, 0x19AC, 0x19AD, 0 });
            if (context == IntPtr.Zero) throw new Exception("Cannot create spatial audio context.");
            Thread.Sleep(5000);
            int connected;
            alcGetIntegerv(device, 0x313, 1, out connected);
            return connected != 0;
        } finally {
            if (context != IntPtr.Zero) alcDestroyContext(context);
            if (device != IntPtr.Zero) alcCloseDevice(device);
        }
    }
}
'@
    $connected = [SpatialDeviceProbe]::Run($DllPath)
    $evidence = Get-Content -LiteralPath $log -Raw
    if (-not $connected -or $evidence -notmatch 'Spatial audio stream activated: static mask 0x1ffe' -or
        $evidence -notmatch 'Post-start: 7.1.4 Surround' -or $evidence -match 'Failed to start spatial audio stream') {
        throw 'A connected 7.1.4 Windows spatial stream was not confirmed.'
    }
    Write-Host 'Spatial stream activated and remained connected for five seconds. Receiver format is not verified.'
    exit 0
} catch { Write-Error $_; exit 2 }
