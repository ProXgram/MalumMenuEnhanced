using MalumMenu;
using UnityEngine;

var passed = 0;
var failed = 0;
foreach (var gate in new (string Name, Action Arrange)[]
{
    ("main menu", () => { Utils.isClient = false; Utils.isInGame = false; }),
    ("lobby", () => Utils.isInGame = false),
    ("no local player", () => PlayerControl.LocalPlayer = null),
    ("no ship", () => ShipStatus.Instance = null),
    ("unowned player", () => PlayerControl.LocalPlayer.AmOwner = false),
    ("missing game options", () => GameOptionsManager.Instance.CurrentGameOptions = null),
})
    Check(gate.Name + " does not acquire HUD or complete tasks", () =>
    {
        SetUp(); var task = AddTask(); gate.Arrange();
        Require(!AiTasksHandler.TryGetDestination(out _, out _, out _));
        AiTasksHandler.OnArrived();
        Require(HudManager.Acquisitions == 0 && task.StepsCalled == 0);
    });

foreach (var pause in new (string Name, Action Arrange)[]
{
    ("dead", () => PlayerControl.LocalPlayer.Data.IsDead = true),
    ("Impostor", () => PlayerControl.LocalPlayer.Data.Role.TeamType = RoleTeamTypes.Impostor),
    ("fake tasks", () => PlayerControl.LocalPlayer.Data.Role.TasksCountTowardProgress = false),
    ("meeting", () => Utils.isMeeting = true),
    ("chat", () => HudManager.Existing.Chat.IsOpenOrOpening = true),
    ("mod menu", () => MenuUI.isGUIActive = true),
    ("minigame", () => Minigame.Instance = new()),
    ("vent", () => PlayerControl.LocalPlayer.inVent = true),
})
    Check(pause.Name + " pauses destination and arrival", () =>
    {
        SetUp(); var task = AddTask(); pause.Arrange();
        Require(!AiTasksHandler.TryGetDestination(out _, out _, out _));
        AiTasksHandler.OnArrived(); Require(task.StepsCalled == 0);
    });

