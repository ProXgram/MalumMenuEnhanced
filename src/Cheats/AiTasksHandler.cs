using System;
using System.Collections.Generic;
using UnityEngine;

namespace MalumMenu;

/// <summary>Visits the current console for an assigned task before advancing one native step.</summary>
public static class AiTasksHandler
{
    private const double ActionInterval = 0.5;
    private const double ResolveInterval = 0.35;
    private const double UnavailableResolveInterval = 1;
    private const int MaximumApproachRetries = 6;
    private static readonly HashSet<(long Pointer, int Step)> SkippedSteps = new();
    private static readonly Dictionary<(long Task, int Step, long Console), List<Vector2>> RejectedApproaches = new();
    private static readonly Dictionary<(long Task, int Step, long Console), int> ApproachRetries = new();
    private static IntPtr _playerPointer;
    private static IntPtr _shipPointer;
    private static NormalPlayerTask _task;
    private static Console _console;
    private static int _selectedStep;
    private static Vector2 _goal;
    private static double _nextActionAt;
    private static double _nextResolveAt;
    private static double _nextErrorAt;
    private static int _completed;
    private static string _status = "AI tasks: waiting for your assigned tasks";

    public static string StatusText => _status;
    public static int CompletedCount => _completed;
    public static bool IsFinished { get; private set; }

    public static void Reset()
    {
        _playerPointer = IntPtr.Zero;
        _shipPointer = IntPtr.Zero;
        ClearTarget();
        SkippedSteps.Clear();
        RejectedApproaches.Clear();
        ApproachRetries.Clear();
        _nextActionAt = 0;
        _nextResolveAt = 0;
        _nextErrorAt = 0;
        _completed = 0;
        IsFinished = false;
        _status = "AI tasks: waiting for your assigned tasks";
    }

    public static bool TryGetDestination(out Vector2 goal, out float arrivalDistance, out string status)
    {
        goal = default;
        arrivalDistance = 0.12f;
        try
        {
            // Only this resolution may confirm completion. Missing role/task
            // data during a handoff must not latch a previous finished state.
            IsFinished = false;
            if (!TryGetContext(out var local, out var ship))
            {
                status = _status;
                return false;
            }

            if (_playerPointer != local.Pointer || _shipPointer != ship.Pointer)
            {
                Reset();
                _playerPointer = local.Pointer;
                _shipPointer = ship.Pointer;
            }

            if (IsSelectedTaskValid(local) && _console && _task.ValidConsole(_console))
            {
                if (IsWaiting(_task))
                {
                    _status = $"AI tasks: waiting {_task.TaskTimer:0}s for {_task.TaskType}";
                    ClearTarget(); // Work on another task during the native wait.
                }
                else
                {
                    goal = _goal;
                    _status = $"AI tasks: going to {_task.TaskType} ({_selectedStep + 1}/{_task.MaxStep})";
                    status = _status;
                    return true;
                }
            }

            ClearTarget();
            var now = Time.realtimeSinceStartupAsDouble;
            if (now < _nextResolveAt)
            {
                status = _status;
                return false;
            }
            _nextResolveAt = now + ResolveInterval;

            var bestDistance = float.PositiveInfinity;
            var remaining = 0;
            var assignedOwnedTasks = 0;
            var awaitingAssignment = false;
            var waiting = false;
            var unsupported = false;
            var position = local.GetTruePosition();
            if (local.myTasks == null)
            {
                _status = "AI tasks: waiting for your assigned tasks";
                status = _status;
                return false;
            }

            foreach (var entry in local.myTasks)
            {
                if (!entry) continue;
                var task = entry.TryCast<NormalPlayerTask>();
                if (!IsOwnNormalTask(task, local)) continue;
                var assigned = local.Data.FindTaskById(task.Id);
                if (assigned == null)
                {
                    awaitingAssignment = true;
                    continue;
                }
                assignedOwnedTasks++;
                if (task.IsComplete || assigned.Complete) continue;
                remaining++;
                if (task.taskStep < 0 || task.taskStep >= task.MaxStep)
                {
                    unsupported = true;
                    continue;
                }
                if (IsWaiting(task))
                {
                    waiting = true;
                    continue;
                }
                if (SkippedSteps.Contains((task.Pointer.ToInt64(), task.taskStep)))
                {
                    unsupported = true;
                    continue;
                }
                // Skip a timed task only if its actual duration is unavailable.
                // The supported native minigames' timer starts are reproduced at
                // the console, then the game's FixedUpdate counts down normally.
                if (NeedsTimerSetup(task) && !TryGetTimerDuration(task, out _))
                {
                    unsupported = true;
                    continue;
                }
                var foundConsole = false;
                if (ship.AllConsoles != null)
                {
                    foreach (var console in ship.AllConsoles)
                    {
                        if (!console || !task.ValidConsole(console) ||
                            !TryApproach(local, task, console, out var approach)) continue;
                        foundConsole = true;
                        var distance = (approach - position).sqrMagnitude;
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        _task = task;
                        _selectedStep = task.taskStep;
                        _console = console;
                        _goal = approach;
                    }
                }
                if (!foundConsole) unsupported = true;
            }

            if (_task)
            {
                goal = _goal;
                _status = $"AI tasks: going to {_task.TaskType} ({_selectedStep + 1}/{_task.MaxStep})";
                status = _status;
                return true;
            }

            if (waiting) _status = "AI tasks: waiting for a task timer";
            else if (remaining > 0)
            {
                // An unavailable console, timer prefab, or intentionally
                // skipped native step is unfinished work, not a stop signal.
                // Recheck at a bounded cadence without retrying skipped steps.
                _nextResolveAt = now + UnavailableResolveInterval;
                _status = unsupported
                    ? $"AI tasks: {_completed} completed; waiting for consoles or manual task use"
                    : "AI tasks: waiting for an available task console";
            }
            else if (assignedOwnedTasks == 0 || awaitingAssignment || HasPendingAssignment(local))
            {
                _nextResolveAt = now + UnavailableResolveInterval;
                _status = "AI tasks: waiting for assigned task updates";
            }
            else
            {
                IsFinished = true;
                _status = $"AI tasks: finished ({_completed} completed)";
            }
            status = _status;
            return false;
        }
        catch (Exception error)
        {
            SkipCurrent("task could not be resolved");
            _status = "AI tasks: task could not be resolved; retrying other tasks";
            LogFailure("resolution paused", error);
            _nextResolveAt = Time.realtimeSinceStartupAsDouble + 2;
            status = _status;
            return false;
        }
    }

