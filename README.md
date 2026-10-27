# DLSSG SM86 Swap Tool

A Windows desktop manager for the
[dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86) DLSS Frame
Generation mod: installs the proxy DLL, the INI and the mod's data folder next
to a game's rendering EXE, swaps entry points, edits the INI, backs up
originals, and uninstalls cleanly. Built with WinUI 3 / Windows App SDK.

## Features

- Installs any proxy (`version.dll`, `winmm.dll`, `dbghelp.dll`, `dinput8.dll`,
  `dxgi.dll`, `d3d12.dll`), `dlssg_sm86.ini` and the payload's `dlssg_sm86\`
  folder; swapping keeps only one package proxy. `version.dll` is the default.
- Bundles the 0.4.1 payload (runtime 310.9.16, up to 6X), SHA256-verified. The
  proxy alone does nothing — the mod loads its runtime, backend and kernel packs
  from `dlssg_sm86\` — so the tool writes and removes the whole set, and reports
  a 0.3.x install as needing a reinstall instead of a silent upgrade.
- Writes the supported INI keys, preserving comments, clamped to the payload:
  `[General] Enabled`, `[FrameGeneration] Engine` / `MaxGeneratedFrames`,
  `[DLSSG] Optimized` / `Preset`, `[Logging] Level`. Factory default is 4X; 6X
  needs `MaxGeneratedFrames=5`, `Engine=XeSS` stops at 4X, and the preset is
  A / B / C (0.4.1 removed `Auto`).
- Backs up pre-existing files once per game and restores them on uninstall.
  Foreign files are only overwritten after confirmation, and a `dlssg_sm86`
  file that no longer matches what was installed is left alone.
- Manual folder picker and Steam library scanner.
- Verifies installed hashes and reads the mod's own log
  (`dlssg_sm86\logs\dlssg_sm86.log`, one file for every process since 0.4.1):
  the route is confirmed from the game's own lines, and a rejected INI key is
  reported with the key and the values it accepts.
- Detects the GPU via `nvidia-smi`: RTX 30 (SM86) and RTX 20 (Turing / SM75)
  are supported; other GPUs are refused.

## Requirements

- Windows 10 1809+ / 11 x64, with the
  [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
  and
  [Windows App SDK 1.8 Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).
- An RTX 30 (SM86) or RTX 20 (SM75) GPU, an NVIDIA driver with `nvidia-smi`
  (R580+ recommended), and the game **exited** during install/uninstall.
- Releases ship as a small `.7z` (recommended) and a plain `.zip` fallback with
  identical contents. Windows 11 (24H2+) opens `.7z` in File Explorer; on
  Windows 10 either install [7-Zip](https://7-zip.org/) or use the `.zip`.

## Build

```sh
git clone --recursive https://github.com/you/dlssg-swapper.git
cd dlssg-swapper

dotnet build DlssgSwapper.sln -c Release
# run: src/DlssgSwapper.App/bin/x64/Release/net9.0-windows10.0.19041.0/DlssgSwapper.exe

# or publish a runnable x64 folder (needs the Windows App SDK Runtime)
dotnet publish src/DlssgSwapper.App -c Release -r win-x64 -o out
```

`payloads/catalog.json` verifies the bundled proxy DLLs and `dlssg_sm86\` files
against the pinned `external/dlssg_for_sm86` submodule; update both together. A
file from an older release is still recognised (`legacySha256` for proxies,
`legacyAssetSha256` for the `dlssg_sm86\` tree), so it is replaced or
uninstalled instead of being treated as someone else's file.

The super-resolution files (`dlssg_sm86\sr\`) ship only in the upstream release
asset, not in the repository, so `[SuperResolution] Mode` is left at `Off` here.

## Usage

1. Run the tool and let it detect your GPU.
2. Add a game via **Add…** or **Steam…**.
3. Pick an entry point (usually `version.dll`) and the frame-generation settings.
4. **Install / Swap**. Confirm if the DLL name belongs to another mod.
5. Enable DLSS frame generation in-game, then use **Verify**.
6. **Uninstall** restores the originals; **Remove** forgets a game.

## Safety

The mod uses a system-DLL proxy and LoadLibrary hooks, so antivirus/SmartScreen
may flag the bundled DLLs. Keep one package proxy per game; prefer the utility
proxies, and use `dxgi.dll` / `d3d12.dll` only when the game ignores the others
(never both). Do not use with online/anti-cheat games unless you accept the
risk. Backups: `%LOCALAPPDATA%\DlssgSwapper\backups\<profileId>\`.

## License

UI: WinUI 3 and Windows App SDK (MIT). Payload DLLs and INI templates:
[sdli1995/dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86) (GPLv3);
the NVNGX assets inside them are third-party and not relicensed here. See
`external/dlssg_for_sm86/THIRD_PARTY_NOTICES.txt`.
