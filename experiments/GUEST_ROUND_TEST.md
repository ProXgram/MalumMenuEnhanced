# Manual guest role and kill test for Among Us 19.0.0

## Observed result: failed

On September 30, 2026, one own-player kill test in a public Normal guest round
was followed by a server disconnect. The game log records
`Server > Client DC because Hacking: null`. The local player-info object was
server-owned (`OwnerId == ServerOwned == -4`). No remote death was confirmed;
the role-request button was not tested. This is not a working non-host kill
feature. The installed plugin was restored to the ordinary 3.3.3-au19 release.
The experiment's source and evidence remain for review, not as a success claim.
Local evidence is in `artifacts/verification/guest-experiment-trial.json`.

The procedure below records the archived 3.3.3 experiment. Current 3.3.4 source
blocks a kill attempt while Always Impostor owns a local-only guest role and
reports **BLOCKED; no call sent**. It is not an available kick-bypass route.

This separate build adds a **Guest Test** tab. Its role request and kill call
are experiments, not confirmed non-host features. The ordinary 3.3.3-au19
release excludes this tab and its network actions.

## Original experiment procedure

1. Join a Normal online game as a guest. Wait until the round has started and
   your player can move, outside the intro, meetings and ejection scenes.
2. Press **Delete**, open **Guest Test**, and choose **Bind current guest round**.
3. For the role request, start with Always Impostor off and an actual local
   Crewmate role. Choose **Send own Impostor role request ONCE**. This is blocked
   if the role counts would immediately give impostors a parity win.
4. To test the kill call separately, enable **Roles -> Always Impostor** to
   obtain local kill controls. Disable Kill Reach, No Kill Cooldown, Kill Anyone
   and Kill While Vanished. Approach a living crewmate until the normal kill
   button selects them and the normal cooldown has finished. Open **Guest Test**
   and choose **Send own kill test ONCE**.
5. Record whether another player sees the same role/death, whether the round
   continues, and whether the host/server disconnects you. A local body,
   animation or role label is not proof that the action was accepted.

Public and private rounds are supported. Switching lobby visibility, host,
game, ship or player cancels the binding. Cancel/rebind never restores used
attempts. Each role and kill action is attempted at most once per round, even
if its call throws. The buttons recheck conditions immediately before sending.
No action is sent automatically.

The calls use only your own player object and the game's existing typed
`RpcSetRole` and `RpcMurderPlayer` methods. The standard Kill button continues
to use the host-checked request route. This test does not bypass or establish
the official server's acceptance rules.

## Build

```powershell
dotnet build MalumMenu.sln -c Release --nologo -p:GuestKillExperiment=true -p:OutputPath=../artifacts/experiments/guest-impostor/bin/
dotnet run --project tests/GuestRoundTestPolicy.Tests/GuestRoundTestPolicy.Tests.csproj -c Release
```

The menu label is **3.3.3-guest-test-au19**. Install only its MalumMenu.dll,
with Among Us closed. Preserve the ordinary 3.3.3-au19 DLL outside the plugins
folder and restore that DLL to remove this experiment. Existing game files,
profiles and configuration do not need to be replaced.