    public static void OnArrived()
    {
        var now = Time.realtimeSinceStartupAsDouble;
        if (now < _nextActionAt) return;
        _nextActionAt = now + ActionInterval;
        try
        {
            if (!TryGetContext(out var local, out var ship) ||
                local.Pointer != _playerPointer || ship.Pointer != _shipPointer ||
                !IsSelectedTaskValid(local) || !_console || !_task.ValidConsole(_console))
            {
                ClearTarget();
                return;
            }
            if (IsWaiting(_task)) return;

            var distance = _console.CanUse(local.Data, out var canUse, out var couldUse);
            if (!couldUse || !canUse || !float.IsFinite(distance) ||
                distance > _console.UsableDistance ||
                Vector2.Distance(local.GetTruePosition(), _goal) > 0.25f)
            {
                if (!TryAlternateApproach())
                    SkipCurrent("console cannot be used from the available arrival points");
                return;
            }

            var task = _task;
            if (NeedsTimerSetup(task))
            {
                if (!TryGetTimerDuration(task, out var duration))
                {
                    SkipCurrent("task timer setup needs manual use");
                    return;
                }
                // The native Sample/Diagnostic/Wifi/Photos actions initialize
                // these exact fields without advancing taskStep. Keep the real
                // configured duration and leave countdown to task.FixedUpdate.
                task.TaskTimer = duration;
                task.TimerStarted = NormalPlayerTask.TimerState.Started;
                _status = $"AI tasks: started {task.TaskType}; waiting {duration:0}s";
                ClearTarget();
                _nextResolveAt = now + ActionInterval;
                return;
            }
            var previousStep = task.taskStep;
            // NextStep owns the normal final-completion RPC. Never send another
            // RPC and never loop through the later console stages at this spot.
            task.NextStep();
            if (!task.IsComplete && task.taskStep <= previousStep)
            {
                SkipCurrent("native task did not advance");
                return;
            }
            if (task.IsComplete)
            {
                _completed++;
                _status = $"AI tasks: completed {task.TaskType} ({_completed})";
            }
            ClearTarget();
            _nextResolveAt = now + ActionInterval;
        }
        catch (Exception error)
        {
            SkipCurrent("native task use failed");
            LogFailure("skipped a failed native task use", error);
        }
    }