Check("one console arrival advances exactly one stage", () =>
{
    SetUp(); var task = AddTask(2);
    ShipStatus.Instance.AllConsoles = new[] { MakeConsole(task, 0, new(3, 0)), MakeConsole(task, 1, new(-5, 0)) };
    Arrive();
    Require(task.taskStep == 1 && task.StepsCalled == 1 && AiTasksHandler.CompletedCount == 0);
    Time.realtimeSinceStartupAsDouble = 1;
    Require(AiTasksHandler.TryGetDestination(out var second, out _, out _));
    Require(second.x < -4, "Second stage did not navigate to its new console");
    PlayerControl.LocalPlayer.Position = second;
    AiTasksHandler.OnArrived();
    Require(task.IsComplete && task.StepsCalled == 2 && AiTasksHandler.CompletedCount == 1);
});
Check("completing the first task hands off to a different assigned task", () =>
{
    SetUp(); var first = AddTask(); var second = AddTask();
    ShipStatus.Instance.AllConsoles = new[] { MakeConsole(first, 0, new(3, 0)), MakeConsole(second, 0, new(8, 0)) };
    Arrive(); Require(first.IsComplete && !second.IsComplete && AiTasksHandler.CompletedCount == 1);
    Time.realtimeSinceStartupAsDouble = 0.2;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
    Time.realtimeSinceStartupAsDouble = 1;
    Require(AiTasksHandler.TryGetDestination(out var next, out _, out _) && next.x > 6);
    PlayerControl.LocalPlayer.Position = next; AiTasksHandler.OnArrived();
    Require(second.IsComplete && AiTasksHandler.CompletedCount == 2 && second.StepsCalled == 1);
    Time.realtimeSinceStartupAsDouble = 2;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && AiTasksHandler.IsFinished);
});
Check("missing next console after a completion waits then resumes without restart", () =>
{
    SetUp(); var first = AddTask(); var second = AddTask();
    ShipStatus.Instance.AllConsoles = new[] { MakeConsole(first, 0, new(3, 0)) };
    Arrive(); Require(first.IsComplete && !second.IsComplete);
    Time.realtimeSinceStartupAsDouble = 1;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out var status) && !AiTasksHandler.IsFinished);
    Require(status.Contains("manual") || status.Contains("waiting"));
    ShipStatus.Instance.AllConsoles = new[] { MakeConsole(second, 0, new(8, 0)) };
    Time.realtimeSinceStartupAsDouble = 3; Arrive();
    Require(second.IsComplete && AiTasksHandler.CompletedCount == 2);
});
Check("an unfinished assignment without a task instance cannot finish the run", () =>
{
    SetUp(); var first = AddTask(); Arrive();
    PlayerControl.LocalPlayer.Data.Assigned[2] = new() { Id = 2 };
    Time.realtimeSinceStartupAsDouble = 1;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
    var second = AddTask(); Time.realtimeSinceStartupAsDouble = 3; Arrive();
    Require(first.IsComplete && second.IsComplete && AiTasksHandler.CompletedCount == 2);
});
Check("empty task instances during a handoff stay armed until they return", () =>
{
    SetUp(); var first = AddTask(); var second = AddTask();
    ShipStatus.Instance.AllConsoles = new[] { MakeConsole(first, 0, new(3, 0)) };
    Arrive(); PlayerControl.LocalPlayer.myTasks.Clear();
    Time.realtimeSinceStartupAsDouble = 1;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
    PlayerControl.LocalPlayer.myTasks.Add(first); PlayerControl.LocalPlayer.myTasks.Add(second);
    ShipStatus.Instance.AllConsoles = new[] { MakeConsole(second, 0, new(8, 0)) };
    Time.realtimeSinceStartupAsDouble = 3; Arrive(); Require(second.IsComplete);
});
Check("empty initial assignments wait instead of declaring completion", () =>
{
    SetUp(); Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
    Time.realtimeSinceStartupAsDouble = 2; var task = AddTask(); Arrive(); Require(task.IsComplete);
});
Check("temporarily missing role data does not latch finished before a Crew role arrives", () =>
{
    SetUp(); var task = AddTask(); PlayerControl.LocalPlayer.Data.Role = null;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
    PlayerControl.LocalPlayer.Data.Role = new(); Time.realtimeSinceStartupAsDouble = 1; Arrive();
    Require(task.IsComplete);
});
Check("a timed task with late prefab data remains armed after another task completes", () =>
{
    SetUp(); var first = AddTask(); var timed = AddTask(); timed.TaskType = TaskTypes.InspectSample;
    ShipStatus.Instance.AllConsoles = new[] { MakeConsole(first, 0, new(3, 0)), MakeConsole(timed, 0, new(8, 0)) };
    Arrive(); Time.realtimeSinceStartupAsDouble = 1;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
    timed.MinigamePrefab = new SampleMinigame { TimePerStep = 45 };
    Time.realtimeSinceStartupAsDouble = 3; Arrive();
    Require(first.IsComplete && timed.TimerStarted == NormalPlayerTask.TimerState.Started && timed.StepsCalled == 0);
    timed.TaskTimer = 0; timed.TimerStarted = NormalPlayerTask.TimerState.Finished;
    Time.realtimeSinceStartupAsDouble = 4; Arrive();
    Require(timed.IsComplete && AiTasksHandler.CompletedCount == 2);
});
Check("manual tasks use a bounded resolution cadence without native stepping", () =>
{
    SetUp(); var task = AddTask(); task.TaskType = TaskTypes.MonitorMushroom;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
    var calls = PlayerControl.LocalPlayer.Data.FindTaskCalls;
    for (var tick = 1; tick < 100; tick++)
    {
        Time.realtimeSinceStartupAsDouble = tick * 0.01;
        Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
        AiTasksHandler.OnArrived();
    }
    Require(PlayerControl.LocalPlayer.Data.FindTaskCalls == calls && task.StepsCalled == 0);
    Time.realtimeSinceStartupAsDouble = 1.1;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _));
    Require(PlayerControl.LocalPlayer.Data.FindTaskCalls == calls + 1 && task.StepsCalled == 0);
});
Check("a skipped task remains pending while the AI completes another task", () =>
{
    SetUp(); var skipped = AddTask(); var regular = AddTask();
    ShipStatus.Instance.AllConsoles = new[] { MakeConsole(skipped, 0, new(3, 0)), MakeConsole(regular, 0, new(8, 0)) };
    Require(AiTasksHandler.TryGetDestination(out _, out _, out _)); AiTasksHandler.SkipCurrent("blocked route");
    Time.realtimeSinceStartupAsDouble = 1; Arrive();
    Require(regular.IsComplete && skipped.StepsCalled == 0);
    for (var tick = 2; tick <= 5; tick++)
    {
        Time.realtimeSinceStartupAsDouble = tick;
        Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
        AiTasksHandler.OnArrived();
    }
    Require(skipped.StepsCalled == 0);
    skipped.taskStep = skipped.MaxStep; PlayerControl.LocalPlayer.Data.Assigned[skipped.Id].Complete = true;
    Time.realtimeSinceStartupAsDouble = 6;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && AiTasksHandler.IsFinished);
});
Check("completion waits for native assignment flags without another NextStep", () =>
{
    SetUp(); var task = AddTask(); Arrive();
    PlayerControl.LocalPlayer.Data.Assigned[task.Id].Complete = false;
    Time.realtimeSinceStartupAsDouble = 1;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
    AiTasksHandler.OnArrived(); Require(task.StepsCalled == 1);
    PlayerControl.LocalPlayer.Data.Assigned[task.Id].Complete = true;
    Time.realtimeSinceStartupAsDouble = 3;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && AiTasksHandler.IsFinished);
});
Check("temporary fake-task role data cannot prevent a later genuine Crew assignment", () =>
{
    SetUp(); var task = AddTask(); PlayerControl.LocalPlayer.Data.Role.TasksCountTowardProgress = false;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
    PlayerControl.LocalPlayer.Data.Role.TasksCountTowardProgress = true;
    Time.realtimeSinceStartupAsDouble = 1; Arrive(); Require(task.IsComplete);
});
Check("calling arrival from far away never advances", () =>
{
    SetUp(); var task = AddTask();
    Require(AiTasksHandler.TryGetDestination(out _, out _, out _));
    AiTasksHandler.OnArrived(); Require(task.StepsCalled == 0);
});
Check("native CanUse denies a nearby action", () =>
{
    SetUp(); var task = AddTask(); ShipStatus.Instance.AllConsoles[0].CanBeUsed = false;
    Arrive(); Require(task.StepsCalled == 0);
});
Check("native CanUse-denied arrival retries another legal spot before abandoning task", () =>
{
    SetUp(); var task = AddTask();
    Require(AiTasksHandler.TryGetDestination(out var original, out _, out _));
    ShipStatus.Instance.AllConsoles[0].UseCheck = point => Vector2.Distance(point, original) > 0.2f;
    PlayerControl.LocalPlayer.Position = original;
    AiTasksHandler.OnArrived(); Require(task.StepsCalled == 0);
    Require(AiTasksHandler.TryGetDestination(out var alternate, out _, out _));
    Require(Vector2.Distance(original, alternate) > 0.2f);
    PlayerControl.LocalPlayer.Position = alternate; Time.realtimeSinceStartupAsDouble = 1;
    AiTasksHandler.OnArrived(); Require(task.IsComplete && task.StepsCalled == 1);
});
Check("repeated native CanUse denial remains bounded and never completes the task", () =>
{
    SetUp(); var task = AddTask(); ShipStatus.Instance.AllConsoles[0].CanBeUsed = false;
    var arrivals = 0;
    for (var tick = 0; tick < 20; tick++)
    {
        Time.realtimeSinceStartupAsDouble = tick;
        if (!AiTasksHandler.TryGetDestination(out var goal, out _, out _)) continue;
        PlayerControl.LocalPlayer.Position = goal; AiTasksHandler.OnArrived(); arrivals++;
    }
    Require(arrivals <= 7 && !AiTasksHandler.IsFinished && task.StepsCalled == 0);
});
Check("task not assigned to local data is excluded", () =>
{
    SetUp(); var task = AddTask(); PlayerControl.LocalPlayer.Data.Assigned.Clear();
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && task.StepsCalled == 0);
});
Check("other crew task is excluded even if ID appears assigned", () =>
{
    SetUp(); var task = AddTask(); task.Owner = new();
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && task.StepsCalled == 0);
});
Check("emergency task is excluded", () =>
{
    SetUp(); var task = AddTask(); task.Emergency = true;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _));
});
Check("waiting task remains pending until native timer expires", () =>
{
    SetUp(); var task = AddTask(); task.TaskType = TaskTypes.InspectSample;
    task.TimerStarted = NormalPlayerTask.TimerState.Started; task.TaskTimer = 10;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
    AiTasksHandler.OnArrived(); Require(task.StepsCalled == 0);
    task.TaskTimer = 0; task.TimerStarted = NormalPlayerTask.TimerState.Finished;
    Time.realtimeSinceStartupAsDouble = 1; Arrive(); Require(task.IsComplete);
});
Check("untouched timed minigame requires manual setup", () =>
{
    SetUp(); var task = AddTask(); task.TaskType = TaskTypes.RunDiagnostics;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out var status));
    Require(!AiTasksHandler.IsFinished && status.Contains("manual") && task.StepsCalled == 0);
});
foreach (var timed in new[] { TaskTypes.InspectSample, TaskTypes.RunDiagnostics, TaskTypes.RebootWifi, TaskTypes.DevelopPhotos })
    Check(timed + " starts its configured native timer at the console", () =>
    {
        SetUp(); var task = AddTask(); task.TaskType = timed;
        task.MinigamePrefab = timed == TaskTypes.InspectSample ? new SampleMinigame { TimePerStep = 75 }
            : new DiagnosticGame { TimePerStep = 85 };
        task.TaskTimer = 180;
        var expected = timed == TaskTypes.InspectSample ? 75 : timed == TaskTypes.RunDiagnostics ? 85
            : timed == TaskTypes.RebootWifi ? 60 : 180;
        Arrive(); Require(task.StepsCalled == 0 && !task.IsComplete);
        Require(task.TimerStarted == NormalPlayerTask.TimerState.Started && task.TaskTimer == expected);
        Time.realtimeSinceStartupAsDouble = 1;
        Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
        AiTasksHandler.OnArrived(); Require(task.StepsCalled == 0);
        task.TaskTimer = 0; task.TimerStarted = NormalPlayerTask.TimerState.Finished;
        Time.realtimeSinceStartupAsDouble = 2; Arrive(); Require(task.IsComplete && task.StepsCalled == 1);
    });
