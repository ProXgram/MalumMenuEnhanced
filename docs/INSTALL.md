# Installation help

## Easy installation

Download [MalumMenuEnhancedSetup.exe](../downloads/setup/MalumMenuEnhancedSetup.exe?raw=true).
Close Among Us, open the file and click **Install**. Then launch Among Us from
your usual game launcher and press **Delete**.

The installer runs without a separate .NET download. It needs an internet
connection and downloads the compatible loader directly from the official
BepInEx site. It installs the verified version 1.0 plugin from this repository.

## Finding your game

If automatic detection misses your game, click **Browse** in the installer and
select `Among Us.exe`.

- **Xbox App:** Among Us → Manage → Files → Browse. Open the `Content` folder.
- **Epic Games:** Among Us → Manage → Installation → folder icon.

Choose the editable game folder exposed by your launcher. The installer does
not change Windows permissions or write into the protected WindowsApps folder.

## Supported version

This edition is for Among Us **2026.9.29** on 64-bit Windows 10/11. The tested
Xbox package version is **2026.9.293.0**. Epic Games installation has not been
tested. The installer does not support Steam or guess compatibility with newer
game versions.

## Updates and backups

Your settings and unrelated plugins are preserved. Previous menu DLLs are
backed up outside the active plugins folder before replacement. Cancelled or
failed installations restore any files changed during that attempt.

The installer does not download the game or purchased cosmetics. Among Us must
already be installed.

## Manual installation

1. Close Among Us.
2. If BepInEx is not installed, download the **BepInEx 6 Unity.IL2CPP-win-x64**
   loader from the [official downloads](https://builds.bepinex.dev/projects/bepinex_be).
   Follow the [official IL2CPP guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html).
3. Download the [plugin ZIP](../downloads/v1.0/MalumMenuEnhanced-1.0-Plugin.zip).
   Extract it into the game folder, placing `MalumMenuEnhanced.dll` in
   `BepInEx/plugins`.
4. Back up old `MalumMenu.dll`, `HaddadMenu.dll` or `MalumMenuEnhanced.dll` copies
   outside `BepInEx/plugins`. Keep one active menu DLL.
5. Launch Among Us normally and press **Delete**.

The corresponding [source archive](../downloads/v1.0/MalumMenuEnhanced-1.0-Source.zip)
and [credits](../CREDITS.md) are available separately.
