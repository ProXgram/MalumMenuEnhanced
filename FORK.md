# MalumMenu Enhanced — Rifegul's Among Us 19.0.0 edition

Based on [scp222thj/MalumMenu v3.3.0](https://github.com/scp222thj/MalumMenu/releases/tag/v3.3.0),
commit `e02c28a0ad2aedca51531606ca82114a19584ca6`, licensed under GPL-3.0.
This is an independent local fork, not an upstream release.

Version **3.3.7-au19** adopted **Haddad Menu** as the visible name, **MHadd** as
the personal edition's maintainer, and `HaddadMenu.dll` as the plugin filename.
The menu's Config tab and [CREDITS.md](CREDITS.md) retain the original authors.
The plugin ID, configuration sections and profile filename stay compatible.

## MalumMenu Enhanced branding (1.0)

Version **1.0** is a branding-only release. The visible edition name is
**MalumMenu Enhanced**, its creator and maintainer is **Rifegul**, and the plugin
filename is `MalumMenuEnhanced.dll`. Version numbering resets to **1.0** for
Rifegul's renamed edition. The gameplay implementation, host authority,
settings and keybind profile format carry forward from 3.3.38. Original authors
and the GPL-3.0 license remain credited. Historical Haddad Menu releases,
archives and verification records retain their original names and versions;
3.3.38 live tests are not relabeled as tests of 1.0. The separate Haddad Jester
project remains independent of this menu.

## Target and changes

- Game: Among Us **19.0.0**, internal version **2026.9.29**.
- Mod: **MalumMenu Enhanced 1.0**.
- Requested Windows edition: Microsoft Store, Xbox App, or Epic Games.
- Update the game-library reference from `2026.8.18` to `2026.9.29` and rebuild
  against the actual new library, following the dependency version in
  [upstream PR #996](https://github.com/scp222thj/MalumMenu/pull/996).
- Update the supported-version check and the visible version label.
- Keep the upstream disabled Overload menu disabled. Replace its malformed
  packet sender and runner with inert compatibility methods.
- Retain the original menu and the other existing features. New Influencer
  abilities are not added by this compatibility update.

## Full Multi Role (3.3.38)

The bundle adds native Detective Notes and nearby selected-player Interrogate.
Its detached case model preserves the player's actual assigned role, native
ability button and native Detective controller data. A scoped renderer handles
only the bundle's notes panel; native Detective panels retain their behavior.
The owned page uses the game's empty-suspect layout setup, then fills its three
suspect views from managed case records containing actual player-data references.
Native suspect structs are not constructed or passed through a detour. Portraits
use a checked outfit reference directly; missing outfits receive a placeholder.
The crash dump isolated the earlier failure to UpdateFromPlayerData's native
outfit lookup. That method is no longer used for the bundle's suspect portraits.
Confirmed observed deaths create cases with pre-animation room snapshots.
Failed or protected kills and deaths before enable do not create evidence.
Interrogate uses native range and a local cooldown and adds the selected
player's recorded room to the selected case without sending a role or tracking
request. Missing room data stays unknown. Disable, death, departure and new
player/ship sessions discard cases and close only owned panels. Meetings
close owned panels while retaining the current round's case history.
The victim-location link opens a normal ship map with a separate pink marker
at the selected case's captured death position. Map scale and mirrored maps
are respected. Closing the map or notebook destroys only that owned marker.
Map initialization has a synchronous rendering scope for the owned active
notebook, allowing the normal-map movement guard without changing the player's
movement field, assigned role or host identity. The scope ends in a finally
block before gameplay updates resume.
The owned case map appears in front of Notes and restores its original position
on close. Escape closes that map first, then closes Notes on the next press.
A separate Close Detective Notes button and Escape shortcut allow a player
without the native Detective role to dismiss the owned notebook. Escape is
left to an unrelated map, chat, menu or minigame. The notebook's close button
leaves the native map close control unobstructed.

## Multi Role foundation (3.3.35)

Roles now contains an opt-in local ability bundle combining Vent, Track and
Vitals. It preserves the assigned RoleBehaviour and native role button. Vent
uses the existing button and CanUse distance/obstruction checks; the host/server
still validates the request. Tracking uses client positions, keeps one living
target and adds a yellow selected-player icon to the normal map. No tracking or
role-assignment RPC is emitted. Existing map toggles remain independent.

Vitals instantiates the native Scientist Vitals prefab directly with Begin(null),
without attaching it to a ScientistRole or changing battery/cooldown toggles.
Its native communications blocking remains intact. A collapsible, draggable HUD
panel appears only during living-player gameplay outside menus, meetings,
intros and minigames. Native role tools, tasks and unrelated toggles retain
their settings. Only the owned Vitals panel is closed on disable, death,
departure, meeting or player/ship changes. Guest vent acceptance still needs
separate multiplayer verification.

## Menu restructure (3.3.34)

Host Only now contains Ghost Voting, the real hosted/practice Impostor switch,
shared meeting and kill controls, Unfixable Lights and Kick All From Vents.
Host actions are disabled for guests. Ghost Voting stays accessible in this tab
because the host and each modded voting ghost must enable the same switch.
Moving its control does not change the voting patches or establish a new
multiplayer verification result. Unfixable Lights and Kick All From Vents now
reject guest activation from profiles and keybinds as well as the menu.

Tasks collects manual Automatic Tasks, walking AI Tasks, task arrows and the
task window. Practice holds Freeplay role assignment and Infinite Judge. Roles
keeps local previews and ability controls; its guest Impostor preview explicitly
does not change the host-assigned role or grant counted kills. Appearance,
Visuals and Logs replace Colors, ESP and Console. Settings combines profiles,
configuration, the previous Passive controls and menu display modes.

The Ship tab distinguishes a host-checked emergency request from closing only
the local meeting screen. Profile and keybind field names remain compatible.
No task completion is enabled automatically by this reorganization.

## Jester removed (3.3.31)

Jester is no longer part of Haddad Menu. Its menu tab, role selection, kill/vote
hooks, custom outcome, notices, task text, previews, and host-registration
patches have been removed from the production source. The other menu features
and profile format remain available. The separate Haddad Jester project is
preserved independently and is not bundled or loaded by this menu.

The 34 removed source files and 17 Jester test directories were moved to
`artifacts/removed-features/jester-before-3.3.31`. They are historical archives,
excluded from builds and release packages. Frozen 3.3.30 archives are retained.

## Mixed walking and stationary teleport Lag Mode (3.3.33)

**Delete > Fun > Lag Mode** combines normal walking with occasional backward
and forward position snaps, then brief bursts where the owned character stays
still between teleports. Keep a movement key held to cycle through both.
After two successful walking corrections, the stationary phase attempts two
spaced teleports, then walking returns automatically. Blocked attempts still
finish the burst; a 4.1-second limit prevents an extended freeze. Releasing
movement cancels the burst and its pending jumps.

Walking corrections follow a bounded history of actual recent movement.
Stationary bursts deliberately suppress walking for their short duration;
they still check every teleport against the current ground path. Interval and
distance settings remain: defaults are 0.75 seconds and 1.25 units, bounded to
0.5-2 seconds and 0.25-2 units. Missed frames never produce a burst of queued
network requests.

Only the owned character is moved. Native steering and speed are preserved
during the walking phase. Turning, an external teleport, a long stalled frame,
pauses and context changes discard saved history and cancel stationary bursts.
Meeting, death, focus, vent and menu guards still apply. **F8** or **STOP ALL**
stops the mode. Follow, Orbit and AI Tasks retain their walking behavior.
Online acceptance and how other clients display the effect remain unverified;
this feature simulates lag through movement without changing the connection.

## Random Outfit (3.3.19)

**Delete > Colors > Pick random outfit** selects a native unlocked hat, skin,
visor and pet catalog entry per explicit click. The existing Free Cosmetics
purchase-result patch affects the native unlocked lists; the button never
changes ownership, makes a purchase or adds assets. It preserves color and name.
Main-menu use saves the chosen customization; lobby/practice use also applies
normal own-player cosmetic setters. Online rounds, ghosts and disguised outfits
are excluded. Repeated clicks are limited to one operation per two seconds.
**Restore previous outfit** returns to the selection saved before the first
randomization. Native Skeld practice verified one random hat/skin/visor/pet selection and
restoring the original empty slots, with body color unchanged. Remote display
and acceptance of unowned entries remain unverified; a local catalog entry does
not prove server acceptance.

## Menu compatibility and walking corrections

Version **3.3.18-au19** retains the supported replacement for the failing `GUILayout.TextField` nickname control.
The installed Unity build throws `Method unstripping failed` from `GUI.DoTextField`;
that exception prevented the rest of Fun from drawing. The replacement uses
supported buttons and `Input.inputString` for an explicitly active draft editor.
Only explicit Paste/Ctrl+V reads the clipboard. Enter finishes editing without
applying a name, and native own-name validation/request stays in `NicknamePreset`.
Only the validated owned physics is stopped while typing.

The menu now has a visible **Close menu (Delete)** button and clamps its window
position to the game viewport. Ground navigation temporarily keeps the owned
collider enabled even if the user's noclip toggle is on, preserving that toggle
for after the mode stops. Recovery direction is rechecked each tick and ordinary
routing resumes once clear; native steering is bounded to a 0.06-unit physics
step. The finite-clock guard now runs before the bounded yo-yo branch.

## Changing body colors

Version **3.3.11-au19** adds **Delete → Colors**. **Rainbow body (my screen)**
cycles palette colors on the owned player's cosmetics only, with a configurable
0.15–3-second interval. It sends no messages, changes no task/role/player data,
and restores the current actual outfit color on stop or pause. Disguised outfits
are excluded. **Cycle lobby colors (shared)** requests available palette colors
through the normal own-player `CmdCheckColor(byte)` path, at a configurable
2–10-second interval. It runs only in a Joined lobby and stops at round start.
It does not force duplicate colors, send direct color assignments or change
saved customization directly. The last accepted lobby color remains for that session.

Both modes are off until clicked and stop through **F8**, focus loss, player
replacement, disconnect, end/join lifecycle events and panic mode. Remote
visibility and repeated server acceptance of lobby requests remain unverified.
The round preview is local and is labeled that way in the menu.

## Earlier Lag Walk, custom names and walking recovery

Version **3.3.11-au19** originally added **Fun → Lag Walk** (replaced in 3.3.19): ordinary owned movement
alternates a 0.65-second stationary phase and a 0.35-second burst while a movement
key is held. It shares the stunt boost setting, restores the signed base speed,
and sends no snaps or artificial connection delays. Other players' view remains
unverified. The **Set my nickname** field/button accepts entered text, using
native validation and the ordinary own-name setting/request before a round.
The game/host retains name validation; installation changes no name.

AI Tasks now measures actual walking progress. It caps movement through steering
input using native `TrueSpeed`, which includes game-option speed factors, and
replans or walks clear after a stall. Several legal interaction rings and native
valid consoles provide alternate task approaches before skipping an inaccessible
step. Body queries use live collider geometry, collision layers and contact
filtering rather than assuming a fixed radius. Live map walking remains to test.

## Movement stunts and AI Tasks

Version **3.3.10-au19** adds **Delete → Fun**:

- **Turbo Orbit** routes your character around a selected living player with a
  temporary speed boost.
- **Zigzag Dash** alternates lateral movement, using your movement input as its
  forward direction when available.
- **Follow Player** approaches a clear nearby spot and follows the selected player's observed movement.
- **Teleport Yo-Yo** alternates two saved floor positions, one own-player snap
  per configured 1–5 seconds, at most 12 jumps per activation. Delayed frames
  never send catch-up bursts; points clear when the player/ship context changes.
- **AI Tasks** walks to the current valid console for your own assigned normal
  task before advancing a single native step. Later stages resolve their own
  locations. It excludes emergency tasks, other crew tasks and fake tasks.

Movement modes are exclusive. Closing the menu/chat starts or resumes the
chosen mode; **F8** and **STOP ALL** stop it. Pause/stop restores captured signed
speed and clears only the controller's own motion. Focus loss, death, disconnect,
round changes and panic mode stop/reset automation. Sprint is suspended while a
movement mode owns control. AI Tasks disables instant Automatic Tasks so tasks
cannot finish before the character walks to them.

Ground routes use swept body clearance and a bounded grid search; closed doors,
ladders/platforms and inaccessible task locations can require manual movement.
AI Tasks initializes native Sample/Diagnostics/Wifi/Photos countdown fields at
their usable consoles, preserving configured durations, then works on other
tasks while the game counts down. It waits for the native Finished timer state.
Untouched Monitor Mushroom timer setup and missing timer durations remain
manual, with a visible status. AI can be armed in the current lobby and survives
that lobby's round-loading transition; joining another lobby cancels it.
Linked-source
tests validate ownership, arrival, staged consoles and cleanup. Native game
execution and other-host multiplayer acceptance require separate verification.

The **Set my nickname** field/button uses `NameTextBehaviour.IsValidName`,
the normal saved own-name setting and an own-player `RpcSetName` request only
when clicked before a round. It does not change another player's name or invent
a custom overhead label for vanilla clients. The name request's server acceptance
has not been verified. No nickname is changed automatically during installation.

## Hold to Sprint

Version **3.3.8-au19** adds **Delete → Movement → Hold to Sprint**, enabled by
default. Close the menu and hold **Left Shift** while moving for a **2×** boost.
Releasing the key restores the exact captured speed, including inverted controls.
The Movement tab changes the hold key and multiplier (1–8×). Settings are stored
in the existing config under `MalumMenu.Movement`; the enabled toggle uses the
existing profile system. The faster sprint raises its maximum speed magnitude to 40,
so high base speeds may receive less than the selected multiplier.

Version **3.3.9-au19** also allows lobby movement while waiting for the host;
3.3.8 incorrectly restricted sprint to rounds and Freeplay. Holding the sprint
key displays its active speed or paused reason near the ping.

Sprint applies only to your living, connected player in a lobby, round or Freeplay.
It restores speed when menus/chat/meetings open, movement is blocked, focus is
lost, the round ends, the player changes or panic mode runs. It does not change
ghost speed. Linked-source offline tests cover hold/release, cleanup and identity
replacement; online acceptance under another host remains unverified.

## Always Impostor

Open the **Roles** tab and enable **Always Impostor**.
The setting starts off. It also supports the existing profile and keybind system.

- In **Freeplay**, enabling it assigns your living practice player the Impostor
  role once. It also applies when entering a new practice session while enabled.
- In **Normal rounds you host**, it swaps your assigned role with one living
  selected impostor before the intro. Exact roles are exchanged, preserving the
  impostor count and special-role counts. If you were already an impostor, your
  assigned special role stays intact.
- In **someone else's round**, it applies an explicitly labelled **local-only**
  Impostor role once. The host retains the original role assignment. Version
  3.3.4 blocks outgoing kills while this handler owns the local-only role;
  the status explicitly says **kills disabled**. This path sends no
  role-changing RPCs. Turning
  it off restores the saved local role only while alive and only if no later
  role change replaced it. Host migration also cleans up its own local change.
- Hide-and-Seek is excluded. Ghosts are never changed into living impostors.
  If there is no eligible impostor to swap with, the existing roles stay intact.
- Enabling it during a hosted match takes effect in the **next** round.
  Turning it off stops future forcing; it does not rewrite the current round.

The 23 offline planner tests cover authority, deaths/disconnections, multiple
impostors, repeated application and preservation of exact special roles.
These tests are not a substitute for a live multiplayer synchronization test.

The normal v19 kill-request handler rejects a killer whose authority-recorded
role is not Impostor. A direct native murder notification has no equivalent
client-side check, but its official-server acceptance is unverified; a local
death animation is not evidence that other players accept it. The native audit
is in `artifacts/verification/nonhost-kill-native.json`. This fork does not claim
that local-only mode grants accepted kills in an unmodified host's game.

A separate conditional guest experiment was tested on September 30, 2026.
Its one direct own-player kill call in a public Normal round was followed by
a server disconnect with reason `Hacking`. No remote death was confirmed and
the own-role request was not tested. The installed plugin was restored to
3.3.3-au19, which excludes those experimental buttons. See
`experiments/GUEST_ROUND_TEST.md` and the local trial record for scope and
evidence; this result does not establish that every modded authority behaves
identically.

Version 3.3.3 also fixes Kill Reach throwing when there are no valid targets.

Version 3.3.4 intercepts the Kill button, normal kill request, direct kill
notification and the menu's central kill helper before they send from this
handler's local-only guest role. The block does not depend on the toggle: if
restoring the original role fails, its local-only role remains guarded. It
leaves genuine assigned Impostors, Freeplay and hosted games unchanged, and
does not intercept incoming accepted death decisions. The guard follows the
exact player and applied-role objects; a later role replacement is retained.
This update does not implement a server kick bypass or accepted non-host
role changes. The failed 3.3.3 guest trial remains recorded separately.

## Automatic Tasks

Open **Roles -> Crewmate -> Automatic Tasks**. It starts off, persists while
enabled between rounds, and supports the existing profile and keybind system.

- It completes only your own assigned, unfinished **normal tasks**, in Normal
  rounds and Freeplay. Host status is not required. Genuine crew ghosts can
  finish their remaining tasks; Impostor and fake-task roles are excluded.
- It waits for the ship, player, intro and task initialization. You may also
  enable it during a round. It pauses during meetings and exile scenes.
- Version 3.3.5 checks the active round, ship and owned player before accessing
  the HUD singleton. A saved enabled setting no longer creates a gameplay HUD
  on the main menu or in a lobby.
- Each task advances through the game's native `NextStep` method. The native
  final step sends its usual completion message; no additional RPC is created.
  Sabotage, role instructions and other players' tasks are untouched.
- It attempts at most one task every 0.25 seconds, once per task instance.
  Disabling stops further attempts. Failures pause completion until toggled
  off/on or a fresh round/scene begins.
- The 37 scheduler tests verify ownership, assigned-task filtering, sabotage
  exclusion, duplicate handling, pacing, resets, replacements and clock changes.
  Multiplayer acceptance still needs a controlled second-client test.

## Infinite Judge

Open **Roles -> Judge -> Infinite Judge (Freeplay only)**. Version 3.3.6 blocks
all online refills, including when hosting, and disables this menu control
outside Freeplay. The existing profile and keybind setting cannot override
this restriction. Ordinary Judge uses remain unchanged.

On 2026-10-01, the user reported a normally assigned Judge, and the preserved
game log recorded one successful Overrule followed by another Overrule attempt
and `Server > Client DC because Hacking: null`. BepInEx recorded a local refill.
This establishes a failed online trial, not an accepted infinite-use feature.
The logs do not expose the server's exact validation rule. Evidence is saved
under `artifacts/verification/infinite-judge-guest-disconnect/`.

- Freeplay binds its current living, connected, owned Judge player and role.
  Practice role changes can bind a new role; callbacks for old roles are ignored.
  Death or disconnection invalidates the existing binding.
- Native task requirements, targets, nonce, request routing and the separate
  one-use-per-meeting flag are untouched. Automatic Tasks can finish the assigned
  Judge's own tasks normally. No automatic Overrule request is sent.
- Native outcome consumption remains in place. Only meeting start replenishes
  a spent charge. Enabling during a meeting takes effect at the next meeting.
  Disabling clears only an unused refill granted by this feature; it does not
  restore a legitimately spent original charge or cancel a submitted Overrule.
- Leaving Freeplay or beginning a network round removes an unused charge
  granted by this feature before clearing the practice binding. Original
  native charges and native refunds are preserved.
- Online host status alone is insufficient evidence of native local authority:
  the native queue distinguishes a local host from a registered modded host.
  This fork does not register such a host mode. No disconnect bypass is added.

The linked-source tests verify online guest and host blocking, preservation of
normal uses, Freeplay behavior, identity guards and practice-exit cleanup. They
do not establish any production-server infinite-use support.

## Ghost Voting

Open **Ship** and enable **Ghost Voting (Host support required)**. It starts off
and supports the existing profile and keybind system.

- Ghosts using the feature can use the normal meeting selection and confirmation
  controls. The local dead flag is overridden only for those immediate calls and
  restored even when a call throws. Targets that are dead remain unselectable.
- When the **host also enables it**, the host can accept a connected ghost's vote
  by temporarily clearing that voter's meeting-only dead flag during `CastVote`.
  It preserves the actual dead state and ghost role. The native tally counts the
  resulting vote normally, without rewriting results or creating extra votes.
- An **unmodified host rejects ghost votes**. Client controls alone cannot make
  that host count them. Other ghosts need a mod that enables their voting controls.
- Ghost votes are optional: living players still determine when voting ends.
  Submit the vote before the normal meeting ends. Normal discussion, duplicate
  vote, target and end-state restrictions still apply.
- Exact Xbox 19.0.0 native inspection is recorded in
  `artifacts/verification/ghost-vote-native.json`. Multiplayer behavior requires a
  separate controlled host-and-client test; it is not established by compilation.

## Install

Close Among Us before replacing the plugin. For an existing working BepInEx
installation, back up the current `MalumMenu.dll`, `HaddadMenu.dll` or
`MalumMenuEnhanced.dll` outside the plugins folder. Install this edition's
`BepInEx/plugins/MalumMenuEnhanced.dll` and remove the old active menu DLL.
These filenames share the same plugin ID; keep only one active copy.

Current downloads are on the [release page](https://github.com/ProXgram/MalumMenuEnhanced/releases/latest).
The complete manual ZIP includes a compatible BepInEx 6 IL2CPP loader; the
plugin-only ZIP contains the plugin, documentation and GPL license and needs
an existing loader. See [installation help](docs/INSTALL.md).

Version **1.1.0** adds automatic compatible-release checks when the game opens.
Verified updates wait until Among Us closes, then back up and replace the mod
while preserving settings and unrelated plugins. **Delete → Settings** shows
the update status and the switch for the next launch. A game update that breaks
compatibility requires a newly tested mod build; no code repair is automatic.

For a new installation, download **BepInEx 6 Unity.IL2CPP-win-x64** from the
[official BepInEx downloads](https://builds.bepinex.dev/projects/bepinex_be).
Follow the [official IL2CPP installation guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html):
extract the loader alongside `Among Us.exe`, launch once to generate its files,
then close the game. Extract the plugin package into the same game folder,
placing `MalumMenuEnhanced.dll` in `BepInEx/plugins`. Launch the game normally
and press **Delete** to open the menu.
The bottom-left label should show `MalumMenu Enhanced v1.0 (v2026.9.29)`.

For Xbox App, find the folder from Manage -> Files -> Browse.
For Epic Games, use Manage -> Installation -> the folder icon.

To undo this rename, close the game, remove the active `MalumMenuEnhanced.dll`, and
restore the backed-up DLL under its original filename.

## Build and source

With a .NET SDK installed, run from the repository root:

```powershell
dotnet build MalumMenu.sln -c Release --nologo
dotnet run --project tests/AlwaysImpostorPlanner.Tests/AlwaysImpostorPlanner.Tests.csproj -c Release
dotnet run --project tests/AutomaticTaskScheduler.Tests/AutomaticTaskScheduler.Tests.csproj -c Release
dotnet run --project tests/LocalImpostorGuard.Tests/LocalImpostorGuard.Tests.csproj -c Release
```

The result is `src/bin/Release/net6.0/MalumMenuEnhanced.dll`. Dependencies restore through
the BepInEx NuGet feed in `nuget.config`. Generated game libraries are build-time
dependencies; they are not included in the plugin installation archive.

After initializing or cloning this Git repository, run
`python scripts/package-haddad-menu.py` to package the normal Release output as
`artifacts/release/v1.0/MalumMenuEnhanced-1.0-Plugin.zip` together with its matching
`MalumMenuEnhanced-1.0-Source.zip`. The script also accepts `--dll` for an explicit
build output. Only when `--loader-package` supplies a compatible Windows 64-bit
BepInEx 6 IL2CPP loader ZIP does it produce a local full installation archive,
`MalumMenuEnhanced-1.0-MicrosoftStore-EpicGames-XboxApp.zip` instead.
The earlier local baseline is optional, with its known hash check retained when
that baseline is supplied. Release files and verification reports are generated
under ignored `artifacts/` directories. Existing archives are not overwritten;
packaging does not install or publish the mod. Source archives exclude
`downloads/` as well as build directories, preventing binary or archive recursion
when the downloadable packages are included in the repository.

The accompanying source archive contains the corresponding GPL-3.0 source.
`LICENSE` retains the upstream license, and `CREDITS.md` records the original authors.

## Validation

The 3.3.38 Release build passed with zero warnings and errors against GameLibs
`2026.9.29`. Runtime validation and the installed DLL hashes are recorded in
the local `artifacts/verification` directory under their actual release versions.
The 1.0 rename does not relabel those earlier tests. See the records for the
exact checks performed and their limits; a successful build does not establish
every feature's behavior during a multiplayer match.

The main menu now keeps its close button outside independently scrollable tabs and content, so long Fun controls remain reachable. Its height adjusts to the game window.

### Follow, orbit and task continuation repairs

Follow/orbit use short incremental moving-target routing instead of running a full 6000-node search in one physics frame. Pending routes retain their work and a safe cached path; every emitted route segment is collision checked. Controller retries do not erase that planner or insert a half-second hold. Shadow heading ignores small received corrections and turns gradually. Orbit aims a short arc ahead of the actual player position. Native TrueSpeed controls steering; the base speed is restored on pause/stop.

AI Tasks keeps unfinished assignments active when no usable console is available, rechecks on a bounded schedule, and moves to the next eligible task after completion. Timed and manual tasks keep their native restrictions. Offline fixture checks are separate from live game and multiplayer verification.

Automatic Tasks is always off at game launch, lobby join, round start and round end. Saved profiles keep its shortcut but never restore its enabled state, and saving an active session writes this toggle as False. The handler reset remains separate from the toggle so manually enabling it within a round still works.

### Blocked follow destinations (3.3.16)

Live Skeld practice reproduced Follow stopping without movement. Diagnostics showed its fixed north-side destination overlapping a layer-12 wall, with valid native circle geometry and no native exception. Follow now samples a bounded set of nearby stand positions, approaches a stationary target from the owner's side, and favors a clear direct approach. Successful offsets are cached briefly and invalidated by target heading changes, obstruction or target selection. Orbit checks alternate radii, directions and short arcs. When no sampled floor is usable, the mode waits with zero motion and retries at a bounded cadence; the previous route timeout is cleared.

Follow/Orbit armed in a lobby survive its round's ship creation only for the same local player and a still-valid target. Other context changes reset them. This does not change Automatic Tasks activation.

### Distant follow and orbit catch-up (3.3.17)

Follow now receives a temporary native speed boost as Orbit does. A separate `TargetMovementMultiplier` setting defaults to 3x and controls both modes; a goal farther than 2 units uses at least 2x. This preserves the existing dash/lag setting. Follow brakes near its goal, while Orbit can use its selected boost along clear arcs. Target walking is capped at 12 units/second and 0.24 units per physics frame, stops at the current route waypoint, and checks the actual boosted step against live collision geometry. Contact recovery retains the smaller 2.5 units/second and 0.06-unit limits. AI Tasks keeps its previous walking limits and manual activation.

Moving-target routes retain their work through small native body-center shifts. Collider identity, mask, radius changes and substantial center jumps still invalidate the cached route. Each returned movement segment uses current geometry. A completed search tries up to six collision-checked route joins instead of discarding the route when its nearest point lies across a crate. Repeated rejection of an actual boosted step waits briefly before retrying and stops after five seconds without progress. Offline fixtures cover moving distant targets, geometry drift, waypoint arrival and exact signed speed restoration; they do not establish another client's view or official-server acceptance.

### Unreachable nearby floor (3.3.18)

A live Security-to-Upper-Engine test found Orbit repeatedly choosing a clear but unreachable floor spot beside the engine. Follow reached the same player using another nearby spot. Completed moving-route failures now advance through bounded alternate floor candidates while retaining the total no-route timer. Pending searches keep their selected approach; Orbit changes its preferred turn only after a checked direct arc. Moving a target substantially or choosing another player resets the candidate selection. Failed-search diagnostics distinguish an exhausted planner from an unusable route join.
