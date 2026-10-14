# Erase Launcher

Erase Launcher is an independent community utility for installing supported legacy Minecraft Bedrock builds used by Erase servers.

It is not affiliated with, endorsed by, or provided by Mojang, Microsoft, or Minecraft.

## What it does

- Gets its supported-version list from `https://cdn.erasemc.com/v2/manifest.json`.
- Downloads packages through HTTPS mirrors with retry and fallback support.
- Verifies every downloaded artifact with SHA-256 before package changes begin.
- Backs up Minecraft worlds and packs when the user opts in.
- Installs the required certificate, dependencies and APPX through explicit Windows package operations.
- Installs GDK builds from Microsoft's Xbox CDN through Gaming Services, pinned to a SHA-256 like every other download.
- Adds the Erase server (`erasemc.com:19132`) to Minecraft's server list before every launch.
- Detects the installed Minecraft package and launches it.

## Supported versions

| Version | Package | Notes |
|---|---|---|
| `1.16.100` | UWP APPX | Sideloaded with the Erase package certificate |
| `1.18.12` | UWP APPX | Sideloaded with the Erase package certificate |
| `1.21.50` | UWP APPX | Signed by Microsoft, no extra certificate |
| `1.26.52` | GDK MSIXVC | Downloaded from Microsoft; needs Gaming Services and a Minecraft licence on the signed-in account |

Available versions are supplied dynamically by the Erase CDN; the launcher deliberately does not show unverified, hardcoded fallback entries.

## Requirements

- Windows 10 or Windows 11, x64
- Internet access to `cdn.erasemc.com` (and to `assets1.xboxlive.com` for GDK builds)
- Xbox Gaming Services and a Minecraft licence for GDK builds
- Permission to approve Windows UAC once, when the package certificate is not yet trusted

## Installation

Download `EraseLauncher.exe` from the GitHub Releases page, launch it, choose a version, and confirm installation. The launcher asks before it replaces an existing Minecraft package and before it preserves user data.

## Developer build

Install the .NET 8 SDK, then run:

```powershell
dotnet build src/EraseLauncher -c Release
dotnet run --project src/EraseLauncher
```

To publish a self-contained executable:

```powershell
dotnet publish src/EraseLauncher -c Release -r win-x64 --self-contained true -o artifacts\win-x64
```

## Project layout

The WPF application is organized around services and MVVM:

- `src/EraseLauncher/Services` — manifest, downloading, hashing, package, backup and logging services
- `src/EraseLauncher/ViewModels` — navigation, user-facing state and commands
- `src/EraseLauncher/Views` — WPF presentation and window shell
- `src/EraseLauncher/Resources` — reusable design tokens and controls

The in-game client is a separate C++ project in `client/` (see below).

More detail, including the manifest contract and safe installation sequence, is in [ARCHITECTURE.md](ARCHITECTURE.md).

## Erase Client

`client/` holds Erase Client, a DLL that draws an in-game menu and HUD modules on top of Minecraft. It is in early development: the launcher does not load it yet. The client is licensed under the GNU AGPL v3 (`client/LICENSE`), and the libraries it bundles are listed in `client/THIRD_PARTY_NOTICES.md`.

It is built by the `client` GitHub Actions workflow on every push that touches it; the DLL and its PDB are attached to the run as the `EraseClient-win-x64` artifact. A local build needs Visual Studio 2022 with the C++ workload and CMake 3.25+:

```powershell
cmake -S client -B client/build -A x64
cmake --build client/build --config Release
```

## Integrity and privacy

The launcher does not execute downloaded code. It downloads package files, compares SHA-256 checksums against a validated manifest, and only then asks Windows to perform documented APPX and certificate operations. It does not disable security features or add antivirus exclusions.

Local cache, backups, settings and logs are stored under `%LocalAppData%\EraseLauncher`. Certificates, PFX files, credentials, build output and logs are excluded from source control.

## Community

Join Erase: https://dsc.gg/erasemc
