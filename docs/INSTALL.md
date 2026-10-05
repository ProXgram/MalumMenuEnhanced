# Install MalumMenu Enhanced

For **Steam / Microsoft Store / Epic Games / Xbox App on Windows PC**.
Use Among Us **19.0.0 / 2026.9.29**, Windows 10/11, 64-bit.

## Option 1: All-in-one installer

For **Steam / Microsoft Store / Xbox App**.

1. [Download the installer](https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v1.0.1/MalumMenuEnhancedSetup.exe).
2. Close Among Us. Open the downloaded file and click **Install**.
3. Open Among Us normally and press **Delete**.

The installer finds your game and chooses the correct loader. If it cannot
find the game, click **Browse** and select `Among Us.exe`. It keeps your settings
and backs up the previous menu. No separate .NET download is needed.

## Option 2: Manual ZIP

Choose the ZIP for your launcher:

- **[Steam ZIP](https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v1.0.1/MalumMenuEnhanced-1.0-Steam.zip)**
- **[Microsoft Store / Epic Games / Xbox App ZIP](https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v1.0.1/MalumMenuEnhanced-1.0-MicrosoftStore-EpicGames-XboxApp.zip)**

1. Download your ZIP.
2. Close Among Us and open your game folder using the steps below.
3. Extract the ZIP. Copy **all the files and folders inside it** into the folder containing `Among Us.exe`.
4. Open Among Us from your usual launcher. The first launch takes longer while the mod prepares its files. When it finishes, press **Delete**.

Copy `BepInEx`, `dotnet` and the loose files directly beside `Among Us.exe`.
Do not copy just the outer ZIP folder. The ZIP already includes the loader;
there is no separate loader download.

## Find your game folder

| Your PC launcher | Open this folder |
| --- | --- |
| Steam | **Library** → right-click **Among Us** → **Manage** → **Browse local files**. |
| Microsoft Store / Xbox App | Open **Xbox App** → **Among Us** → **Manage** → **Files** → **Browse** → **Content**. |
| Epic Games | **Library** → **Among Us** → **Manage** → **Installation** → click the **folder icon**. |

If you bought the game through Microsoft Store, use its **Xbox App** entry to
browse the game files. Choose the editable game folder; these instructions do
not require changing Windows permissions.

## Replacing another menu

Before copying the new files, back up old `MalumMenu.dll`, `HaddadMenu.dll` or
`MalumMenuEnhanced.dll` outside `BepInEx/plugins`. Keep only one menu DLL active.

Xbox / Microsoft Store gameplay has been tested. Steam and Epic Games gameplay
have not yet been tested. These packages are for Among Us 2026.9.29 on Windows
PC.

[Download page](https://github.com/ProXgram/MalumMenuEnhanced/releases/tag/v1.0.1) · [Source](https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v1.0.1/MalumMenuEnhanced-1.0.1-Source.zip) · [Credits](../CREDITS.md)
