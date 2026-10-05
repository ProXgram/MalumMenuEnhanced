# 📋 Features

MalumMenu Enhanced's inherited feature catalog. The current edition and additions are
documented in [FORK.md](FORK.md). Availability depends on game role and host
authority; the rename does not change those requirements.

Current navigation (1.0): **Movement**, **Fun**, **Appearance**, **Visuals**,
**Tasks**, **Host Only**, **Roles**, **Ship**, **Chat**, **Animations**, **Logs**,
**Practice**, **Settings**. Task automation/arrows are in Tasks; host rules and
Ghost Voting are in Host Only; Infinite Judge is in Practice. Historical
category names below describe the inherited features, not the current tabs.

Version **1.0** changes only the edition branding to **MalumMenu Enhanced**,
maintained by **Rifegul**, and resets version numbering to **1.0** for this renamed
edition. Feature behavior and host requirements carry forward from 3.3.38; its
live verification records retain that version.

## 👱 Player

| Cheat | Description | Type | Default|
|------------|-------------|------|--------|
| NoClip     | Allows you to walk through walls like a ghost | Toggle | Off
| Movement Speed | Adjusts your own movement speed | Slider | Normal |
| Hold to Sprint | Hold Left Shift for a temporary 2× boost; key and multiplier adjustable in Movement. Restores previous speed on release. Online guest acceptance unverified. | Hold / Toggle | On |
| Turbo Orbit | Walks around a selected living player with a temporary speed boost | Fun mode | Off |
| Zigzag Dash | Alternates sideways movement at a temporary boosted speed | Fun mode | Off |
| Random Outfit | Picks an unlocked native hat, skin, visor and pet; follows Free Cosmetics and supports restoring the previous outfit | Colors button before round/practice | Manual |
| Lag Mode | Mixes normal walking/snaps with brief stationary teleport bursts; adjustable interval/distance and wall checks | Fun mode | Off |
| Follow Player | Walks to a clear spot beside the selected player and trails their observed movement | Fun mode | Off |
| Teleport Yo-Yo | Alternates two saved floor positions at 1–5-second intervals, up to 12 jumps | Fun mode | Off |
| AI Tasks | Walks to your own current task consoles and advances one native step on arrival; unsupported timed stages remain manual | Tasks button | Off |
| Custom nickname | Enter your own name before a round; normal game/host validation still applies | Fun field/button | User action |
| Rainbow body | Cycles only your local body preview through palette colors; restores actual outfit on stop | Colors mode | Off |
| Lobby color cycling | Requests available colors for your own player before a round; shared visibility untested | Colors mode | Off |

#### Murder

| Cheat | Description | Type | Default|
|------------|-------------|------|--------|
| Kill Player | Select a player to kill them immediatly | Menu |
| Kill All Crewmates | Kill all crewmates immediatly | Button |
| Kill All Impostors | Kill all impostors immediatly | Button |
| Kill All | Kill all players immediatly | Button |

#### Teleport

| Cheat | Description | Type | Default|
|------------|-------------|------|--------|
| to Cursor | Teleport by right-clicking with your cursor. Works best with the ZoomOut cheat | Toggle | Off |
| to Player | Teleport to a player's position by selecting them | Menu |

## 👁️ ESP

MalumMenu Enhanced's ESP display is client-side. Server acceptance of other actions is a separate question.

| Cheat | Description | Type | Default|
|------------|-------------|------|--------|
| See Roles | See every player's role through their nametag | Toggle | Off |
| See Ghosts | Allows you to see ghosts, protections, and ghost chat even if you are alive | Toggle | Off
| No Shadows | Removes all shadows, allowing you to see during blackouts and even through walls<br>Also, lets you see through spore clouds in the Fungle Jungle | Toggle | Off |
| Reveal Votes | Reveals votes as they are being cast rather than at the end of the meeting<br>Also, lets you see colored votes even if votes are set to anonymous | Toggle | Off |
| Always Chat | Keeps the chat icon always enabled, allowing you to chat at any time (even while not in a meeting or the lobby) | Toggle | Off |

#### Camera
    
