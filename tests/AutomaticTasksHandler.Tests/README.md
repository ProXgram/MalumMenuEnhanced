# Automatic Tasks HUD lifecycle regression

```powershell
dotnet run --project tests/AutomaticTasksHandler.Tests/AutomaticTasksHandler.Tests.csproj -c Release
```

The harness links the actual task handler and scheduler. Its HUD singleton
counts reads and creates a stub HUD when first requested, modeling the side
effect responsible for the startup regression. Tests assert that disabled,
main-menu/lobby, unavailable, unowned/disconnected, and non-Normal contexts
never request that singleton. Eligible Normal and Freeplay contexts reach it,
while intro, meeting, and exile states pause task handling. Returning to the
main menu must stop further HUD creation.

The harness does not launch the installed game, execute Unity/IL2CPP, patch
Harmony at runtime, complete tasks, or send network traffic. It verifies
handler control flow and HUD acquisition ordering only.
