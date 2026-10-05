# AI task console and progress checks

Run `dotnet run --project tests/AiTasksHandler.Tests/AiTasksHandler.Tests.csproj --configuration Release`.

The tests compile the real AI handler against explicit game doubles. They verify own assigned task selection, gameplay pause gates, native console eligibility, offsite refusal, separate destinations for later stages, task timer waits, bounded native failures, scene replacement, and Freeplay. They do not run the game or prove that an online host accepts task completion.

The 58 checks include a complete first-task-to-second-task handoff and the temporary gaps that previously stopped the run: unavailable next consoles, missing task instances, empty assignment lists, missing role data, and late timer prefabs. An unavailable or manual task remains armed with resolution limited to once per second. Previously skipped steps do not repeat native use, while other available tasks continue. Completion requires an observed owned task list with all work complete and no pending native assignment flags; empty or unresolved data is a wait, not a completion signal.

Approach recovery rejects a previously blocked point, samples three radii within the legal console use distance, and tries another console valid for the same native task stage when available. It never uses the console center as a navigation shortcut. Recovery is bounded to six successful alternative selections per task stage and console, performs no task progress changes, and refuses invalid ownership, changed stages, dead players, or paused gameplay. The regression scenarios exercise inner-ring access, alternate-console selection, bounded retries, and final native use only after arrival.

The read-only `NativeReview` utility examines the installed Xbox game binary and metadata using LibCpp2IL and Iced. It does not execute or patch game instructions or control a running process. Its output is generated under its ignored build directory.

On the installed Among Us 19 build, `NormalPlayerTask.NextStep` is at RVA `0x760300`: it increments one task step, updates the arrow/location, and calls the ordinary final `PlayerControl.RpcCompleteTask` once. It does not initialize timers. `NormalPlayerTask.FixedUpdate` (`0x75F4C0`) decrements `TaskTimer` while `TimerStarted` is Started, then marks Finished when the duration expires.

Timer starts reproduced only at a valid nearby task console:

- Inspect Sample: `SampleMinigame.CoStartProcessing.MoveNext` (`0x6A6130`) copies the minigame's `TimePerStep` into the task timer and sets Started.
- Run Diagnostics: `DiagnosticGame.StartDiagnostic` (`0x613E40`) copies its `TimePerStep` and sets Started.
- Reboot Wifi: `WifiGame.TurnOff` (`0x6C95D0`) uses `WaitDuration` (60 seconds) and sets Started.
- Develop Photos: `PhotosMinigame.Update` (`0x68E6A0`) starts the task's existing serialized duration.

The AI uses those existing configured durations, leaves countdown to the game, and completes another available task during a wait. It returns to the proper console after the native Finished flag is set. Unavailable durations, unsupported Monitor Mushroom setup, or inaccessible console stages are marked as waiting for console availability or manual use, while the mode remains armed. The handler advances task progress at the console; it does not simulate each task minigame's mouse gestures.
