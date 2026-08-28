# Erase Launcher

Erase Launcher is an independent community utility for installing supported legacy Minecraft Bedrock builds used by Erase servers.

It is not affiliated with, endorsed by, or provided by Mojang, Microsoft, or Minecraft.

## What it does

- Gets its supported-version list from `https://cdn.erasemc.com/manifest.json`.
- Downloads packages through HTTPS mirrors with retry and fallback support.
- Verifies every downloaded artifact with SHA-256 before package changes begin.
- Backs up Minecraft worlds and packs when the user opts in.
- Installs the required certificate, dependencies and APPX through explicit Windows package operations.
- Detects the installed Minecraft package and can launch it through Windows AppsFolder.

## Supported versions

The target production builds are Minecraft Bedrock `1.16.100` and `1.18.12`. Available versions are supplied dynamically by the Erase CDN; the launcher deliberately does not show unverified, hardcoded fallback entries.

## Requirements

- Windows 10 or Windows 11, x64
- Internet access to `cdn.erasemc.com`
- Permission to approve Windows UAC only when installing certificates or APPX packages

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

More detail, including the manifest contract and safe installation sequence, is in [ARCHITECTURE.md](ARCHITECTURE.md).

## Integrity and privacy

The launcher does not execute downloaded code. It downloads package files, compares SHA-256 checksums against a validated manifest, and only then asks Windows to perform documented APPX and certificate operations. It does not disable security features or add antivirus exclusions.

Local cache, backups, settings and logs are stored under `%LocalAppData%\EraseLauncher`. Certificates, PFX files, credentials, build output and logs are excluded from source control.

## Community

Join Erase: https://dsc.gg/erasemc