| Cheat | Description | Type | Default|
|------------|-------------|------|--------|
| Zoom Out | Allows you to zoom-out the player's camera using your mouse's scrollwheel | Toggle | Off
| Spectate | Allows you to pick a player to spectate with your camera | Menu |
| Freecam | Allows you to freely move your camera around without also moving your player | Toggle | Off |

#### Tracers

| Cheat | Description | Type | Default|
|------------|-------------|------|--------|
| Crewmates | Shows tracer lines for alive crewmates (color: cyan) | Toggle | Off |
| Impostors | Shows tracer lines for alive impostors (color: red) | Toggle | Off
| Ghosts | Shows tracer lines for ghosts (color: white) | Toggle | Off |
| Dead Bodies | Shows tracer lines for dead bodies on the ground (color: yellow) | Toggle | Off |
| Color-based | Changes the color of tracer lines to the color of their players| Toggle | Off |

#### Minimap

| Cheat | Description | Type | Default|
|------------|-------------|------|--------|
| Crewmates | Changes the map so that it shows the position of every alive crewmate (color: cyan) | Toggle | Off |
| Impostors | Changes the map so that it shows the position of every alive impostor (color: red) | Toggle | Off
| Ghosts | Changes the map so that it shows the position of every ghost (color: white) | Toggle | Off |
| Color-based | Changes the color of map icons to the color of their players | Toggle | Off |

## 🎭 Roles

| Cheat | Description | Type | Default |
|------------|-------------|------|----|
| Set Fake Role | Change your current role to any role you want<br>(Shapeshifter & Phantom are disabled by default to prevent getting detected by the anticheat) | Menu |
| Multi Role | Vent access, selected-player tracking, native Vitals, Detective Notes and nearby Interrogate in one local panel; preserves the assigned role. Guest vent acceptance unverified. | Toggle | Off |

#### Impostor

| Cheat | Description | Type | Default |
|------------|-------------|------|----|
| Kill Anyone | Allows you to kill anyone, regardless if they are protected, impostors, crawling in a vent, or a ghost | Toggle | Off |
| No Kill Cooldown | Removes the cooldown period after kills, allowing you to spam-kill as much as you please | Toggle | Off |
| Kill Reach | Allows you to kill players regardless of how far they are on the map | Toggle | Off |

#### Phantom

| Cheat | Description | Type | Default |
|------------|-------------|------|----|
| Kill While Vanished | Allows you to kill while invisible | Toggle | Off |

#### Shapeshifter

| Cheat | Description | Type | Default |
|------------|-------------|------|----|
| No Ss Animation | Removes the shapeshift animation, making shapeshifting much quicker | Toggle | Off |
| Endless Ss Duration | Allows you to remain shapeshifted forever | Toggle | Off |

#### Crewmate

| Cheat | Description | Type | Default |
|------------|-------------|------|----|
| Complete My Tasks | Complete all of your crewmate tasks immediatly | Button |

#### Tracker

| Cheat | Description | Type | Default |
|------------|-------------|------|----|
| Endless Tracking | Allows you to track another player forever | Toggle | Off |
| No Track Delay | Removes the short delay between the tracked player and their icon on your tracker map | Toggle | Off |
| No Track Cooldown | Removes the cooldown period after tracking someone | Toggle | Off |

#### Engineer

| Cheat | Description | Type | Default |
|------------|-------------|------|----|
| Endless Vent Time | Allows you to remain inside a vent forever despite being an engineer | Toggle | Off |
| No Vent Cooldown | Removes the cooldown period after coming out of a vent | Toggle | Off |

#### Scientist

| Cheat | Description | Type | Default |
|------------|-------------|------|----|
| Endless Battery | The battery on your vitals panel will never run out | Toggle | Off |
| No Vitals Cooldown | Removes the cooldown period after closing vitals panel  | Toggle | Off |
    
## 🚀 Ship

| Cheat | Description | Type | Default |
|------------|-------------|------|----|
| Unfixable Lights | Disables lights completely (they cannot be fixed manually by players)<br>You can enable them again by clicking the button | Toggle | Off |
| Report Body | Report any player as a dead body to start a meeting | Button |
| Close Meeting | Forcefully closes the meeting window (only for you), allowing you to move and interact with the game during meetings | Button |