    public static void SkipCurrent(string reason)
    {
        if (_task) SkippedSteps.Add((_task.Pointer.ToInt64(), _selectedStep));
        ClearTarget();
        _status = "AI tasks: skipping " + reason;
        _nextResolveAt = Time.realtimeSinceStartupAsDouble + ActionInterval;
    }

    public static bool TryAlternateApproach()
    {
        try
        {
            if (!TryGetContext(out var local, out var ship) ||
                local.Pointer != _playerPointer || ship.Pointer != _shipPointer ||
                !IsSelectedTaskValid(local) || !_console || IsWaiting(_task)) return false;

            var current = _console;
            var rejectedKey = ApproachKey(_task, current);
            if (!RejectedApproaches.TryGetValue(rejectedKey, out var rejected))
                RejectedApproaches[rejectedKey] = rejected = new List<Vector2>();
            if (!rejected.Exists(point => (point - _goal).sqrMagnitude < 0.0001f))
                rejected.Add(_goal);

            if (TryConsole(current)) return true;
            // Some stages permit several consoles (for example towels). Retry
            // another valid console before abandoning the entire native step.
            if (ship.AllConsoles != null)
                foreach (var console in ship.AllConsoles)
                    if (console && console.Pointer != current.Pointer && TryConsole(console)) return true;
            return false;

            bool TryConsole(Console console)
            {
                if (!_task.ValidConsole(console)) return false;
                var key = ApproachKey(_task, console);
                var attempts = ApproachRetries.TryGetValue(key, out var count) ? count : 0;
                if (attempts >= MaximumApproachRetries) return false;
                if (!TryApproach(local, _task, console, out var goal)) return false;
                ApproachRetries[key] = attempts + 1;
                _console = console;
                _goal = goal;
                _status = "AI tasks: trying another route to " + _task.TaskType;
                return true;
            }
        }
        catch (Exception error)
        {
            LogFailure("alternate task approach unavailable", error);
            return false;
        }
    }

    private static bool TryGetContext(out PlayerControl local, out ShipStatus ship)
    {
        local = PlayerControl.LocalPlayer;
        ship = ShipStatus.Instance;
        if (!Utils.isClient || (!Utils.isInGame && !Utils.isFreePlay) || !Utils.isNormalGame ||
            !local || !ship || !local.AmOwner || local.Data == null || local.Data.Disconnected ||
            GameOptionsManager.Instance == null || GameOptionsManager.Instance.CurrentGameOptions == null)
        {
            _status = "AI tasks: waiting for a normal round";
            return false;
        }
        var role = local.Data.Role;
        if (local.Data.IsDead || !role || role.TeamType != RoleTeamTypes.Crewmate || !role.TasksCountTowardProgress)
        {
            _status = "AI tasks: living Crewmates with real tasks only";
            return false;
        }
        if (MalumMenu.isPanicked || !Application.isFocused || MenuUI.isGUIActive ||
            Utils.isMeeting || Utils.isExiling || !HudManager.InstanceExists ||
            !local.CanMove || local.inVent || local.onLadder || local.inMovingPlat)
        {
            _status = "AI tasks: paused while movement is unavailable";
            return false;
        }
        var hud = HudManager.Instance;
        if (!hud || hud.IsIntroDisplayed || (hud.Chat && hud.Chat.IsOpenOrOpening) || Minigame.Instance)
        {
            _status = "AI tasks: close chat or the task window to continue";
            return false;
        }
        return true;
    }

    private static bool IsEligibleTask(NormalPlayerTask task, PlayerControl local)
    {
        if (!IsOwnNormalTask(task, local) || task.IsComplete ||
            task.taskStep < 0 || task.taskStep >= task.MaxStep) return false;
        var assigned = local.Data.FindTaskById(task.Id);
        return assigned != null && !assigned.Complete;
    }

    private static bool IsOwnNormalTask(NormalPlayerTask task, PlayerControl local) =>
        task && task.Owner && task.Owner.AmOwner && task.Owner.Pointer == local.Pointer &&
        !PlayerTask.TaskIsEmergency(task);

