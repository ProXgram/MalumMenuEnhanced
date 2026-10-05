# MalumMenu Enhanced Setup

A single-file Windows downloader and installer by Rifegul. The GUI is a
self-contained WinForms application; users do not install .NET separately.

The core installer stages both verified downloads before modifying the game,
checks the selected game folder and archive paths, preserves existing settings
and unrelated plugins, backs up previous menu files, and rolls back on failure
or cancellation. It never changes permissions or requests administrator access.

Automatic installation supports Steam, Microsoft Store and Xbox App for
Among Us 2026.9.29 on Windows 10/11 x64. Steam libraries are discovered from
the launcher and its library manifests; users can also browse to the game.
The installer verifies the game's identity, version and native architecture
before making changes. Older 32-bit Steam builds are not accepted.

## Build

Use the .NET 10 SDK:

```powershell
dotnet publish installer/MalumMenuEnhanced.Setup/MalumMenuEnhanced.Setup.csproj -c Release -o artifacts/setup/publish
```

The output is `MalumMenuEnhancedSetup.exe`, targeting Windows 10/11 x64.
Debug symbols are excluded and our source paths are normalized. Build outputs
stay ignored; the verified executable is copied into `downloads/setup/` for
distribution with the repository's corresponding source archive.

## Download pins

The built-in release has pinned SHA256 values. A newer compatible release is
accepted only through the publisher's RSA/SHA256 signed update manifest.

| Component | Source | SHA256 |
| --- | --- | --- |
| BepInEx 6 IL2CPP x64 build 755 | [Official archive](https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip) | `3616D6A67F5F595973EC4AA7BD7EDAF7F799D5BB9926F7146A6DCC7B4ABF478F` |
| MalumMenu Enhanced plugin | Current release's `MalumMenuEnhanced-1.1.0-Plugin.zip` | Pinned in `SetupForm.Catalog` and authenticated by `updates/latest.json` |

The loader hash was computed from the official archive. It is not a signed
checksum supplied by its publisher. BepInEx is downloaded directly and is not
bundled in our executable. Rebuild the installer to change a pin after reviewing
and testing the new component.

## Licenses

Our source is covered by the repository's [GPL-3.0 license](../LICENSE). Original
mod credits and .NET runtime notices are embedded in the executable and visible
through **Credits & licenses**. The authoritative bundled runtime notices and
their provenance are under `MalumMenuEnhanced.Setup/Legal`.

Game compatibility and beginner instructions are in [installation help](../docs/INSTALL.md).

## Complete manual ZIP

The manual package includes the verified BepInEx loader and the version 1.1.0
plugin. There is a Steam ZIP and a Microsoft Store / Epic Games / Xbox App ZIP;
both use the same extraction layout and x64 loader for Among Us 2026.9.29.
Xbox / Microsoft Store gameplay is tested; Steam and Epic gameplay are not
yet tested. [Steam became 64-bit in this game update](https://github.com/Gurge44/EndlessHostRoles/releases/tag/v8.0.2).

Use `scripts/package-manual.py --version 1.1.0 --plugin-sha <SHA256> --dll-sha
<SHA256>` with the verified plugin archive and DLL hashes; add `--platform steam`
for Steam. Historical `--version 1.0` packages retain their original pins. Loader
inputs, checksums and licenses are documented under `ManualLegal`.

## Automatic updates

Steam / Microsoft Store / Xbox App plugins check `updates/latest.json` once at startup. Epic Games uses manual updates. The updater verifies the
publisher signature, product, numeric version, supported game versions, exact
release asset URLs, sizes and SHA256 values before launching a downloaded
installer. The helper waits for Among Us to close, verifies the actual native
game and installed DLL, and installs through the normal backup/rollback path.
It never closes or restarts the game. Offline or rejected checks leave it alone.
Updater settings are under **Delete → Settings** and take effect next launch.

An Among Us update may require SDK, patches, loader or native validation changes.
These must be implemented and tested before publishing a compatible mod build.
If a game update prevents the old plugin from loading, its updater cannot run;
the user must run the latest installer.

## Publishing an update

1. Update the mod and installer versions, supported game versions and SDK/native
   validation as needed. Build and test the actual target game release.
2. Package the plugin into `downloads/v<VERSION>/`, update the built-in installer
   pin, publish the single-file installer and create the manual ZIPs.
3. Install Python's `cryptography` library. Run `scripts/sign-update-manifest.py
   --version <VERSION> --private-key <KEY-OUTSIDE-THE-CHECKOUT> --game-version
   <GAME-VERSION>`; repeat `--game-version` for verified version aliases.
   The signing key must match `UpdateTrust.PublicKeyPem`. Keep it private and
   outside this repository; the script does not generate or publish keys.
4. Verify with the manifest test CLI, then package the complete corresponding
   source using `scripts/package-setup.py --release <VERSION>`.
5. Publish the matching plugin ZIP and setup EXE assets before activating the
   signed `updates/latest.json` on `main`. Keep previous release assets intact.

Maintainer tests: `tests/UpdateManifest.Tests`, `tests/AutomaticUpdate.Tests`,
`tests/AutoUpdate.Tests`, and the existing `tests/Setup.Core.Tests`.
