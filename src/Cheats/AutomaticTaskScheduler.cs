using System;
using System.Collections.Generic;

namespace MalumMenu;

internal sealed class AutomaticTaskScheduler
{
    public const double IntervalSeconds = 0.25;

    public readonly record struct TaskSlot(
        uint Id,
        long InstanceId,
        bool IsOwned,
        bool IsAssigned,
        bool IsNormal,
        bool IsComplete,
        bool IsSabotage,
        int RemainingSteps);

    private readonly HashSet<(uint Id, long InstanceId)> _attempted = new();
    private readonly HashSet<uint> _seenIds = new();
    private readonly HashSet<uint> _duplicateIds = new();
    private bool _hasTaken;
    private double _lastTakenAt;

    public bool TryTake(
        double now,
        bool enabled,
        bool gameplayReady,
        bool roleAllowsProgress,
        IReadOnlyList<TaskSlot> tasks,
        out TaskSlot next)
    {
        next = default;
        if (!enabled || !gameplayReady || !roleAllowsProgress ||
            !double.IsFinite(now) || now < 0 || tasks == null || tasks.Count == 0)
            return false;

        if (_hasTaken && now - _lastTakenAt < IntervalSeconds)
            return false;

        _seenIds.Clear();
        _duplicateIds.Clear();
        for (var i = 0; i < tasks.Count; i++)
        {
            if (!_seenIds.Add(tasks[i].Id))
                _duplicateIds.Add(tasks[i].Id);
        }

        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            if (_duplicateIds.Contains(task.Id) || task.InstanceId == 0 ||
                !task.IsOwned || !task.IsAssigned || !task.IsNormal ||
                task.IsComplete || task.IsSabotage ||
                task.RemainingSteps < 1 || task.RemainingSteps > 64 ||
                !_attempted.Add((task.Id, task.InstanceId)))
                continue;

            _hasTaken = true;
            _lastTakenAt = now;
            next = task;
            return true;
        }

        return false;
    }

    public void Reset()
    {
        _attempted.Clear();
        _seenIds.Clear();
        _duplicateIds.Clear();
        _hasTaken = false;
        _lastTakenAt = 0;
    }
}
