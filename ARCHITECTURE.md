# Architecture

## Platform choice

Erase Launcher uses WPF on .NET 8 for a small, self-contained Windows 10/11 x64 application. WPF is deliberately chosen over WinUI 3 here: it provides predictable self-contained deployment without a Windows App SDK bootstrapper, while still allowing a fully custom DPI-aware interface.

The application starts as a normal user. Packages are deployed and removed for the current user through `Windows.Management.Deployment.PackageManager`, which needs no elevation and reports real progress. Only importing the signing certificate requests UAC, through an elevated, narrowly scoped PowerShell process, and that step is skipped when the certificate is already trusted. The launcher never changes Defender, SmartScreen, certificate validation, or system-wide security settings.

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

Download artifacts are written to `%LocalAppData%\EraseLauncher\Cache\*.partial`. A file is renamed into its final cache path only after its SHA-256 matches the remote manifest. The download service retries a source three times, then falls back to the next HTTPS mirror. Interrupted downloads resume with an HTTP Range request; a partial file that fails verification is deleted and never resumed. Progress uses the manifest size, so it is accurate even when the server omits Content-Length.

## Progress

`InstallationProgressPlan` gives each state that will actually run a weight. Downloads share their budget by byte count (download plus SHA-256 verification), APPX removal and deployment use the percentage reported by Windows, and backup/restore report copied bytes. The overall percentage never decreases and only reads 100% when the installation has completed.

The launcher never removes a Minecraft package before its replacement and every required artifact have downloaded and passed verification. It refuses to switch versions while `Minecraft.Windows` is running.

Data preservation intentionally backs up the relevant `LocalState\games\com.mojang` tree, rather than deleting the entire package directory. Backup copies remain under `%LocalAppData%\EraseLauncher\Backups` if an operation fails, so recovery is possible.

## Manifest schema

The launcher reads schema version `2` from `https://cdn.erasemc.com/v2/manifest.json`. Schema `2` adds GDK builds, which launchers released before it cannot install, so the schema `1` manifest at `/manifest.json` stays in place for them and only lists APPX builds. Each version is dynamic and must supply its own package metadata; the UI does not manufacture fallback version data.

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

Schema `2` adds two optional fields:

- `format` on a version: `appx` (the default) or `msixvc` for a GDK build. A GDK build has no certificate.
- `minVersion` on a dependency: the oldest installed version that satisfies it.
- `client` at the top level: the Erase Client DLL (`fileName`, `urls`, `sha256`, `size`), validated like any other artifact.

Every URL must be HTTPS, every filename must be a filename only (not a path), and every SHA-256 value must be exactly 64 hexadecimal characters. The single exception is a GDK package: Microsoft only publishes those on `assets1.xboxlive.com` and `assets2.xboxlive.com` over plain HTTP, so those two hosts are accepted for `msixvc` packages. The pinned SHA-256 still has to match before anything is installed. Invalid manifests fail closed.

## GDK builds

Minecraft moved from UWP to the Microsoft GDK in 1.21.120. GDK packages are encrypted `.msixvc` files tied to the account's licence, so they are never mirrored on the Erase CDN. They share the `Microsoft.MinecraftUWP_8wekyb3d8bbwe` family with the UWP builds, so only one build is installed at a time:

- GDK to GDK replaces the package in place (`ForceUpdateFromAnyVersion`); game data under `%AppData%\Minecraft Bedrock` is untouched.
- Any switch that involves a UWP build removes the installed package first. That deletes the UWP `LocalState`, which is why the optional backup runs only in this case. A backup restored onto a GDK build goes to `Users\Shared\games\com.mojang`, without the UWP settings folder.

Installing a GDK build requires Xbox Gaming Services; when it is missing the installation stops before downloading and the launcher opens its Store page. A GDK install is recognised by the `MicrosoftGame.config` file in the package folder, and the game is launched through the package's own app-list entry, because GDK and UWP builds use different application ids.

## Server list

