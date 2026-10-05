# MalumMenu Enhanced

**Rifegul's edition for Among Us 19.0.0 / 2026.9.29.**

Version **1.0** is a branding-only release: the edition is now **MalumMenu Enhanced**, maintained by **Rifegul**, with the plugin filename `MalumMenuEnhanced.dll`. Version numbering resets to **1.0** for Rifegul's renamed edition. Gameplay and host requirements are unchanged. Earlier release names and validation records retain their original versions.

The **Multi Role** bundle added in **3.3.38-au19** is under **Roles**: one toggle combines Vent access, selected-player tracking, native Scientist Vitals and Detective Notes with nearby-player Interrogate. The information tools run locally without hosting or changing the assigned role; vent requests still depend on host validation. Notes record confirmed deaths observed while enabled and preserve the rooms captured before kill animation movement. The draggable ability panel appears during a living player's Normal round or Freeplay, after closing the menu. **Close Detective Notes** and **Escape** close the bundle's notebook without needing the real Detective role. Automatic Tasks still requires manual activation each round. Original authors and source history are in [CREDITS.md](CREDITS.md).

## Install

Download the **1.0** [plugin package](downloads/v1.0/MalumMenuEnhanced-1.0-Plugin.zip) and its matching [source archive](downloads/v1.0/MalumMenuEnhanced-1.0-Source.zip) from `downloads/v1.0/`. **GitHub Release upload is pending**; these repository downloads are separate from a published GitHub Release.

`MalumMenuEnhanced-1.0-Plugin.zip` contains the plugin, documentation and license. It **requires a compatible BepInEx 6 IL2CPP installation**. Download the loader separately from the official BepInEx site.

