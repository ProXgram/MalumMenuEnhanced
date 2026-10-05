# SprintHandler offline regression harness

Links the actual `src/Cheats/SprintHandler.cs` into a standalone .NET 10 executable with local Unity/game/config stubs. No game, Unity, BepInEx or network package is referenced; no native code, RPC or game process runs.

The 96 cases pass against the linked production handler. They exercise stable speed across 100 repeated frames; exact signed float restoration; multiplier limits/nonfinite fallback and signed magnitude cap; nonfinite baseline rejection; release/disable/panic/scene/pause/death/movement cleanup; Freeplay; configured key; HUD creation prevention; replacement player/physics/ship isolation; and safe reset after native physics destruction. Lobby cases permit an owned, living, connected, movable player without ShipStatus, verify release and lobby/round transitions preserve the original speed, and retain chat/player cleanup. Missing or destroyed ship still blocks sprint outside a lobby. Stub Speed getters/setters throw when destroyed and count access, so cleanup cannot silently touch a dead native object. Every case also fails if the handler logs a warning/error, preventing caught exceptions from masking failures.

Run from the repository root:

```powershell
dotnet run --project .\tests\SprintHandler.Tests\SprintHandler.Tests.csproj -c Release
```

Passing these cases validates the handler's local state transitions against stub contracts. It does not establish multiplayer movement behavior, Unity input delivery, live native object lifetime behavior or host/server acceptance. KeybindListener/MenuUI/lifecycle hook wiring is outside this linked-handler harness and must be reviewed or checked in the actual application.
