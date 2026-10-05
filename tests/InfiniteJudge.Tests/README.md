# Infinite Judge offline regression harness

Run with the installed .NET 10 SDK:

```powershell
dotnet run --project tests/InfiniteJudge.Tests/InfiniteJudge.Tests.csproj -c Release
```

This harness links the actual `InfiniteJudgeHandler.cs` and
`InfiniteJudgePatches.cs`. Offline stubs model native player/role identities,
ownership, Unity destroyed-object truthiness, native charge consumption, and
the native meeting-lock reset. Both production patch entry points are invoked
directly. Modeled network actions throw if called.

The positive rearm fixtures run in Normal Freeplay. Tests verify meeting-only
rearm, preservation of native charges, cleanup of unused feature grants,
native consumption and refund lifetime, late enablement, foreign callbacks,
practice role/player replacement, death/disconnection invalidation, mode gates,
and practice reset. Sentinel state verifies that the feature leaves the
meeting lock, task gate, target, and nonce unchanged.

Online guest and online host fixtures retain their assigned Judge's initial
native use, consume it through the modeled native method, and run several
later native meeting callbacks with the toggle still enabled. The feature
must never refill in either network context. Leaving Freeplay must remove an
unused charge this feature granted while preserving original native charges
and charges independently refunded after native consumption.

This is offline lifecycle evidence. It does not invoke Harmony runtime patch
installation, IL2CPP, an actual Judge action, network traffic, an official
server, or another lobby's host. The native methods and unrelated state are
modeled stubs. These tests verify that online refills are disabled; they do not
verify actual task unlocking or native once-per-meeting enforcement.