1. Close Among Us.
2. For a fresh installation, download the **BepInEx 6 Unity.IL2CPP-win-x64** package from the [official BepInEx downloads](https://builds.bepinex.dev/projects/bepinex_be). Follow the [official IL2CPP installation guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html): extract the loader beside `Among Us.exe`, launch the game once to generate its files, then close the game.
3. If updating an existing menu, back up its DLL outside `BepInEx/plugins`. Extract the plugin package into the game folder so the new file is `BepInEx/plugins/MalumMenuEnhanced.dll`. Remove the previous active `MalumMenu.dll`, `HaddadMenu.dll` or older `MalumMenuEnhanced.dll`; keep only one copy of this menu active.
4. Launch the game normally. The version label shows **MalumMenu Enhanced v1.0**.
5. Press **Delete** to open the menu. **Settings** shows your edition and credits.

This edition targets Microsoft Store, Xbox App and Epic Games on Windows. For Xbox App, find the game folder through **Manage > Files > Browse**. For Epic Games, use **Manage > Installation > the folder icon**.

Existing settings and keybind profiles are preserved. The internal plugin ID, `BepInEx/config/MalumMenu.cfg` and `MalumProfile.txt` retain their compatibility names.

## Features

The menu includes movement, teleportation, player information, task controls, role options and host controls. [FEATURES.md](FEATURES.md) catalogs the inherited menu, and [FORK.md](FORK.md) describes the current additions and verified limits.

- **Automatic Tasks (manual activation each round):** **Delete > Tasks** completes your own eligible tasks. Completing other crew tasks requires host authority.
- **Changing colors:** **Delete > Appearance** offers **Rainbow body (my screen)**, a local palette preview in lobbies/rounds/Freeplay, and **Cycle lobby colors (shared)**, normal requests for available own-player colors before a round. Each has an adjustable interval. The lobby mode stops when the round starts and does not directly edit saved customization. **F8** stops either option; the local preview restores the current actual outfit color. Shared lobby visibility/server acceptance still needs testing.
- **Random Outfit:** **Delete > Appearance > Pick random outfit** chooses an available hat, skin, visor and pet. Free Cosmetics exposes the game's unlocked choices when enabled. Use it before a round or in practice; normal online rounds are excluded. A separate button restores the outfit from before the first random selection. Other clients' display and acceptance of unowned items remain unverified.
- **Fun:** **Delete > Fun** contains **Turbo Orbit**, **Follow Player**, **Zigzag Dash** and **Teleport Yo-Yo**. Choose a player for orbit/follow. The **Follow / orbit speed** slider defaults to **3x**; distant goals use at least **2x** to catch up. Follow approaches a clear spot beside the player and trails their movement; Orbit adjusts radius/direction around obstacles. Both wait if no nearby floor is usable. Save two floor positions for yo-yo; it alternates them at your selected interval for up to 12 jumps. Close the menu and chat to run a mode. **F8** or **STOP ALL** immediately stops it. Switching away from the game stops the mode. Only one movement mode runs at a time; speed is restored when it pauses or stops.
- **AI Tasks:** **Delete > Tasks > Start AI Tasks** walks your living Crewmate to a valid console for an assigned task, then advances one task step at that console. It visits later stages at their own locations and navigates around ground obstacles. You can enable it in the lobby before the round starts. It pauses for meetings/chat, handles Sample/Diagnostics/Wifi/Photos timers and skips unreachable stages. Unsupported stages, including untouched Monitor Mushroom timer setup, need manual use. Instant Automatic Tasks is disabled while AI Tasks controls completion.
- **Custom nickname:** under **Fun**, click the nickname preview to type; **Ctrl+A** selects the draft, **Backspace** deletes and **Enter** finishes editing. **Paste** and **Clear** are explicit buttons. **Set my nickname** applies the draft before a round through native validation and the normal own-name request. The game/host still decides which names it accepts. Typing alone never applies/saves the name and does not move your player.
- **Lag Mode:** **Delete > Fun > Lag Mode** alternates normal walking with occasional backward/forward snaps and brief bursts that hold your character still between teleports. Keep a movement key held to cycle through both, then release it to cancel pending jumps. The mode returns to normal walking automatically after each burst. Interval and distance sliders still apply, and every correction checks the ground path. Releasing input, pausing, changing direction, an external teleport or a long stalled frame clears saved movement history. **F8** stops it. Other players' view and online server acceptance remain unverified.
- **AI obstacle recovery:** walking checks real body clearance and movement progress, limits effective steering speed/step distance, rechecks recovery every tick, and retries other legal approaches to the task. AI/orbit/follow/lag temporarily use native collision while keeping your noclip toggle preference. A persistently inaccessible task step is skipped for manual use. AI still walks to tasks.
- **Hold to Sprint:** enabled by default under **Delete > Movement**. Close the menu/chat and hold **Left Shift** while moving in a lobby or round for a **2x** boost; release to restore your exact previous speed. The Movement tab lets you select a Shift/Control key and a 1-4x multiplier. A status near the ping shows the applied speed or why sprint is paused while the key is held. Sprint pauses in menus, chat, meetings, vents, on death and when the game loses focus. It changes your own movement speed and does not require a host mod; acceptance in another host's online lobby is unverified.
- **Always Impostor:** real hosted assignment is under **Host Only**, and Freeplay assignment is under **Practice**. **Roles > Local Impostor preview** is explicitly local when someone else hosts and does not grant real kills.
- **Multi Role:** **Delete > Roles > Multi Role (Vent + Track + Vitals + Detective)** leaves the actual assigned role and native role button unchanged. Close the menu to see a collapsible ability panel. **Choose target / Next target** cycles living players; the panel displays room, distance and direction from client data. **Tracking map** highlights that player in yellow alongside separately enabled map overlays. **Vitals** opens the game's native panel with communications blocking preserved. **Detective Notes** opens the native notes interface using a separate local case model. **Interrogate target** adds a nearby selected player's recorded location to the selected case, subject to range and cooldown; choose a case inside Notes first. Only confirmed deaths observed while Multi Role was enabled create cases; earlier deaths are not reconstructed, and unknown rooms remain unknown. No tracking, role-assignment or interrogation RPC is emitted. The existing Vent button uses normal distance/obstruction checks. The bundle defaults off; death, departure, disable and player/ship replacement clear its target and cases and close its owned panels. Meetings close owned panels and temporarily hide the ability panel. Guest vent acceptance and multiplayer behavior remain unverified.
- **Ghost Voting:** **Host Only > Ghost Voting** needs a compatible modded host and enabled ghost controls. This switch remains accessible to guests because each voting ghost also needs to enable its local controls; the remaining host controls are disabled for guests.
- **Infinite Judge:** **Practice** refills practice charges in Freeplay. Repeated online uses were rejected and remain disabled.
- **Judge trace:** the separate optional diagnostics plugin records local attempts and vote-result callbacks. It does not grant another use.

Movement stunts use your own movement or existing own-player snap method. Other players' view and server acceptance remain unverified in another host's online game. Renaming this edition does not change gameplay authority.

## Build

```powershell
dotnet build MalumMenu.sln -c Release --nologo
```

Output: `src/bin/Release/net6.0/MalumMenuEnhanced.dll`. The solution filename and C# namespace keep their compatibility names. The matching GPL-3.0 source, including local changes, accompanies the release.

After initializing or cloning this Git repository, package the normal Release build with Python:

```powershell
python scripts/package-haddad-menu.py
```

This creates the plugin ZIP and matching source ZIP in `artifacts/release/v1.0/`.
Build outputs remain ignored. Source archives exclude `downloads/` so packaged
downloads are never embedded recursively in the source ZIP.
Use `--dll "path/to/MalumMenuEnhanced.dll"` for another build output. To include a
compatible Windows 64-bit BepInEx 6 IL2CPP loader in a local installation archive,
supply its ZIP explicitly:

```powershell
python scripts/package-haddad-menu.py --loader-package "path/to/windows-loader-package.zip"
```

That command produces the full `MalumMenuEnhanced-1.0-MicrosoftStore-EpicGames-XboxApp.zip`
instead of the plugin ZIP. Existing release archives are never overwritten.
The historical loader archive is optional; a fresh clone can create the plugin
package without it. Use `--help` for all options. Packaging does not install or
publish anything.

## Credits and license

MalumMenu Enhanced is maintained by **Rifegul**. MalumMenu was originally developed by **scp222thj** and **astra1dev (Astral)**. The previous personal edition was named Haddad Menu and credited MHadd. The original license is preserved in [LICENSE](LICENSE); see [CREDITS.md](CREDITS.md) for attribution and upstream provenance.

## Disclaimer

This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.
