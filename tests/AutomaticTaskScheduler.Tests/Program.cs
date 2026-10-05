using MalumMenu;
using TaskSlot = MalumMenu.AutomaticTaskScheduler.TaskSlot;

var tests = new (string Name, Action Run)[]
{
    ("Disabled setting does nothing", () => GateRejection(enabled: false)),
    ("Gameplay-not-ready does nothing", () => GateRejection(gameplayReady: false)),
    ("Disallowed role does nothing", () => GateRejection(roleAllowsProgress: false)),
    ("Negative clock is rejected", () => InvalidClock(-1)),
    ("NaN clock is rejected", () => InvalidClock(double.NaN)),
    ("Positive infinity clock is rejected", () => InvalidClock(double.PositiveInfinity)),
    ("Negative infinity clock is rejected", () => InvalidClock(double.NegativeInfinity)),
    ("Null snapshot is rejected", NullSnapshot),
    ("Empty snapshot is rejected", EmptySnapshot),
    ("Unowned task is rejected", () => Ineligible(Task(1) with { IsOwned = false })),
    ("Unassigned task is rejected", () => Ineligible(Task(1) with { IsAssigned = false })),
    ("Abnormal task is rejected", () => Ineligible(Task(1) with { IsNormal = false })),
    ("Completed task is rejected", () => Ineligible(Task(1) with { IsComplete = true })),
    ("Sabotage task is rejected", () => Ineligible(Task(1) with { IsSabotage = true })),
    ("Missing instance is rejected", () => Ineligible(Task(1) with { InstanceId = 0 })),
    ("Zero steps is rejected", () => Ineligible(Task(1) with { RemainingSteps = 0 })),
    ("Negative steps is rejected", () => Ineligible(Task(1) with { RemainingSteps = -1 })),
    ("Excessive steps is rejected", () => Ineligible(Task(1) with { RemainingSteps = 65 })),
    ("Task ID zero is eligible", TaskIdZero),
    ("Negative instance IDs are eligible", NegativeInstanceId),
    ("Maximum supported steps are eligible", MaximumSteps),
    ("One selection per interval", IntervalPacing),
    ("Long delays never produce a catch-up burst", NoCatchUpBurst),
    ("Same ID and instance are attempted once", OncePerInstance),
    ("Reordered task lists do not repeat attempts", ReorderedTasks),
    ("Replacement with same ID can be attempted", ReplacementTask),
    ("Different IDs sharing an instance remain distinct", IdentityPair),
    ("Reset clears attempt history and pacing", Reset),
    ("Backwards clock does not cause bursts", BackwardsClock),
    ("Duplicate IDs are ambiguous", DuplicateIds),
    ("Ambiguous ID does not block unrelated tasks", AmbiguousAndValid),
    ("Ineligible duplicate still makes ID ambiguous", IneligibleDuplicate),
    ("Resolved ambiguity can later be selected", ResolvedAmbiguity),
    ("Failed eligibility does not consume attempt", EligibilityRecovery),
    ("No eligible task does not consume interval", EmptyThenReady),
    ("Selection is recorded before caller work", CallerFailure),
    ("Task snapshot remains unchanged", SnapshotImmutability),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static TaskSlot Task(uint id, long? instanceId = null) =>
    new(id, instanceId ?? id + 100L, true, true, true, false, false, 1);

static void ExpectNoTake(
    AutomaticTaskScheduler scheduler,
    double now,
    IReadOnlyList<TaskSlot>? tasks,
    bool enabled = true,
    bool gameplayReady = true,
    bool roleAllowsProgress = true)
{
    Require(!scheduler.TryTake(now, enabled, gameplayReady, roleAllowsProgress, tasks!, out var next),
        "Unexpected task selection.");
    Require(next == default, "A rejected selection must clear its output.");
}

static TaskSlot Take(AutomaticTaskScheduler scheduler, double now, IReadOnlyList<TaskSlot> tasks)
{
    Require(scheduler.TryTake(now, true, true, true, tasks, out var next), "Expected a task selection.");
    Require(tasks.Contains(next), "Selected a task outside the snapshot.");
    return next;
}

static void GateRejection(bool enabled = true, bool gameplayReady = true, bool roleAllowsProgress = true)
{
    var scheduler = new AutomaticTaskScheduler();
    TaskSlot[] tasks = [Task(1)];
    ExpectNoTake(scheduler, 0, tasks, enabled, gameplayReady, roleAllowsProgress);
    Require(Take(scheduler, 0, tasks) == tasks[0], "A blocked gate consumed the task or interval.");
}

static void InvalidClock(double now)
{
    var scheduler = new AutomaticTaskScheduler();
    TaskSlot[] tasks = [Task(1)];
    ExpectNoTake(scheduler, now, tasks);
    Require(Take(scheduler, 0, tasks) == tasks[0], "Invalid clock consumed the task or interval.");
}

static void NullSnapshot()
{
    var scheduler = new AutomaticTaskScheduler();
    ExpectNoTake(scheduler, 0, null);
    _ = Take(scheduler, 0, [Task(1)]);
}

static void EmptySnapshot()
{
    var scheduler = new AutomaticTaskScheduler();
    ExpectNoTake(scheduler, 0, []);
    _ = Take(scheduler, 0, [Task(1)]);
}

static void Ineligible(TaskSlot task)
{
    var scheduler = new AutomaticTaskScheduler();
    ExpectNoTake(scheduler, 0, [task]);
    Require(Take(scheduler, 0, [Task(task.Id)]) == Task(task.Id), "Rejected task consumed the interval.");
}

static void TaskIdZero()
{
    var scheduler = new AutomaticTaskScheduler();
    var task = Task(0);
    Require(Take(scheduler, 0, [task]) == task, "ID zero was treated as missing.");
    ExpectNoTake(scheduler, 1, [task]);
}

static void NegativeInstanceId()
{
    var scheduler = new AutomaticTaskScheduler();
    var task = Task(1, -1);
    Require(Take(scheduler, 0, [task]) == task, "A valid nonzero negative instance was rejected.");
}

static void MaximumSteps()
{
    var scheduler = new AutomaticTaskScheduler();
    var task = Task(1) with { RemainingSteps = 64 };
    Require(Take(scheduler, 0, [task]) == task, "The inclusive step limit was rejected.");
}

static void IntervalPacing()
{
    var scheduler = new AutomaticTaskScheduler();
    TaskSlot[] tasks = [Task(1), Task(2), Task(3)];
    Require(Take(scheduler, 0, tasks).Id == 1, "Wrong first task.");
    ExpectNoTake(scheduler, 0, tasks);
    ExpectNoTake(scheduler, 0.249, tasks);
    Require(Take(scheduler, 0.25, tasks).Id == 2, "Exact interval boundary was rejected.");
    ExpectNoTake(scheduler, 0.25, tasks);
    ExpectNoTake(scheduler, 0.499, tasks);
    Require(Take(scheduler, 0.5, tasks).Id == 3, "Second interval boundary was rejected.");
}

static void NoCatchUpBurst()
{
    var scheduler = new AutomaticTaskScheduler();
    TaskSlot[] tasks = [Task(1), Task(2), Task(3)];
    _ = Take(scheduler, 0, tasks);
    Require(Take(scheduler, 100, tasks).Id == 2, "Long delay prevented a task.");
    ExpectNoTake(scheduler, 100, tasks);
    ExpectNoTake(scheduler, 100.249, tasks);
    Require(Take(scheduler, 100.25, tasks).Id == 3, "Pacing did not restart from actual attempt time.");
}

static void OncePerInstance()
{
    var scheduler = new AutomaticTaskScheduler();
    var task = Task(1);
    _ = Take(scheduler, 0, [task]);
    ExpectNoTake(scheduler, 0.25, [task]);
    ExpectNoTake(scheduler, 100, [task with { RemainingSteps = 2 }]);
    ExpectNoTake(scheduler, 200, [task]);
}

static void ReorderedTasks()
{
    var scheduler = new AutomaticTaskScheduler();
    TaskSlot[] tasks = [Task(1), Task(2), Task(3)];
    var selected = new HashSet<(uint, long)>();
    foreach (var (now, snapshot) in new[]
             {
                 (0.0, tasks),
                 (0.25, tasks.Reverse().ToArray()),
                 (0.5, new[] { tasks[1], tasks[0], tasks[2] }),
             })
    {
        var next = Take(scheduler, now, snapshot);
        Require(selected.Add((next.Id, next.InstanceId)), "Reordered snapshot repeated a task.");
    }
    Require(selected.Count == tasks.Length, "A task was skipped.");
    ExpectNoTake(scheduler, 1, tasks.Reverse().ToArray());
}

static void ReplacementTask()
{
    var scheduler = new AutomaticTaskScheduler();
    var original = Task(1, 101);
    var replacement = Task(1, 201);
    _ = Take(scheduler, 0, [original]);
    Require(Take(scheduler, 0.25, [replacement]) == replacement, "Same-ID replacement was blocked.");
    ExpectNoTake(scheduler, 0.5, [original]);
    ExpectNoTake(scheduler, 0.5, [replacement]);
}

static void IdentityPair()
{
    var scheduler = new AutomaticTaskScheduler();
    var first = Task(1, 100);
    var second = Task(2, 100);
    _ = Take(scheduler, 0, [first, second]);
    Require(Take(scheduler, 0.25, [first, second]) == second, "Different task IDs were conflated.");
}

static void Reset()
{
    var scheduler = new AutomaticTaskScheduler();
    var task = Task(1);
    _ = Take(scheduler, 100, [task]);
    scheduler.Reset();
    Require(Take(scheduler, 0, [task]) == task, "Reset did not clear attempt history or clock state.");
    ExpectNoTake(scheduler, 0.25, [task]);
    scheduler.Reset();
    scheduler.Reset();
    _ = Take(scheduler, 0, [task]);
}

static void BackwardsClock()
{
    var scheduler = new AutomaticTaskScheduler();
    TaskSlot[] tasks = [Task(1), Task(2), Task(3)];
    _ = Take(scheduler, 10, tasks);
    ExpectNoTake(scheduler, 1, tasks);
    ExpectNoTake(scheduler, 9.75, tasks);
    ExpectNoTake(scheduler, 10, tasks);
    ExpectNoTake(scheduler, 10.249, tasks);
    Require(Take(scheduler, 10.25, tasks).Id == 2, "Clock recovery did not resume correctly.");
    ExpectNoTake(scheduler, 10.25, tasks);
    Require(Take(scheduler, 10.5, tasks).Id == 3, "Clock recovery caused pacing drift.");
}

static void DuplicateIds()
{
    var scheduler = new AutomaticTaskScheduler();
    ExpectNoTake(scheduler, 0, [Task(1, 101), Task(1, 201)]);
    ExpectNoTake(scheduler, 1, [Task(1, 101), Task(1, 101)]);
}

static void AmbiguousAndValid()
{
    var scheduler = new AutomaticTaskScheduler();
    TaskSlot[] tasks = [Task(1, 101), Task(2), Task(1, 201)];
    Require(Take(scheduler, 0, tasks).Id == 2, "Duplicate ID blocked an unrelated eligible task.");
    ExpectNoTake(scheduler, 0.25, tasks);
}

static void IneligibleDuplicate()
{
    var scheduler = new AutomaticTaskScheduler();
    ExpectNoTake(scheduler, 0, [Task(1, 101), Task(1, 201) with { IsOwned = false }]);
}

static void ResolvedAmbiguity()
{
    var scheduler = new AutomaticTaskScheduler();
    var task = Task(1, 101);
    ExpectNoTake(scheduler, 0, [task, Task(1, 201)]);
    Require(Take(scheduler, 0, [task]) == task, "Ambiguity consumed an attempt or interval.");
}

static void EligibilityRecovery()
{
    var scheduler = new AutomaticTaskScheduler();
    var task = Task(1);
    ExpectNoTake(scheduler, 0, [task with { IsAssigned = false }]);
    Require(Take(scheduler, 0, [task]) == task, "An ineligible task was recorded as attempted.");
}

static void EmptyThenReady()
{
    var scheduler = new AutomaticTaskScheduler();
    _ = Take(scheduler, 0, [Task(1)]);
    ExpectNoTake(scheduler, 0.25, []);
    _ = Take(scheduler, 0.25, [Task(2)]);
}

static void CallerFailure()
{
    var scheduler = new AutomaticTaskScheduler();
    var task = Task(1);
    _ = Take(scheduler, 0, [task]);
    try
    {
        throw new InvalidOperationException("Simulated caller work failure.");
    }
    catch (InvalidOperationException)
    {
        ExpectNoTake(scheduler, 1, [task]);
    }
}

static void SnapshotImmutability()
{
    var scheduler = new AutomaticTaskScheduler();
    TaskSlot[] tasks = [Task(1), Task(2)];
    var original = tasks.ToArray();
    _ = Take(scheduler, 0, tasks);
    Require(tasks.SequenceEqual(original), "Scheduler mutated the snapshot.");
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