Check("waiting timer task does not stall a different ordinary task", () =>
{
    SetUp(); var timed = AddTask(); timed.TaskType = TaskTypes.InspectSample; timed.MinigamePrefab = new SampleMinigame();
    Arrive();
    var regular = AddTask(); Time.realtimeSinceStartupAsDouble = 1; Arrive();
    Require(regular.IsComplete && !timed.IsComplete && timed.StepsCalled == 0);
});
Check("zero timer waits for native Finished flag before completion", () =>
{
    SetUp(); var task = AddTask(); task.TimerStarted = NormalPlayerTask.TimerState.Started;
    task.TaskTimer = 0;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
    AiTasksHandler.OnArrived(); Require(task.StepsCalled == 0);
});
Check("invalid active timer never becomes an instant completion", () =>
{
    SetUp(); var task = AddTask(); task.TimerStarted = NormalPlayerTask.TimerState.Started;
    task.TaskTimer = float.NaN;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _));
    AiTasksHandler.OnArrived(); Require(task.StepsCalled == 0);
});
Check("console with wall-blocked approach is excluded", () =>
{
    SetUp(); var task = AddTask(); ShipStatus.Instance.AllConsoles[0].checkWalls = true; PhysicsHelpers.Blocked = true;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && task.StepsCalled == 0);
});
Check("console from-below restriction changes approach", () =>
{
    SetUp(); AddTask(); ShipStatus.Instance.AllConsoles[0].onlyFromBelow = true;
    Require(AiTasksHandler.TryGetDestination(out var goal, out _, out _));
    Require(goal.y < ShipStatus.Instance.AllConsoles[0].transform.position.y);
});
Check("alternate task approach rejects the blocked point without task stepping", () =>
{
    SetUp(); var task = AddTask();
    Require(AiTasksHandler.TryGetDestination(out var original, out _, out _));
    Require(AiTasksHandler.TryAlternateApproach());
    Require(AiTasksHandler.TryGetDestination(out var alternate, out _, out _));
    Require(Vector2.Distance(original, alternate) > 0.2f && task.StepsCalled == 0);
    var center = ShipStatus.Instance.AllConsoles[0].transform.position;
    Require(Vector2.Distance(center, alternate) <= ShipStatus.Instance.AllConsoles[0].UsableDistance);
    PlayerControl.LocalPlayer.Position = alternate; AiTasksHandler.OnArrived();
    Require(task.IsComplete && task.StepsCalled == 1);
});
Check("task approaches include a clear inner radius but never console center", () =>
{
    SetUp(); AddTask(); var center = ShipStatus.Instance.AllConsoles[0].transform.position;
    NavigationRouter.StandCheck = point => Vector2.Distance(point, center) is > 0.45f and < 0.6f;
    Require(AiTasksHandler.TryGetDestination(out var goal, out _, out _));
    Require(Vector2.Distance(goal, center) is > 0.45f and < 0.6f);
    Require(AiTasksHandler.TryAlternateApproach());
    Require(AiTasksHandler.TryGetDestination(out var alternate, out _, out _));
    Require(Vector2.Distance(alternate, center) is > 0.45f and < 0.6f);
});
Check("alternate approach can change valid console when original becomes inaccessible", () =>
{
    SetUp(); var task = AddTask(); var firstConsole = ShipStatus.Instance.AllConsoles[0];
    var secondConsole = MakeConsole(task, 0, new(8, 0));
    ShipStatus.Instance.AllConsoles = new[] { firstConsole, secondConsole };
    Require(AiTasksHandler.TryGetDestination(out var original, out _, out _) && original.x < 4);
    NavigationRouter.StandCheck = point => point.x > 6;
    Require(AiTasksHandler.TryAlternateApproach());
    Require(AiTasksHandler.TryGetDestination(out var alternate, out _, out _) && alternate.x > 6);
    Require(task.StepsCalled == 0);
});
Check("alternate approach stops after bounded retries rather than circling indefinitely", () =>
{
    SetUp(); var task = AddTask(); Require(AiTasksHandler.TryGetDestination(out _, out _, out _));
    var retries = 0;
    for (var count = 0; count < 20 && AiTasksHandler.TryAlternateApproach(); count++) retries++;
    Require(retries == 6 && task.StepsCalled == 0);
});
foreach (var invalid in new (string Name, Action<NormalPlayerTask> Arrange)[]
{
    ("foreign task", task => task.Owner = new()),
    ("dead player", _ => PlayerControl.LocalPlayer.Data.IsDead = true),
    ("changed native step", task => task.taskStep++),
    ("meeting pause", _ => Utils.isMeeting = true),
})
    Check("alternate approach refuses " + invalid.Name, () =>
    {
        SetUp(); var task = AddTask(2); Require(AiTasksHandler.TryGetDestination(out _, out _, out _));
        invalid.Arrange(task);
        Require(!AiTasksHandler.TryAlternateApproach() && task.StepsCalled == 0);
    });
