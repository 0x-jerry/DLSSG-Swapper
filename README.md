# DLSSG SM86 Swap Tool

A Windows desktop manager for the [dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86)
DLSS Frame Generation mod: install, swap between payload versions and proxy
entry points, edit the mod's INI, back up originals, and uninstall cleanly.

The UI is a Windows 11 Fluent shell built on WinUI 3
([microsoft-ui-xaml](https://github.com/microsoft/microsoft-ui-xaml) /
Windows App SDK) with a left navigation pane
(**Games / Install / Settings / About**), Mica window backdrop, rounded corners
and light/dark theming that follows the system by default (overridable on the
Settings page).

The mod ships as a proxy DLL plus a `dlssg_sm86.ini` that must be placed next
to the game's actual rendering executable (e.g. `b1-Win64-Shipping.exe`).
This tool does that for you and keeps track of what it changed.

## What it does

- **Payload catalog** — knows the bundled 0.2.4 Native payload and
  verifies each DLL against its SHA256 before use.
- **Install / swap** — copies the chosen proxy + INI next to the game EXE.
  Swapping entry points (`version.dll`, `winmm.dll`, `dinput8.dll`,
  `winhttp.dll`, `dxgi.dll`) or versions removes the previously installed
  proxy so only one package proxy remains. Entry points whose DLL name
  already exists in the game folder are marked as found; every name stays
  selectable and `version.dll` remains the recommended default.
- **Schema-faithful INI** — writes the five native 0.2.4 keys (`Router`,
  `KernelImage`, `HardwareBilinear`, `MaxGeneratedFrames`, `Logging.Level`)
  into the bundled template, preserving comments and untouched keys.
- **Safe backup/restore** — pre-existing files are backed up once per game;
  uninstall restores byte-identical originals. Foreign files (e.g. ReShade's
  `dxgi.dll`) are never overwritten without confirmation and are backed up
  before replacement.
- **Game discovery** — manual folder picker and a Steam library scanner
  (libraryfolders.vdf + app manifests) that suggests likely rendering EXEs.
- **GPU detection** — reads `nvidia-smi` compute capability (8.x → SM86,
  7.x → SM75) and pre-selects the Router suggestion.
- **Verify** — checks installed file hashes and, if the mod has written logs,
  reports `install.active` / `backend_install.status` from `dlssg_sm86/logs`.

## Requirements

- Windows 10 1809+ / 11 x64, .NET 9 Desktop Runtime (install via
  https://dotnet.microsoft.com/download/dotnet/9.0).
- Windows App SDK 1.8 Runtime (x64). The app is unpackaged and
  framework-dependent, so this runtime must be installed: see
  https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads.
- The game must be **exited** before install/uninstall.
- NVIDIA driver with `nvidia-smi` (ships with the driver) for GPU detection;
  fall back to a manual Router choice otherwise.

## Build

```sh
# clone with the payload submodule
git clone --recursive https://github.com/you/dlssg-swapper.git
cd dlssg-swapper

dotnet build DlssgSwapper.sln -c Release
# run: src/DlssgSwapper.App/bin/x64/Release/net9.0-windows10.0.19041.0/DlssgSwapper.exe

# or publish a runnable x64 folder (framework-dependent: requires the
# Windows App SDK Runtime on the target machine)
dotnet publish src/DlssgSwapper.App -c Release -r win-x64 -o out
```

The default `catalog.json` verifies the bundled DLLs against the hashes of the
`dlssg_for_sm86` submodule commit pinned in this repository. If the payload
files are missing or corrupted, the tool refuses to start and names the file.
Update the submodule and `catalog.json` together when adopting a new version.

## Usage

1. Run the tool. It detects your GPU and shows the suggested Router.
2. Add a game: **Add…** (browse to the rendering EXE) or **Steam…** (pick from
   the installed-game list, then its rendering executable).
3. Choose **Entry point** (usually `version.dll`), **Preset** (Default /
   Performance), and the INI settings.
4. **Install / Swap**. If the chosen DLL name is held by another mod (foreign
   file), the tool asks for confirmation; tick **Allow overwriting foreign
   files** and retry.
5. Launch the game, enable DLSS frame generation, and select 2X/3X/4X.
   Use **Verify** after playing to confirm the route from the mod's logs.
6. **Uninstall** restores the original files; **Remove** forgets a game and
   deletes its saved backups.

## Payloads

```
payloads/
├── catalog.json              # versions, entry points, SHA256, templates
├── templates/                # INI templates
└── bin/<version>/…           # the proxy DLLs, copied at build time from the submodule
```

The binaries are not stored in this repository: the App project copies them
from `external/dlssg_for_sm86` (the git submodule) into the build output.
To add a future payload version, add the files, register a `versions` entry in
`catalog.json` (with the real SHA256), and add the copy item to
`src/DlssgSwapper.Core/DlssgSwapper.Core.csproj`.

## Safety notes

- The mod uses a system-DLL proxy and LoadLibrary hooks. Matching this tool's
  behavior, antivirus and SmartScreen may flag the **bundled proxy DLLs**;
  verify hashes from `catalog.json` / the upstream release notes before use.
- Only one proxy from the package may be installed per game; the tool removes
  an earlier package proxy when you swap entry points.
- Do not install into online/anti-cheat-enabled games unless you accept the
  associated risk.
- Backups live in `%LOCALAPPDATA%\DlssgSwapper\backups\<profileId>\`; keep them
  until you have confirmed the mod works.

## License / third-party

The UI uses [WinUI 3 / microsoft-ui-xaml](https://github.com/microsoft/microsoft-ui-xaml)
and the [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) (MIT).

The bundled payload DLLs and INI templates come from
[sdli1995/dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86)
(project source: GPLv3 per the upstream `THIRD_PARTY_NOTICES.txt` reference).
The NVNGX-based assets inside them are third-party material with separate
licensing; this tool does not relicense them. See
`external/dlssg_for_sm86/THIRD_PARTY_NOTICES.txt` for provenance.