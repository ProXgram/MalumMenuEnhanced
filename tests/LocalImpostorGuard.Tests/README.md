# Local impostor kill guard regression harness

Run with the installed .NET 10 SDK:

```powershell
dotnet run --project tests/LocalImpostorGuard.Tests/LocalImpostorGuard.Tests.csproj -c Release
```

The harness compiles the actual `LocalImpostorHandler.cs` and
`LocalImpostorKillPatches.cs` using linked source files. Its small game stubs
model native player/role pointers, Unity destroyed-object truthiness, assigned
role teams, native setter failures before/after mutation, and plugin/visible
console logging.

Checks cover local-only guest kill suppression, original role restoration,
failed/partial native role changes, host-assigned special impostor roles,
external replacements, host migration, player/round replacement, freeplay,
query purity, and warning lifetime. The production button and outgoing-RPC
prefixes are called directly; normal-action counters verify that their returned
decision suppresses execution for a guarded local-only assignment.

This is offline regression evidence. It does not run Harmony's runtime patch
installation, IL2CPP, the installed game, network traffic, multiplayer deaths,
host validation, or remote role assignment. A passing harness does not prove
that a guest can kill or bypass disconnection in a live lobby.
