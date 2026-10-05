# Isolated own-player Judge assignment trial

This is an unverified trial, not a working forced-Judge feature. The ordinary
3.3.5-au19 installation excludes the test and remains the stable release.

Build with `JudgeRoleExperiment=true`. The visible version is
`3.3.5-judge-test-au19`, and **Delete -> Judge Test** exposes one manual own-player
Judge assignment call. It uses the existing `PlayerControl.RpcSetRole` API with
the local player's identity and `RoleTypes.Judge`. No alternate opcode, forged
host identity, automatic resend, kill call, overrule call, host migration or
server-disconnect bypass is added.

The sender must be a living, connected owner of a local Crewmate role in a
started Normal online round hosted by another player. Intro, meeting, exile,
movement and exact bound round/player/ship checks remain required. Existing
Judge, Always Impostor and an active Fake Role selection block the trial. A
thrown call consumes the attempt; cancel/rebind does not restore it.

Bind the current guest round, then choose **Send own Judge role request ONCE**.
Record server rejection/disconnection separately from local UI behavior. A
controlled second client must verify the assigned role and an ordinary first
Overrule outcome before calling this a real remotely accepted assignment.
Remaining connected or seeing Judge locally is insufficient. The existing
Infinite Judge binding deliberately does not trust this late local role change.

Actual v19 receive inspection shows SetRole RPC44 reads a role and flag, then
runs `CoSetRole`. The client receive branch has no local host check, but this
does not establish production-server acceptance or forwarding of a guest send.
The community Impostor server requires host origin for SetRole. It is a private
server reimplementation, not the official backend.

```powershell
dotnet build MalumMenu.sln -c Release --nologo -p:JudgeRoleExperiment=true -p:OutputPath=../artifacts/experiments/guest-judge-3.3.5/bin/
dotnet run --project tests/GuestJudgeRequest.Tests/GuestJudgeRequest.Tests.csproj -c Release
```

Install only this experimental DLL while Among Us is stopped, preserving the
stable installed DLL outside `BepInEx/plugins`. Restore the stable DLL after
the trial. No successful multiplayer assignment is claimed by a build or an
offline test.

## Live trial result - 2026-10-01

BepInEx recorded exactly one manual own-player Judge role request in a public
guest round. The user then reported being kicked. The preserved log contains
no exact disconnect reason. No second client verified a remotely accepted
Judge assignment or Overrule; the host's mod status was not verified. This
trial did not establish a usable forced-Judge feature.

The trial log is `artifacts/verification/judge-trial-live.log`, with the build
and restore record in `artifacts/verification/guest-judge-trial.json`. The
normal mod is being restored after the user's current lobby is finished.