Check("blocked alternate points do not trigger offsite native completion", () =>
{
    SetUp(); var task = AddTask(); Require(AiTasksHandler.TryGetDestination(out _, out _, out _));
    NavigationRouter.StandCheck = _ => false;
    Require(!AiTasksHandler.TryAlternateApproach());
    AiTasksHandler.OnArrived(); Require(task.StepsCalled == 0);
});
Check("unreachable stage is skipped without a completion", () =>
{
    SetUp(); var task = AddTask(); Require(AiTasksHandler.TryGetDestination(out _, out _, out _));
    AiTasksHandler.SkipCurrent("blocked route"); Time.realtimeSinceStartupAsDouble = 1;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished && task.StepsCalled == 0);
});
Check("native no-progress failure cannot retry indefinitely", () =>
{
    SetUp(); var task = AddTask(); task.FailAdvance = true; Arrive();
    Time.realtimeSinceStartupAsDouble = 1;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && task.StepsCalled == 1);
});
Check("changed task stage cancels stale arrival", () =>
{
    SetUp(); var task = AddTask(2); Require(AiTasksHandler.TryGetDestination(out _, out _, out _));
    task.taskStep = 1; AiTasksHandler.OnArrived(); Require(task.StepsCalled == 0);
});
Check("new scene resets finished status and task selection", () =>
{
    SetUp(); AddTask(); Arrive(); Time.realtimeSinceStartupAsDouble = 1;
    Require(!AiTasksHandler.TryGetDestination(out _, out _, out _) && AiTasksHandler.IsFinished);
    ShipStatus.Instance = new(); AddTask();
    Require(AiTasksHandler.TryGetDestination(out _, out _, out _) && !AiTasksHandler.IsFinished);
});
Check("Freeplay accepts own normal tasks without online Started state", () =>
{
    SetUp(); Utils.isInGame = false; Utils.isFreePlay = true; var task = AddTask(); Arrive(); Require(task.IsComplete);
});

