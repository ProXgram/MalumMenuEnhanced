# Judge Lifecycle Trace 0.1.0

Separate passive BepInEx 6 IL2CPP diagnostic plugin for Among Us 19.0.0 / game libraries `2026.9.29`. ID: `local.diagnostics.judge-trace`. It does not replace MalumMenu. Build output is only `artifacts/experiments/judge-trace`; there is no install target.

The plugin observes native calls and writes `JudgeTrace` tagged key=value records. It never grants an Overrule charge, changes a target/nonce/role/task, skips native execution, sends an RPC, reads a shared network reader, or bypasses a kick. It writes only its own trace counters and cached identities. Tracing and argument formatting have exception guards; diagnostics errors include exception type only.

## Capture and configuration

`Diagnostics.Enabled` defaults to true in `BepInEx/config/local.diagnostics.judge-trace.cfg`. False stops logging and bookkeeping. Network-round role identity is observed at `IntroCutscene.CoBegin` before native execution with first Harmony priority. Re-enabling midround cannot establish a missing intro capture. Freeplay follows the current owned local Judge. A replacement player/role pointer falls outside the captured network-round scope. Non-Judge roles are never cast as Judge.

Observed-at-intro is local evidence, not proof of server assignment: another mod may already have changed the role. First/Last priorities help before/after observation but do not guarantee ordering against patches of equal priority. A post-native snapshot can include effects of other mods, such as an existing consumption postfix.

Hooks: `IntroCutscene.CoBegin`; `AmongUsClient.OnGameJoined`/`OnGameEnd`; `MeetingHud.Awake`/`Close`/`CmdQueueOverruleVotes`/`VotingComplete`; `JudgeRole.OnMeetingStart`/`TryOverrule`/`ClearOverrule`/`ConsumeOverruleVotesUsage`. Judge actions and meeting results are observed before and after. Lobby join clears diagnostic identities without recording the lobby string.

Records include round/meeting/sequence counters, numeric local player ID, role enum/native instance, meeting NetId/OwnerId/native instance, Freeplay/host flags, charge, once-per-meeting flag, target, native nonce and completed/total tasks. No names, chat, friend/PUID/account IDs, lobby codes or voter-state contents are recorded. `meeting_owner_id` is numeric session ownership; native pointers are process-local diagnostics. `Awake` may precede assignment of NetId; correlate the request-time meeting instance and NetId.

## Interpreting the trace

- `try_overrule` records requested target and local return. A true return does not prove a packet was sent or accepted.
- `cmd_queue_overrule` records typed native command entry/return. `typed_cmd_entries_for_nonce` counts prefix entries grouped by observed meeting NetId and supplied nonce. A new nonce starts a new bucket; this is not a total per meeting. **It is not an outgoing packet count.** A host may process the command locally; a guest may fail before serialization. No reader/writer is hooked.
- `voting_complete` records the native callback's result nonce, Overrule/tie flags and numeric exiled ID. It does not establish transport sender identity or host/backend acceptance.
- `clear_overrule`, `consume_overrule_use` and meeting-start snapshots show native cleanup, charge consumption, cancel/refund controls and per-meeting reset. The plugin never creates a refund or nonce.

These records can correlate local meeting identity, duplicate command attempts, task eligibility, charge and native result callbacks. They cannot alone establish why an official server rejected a request. Offline verification includes no gameplay or network trial.

## Reproduce offline checks

From the repository root in PowerShell:

```powershell
dotnet build .\experiments\judge-trace\JudgeTrace.csproj -c Release
dotnet run --project .\experiments\judge-trace\metadata-check\MetadataCheck.csproj -c Release -- "$env:USERPROFILE\.nuget\packages\amongus.gamelibs.steam\2026.9.29\build\Assembly-CSharp.dll" .\artifacts\experiments\judge-trace\JudgeTrace.dll .\artifacts\experiments\judge-trace\metadata-report.json
```

The metadata tool reads PE metadata without loading or executing game code. It checks exact native target signatures, void observer signatures without ref/out, and compiled references for setters/direct actions/transport writes/shared readers. The SDK represents `VotingComplete` argument 0 as `VoterState[]`; installed generated interop represents it as `Il2CppStructArray<VoterState>`. Only those two explicit signatures are accepted. The observer omits argument 0 and its injected arguments 1–4 match both forms. Each report records the actual signature, array form and input assembly hash. `metadata-report.json` verifies the SDK; `installed-interop-metadata-report.json` verifies the installed generated assembly. These checks support source review; they do not establish live Harmony compatibility, request acceptance, installation or startup behavior.