Before every launch the launcher makes sure `erasemc.com:19132` is in `external_servers.txt`: in the UWP package's `LocalState`, and in each GDK account folder under `%AppData%\Minecraft Bedrock\Users`. When the server is missing it is added as the first entry and the others move down; an existing entry, under any name, is left untouched.

## CDN and deployment

The production endpoint is `https://cdn.erasemc.com/manifest.json`; normal installation never uses GitHub. CDN payloads are external deployment artifacts and are intentionally not committed to this repository.

The CDN is served by Nginx with HTTPS, automatic certificate renewal and byte-range support. Its payloads and manifest are deployment artifacts, not repository files. The live manifests list Minecraft Bedrock `1.16.100`, `1.18.12` and `1.21.50`; the schema `2` manifest also lists the GDK build `1.26.52`. Package, certificate and prerequisite hashes are generated from the deployed files and, for GDK, from the file Microsoft serves.

The live deployment uses public `.cer` signing certificates only. A `.pfx` is optional in the client schema, but private-key material must not be published to the CDN or committed to source control. Each deployment must validate the JSON, run `nginx -t`, and verify the public manifest and a ranged package response before it is considered ready.

## Publishing

The project publishes self-contained for `win-x64` with a single-file executable:

```powershell
dotnet publish src/EraseLauncher -c Release -r win-x64 --self-contained true -o artifacts\win-x64
```

Single-file publishing is enabled because no app-local native payload has been added. The release must be smoke-tested on a Windows machine without the .NET SDK before publishing to GitHub.

## Erase Client

Erase Client (`client/`) is a C++20 DLL loaded into `Minecraft.Windows.exe`. `DllMain` only starts a thread; everything else runs from `client::run`, which loads the config, installs the hooks and waits for an unload request.

- **Rendering.** The client creates throwaway D3D11 and D3D12 swap chains to find DXGI's `Present`, `Present1`, `ResizeBuffers` and `ResizeBuffers1`, and hooks them with MinHook. On the first frame it picks a backend from the game's swap chain: D3D11 (1.16.100) or D3D12 (RenderDragon builds). D3D12 work must go to the queue the game presents on, which is taken from the swap chain or, failing that, from the game's first `ExecuteCommandLists` call on a direct queue.
- **Input.** The game window is subclassed. Messages are queued for the render thread, which owns ImGui, so ImGui is never touched from the UI thread. While the menu is open, keyboard, mouse, pointer and raw input are kept from the game. UWP builds deliver the mouse as pointer messages in screen coordinates; both forms are handled.
- **Platform.** A process in an AppContainer is a UWP build, any other is GDK. The game version comes from the package version (`1.21.5003.0` is `1.21.50`). Config and logs live in the package's `RoamingState\EraseClient`, which both platforms can write to.
- **Window title.** UWP titles are set through `ApplicationView` on the UI thread. GDK titles are set with `WM_SETTEXT` and checked every second, because the game may write its own back.
- **Modules.** A `Module` has a name, an enabled state, an optional key binding and a `render` method that draws on the HUD. Every module method runs on the render thread. Settings are stored under `modules.<name>` in `config.json` and written whenever the menu closes and on unload.

### Loading the client

On Play, with **Load Erase Client** on, `ClientService` takes `EraseClient.dll` from next to the launcher (a development build) or downloads and verifies the manifest's `client` artifact. The DLL is copied to `%LocalAppData%\EraseLauncher\Client\<hash>\EraseClient.dll`: a DLL Minecraft still has loaded is locked, so every build gets its own folder. The copy is readable by `ALL APPLICATION PACKAGES` and `ALL RESTRICTED APPLICATION PACKAGES`; without that, a UWP build in its AppContainer cannot load it.

After launching the game, the launcher waits for `Minecraft.Windows.exe` and for it to load `dxgi.dll`, then runs `LoadLibraryW` on a remote thread and checks that the module appeared. It does nothing if the client is already loaded, and the DLL itself refuses a second copy in the same game. When the client cannot be prepared or loaded, the game still starts and the reason goes to the launcher log.

Unloading closes the menu, lets the render thread tear down ImGui on its next frame, restores the window procedure, removes the hooks, saves the config and frees the DLL.