System.Console.WriteLine($"{passed}/{passed + failed} AI task checks passed.");
return failed == 0 ? 0 : 1;

void Check(string name, Action body)
{
    try { body(); passed++; System.Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; System.Console.WriteLine("FAIL " + name + ": " + error.Message); }
}
static void Require(bool value, string message = "Condition failed") { if (!value) throw new InvalidOperationException(message); }
static void SetUp()
{
    AiTasksHandler.Reset();
    Utils.isClient = true; Utils.isInGame = true; Utils.isFreePlay = false; Utils.isNormalGame = true; Utils.isMeeting = false; Utils.isExiling = false;
    PlayerControl.LocalPlayer = new(); ShipStatus.Instance = new();
    HudManager.InstanceExists = true; HudManager.Acquisitions = 0; HudManager.Existing = new();
    GameOptionsManager.Instance = new(); Minigame.Instance = null;
    MenuUI.isGUIActive = false; MalumMenu.MalumMenu.isPanicked = false; MalumMenu.MalumMenu.Log = new();
    Application.isFocused = true; Time.realtimeSinceStartupAsDouble = 0;
    NavigationRouter.StandCheck = _ => true; PhysicsHelpers.Blocked = false;
}
static NormalPlayerTask AddTask(int stages = 1)
{
    var local = PlayerControl.LocalPlayer;
    var task = new NormalPlayerTask { Id = (uint)local.myTasks.Count + 1, Owner = local, MaxStep = stages };
    local.myTasks.Add(task); local.Data.Assigned[task.Id] = new() { Id = task.Id };
    ShipStatus.Instance.AllConsoles = new[] { MakeConsole(task, 0, new(3, 0)) };
    return task;
}
static Console MakeConsole(NormalPlayerTask task, int step, Vector2 position) => new()
{
    TaskId = task.Id, ValidAtStep = step, transform = new() { position = position },
};
static void Arrive()
{
    Require(AiTasksHandler.TryGetDestination(out var goal, out _, out _), AiTasksHandler.StatusText);
    PlayerControl.LocalPlayer.Position = goal; AiTasksHandler.OnArrived();
}
