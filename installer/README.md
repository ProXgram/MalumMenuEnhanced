# MalumMenu Enhanced Setup

A single-file Windows downloader and installer by Rifegul. The GUI is a
self-contained WinForms application; users do not install .NET separately.

The core installer stages both verified downloads before modifying the game,
checks the selected game folder and archive paths, preserves existing settings
and unrelated plugins, backs up previous menu files, and rolls back on failure
or cancellation. It never changes permissions or requests administrator access.

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

The installer downloads these exact artifacts and checks their SHA256 before
use. It does not execute an unknown latest download.

| Component | Source | SHA256 |
| --- | --- | --- |
| BepInEx 6 IL2CPP x64 build 755 | [Official archive](https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip) | `3616D6A67F5F595973EC4AA7BD7EDAF7F799D5BB9926F7146A6DCC7B4ABF478F` |
| MalumMenu Enhanced 1.0 plugin | [Pinned repository archive](https://raw.githubusercontent.com/ProXgram/MalumMenuEnhanced/88bdfd5c1ae74f92933b375adb19e5f309895da5/downloads/v1.0/MalumMenuEnhanced-1.0-Plugin.zip) | `227F8D82300F5F89D30C49BEEB4E83C152C072D07BAC5400B194D0D6FBB6B8C1` |

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

The manual package includes the verified BepInEx loader and the version 1.0
plugin. It uses one extraction layout for Microsoft Store / Epic Games / Xbox
App on PC. Xbox / Microsoft Store is tested; Epic Games is not yet tested.

Run `python scripts/package-manual.py` to package the pinned archives with the
bundled dependency notices. Its source inputs, checksums and licenses are
documented under `ManualLegal`. Then run `python scripts/package-setup.py` to
update the corresponding source archive.