    private static bool HasPendingAssignment(PlayerControl local)
    {
        // The task-instance list can be briefly incomplete after native task
        // updates. Do not finish while another real assignment awaits an
        // instance or the native completion acknowledgement.
        var assignments = local.Data.Tasks;
        if (assignments == null) return true;
        foreach (var assigned in assignments)
            if (assigned != null && !assigned.Complete) return true;
        return false;
    }

    private static bool IsSelectedTaskValid(PlayerControl local) =>
        IsEligibleTask(_task, local) && _task.taskStep == _selectedStep &&
        !SkippedSteps.Contains((_task.Pointer.ToInt64(), _selectedStep));

    private static bool IsWaiting(NormalPlayerTask task) =>
        task.TimerStarted == NormalPlayerTask.TimerState.Started;

    private static bool NeedsTimerSetup(NormalPlayerTask task) =>
        task.TimerStarted == NormalPlayerTask.TimerState.NotStarted &&
        task.TaskType is TaskTypes.InspectSample or TaskTypes.RunDiagnostics or TaskTypes.RebootWifi
            or TaskTypes.DevelopPhotos or TaskTypes.MonitorMushroom;

    private static bool TryGetTimerDuration(NormalPlayerTask task, out float duration)
    {
        duration = 0;
        var prefab = task.MinigamePrefab;
        switch (task.TaskType)
        {
            case TaskTypes.InspectSample:
                var sample = prefab ? prefab.TryCast<SampleMinigame>() : null;
                if (sample) duration = sample.TimePerStep;
                break;
            case TaskTypes.RunDiagnostics:
                var diagnostic = prefab ? prefab.TryCast<DiagnosticGame>() : null;
                if (diagnostic) duration = diagnostic.TimePerStep;
                break;
            case TaskTypes.RebootWifi:
                duration = WifiGame.WaitDuration;
                break;
            case TaskTypes.DevelopPhotos:
                duration = task.TaskTimer;
                break;
        }
        return float.IsFinite(duration) && duration > 0;
    }

    private static bool TryApproach(PlayerControl local, NormalPlayerTask task, Console console, out Vector2 approach)
    {
        approach = default;
        var radius = console.UsableDistance;
        if (!float.IsFinite(radius) || radius <= 0.1f) return false;
        radius = Mathf.Min(radius * 0.8f, Mathf.Max(0.1f, radius - 0.15f));
        Vector2 center = console.transform.position;
        var nearest = float.PositiveInfinity;
        var position = local.GetTruePosition();
        RejectedApproaches.TryGetValue(ApproachKey(task, console), out var rejected);
        // Sample legal interaction spots rather than navigating into the panel.
        // The native CanUse check still decides whether arrival may advance it.
        // Several radii cover consoles beside furniture and narrow corridors;
        // never use the center as a shortcut into the console's own footprint.
        for (var ring = 0; ring < 3; ring++)
        {
            var ringRadius = radius * (ring == 0 ? 1f : ring == 1 ? 0.67f : 0.4f);
            for (var index = 0; index < 16; index++)
            {
                var angle = index * (Mathf.PI * 2 / 16);
                var candidate = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ringRadius;
                if (console.onlyFromBelow && candidate.y >= center.y - 0.05f) continue;
                if (rejected != null && rejected.Exists(point => (point - candidate).sqrMagnitude <= 0.04f)) continue;
                if (!NavigationRouter.CanStand(candidate)) continue;
                if (console.checkWalls && PhysicsHelpers.AnythingBetween(local.Collider, candidate, center,
                        Constants.ShipOnlyMask, false)) continue;
                var distance = (candidate - position).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                approach = candidate;
            }
        }
        return !float.IsPositiveInfinity(nearest);
    }

    private static (long Task, int Step, long Console) ApproachKey(NormalPlayerTask task, Console console) =>
        (task.Pointer.ToInt64(), task.taskStep, console.Pointer.ToInt64());

    private static void ClearTarget()
    {
        _task = null;
        _console = null;
        _selectedStep = -1;
        _goal = default;
    }

    private static void LogFailure(string action, Exception error)
    {
        var now = Time.realtimeSinceStartupAsDouble;
        if (now < _nextErrorAt) return;
        _nextErrorAt = now + 5;
        MalumMenu.Log?.LogWarning("AI Tasks: " + action + ": " + error.Message);
    }
}
