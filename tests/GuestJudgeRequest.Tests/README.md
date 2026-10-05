# Guest Judge request offline regression harness

```powershell
dotnet run --project tests/GuestJudgeRequest.Tests/GuestJudgeRequest.Tests.csproj -c Release
```

The project compiles the actual guest experiment and policy source with both
`GUEST_KILL_EXPERIMENT` and `JUDGE_ROLE_EXPERIMENT`. Its typed native-method
recorder verifies the own-player Judge argument, synchronous attempt
consumption, one attempt per round including thrown calls and cancel/rebind,
read-only UI/binding paths, round identity validation, role and gameplay
eligibility, explicit unverified remote status, and complete kill-path exclusion.

All game types and RPC methods are offline stubs. No live game, host, server,
socket, Harmony patch installation, or role mutation is involved. Passing tests
show what the client would ask the typed method to do; they do not show that
the real host/server accepts a guest role change or avoids disconnecting it.
