# ColorCycleHandler offline regression harness

Links the actual color handler into a standalone .NET 10 executable with local game/Unity/config contracts. **52/52 checks pass.** Requests are recorded locally; they never contact a game, host or server. A recorded request does not alter the assigned outfit or simulate host acceptance.

Checks cover denial before a round/Freeplay/ownership/disconnect/disguise/context transition; occupied and mismatched palettes; byte protocol bounds; cooldown floors and nonfinite fallback; forward/backward/nonfinite clock boundaries without catch-up bursts; and local preview restoration across changed accepted colors, disguises, owner/player/cosmetic/client/ship replacements. Every case fails on a caught/logged handler exception.

```powershell
dotnet run --project .\tests\ColorCycleHandler.Tests\ColorCycleHandler.Tests.csproj -c Release
```

No native game, Unity, BepInEx or network library is referenced or executed. Live cosmetic semantics, native disguise updates and other players seeing or accepting a lobby color remain unverified. The harness does not exercise UI/lifecycle hook wiring.