#### Sabotage

MalumMenu Enhanced inherits the sabotage controls. Availability and accepted actions depend on the game's role and host/server validation.

Moreover, different sabotages can be enabled at the same time, and they even work during meetings.

| Cheat | Description | Type | 
|------------|-------------|------|
| Reactor | Allows you to enable/disable Reactor sabotage | Toggle | Off |
| Oxygen | Allows you to enable/disable Oxygen sabotage | Toggle | Off |
| Lights | Allows you to enable/disable Lights sabotage | Toggle | Off |
| Comms | Allows you to enable/disable Communications sabotage | Toggle | Off |
| Doors | Immediatly locks all doors on the ship | Button |
| MushroomMixup | Induces Mushroom Mixup sabotage on Fungle map | Button |

#### Vents

| Cheat | Description | Type | Default|
|------------|-------------|------|--------|
| UseVents | Allows you to use vents even if you are not an impostor or an engineer | Toggle | Off
| KickVents | Forcefully kicks all players from vents | Button |
| WalkInVents | Allows you to move and interact with the game even though you are inside of a vent<br>This gives you a sort of invisibility until you disable the setting and leave the vent<br>(*Some activites such as killing will forcefully make you visible again*) | Toggle | Off

## 💤 Passive

These cheats are constantly running in the background and **cannot be disabled to avoid problems.**

| Cheat | Description | Type | Default|
|------------|-------------|------|--------|
| Free Cosmetics | Gives you access to all of the game's cosmetics for free, including:<br><br>- Hats<br>- Visors<br>- Skins<br>- Pets<br>- Nameplates<br>- Bundles<br>- Cosmicubes | Toggle | On |
| Avoid Penalties | Removes the penalty you receive when disconnecting from games early | Toggle | On |
| Unlock Extra Features | Unlocks many of the game's special features automatically, including:<br><br>- Freechat<br>- Friend list<br>- Custom name<br>- Online gameplay | Toggle | On |

## 📃 Config

You can change all of the following settings in `BepInEx/config/MalumMenu.cfg`

| Config          | Description                                                                                                                                                         | Type   | Default |
|-----------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------|---------|
| GuestMode.GuestMode | When enabled, a new guest account will generate every time you start the game<br><br>Allows you to bypass account bans and PUID detection | Boolean | false |
| GuestMode.FriendName | The username that will be used when setting a friend code for your guest account<br><br>**IMPORTANT**: <br>- Can only be used with GuestMode<br>- Needs to be ≤ 10 characters<br>- Cannot include special characters/discriminator (#1234) | String |  |
| GUI.Keybind | Specifies the keyboard key that toggles the GUI on/off<br><br>**IMPORTANT**: You may only use keycodes from this [list](https://docs.unity3d.com/Packages/com.unity.tiny@0.16/api/Unity.Tiny.Input.KeyCode.html) | String | Delete |
| GUI.Color | Sets the color of MalumMenu Enhanced's GUI using HTML color codes | String | |
| Privacy.HideDeviceId | When enabled, it will hide your unique deviceId from Among Us<br><br>Could **potentially** help bypass hardware bans in the future | Boolean | true |
| Privacy.NoTelemetry | When enabled, it will stop Among Us from collecting analytics of your games using Unity Analytics and sending them to Innersloth | Boolean | true |
| Spoofing.Level | Sets a custom player level to display to others in online games, masking your real level<br><br>**IMPORTANT**: Only integers between 0 and 4294967295 are valid. Decimal values are not accepted | String | |
| Spoofing.Platform | Sets a different gaming platform in online lobbies to disguise your actual platform<br><br>**IMPORTANT**: You may only use platform names from this [list](https://skeld.js.org/enums/constant.Platform.html) | String | |

## Other relevant features of MalumMenu Enhanced:

- MalumMenu Enhanced has a simple **GUI** that is easy to navigate and can be toggled using the **DELETE** key on your keyboard
- [**TEMPORARILY BROKEN**] Inherited custom announcements remain disabled.
