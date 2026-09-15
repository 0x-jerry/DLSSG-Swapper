# DLSSG SM86 Swap Tool

A Windows desktop manager for the
[dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86) DLSS Frame
Generation mod: installs the proxy DLL and INI next to a game's rendering EXE,
swaps versions and entry points, edits the INI, backs up originals, and
uninstalls cleanly. Built with WinUI 3 / Windows App SDK.

## Features

- Installs any proxy (`version.dll`, `winmm.dll`, `dbghelp.dll`, `dinput8.dll`,
  `dxgi.dll`, `d3d12.dll`) plus `dlssg_sm86.ini`; swapping keeps only one
  package proxy. `version.dll` is the default.
- Bundles the 0.3.1 payloads (310.9 up to 6X, 310.1 up to 4X), SHA256-verified.
- Writes the supported INI keys, preserving comments, clamped to the payload.
  Factory default is 4X; 6X needs `MaxGeneratedFrames=5` on 310.9.
- Backs up pre-existing files once per game and restores them on uninstall.
  Foreign files are only overwritten after confirmation.
- Manual folder picker and Steam library scanner.
- Verifies installed hashes and the logs written by the mod.
- Detects the GPU via `nvidia-smi`: RTX 30 (SM86) and RTX 20 (Turing / SM75)
  are supported; other GPUs are refused.

## Requirements

- Windows 10 1809+ / 11 x64, with the
  [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
  and
  [Windows App SDK 1.8 Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).
- An RTX 30 (SM86) or RTX 20 (SM75) GPU, an NVIDIA driver with `nvidia-smi`
  (R580+ recommended), and the game **exited** during install/uninstall.

## Build

```sh
git clone --recursive https://github.com/you/dlssg-swapper.git
cd dlssg-swapper

dotnet build DlssgSwapper.sln -c Release
# run: src/DlssgSwapper.App/bin/x64/Release/net9.0-windows10.0.19041.0/DlssgSwapper.exe

# or publish a runnable x64 folder (needs the Windows App SDK Runtime)
dotnet publish src/DlssgSwapper.App -c Release -r win-x64 -o out
```

`payloads/catalog.json` verifies the bundled DLLs against the pinned
`external/dlssg_for_sm86` submodule; update both together. A proxy from an older
release is still recognised (`legacySha256`) and can be replaced or uninstalled.

## Usage

1. Run the tool and let it detect your GPU.
2. Add a game via **Add…** or **Steam…**.
3. Pick an entry point (usually `version.dll`) and the INI settings.
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
