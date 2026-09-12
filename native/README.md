# OpenAL Soft spatial activation fix

The feature uses OpenAL Soft 1.25.2 plus `openal-soft-spatial-ownership.patch`.

The unmodified upstream Win64 binary and its own `openal-info64.exe` reproducibly terminated with Windows exception `0xc0000374` on the tested NVIDIA HDMI receiver endpoint. The 1.24.3 binary also failed in the probe.

`PropVariant::setBlob()` in `alc/backends/wasapi.cpp` stored a pointer to stack-owned `SpatialAudioObjectRenderStreamActivationParams` data. `PropVariant` subsequently calls `PropVariantClear`, which owns cleanup of a `VT_BLOB`. The patch allocates and copies the data using `CoTaskMemAlloc`, so cleanup can free it correctly. It also adds a trace of successful spatial stream activation for diagnostics. The API, ABI and mixing behavior are unchanged.

After the patch, the isolated native probe successfully started twelve static channels on `OpenAL Soft on AV Receiver (NVIDIA High Definition Audio)`, with mask `0x1ffe`, 48 kHz float output and 480/960-sample update/buffer sizes. Receiver format and audible reproduction are separate checks.

## Rebuild

Run `tools/Build-SpatialRuntime.ps1` with PowerShell 7, Git, CMake, Visual Studio 2022 C++ Build Tools and the Windows SDK installed. It verifies the pinned source archive, applies the checked-in patch, builds a Release x64 DLL and records source/patch/binary SHA-256 values in `bin/openal-build/surround-runtime.json`. A first build can take several minutes; subsequent calls reuse a verified cached binary.

[Source archive](https://github.com/kcat/openal-soft/archive/refs/tags/1.25.2.zip)

SHA-256: `47e22c066dffa2f65a1747272978344db845dcef9c7ee118449ec560a44adc8f`

The exact patched source remains under `bin/openal-source/openal-soft-1.25.2`. The upstream project is LGPL-licensed and includes additional notices for embedded components. The sandbox copies top-level upstream notices into `OpenAL-notices`. If distributing the modified native binary, include the corresponding source (including this patch and build instructions) and retain the applicable upstream notices. Native binaries and source archives are not committed to this repository.

The source patch is local; no upstream issue or pull request has been submitted.

## Distribution build verification

Fresh-build testing exposed a Git behavior that could silently skip the patch when the source was extracted under this repository's ignored `bin` directory. The builder now stops Git repository discovery at the extracted source's parent, normalizes patch line endings, verifies the applied patch in reverse, and checks the ownership fix in the resulting source. Cached builds additionally require `PatchVerified` and a matching full source-tree hash. A legacy cache lacking that evidence is rebuilt.

The release builder produces a complete modified source archive with a standalone build recipe, dated modification notices and the checked-in patch. The add-on retains standalone upstream licenses and the source files containing embedded component notices. The build manifest identifies the native source/patch/binary hashes and compiler. Final publication still requires reviewing the assembled notices/source bundle and testing the exact release candidate.
