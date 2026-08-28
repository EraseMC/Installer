# Architecture

## Platform choice

Erase Launcher uses WPF on .NET 8 for a small, self-contained Windows 10/11 x64 application. WPF is deliberately chosen over WinUI 3 here: it provides predictable self-contained deployment without a Windows App SDK bootstrapper, while still allowing a fully custom DPI-aware interface.

The application starts as a normal user. Only certificate import and Windows package deployment request UAC through an elevated, narrowly scoped PowerShell process. The launcher never changes Defender, SmartScreen, certificate validation, or system-wide security settings.

## Application structure

- `Models` holds manifest, settings, package and installation-state data.
- `Services` own network, hashing, package detection, backup and deployment work.
- `ViewModels` expose UI state and commands with no WPF-specific installation logic.
- `Views` contain presentation and window chrome only.
- `Resources` centralize colour, typography, card and control tokens.

`MainViewModel` coordinates navigation and turns service progress into UI state. `InstallationService` owns the installation state machine; it is the only service allowed to sequence destructive package changes.

## Installation flow

```text
manifest → validate → disk/prerequisite checks → download all artifacts
         → SHA-256 verify → optional data backup → certificate/dependencies
         → remove current package → install target APPX → verify → restore data
```

Download artifacts are written to `%LocalAppData%\EraseLauncher\Cache\*.partial`. A file is renamed into its final cache path only after its SHA-256 matches the remote manifest. The download service retries a source three times, then falls back to the next HTTPS mirror.

The launcher never removes a Minecraft package before its replacement and every required artifact have downloaded and passed verification. It refuses to switch versions while `Minecraft.Windows` is running.

Data preservation intentionally backs up the relevant `LocalState\games\com.mojang` tree, rather than deleting the entire package directory. Backup copies remain under `%LocalAppData%\EraseLauncher\Backups` if an operation fails, so recovery is possible.

## Manifest schema

The client accepts schema version `1`. Each version is dynamic and must supply its own package metadata; the UI does not manufacture fallback version data.

```json
{
  "schemaVersion": 1,
  "versions": [{
    "id": "1.16.100",
    "displayName": "Minecraft Bedrock 1.16.100",
    "package": {
      "fileName": "minecraft.appx",
      "urls": ["https://cdn.erasemc.com/minecraft/1.16.100/minecraft.appx"],
      "sha256": "64-character uppercase-or-lowercase SHA-256",
      "size": 123456
    },
    "certificate": {
      "fileName": "certificate.cer",
      "urls": ["https://cdn.erasemc.com/minecraft/1.16.100/certificate.cer"],
      "sha256": "64-character SHA-256",
      "size": 1234
    },
    "dependencies": []
  }]
}
```

Every URL must be HTTPS, every filename must be a filename only (not a path), and every SHA-256 value must be exactly 64 hexadecimal characters. Invalid manifests fail closed.

## CDN and deployment

The production endpoint is `https://cdn.erasemc.com/manifest.json`; normal installation never uses GitHub. CDN payloads are external deployment artifacts and are intentionally not committed to this repository.

The CDN is served by Nginx with HTTPS, automatic certificate renewal and byte-range support. Its payloads and manifest are deployment artifacts, not repository files. The live manifest lists Minecraft Bedrock `1.16.100` and `1.18.12`, with their package, certificate and prerequisite hashes generated from the deployed files.

The live deployment uses public `.cer` signing certificates only. A `.pfx` is optional in the client schema, but private-key material must not be published to the CDN or committed to source control. Each deployment must validate the JSON, run `nginx -t`, and verify the public manifest and a ranged package response before it is considered ready.

## Publishing

The project publishes self-contained for `win-x64` with a single-file executable:

```powershell
dotnet publish src/EraseLauncher -c Release -r win-x64 --self-contained true -o artifacts\win-x64
```

Single-file publishing is enabled because no app-local native payload has been added. The release must be smoke-tested on a Windows machine without the .NET SDK before publishing to GitHub.
